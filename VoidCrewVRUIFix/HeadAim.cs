using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Aiming with your head. UUVR only turns the picture with your head; the game itself still aims with the mouse,
    /// so the crosshair sat in front of the game camera instead of in the middle of your view. While you play (no menu
    /// or terminal open) and "Aim With Your Head" is on, the UI screen follows your head, so the crosshair is in the
    /// middle of your view, and weapons, tools and interactions aim along the line from your eyes through it.
    /// Your body still turns with the mouse only.
    /// </summary>
    internal static class HeadAim
    {
        private static readonly List<UnityEngine.XR.InputDevice> Heads = new List<UnityEngine.XR.InputDevice>();

        // While a game method runs with the camera turned to your view (see TurnCamera), the view as it was before.
        private static bool _turned;
        private static Quaternion _turnedView;

        /// <summary>True while the UI screen follows the head: always with "UI screen follows your head", and while playing with head aim.</summary>
        public static bool PanelFollowsHead { get; private set; }

        /// <summary>True while weapons, tools and interactions aim where you look.</summary>
        public static bool Aiming { get; private set; }

        /// <summary>Once per frame: whether the player is playing (no menu or terminal) decides both.</summary>
        public static void Update(bool vrActive, bool menuOpen)
        {
            Aiming = vrActive && Settings.AimWithHead.Value && !menuOpen && !PanelLayout.OnTerminalScreen;
            PanelFollowsHead = vrActive && (Settings.Anchor.Value == PanelAnchor.Head || Aiming);
        }

        public static void Stop()
        {
            Aiming = false;
            PanelFollowsHead = false;
        }

        /// <summary>
        /// Which way the eyes look in the world: the game camera turned by the headset, exactly as UUVR draws it (from
        /// its tracking camera, which also includes its "keep horizon level" option), or worked out from the headset.
        /// </summary>
        public static Quaternion ViewRotation(Camera camera)
        {
            // UUVR's tracking camera hangs under the game camera, so while that is turned it reads turned twice.
            if (_turned) return _turnedView;
            Quaternion rotation;
            if (UuvrCompat.TryGetViewRotation(camera, out rotation)) return rotation;
            return camera.transform.rotation * HeadsetRotation();
        }

        /// <summary>
        /// The world rotation of the line from the eyes through the crosshair. The game draws its crosshair in the middle
        /// of the screen, which on the UI screen is "UI screen height" above the middle of your view.
        /// </summary>
        public static Quaternion AimRotation(Camera camera)
        {
            var throughCrosshair = new Vector3(0f, PanelLayout.HeightOffset / PanelLayout.Distance, 1f);
            return ViewRotation(camera) * Quaternion.LookRotation(throughCrosshair);
        }

        /// <summary>
        /// For game code that aims along the game camera's forward direction ("press E" interactions, pings): turns the
        /// camera to the line through the crosshair while that code runs. Returns the rotation to put back with
        /// TurnCameraBack, or null if nothing was turned (not aiming with the head, or already turned).
        /// </summary>
        public static Quaternion? TurnCamera()
        {
            if (!Aiming || _turned) return null;
            Camera camera = MarkerAlignment.PlayerCamera;
            if (camera == null) return null;

            Transform cameraTransform = camera.transform;
            Quaternion aim = AimRotation(camera);
            _turnedView = ViewRotation(camera);
            _turned = true;
            Quaternion original = cameraTransform.rotation;
            cameraTransform.rotation = aim;
            return original;
        }

        public static void TurnCameraBack(Quaternion? original)
        {
            if (!original.HasValue) return;
            _turned = false;
            Camera camera = MarkerAlignment.PlayerCamera;
            if (camera != null) camera.transform.rotation = original.Value;
        }

        private static Quaternion HeadsetRotation()
        {
            Heads.Clear();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, Heads);
            foreach (UnityEngine.XR.InputDevice head in Heads)
            {
                Quaternion rotation;
                if (head.isValid && (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation) ||
                                     head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation)))
                {
                    return rotation;
                }
            }
            return Quaternion.identity;
        }
    }
}
