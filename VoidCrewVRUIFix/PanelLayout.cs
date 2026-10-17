using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Size and placement of the VR UI panel. The panel shows the whole game screen, so its aspect ratio is the
    /// game window's. Both display backends and the marker re-projection use these numbers, which is what keeps
    /// markers lined up with the world.
    /// </summary>
    internal static class PanelLayout
    {
        public static float Distance { get { return Mathf.Clamp(Settings.PanelDistance.Value, 0.3f, 10f); } }

        public static float WidthDegrees { get { return Mathf.Clamp(Settings.PanelWidthDegrees.Value, 20f, 140f); } }

        public static float HeightOffset { get { return Settings.PanelHeightOffset.Value; } }

        public static float Aspect
        {
            get { return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f; }
        }

        public static float TanHalfWidth { get { return Mathf.Tan(WidthDegrees * 0.5f * Mathf.Deg2Rad); } }

        public static float TanHalfHeight { get { return TanHalfWidth / Aspect; } }

        public static float WidthMeters { get { return 2f * Distance * TanHalfWidth; } }

        public static float HeightMeters { get { return WidthMeters / Aspect; } }

        public static string Describe()
        {
            return string.Format("{0:0}° wide ({1:0.00} x {2:0.00} m) at {3:0.0} m, anchor {4}, height offset {5:0.00} m",
                WidthDegrees, WidthMeters, HeightMeters, Distance, Settings.Anchor.Value, HeightOffset);
        }
    }

    /// <summary>
    /// Re-projects Camera.WorldToViewportPoint for the player's camera onto the VR UI panel. Void Crew positions every
    /// ping, objective marker and interaction prompt with that call, so this keeps them on top of the objects they
    /// belong to, whatever size the panel is.
    /// </summary>
    internal static class MarkerAlignment
    {
        public static Camera PlayerCamera;
        public static bool Active;

        public static bool TryRemap(Camera camera, Vector3 worldPosition, Camera.MonoOrStereoscopicEye eye, out Vector3 viewport)
        {
            viewport = default(Vector3);
            if (!Active || eye != Camera.MonoOrStereoscopicEye.Mono || !ReferenceEquals(camera, PlayerCamera) || camera == null)
            {
                return false;
            }

            // Outside rendering the camera transform is the in-game view direction (UUVR only applies head rotation
            // while the frame renders), which is exactly the frame the GameCamera-anchored panel is placed in.
            Transform t = camera.transform;
            Vector3 local = Quaternion.Inverse(t.rotation) * (worldPosition - t.position);
            float z = local.z;
            if (Mathf.Abs(z) < 1e-4f) z = z < 0f ? -1e-4f : 1e-4f;

            float tanX = local.x / z;
            float tanY = local.y / z - PanelLayout.HeightOffset / PanelLayout.Distance;
            viewport = new Vector3(
                0.5f + tanX / (2f * PanelLayout.TanHalfWidth),
                0.5f + tanY / (2f * PanelLayout.TanHalfHeight),
                local.z);
            return true;
        }
    }
}
