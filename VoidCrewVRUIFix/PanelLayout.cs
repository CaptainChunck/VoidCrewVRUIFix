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
    /// Re-projects the player camera's screen/world conversions onto the VR UI panel, in both directions.
    /// World to screen: Void Crew places every ping, objective marker and interaction prompt with
    /// WorldToViewportPoint/WorldToScreenPoint, so they sit on top of the objects they belong to.
    /// Screen to world: in-world terminals aim the mouse with ScreenToWorldPoint (and world-space canvases with
    /// ScreenPointToRay), so clicks land where the VR cursor appears on the panel.
    /// Outside rendering the camera transform is the in-game view direction (UUVR only applies the head rotation
    /// while a frame renders), which is exactly the frame the GameCamera-anchored panel is placed in.
    /// </summary>
    internal static class MarkerAlignment
    {
        public static Camera PlayerCamera;
        public static bool Active;

        public static bool Applies(Camera camera, Camera.MonoOrStereoscopicEye eye)
        {
            return Active && eye == Camera.MonoOrStereoscopicEye.Mono && ReferenceEquals(camera, PlayerCamera) && camera != null;
        }

        public static Vector3 WorldToViewport(Camera camera, Vector3 worldPosition)
        {
            Transform t = camera.transform;
            Vector3 local = Quaternion.Inverse(t.rotation) * (worldPosition - t.position);
            float z = local.z;
            if (Mathf.Abs(z) < 1e-4f) z = z < 0f ? -1e-4f : 1e-4f;

            float tanX = local.x / z;
            float tanY = local.y / z - PanelLayout.HeightOffset / PanelLayout.Distance;
            return new Vector3(
                0.5f + tanX / (2f * PanelLayout.TanHalfWidth),
                0.5f + tanY / (2f * PanelLayout.TanHalfHeight),
                local.z);
        }

        public static Vector3 WorldToScreen(Camera camera, Vector3 worldPosition)
        {
            Vector3 viewport = WorldToViewport(camera, worldPosition);
            return new Vector3(viewport.x * Screen.width, viewport.y * Screen.height, viewport.z);
        }

        /// <summary>Like Camera.ScreenToWorldPoint: screen.z is the distance in front of the camera.</summary>
        public static Vector3 ScreenToWorld(Camera camera, Vector3 screen)
        {
            Transform t = camera.transform;
            return t.position + t.rotation * (ScreenDirection(screen) * screen.z);
        }

        /// <summary>Like Camera.ScreenPointToRay: starts on the near plane and points through the screen position.</summary>
        public static Ray ScreenToRay(Camera camera, Vector3 screen)
        {
            Transform t = camera.transform;
            Vector3 direction = t.rotation * ScreenDirection(screen);
            return new Ray(t.position + direction * camera.nearClipPlane, direction.normalized);
        }

        /// <summary>Camera-local direction (z = 1) through a screen position on the panel.</summary>
        private static Vector3 ScreenDirection(Vector3 screen)
        {
            float tanX = (screen.x / Mathf.Max(1, Screen.width) * 2f - 1f) * PanelLayout.TanHalfWidth;
            float tanY = (screen.y / Mathf.Max(1, Screen.height) * 2f - 1f) * PanelLayout.TanHalfHeight
                         + PanelLayout.HeightOffset / PanelLayout.Distance;
            return new Vector3(tanX, tanY, 1f);
        }
    }
}
