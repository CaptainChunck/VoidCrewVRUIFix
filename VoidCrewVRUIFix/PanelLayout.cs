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
        // While a terminal is in use the panel sits on the terminal's screen (see TerminalScreens), so the mouse
        // pointer and the button hints are on the screen itself, like on a monitor.
        private static bool _onTerminal;
        private static float _terminalDistance;
        private static float _terminalWidthDegrees;
        private static float _terminalHeightOffset;

        public static bool OnTerminalScreen { get { return _onTerminal; } }

        public static float Distance
        {
            get { return _onTerminal ? _terminalDistance : Mathf.Clamp(Settings.PanelDistance.Value, 0.3f, 10f); }
        }

        public static float WidthDegrees
        {
            get { return _onTerminal ? _terminalWidthDegrees : Mathf.Clamp(Settings.PanelWidthDegrees.Value, 20f, 140f); }
        }

        public static float HeightOffset
        {
            get
            {
                if (_onTerminal) return _terminalHeightOffset;
                // A turret shoots along the game camera's centre line, so its crosshair (the middle of the panel) goes there.
                return HeadAim.InVehicleView ? 0f : Settings.PanelHeightOffset.Value;
            }
        }

        /// <summary>
        /// Puts the panel onto a terminal screen. Changes under a centimetre or a quarter degree are ignored, so the
        /// panel doesn't jitter. Returns true when the panel has just moved onto a terminal.
        /// </summary>
        public static bool PlaceOnTerminal(float distance, float widthDegrees, float heightOffset)
        {
            if (_onTerminal && Mathf.Abs(distance - _terminalDistance) < 0.01f && Mathf.Abs(widthDegrees - _terminalWidthDegrees) < 0.25f &&
                Mathf.Abs(heightOffset - _terminalHeightOffset) < 0.01f) return false;
            bool arrived = !_onTerminal;
            _onTerminal = true;
            _terminalDistance = distance;
            _terminalWidthDegrees = widthDegrees;
            _terminalHeightOffset = heightOffset;
            return arrived;
        }

        /// <summary>Puts the panel back where the settings say. Returns true if it was on a terminal.</summary>
        public static bool LeaveTerminal()
        {
            bool was = _onTerminal;
            _onTerminal = false;
            return was;
        }

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
            return string.Format("{0:0}° wide ({1:0.00} x {2:0.00} m) at {3:0.0} m, anchor {4}, height offset {5:0.00} m{6}",
                WidthDegrees, WidthMeters, HeightMeters, Distance, Settings.Anchor.Value, HeightOffset,
                _onTerminal ? ", on a terminal screen" : HeadAim.InVehicleView ? ", centred on the turret or ship camera" : "");
        }
    }

    /// <summary>
    /// Re-projects the player camera's screen/world conversions onto the VR UI panel, in both directions.
    /// World to screen: Void Crew places every ping, objective marker and interaction prompt with
    /// WorldToViewportPoint/WorldToScreenPoint, so they sit on top of the objects they belong to.
    /// Screen to world: in-world terminals aim the mouse with ScreenToWorldPoint (and world-space canvases with
    /// ScreenPointToRay), so clicks land where the VR cursor appears on the panel.
    /// Outside rendering the camera transform is the in-game view direction (UUVR only applies the head rotation
    /// while a frame renders), which is exactly the frame the GameCamera-anchored panel is placed in. While the panel
    /// follows the head (head aim, or "UI screen follows your head"), the head's view direction is used instead, so
    /// markers still line up with the world and the crosshair ray goes where you look.
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
            Vector3 local = Quaternion.Inverse(ViewRotation(camera)) * (worldPosition - t.position);
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
            return t.position + ViewRotation(camera) * (ScreenDirection(screen) * screen.z);
        }

        /// <summary>Like Camera.ScreenPointToRay: starts on the near plane and points through the screen position.</summary>
        public static Ray ScreenToRay(Camera camera, Vector3 screen)
        {
            Transform t = camera.transform;
            Vector3 direction = ViewRotation(camera) * ScreenDirection(screen);
            return new Ray(t.position + direction * camera.nearClipPlane, direction.normalized);
        }

        /// <summary>The direction the UI panel is shown in front of: the game camera's, or the head's while the panel follows it.</summary>
        private static Quaternion ViewRotation(Camera camera)
        {
            return HeadAim.PanelFollowsHead ? HeadAim.ViewRotation(camera) : camera.transform.rotation;
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
