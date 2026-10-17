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
        /// DLSS can be switched on per camera (for example by a saved setting applied before the options menu exists).
        /// It is turned off on every camera except the player's, and on the player's too unless DLSS is allowed
        /// (Fixes > Disable DLSS = false, or the VR tab's experimental toggle). HDRP keeps a DLSS state per eye for
        /// the player camera; the game's other cameras, like the 960x540 interaction-glow camera, must not use it.
        /// </summary>
        public static void DisableDlssOnCameras(Camera playerCamera)
        {
            bool allowOnPlayer = !Settings.DisableDlss.Value;

            foreach (Camera camera in Camera.allCameras)
            {
                if (allowOnPlayer && camera == playerCamera) continue;

                var data = camera.GetComponent<HDAdditionalCameraData>();
                if (data == null || !data.allowDeepLearningSuperSampling) continue;

                data.allowDeepLearningSuperSampling = false;
                if (Reported.Add(camera.name))
                {
                    Log.Info("Turned off DLSS on camera '" + camera.name + "'" +
                             (allowOnPlayer ? " (DLSS is only allowed on the player camera)." : " (DLSS is disabled in VR)."));
                }
            }
        }
    }
}
