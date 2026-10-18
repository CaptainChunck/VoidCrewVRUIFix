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

        // Set once UUVR's UI manager and camera start-up are hooked; the scene is then only searched once per VR start.
        private static bool _hooked;
        private static bool _scanPending = true;

        public static bool Installed { get; private set; }
        public static string Version { get; private set; } = "not installed";
        public static int DisabledUiManagers { get; private set; }
        public static int DisabledHelperCameras { get; private set; }

        /// <summary>Where UUVR's pieces are turned off from, for the diagnostics report.</summary>
        public static string Mode { get { return _hooked ? "hooked into UUVR's start-up" : "searching the scene twice a second"; } }

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
        public static string GetSetting(string fieldName)
        {
            ConfigEntryBase entry = GetEntry(fieldName);
            return entry != null && entry.BoxedValue != null ? entry.BoxedValue.ToString() : null;
        }

        /// <summary>Changes a UUVR setting (the player asked for it in the VR settings tab). UUVR saves its own config.</summary>
        public static bool SetSetting(string fieldName, string value)
        {
            ConfigEntryBase entry = GetEntry(fieldName);
            if (entry == null)
            {
                Log.Warn("Could not change UUVR's '" + fieldName + "' setting (not found in this UUVR version).");
                return false;
            }
            entry.BoxedValue = TomlTypeConverter.ConvertToValue(value, entry.SettingType);
            Log.Info("Changed UUVR's '" + entry.Definition.Key + "' setting to " + value + ".");
            return true;
        }

        /// <summary>
        /// Puts the UUVR settings the VR tab shows at this plugin's defaults: OpenVR (SteamVR), which the sharp SteamVR
        /// overlay needs (UUVR's own default is OpenXR), and, with horizonToo, the horizon not kept level. On the first
        /// start only the VR system is set; RESTORE on the VR tab sets both. UUVR picks its VR system when it starts VR,
        /// just after all the plugins have loaded, so on the first start the change applies straight away.
        /// Returns true if a setting changed.
        /// </summary>
        public static bool ApplyDefaults(bool horizonToo)
        {
            if (!Installed) return false;
            bool changed = false;
            string api = GetSetting("PreferredVrApi");
            if (api != null && api != "OpenVr") changed |= SetSetting("PreferredVrApi", "OpenVr");
            if (horizonToo && GetSetting("AlignCameraToHorizon") == "True") changed |= SetSetting("AlignCameraToHorizon", "False");
            return changed;
        }

        /// <summary>Switches VR on or off the same way UUVR's F3 key does.</summary>
        public static bool ToggleVr()
        {
            Resolve();
            Type coreType = _configType != null ? _configType.Assembly.GetType("Uuvr.UuvrCore") : null;
            UnityEngine.Object core = coreType != null ? UnityEngine.Object.FindObjectOfType(coreType) : null;
            FieldInfo managerField = coreType != null ? AccessTools.Field(coreType, "_vrTogglerManager") : null;
            object manager = core != null && managerField != null ? managerField.GetValue(core) : null;
            MethodInfo toggle = manager != null ? AccessTools.Method(manager.GetType(), "ToggleVr") : null;
            if (toggle == null) return false;

            toggle.Invoke(manager, null);
            return true;
        }

        private static ConfigEntryBase GetEntry(string fieldName)
        {
            try
            {
                Resolve();
                if (_configType == null) return null;
                if (_config == null)
                {
                    FieldInfo instance = AccessTools.Field(_configType, "Instance");
                    _config = instance != null ? instance.GetValue(null) : null;
                    if (_config == null) return null;
                }
                FieldInfo field = AccessTools.Field(_configType, fieldName);
                return field != null ? field.GetValue(_config) as ConfigEntryBase : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The plugin hooked UUVR's UI manager and VR camera start-up (see GamePatches).</summary>
        public static void MarkHooked()
        {
            _hooked = true;
        }

        /// <summary>Searches the scene once more on the next maintenance (VR just started).</summary>
        public static void RequestScan()
        {
            _scanPending = true;
        }

        /// <summary>
        /// Called a couple of times per second while VR is active. With the start-up hooks in place this only searches
        /// the scene once after VR starts, as a safety net; without them it searches every time.
        /// </summary>
        public static void Maintain()
        {
            Resolve();
            if (!Installed) return;
            if (_hooked && !_scanPending) return;
            _scanPending = false;

            if (Settings.DisableUuvrUi.Value && _vrUiManagerType != null)
            {
                foreach (UnityEngine.Object found in UnityEngine.Object.FindObjectsOfType(_vrUiManagerType))
                {
                    TurnOffUiManager(found as Component);
                }
            }

            if (Settings.DisableUuvrHelperCameras.Value && _vrCameraType != null)
            {
                foreach (UnityEngine.Object vrCamera in UnityEngine.Object.FindObjectsOfType(_vrCameraType))
                {
                    DisableHelperCamera(vrCamera as Component);
                }
            }
        }

        /// <summary>Turns off UUVR's own UI handling (a UUVR UI manager just started, or was found in the scene).</summary>
        public static void TurnOffUiManager(Component manager)
        {
            if (manager == null || !Settings.DisableUuvrUi.Value || !manager.gameObject.activeSelf) return;

            // Deactivating the object runs UUVR's own OnDisable cleanup, which turns the desktop mirror back on
            // and stops its UI cameras, UI quad and software cursor.
            manager.gameObject.SetActive(false);
            DisabledUiManagers++;
            Log.Info("Turned off UUVR's built-in UI handling (UI mode was '" + UiPatchMode + "'). This plugin draws the UI instead.");
            GamePatches.RefreshHardwareCursor();
        }

        /// <summary>Disables a UUVR VR camera's tracking helper camera (it just started, or was found in the scene).</summary>
        public static void DisableHelperCamera(Component vrCamera)
        {
            if (vrCamera == null || _childCameraField == null || !Settings.DisableUuvrHelperCameras.Value || CameraTracking != "RelativeTransform") return;

            var helper = _childCameraField.GetValue(vrCamera) as Camera;
            if (helper == null || !helper.enabled) return;

            // In RelativeTransform mode UUVR only reads this camera's transform, which keeps updating while disabled.
            helper.enabled = false;
            DisabledHelperCameras++;
            Log.Info("Disabled UUVR's tracking helper camera for '" + vrCamera.name +
                     "'. HDRP was rendering it as a full extra VR camera every frame.");
        }

        // Looked up once per camera (and again every second while UUVR hasn't made its helper camera yet).
        private static Camera _viewCamera;
        private static Camera _viewHelper;
        private static float _nextViewLookup;
        private static string _tracking;
        private static bool _horizonLevel;
        private static float _nextTrackingCheck;

        /// <summary>
        /// The world rotation UUVR draws this camera with. In UUVR's usual tracking mode that is its hidden tracking
        /// camera's rotation: the game camera turned by the headset (and levelled, with "keep horizon level"). In its
        /// Absolute mode the game camera itself already follows the headset. False if neither applies.
        /// </summary>
        public static bool TryGetViewRotation(Camera camera, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (camera == null || _vrCameraType == null) return false;
            if (Tracking() == "Absolute")
            {
                rotation = camera.transform.rotation;
                return true;
            }

            Camera helper = ViewHelper(camera);
            if (helper == null) return false;
            rotation = helper.transform.rotation;
            return true;
        }

        /// <summary>
        /// The headset's rotation as UUVR last applied it: its tracking camera's rotation under the game camera, which is
        /// the headset's rotation in Unity's tracking space. False with Absolute tracking or before UUVR's camera exists.
        /// </summary>
        public static bool TryGetHeadRotation(Camera camera, out Quaternion head)
        {
            head = Quaternion.identity;
            if (camera == null || _vrCameraType == null || Tracking() == "Absolute") return false;
            Camera helper = ViewHelper(camera);
            if (helper == null) return false;
            head = helper.transform.localRotation;
            return true;
        }

        /// <summary>
        /// The game camera's rotation in UUVR's tracking space, the space the headset turns the view in: none, or with
        /// UUVR's "keep horizon level" the camera's tilt (UUVR then only follows the camera's heading).
        /// </summary>
        public static Quaternion CameraInTrackingSpace(Camera camera)
        {
            if (camera == null || !KeepsHorizonLevel()) return Quaternion.identity;
            Quaternion rotation = camera.transform.rotation;
            Vector3 heading = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            if (heading.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.Inverse(Quaternion.LookRotation(heading, Vector3.up)) * rotation;
        }

        private static string Tracking()
        {
            RefreshTrackingSettings();
            return _tracking;
        }

        private static bool KeepsHorizonLevel()
        {
            RefreshTrackingSettings();
            return _horizonLevel;
        }

        // UUVR's tracking settings, read again once a second (the VR tab can change them).
        private static void RefreshTrackingSettings()
        {
            if (Time.unscaledTime < _nextTrackingCheck) return;
            _nextTrackingCheck = Time.unscaledTime + 1f;
            _tracking = CameraTracking;
            _horizonLevel = GetSetting("AlignCameraToHorizon") == "True";
        }

        /// <summary>UUVR's hidden tracking camera under the given game camera, or null.</summary>
        private static Camera ViewHelper(Camera camera)
        {
            if (_childCameraField == null) return null;
            if (!ReferenceEquals(camera, _viewCamera) || (_viewHelper == null && Time.unscaledTime >= _nextViewLookup))
            {
                _viewCamera = camera;
                _nextViewLookup = Time.unscaledTime + 1f;
                Component vrCamera = camera.GetComponent(_vrCameraType);
                _viewHelper = vrCamera != null ? _childCameraField.GetValue(vrCamera) as Camera : null;
            }
            return _viewHelper != null && _viewHelper.gameObject.activeInHierarchy ? _viewHelper : null;
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
