using System;
using UnityEngine;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>VR runtime state and camera selection.</summary>
    internal static class XrState
    {
        public static bool IsActive
        {
            get
            {
                try { return XRSettings.isDeviceActive; }
                catch { return false; }
            }
        }

        public static string DeviceName
        {
            get
            {
                try { return XRSettings.loadedDeviceName ?? ""; }
                catch { return ""; }
            }
        }

        public static bool IsOpenVr
        {
            get { return DeviceName.IndexOf("OpenVR", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        /// <summary>A camera that renders the game world to the headset (not to a texture, not one of UUVR's helpers).</summary>
        public static bool IsHeadsetCamera(Camera camera)
        {
            return camera != null
                && camera.isActiveAndEnabled
                && camera.cameraType == CameraType.Game
                && camera.targetTexture == null
                && camera.stereoTargetEye != StereoTargetEyeMask.None
                && !UuvrCompat.IsUuvrInternalCamera(camera);
        }

        /// <summary>The camera the player is looking through: Camera.main if it renders to the headset, else the highest-depth headset camera.</summary>
        public static Camera FindMainCamera()
        {
            Camera main = Camera.main;
            if (IsHeadsetCamera(main)) return main;

            Camera best = null;
            foreach (Camera camera in Camera.allCameras)
            {
                if (IsHeadsetCamera(camera) && (best == null || camera.depth > best.depth))
                {
                    best = camera;
                }
            }
            return best;
        }
    }
}
