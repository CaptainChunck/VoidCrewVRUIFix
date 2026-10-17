using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Frame-rate settings that only matter in VR, applied while VR is on and undone when it stops:
    /// the monitor's V-Sync and frame limit (they must never hold the headset back), DLSS camera jitter, and HDRP's
    /// hidden-area mask (pixels the headset's lenses can't show are not drawn).
    /// </summary>
    internal static class VrPerformance
    {
        // Frames DLSS may go without running before its camera jitter is switched off again.
        private const int DlssIdleFrames = 90;

        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();
        private static FieldInfo _hdrpSettingsField;
        private static bool _hdrpLookupDone;

        private static bool _syncOverridden;
        private static int _gameVSync;
        private static int _gameFrameLimit;
        private static bool _jitterOn;
        private static bool _jitterWasOff;
        private static bool _maskTurnedOn;

        /// <summary>Main thread, a couple of times per second while VR is on.</summary>
        public static void Maintain()
        {
            UpdateMonitorSync(Settings.MonitorVSyncOffInVr.Value);
            UpdateHdrpXr();
        }

        /// <summary>Puts back everything changed for VR.</summary>
        public static void OnVrStopped()
        {
            UpdateMonitorSync(false);
            if (_jitterOn) SetCameraJitter(false);
        }

        private static void UpdateMonitorSync(bool wanted)
        {
            if (!wanted)
            {
                if (!_syncOverridden) return;
                _syncOverridden = false;
                QualitySettings.vSyncCount = _gameVSync;
                Application.targetFrameRate = _gameFrameLimit;
                Log.Info("Monitor V-Sync and frame limit are back to the game's settings (V-Sync " + _gameVSync + ", frame limit " +
                         (_gameFrameLimit > 0 ? _gameFrameLimit.ToString() : "none") + ").");
                return;
            }

            int vSync = QualitySettings.vSyncCount;
            int frameLimit = Application.targetFrameRate;
            if (vSync == 0 && frameLimit <= 0) return;

            // First time, or the game applied its display settings again (for example from its Graphics menu).
            _gameVSync = vSync;
            _gameFrameLimit = frameLimit;
            _syncOverridden = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Log.Info("Turned off the monitor's V-Sync (was " + vSync + ") and frame limit (was " + (frameLimit > 0 ? frameLimit.ToString() : "none") +
                     ") while in VR, so they can't hold back the headset. SteamVR paces the frames.");
        }

        private static void UpdateHdrpXr()
        {
            bool wantJitter = Settings.DlssCameraJitter.Value && !Settings.DisableDlss.Value && DlssEyes.RanWithin(DlssIdleFrames);
            if (wantJitter != _jitterOn) SetCameraJitter(wantJitter);

            GlobalXRSettings xr;
            HDRenderPipelineAsset asset;
            if (!TryGetXrSettings(out asset, out xr) || xr.occlusionMesh) return;
            xr.occlusionMesh = true;
            if (SetXrSettings(asset, xr) && !_maskTurnedOn)
            {
                _maskTurnedOn = true;
                Log.Info("Turned on HDRP's hidden-area mask, so the pixels the headset's lenses can't show aren't drawn.");
            }
        }

        private static void SetCameraJitter(bool on)
        {
            GlobalXRSettings xr;
            HDRenderPipelineAsset asset;
            if (!TryGetXrSettings(out asset, out xr))
            {
                _jitterOn = false;
                return;
            }

            if (on)
            {
                _jitterWasOff = !xr.cameraJitter;
                if (_jitterWasOff)
                {
                    xr.cameraJitter = true;
                    if (!SetXrSettings(asset, xr)) return;
                    Log.InfoOnce("dlss-jitter", "DLSS is running: gave it camera jitter (HDRP turns it off in VR), which lets DLSS rebuild fine detail. " +
                                                "Performance > DLSS Camera Jitter turns this off.");
                }
                _jitterOn = true;
                return;
            }

            _jitterOn = false;
            if (_jitterWasOff && xr.cameraJitter)
            {
                xr.cameraJitter = false;
                SetXrSettings(asset, xr);
            }
            _jitterWasOff = false;
        }

        /// <summary>For the diagnostics report.</summary>
        public static string DescribeMonitorSync()
        {
            string game = "V-Sync " + QualitySettings.vSyncCount + ", frame limit " + (Application.targetFrameRate > 0 ? Application.targetFrameRate.ToString() : "none");
            if (!Settings.MonitorVSyncOffInVr.Value) return game + " (the plugin leaves them alone: Performance > Monitor V-Sync Off In VR is off)";
            return game + (_syncOverridden
                ? " (turned off for VR; the game's own: V-Sync " + _gameVSync + ", frame limit " + (_gameFrameLimit > 0 ? _gameFrameLimit.ToString() : "none") + ")"
                : "");
        }

        /// <summary>For the diagnostics report.</summary>
        public static string DescribeHdrpXr(Camera camera)
        {
            GlobalXRSettings xr;
            HDRenderPipelineAsset asset;
            if (!TryGetXrSettings(out asset, out xr)) return "HDRP's VR settings could not be read";

            return "single-pass allowed " + xr.singlePass +
                   " | hidden-area mask " + (xr.occlusionMesh ? "on" : "off") + ", " + DescribeHeadsetMask(camera) +
                   " | camera jitter " + (xr.cameraJitter ? "on" : "off") +
                   (Settings.DlssCameraJitter.Value ? (_jitterOn ? " (given to DLSS while it runs)" : " (only while DLSS runs)") : " (DLSS jitter turned off in the config)");
        }

        private static string DescribeHeadsetMask(Camera camera)
        {
            if (camera == null) return "no player camera to check the headset's mask";
            Displays.Clear();
            SubsystemManager.GetInstances(Displays);
            foreach (XRDisplaySubsystem display in Displays)
            {
                if (!display.running || display.GetRenderPassCount() == 0) continue;
                XRDisplaySubsystem.XRRenderPass pass;
                display.GetRenderPass(0, out pass);
                if (pass.GetRenderParameterCount() == 0) continue;
                XRDisplaySubsystem.XRRenderParameter eye;
                pass.GetRenderParameter(camera, 0, out eye);
                return eye.occlusionMesh != null
                    ? "the headset supplies one (" + eye.occlusionMesh.vertexCount + " vertices per eye)"
                    : "the headset supplies none (normal for SteamVR's null driver)";
            }
            return "no running VR display";
        }

        private static bool TryGetXrSettings(out HDRenderPipelineAsset asset, out GlobalXRSettings xr)
        {
            xr = default(GlobalXRSettings);
            asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (asset == null) return false;
            xr = asset.currentPlatformRenderPipelineSettings.xrSettings;
            return true;
        }

        /// <summary>
        /// Writes HDRP's VR settings straight into the asset's field. The public setter also re-validates the asset,
        /// which can rebuild the whole render pipeline mid-game.
        /// </summary>
        private static bool SetXrSettings(HDRenderPipelineAsset asset, GlobalXRSettings xr)
        {
            if (!_hdrpLookupDone)
            {
                _hdrpLookupDone = true;
                _hdrpSettingsField = AccessTools.Field(typeof(HDRenderPipelineAsset), "m_RenderPipelineSettings");
                if (_hdrpSettingsField == null || _hdrpSettingsField.FieldType != typeof(RenderPipelineSettings))
                {
                    _hdrpSettingsField = null;
                    Log.Warn("Could not find HDRP's VR settings (HDRP may have been updated); DLSS camera jitter and the hidden-area mask check are off.");
                }
            }
            if (_hdrpSettingsField == null) return false;

            var settings = (RenderPipelineSettings)_hdrpSettingsField.GetValue(asset);
            settings.xrSettings = xr;
            _hdrpSettingsField.SetValue(asset, settings);
            return true;
        }

        /// <summary>For the offline patch check: null when everything this class needs from HDRP exists.</summary>
        public static string CheckHdrp()
        {
            FieldInfo field = AccessTools.Field(typeof(HDRenderPipelineAsset), "m_RenderPipelineSettings");
            if (field == null) return "HDRenderPipelineAsset.m_RenderPipelineSettings not found";
            if (field.FieldType != typeof(RenderPipelineSettings)) return "m_RenderPipelineSettings is a " + field.FieldType.Name;
            return null;
        }
    }
}
