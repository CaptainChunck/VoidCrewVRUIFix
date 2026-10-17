using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace VoidCrewVRUIFix
{
    /// <summary>HDRP camera settings that break VR rendering.</summary>
    internal static class HdrpTweaks
    {
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>
        /// Backstop for the settings patch: DLSS can also be switched on per camera (for example by a saved
        /// setting applied before the options menu exists), so it is turned off on every camera while in VR.
        /// </summary>
        public static void DisableDlssOnCameras()
        {
            if (!Settings.DisableDlss.Value) return;

            foreach (Camera camera in Camera.allCameras)
            {
                var data = camera.GetComponent<HDAdditionalCameraData>();
                if (data == null || !data.allowDeepLearningSuperSampling) continue;

                data.allowDeepLearningSuperSampling = false;
                if (Reported.Add(camera.name))
                {
                    Log.Info("Turned off DLSS on camera '" + camera.name + "' (DLSS breaks VR rendering).");
                }
            }
        }
    }
}
