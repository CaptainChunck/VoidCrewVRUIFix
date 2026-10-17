using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Everything this plugin needs from UUVR. All access is by reflection so a UUVR update can never stop
    /// this plugin from loading; missing pieces are reported once and the related fix is skipped.
    /// UUVR's files and config are never modified, only its objects in the running game.
    /// </summary>
    internal static class UuvrCompat
    {
        public const string UuvrGuid = "raicuparta.uuvr-modern";

        private static bool _resolved;
        private static Type _vrUiManagerType;
        private static Type _vrCameraType;
        private static FieldInfo _childCameraField;
        private static Type _configType;
        private static object _config;

        public static bool Installed { get; private set; }
        public static string Version { get; private set; } = "not installed";
        public static int DisabledUiManagers { get; private set; }
        public static int DisabledHelperCameras { get; private set; }

        public static string VrApi { get { return GetSetting("PreferredVrApi") ?? "?"; } }
        public static string CameraTracking { get { return GetSetting("CameraTracking") ?? "?"; } }
        public static string UiPatchMode { get { return GetSetting("PreferredUiPatchMode") ?? "?"; } }

        public static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            PluginInfo info;
            if (Chainloader.PluginInfos.TryGetValue(UuvrGuid, out info))
            {
                Installed = true;
                Version = info.Metadata.Version.ToString();
            }

            Assembly uuvr = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Uuvr");
            if (uuvr == null) return;
            Installed = true;

            _vrUiManagerType = uuvr.GetType("Uuvr.VrUi.VrUiManager");
            _vrCameraType = uuvr.GetType("Uuvr.VrCamera.VrCamera");
            _configType = uuvr.GetType("Uuvr.ModConfiguration");
            if (_vrCameraType != null) _childCameraField = AccessTools.Field(_vrCameraType, "_childCamera");

            if (_vrUiManagerType == null) Log.Warn("UUVR's UI manager type was not found. UUVR's own UI handling cannot be turned off and may conflict with this plugin.");
            if (_childCameraField == null) Log.Verbose("UUVR's helper camera field was not found; helper cameras will be left alone.");
        }

        /// <summary>Reads a UUVR setting by its field name on Uuvr.ModConfiguration; null if unavailable.</summary>
        private static string GetSetting(string fieldName)
        {
            try
            {
                if (_configType == null) return null;
                if (_config == null)
                {
                    FieldInfo instance = AccessTools.Field(_configType, "Instance");
                    _config = instance != null ? instance.GetValue(null) : null;
                    if (_config == null) return null;
                }
                FieldInfo field = AccessTools.Field(_configType, fieldName);
                var entry = field != null ? field.GetValue(_config) as ConfigEntryBase : null;
                return entry != null && entry.BoxedValue != null ? entry.BoxedValue.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Called a couple of times per second while VR is active. Cheap when there is nothing to do.</summary>
        public static void Maintain()
        {
            Resolve();
            if (!Installed) return;

            if (Settings.DisableUuvrUi.Value && _vrUiManagerType != null)
            {
                foreach (UnityEngine.Object found in UnityEngine.Object.FindObjectsOfType(_vrUiManagerType))
                {
                    var component = found as Component;
                    if (component == null || !component.gameObject.activeSelf) continue;

                    // Deactivating the object runs UUVR's own OnDisable cleanup, which turns the desktop mirror back on
                    // and stops its UI cameras, UI quad and software cursor.
                    component.gameObject.SetActive(false);
                    DisabledUiManagers++;
                    Log.Info("Turned off UUVR's built-in UI handling (UI mode was '" + UiPatchMode + "'). This plugin draws the UI instead.");
                    GamePatches.RefreshHardwareCursor();
                }
            }

            if (Settings.DisableUuvrHelperCameras.Value && _childCameraField != null && CameraTracking == "RelativeTransform")
            {
                foreach (UnityEngine.Object vrCamera in UnityEngine.Object.FindObjectsOfType(_vrCameraType))
                {
                    var helper = _childCameraField.GetValue(vrCamera) as Camera;
                    if (helper == null || !helper.enabled) continue;

                    // In RelativeTransform mode UUVR only reads this camera's transform, which keeps updating while disabled.
                    helper.enabled = false;
                    DisabledHelperCameras++;
                    Log.Info("Disabled UUVR's tracking helper camera for '" + ((Component)vrCamera).name +
                             "'. HDRP was rendering it as a full extra VR camera every frame.");
                }
            }
        }

        /// <summary>Cameras UUVR creates for itself. They must never be picked as the player's camera.</summary>
        public static bool IsUuvrInternalCamera(Camera camera)
        {
            switch (camera.gameObject.name)
            {
                case "VrChildCamera":
                case "UuvrPoseDriver":
                case "VrUiCaptureCamera":
                case "VrUiManager":
                    return true;
                default:
                    return false;
            }
        }
    }
}
