using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Keeps terminal screens on their terminals while you use them. 0.75 s after you start using a terminal, the game
    /// attaches the terminal's screen to the camera, so it stays perfectly still on a monitor. In VR, UUVR turns the
    /// camera with your head, so the attached screen turned with it: it floated in front of your eyes wherever you
    /// looked and slid through the terminal and the walls around it. While VR is on, the screen now stays where it is,
    /// and mouse clicks are mapped onto it in a way that doesn't need it attached to the camera.
    /// </summary>
    internal static class TerminalScreens
    {
        private const float GameDelay = 0.75f;

        private static bool _resolved;
        private static MethodInfo _recalculateFov;
        private static FieldInfo _navigatableUi;
        private static bool _screenSizeResolved;
        private static FieldInfo _panelWidth;
        private static FieldInfo _panelHeight;

        public static int KeptInWorld { get; private set; }

        public static bool Applies
        {
            get { return XrState.IsActive && Settings.KeepTerminalScreensInWorld.Value; }
        }

        /// <summary>The game's TerminalScreen coroutine, without attaching the screen to the camera.</summary>
        public static IEnumerator EnterTerminalScreen(MonoBehaviour terminal)
        {
            Kept(terminal);
            Resolve(terminal.GetType());
            Run("Updating the terminal camera's field of view", () =>
            {
                if (_recalculateFov != null) _recalculateFov.Invoke(terminal, null);
            });

            yield return new WaitForSeconds(GameDelay);

            Run("Starting keyboard navigation on the terminal", () =>
            {
                var navigatable = _navigatableUi != null ? _navigatableUi.GetValue(terminal) as NavigatableUI : null;
                if (navigatable != null) navigatable.OnEnterUI();
            });
        }

        /// <summary>The game's ship-terminal coroutine only attaches the screen to the camera, so nothing is left to do.</summary>
        public static IEnumerator EnterShipTerminal(MonoBehaviour terminal)
        {
            Kept(terminal);
            yield break;
        }

        /// <summary>
        /// The game's WorldSpaceUI.TransformPointerEvent, which turns a mouse position into a position on the terminal
        /// screen. The game's version moves the camera to the world origin for the calculation and back, which only
        /// works when the screen is attached to the camera and moves with it. This does the same calculation relative
        /// to the camera instead, so it works for a screen that stays on its terminal. Returns false if it can't.
        /// </summary>
        public static bool TransformPointerEvent(MonoBehaviour worldSpaceUi, PointerEventData eventData)
        {
            if (!ResolveScreenSize(worldSpaceUi.GetType())) return false;
            CinemachineBrainUpdater brain = CinemachineBrainUpdater.Instance;
            Camera camera = brain != null ? brain.GetCamera() : null;
            if (camera == null) return true;

            Transform cameraTransform = camera.transform;
            Transform screen = worldSpaceUi.transform;
            Vector3 pointer = eventData.position;
            pointer.z = 1f;
            Vector3 direction = camera.ScreenToWorldPoint(pointer) - cameraTransform.position;
            Vector3 screenCentre = screen.position - cameraTransform.position;

            // Camera-relative, like the game's version, so large world coordinates don't cost precision.
            var ray = new Ray(Vector3.zero, direction);
            float enter;
            if (!new Plane(screen.forward, screenCentre).Raycast(ray, out enter)) return true;

            Vector3 local = screen.InverseTransformVector(ray.GetPoint(enter) - screenCentre);
            local.x += 0.5f;
            local.y -= 0.5f;
            local = Vector3.Scale(local, new Vector3((int)_panelWidth.GetValue(worldSpaceUi), (int)_panelHeight.GetValue(worldSpaceUi), 1f));
            local.y += Screen.height;

            eventData.position = local;
            RaycastResult raycast = eventData.pointerCurrentRaycast;
            raycast.screenPosition = local;
            eventData.pointerCurrentRaycast = raycast;
            raycast = eventData.pointerPressRaycast;
            raycast.screenPosition = local;
            eventData.pointerPressRaycast = raycast;
            return true;
        }

        private static bool ResolveScreenSize(Type worldSpaceUiType)
        {
            if (!_screenSizeResolved)
            {
                _screenSizeResolved = true;
                _panelWidth = AccessTools.Field(worldSpaceUiType, "_panelWidth");
                _panelHeight = AccessTools.Field(worldSpaceUiType, "_panelHeight");
                if (_panelWidth == null || _panelHeight == null || _panelWidth.FieldType != typeof(int) || _panelHeight.FieldType != typeof(int))
                {
                    _panelWidth = null;
                    _panelHeight = null;
                    Log.Warn("The terminal screen size fields were not found (the game may have been updated); terminal clicks use the game's own calculation.");
                }
            }
            return _panelWidth != null;
        }

        private static void Kept(MonoBehaviour terminal)
        {
            KeptInWorld++;
            Log.InfoOnce("terminal-screens", "Terminal screens stay on their terminals in VR (the game attaches them to the camera, which made them follow your head).");
            Log.Verbose("Kept the screen of terminal '" + (terminal != null ? terminal.name : "?") + "' in the world.");
        }

        private static void Resolve(Type terminalType)
        {
            if (_resolved) return;
            _resolved = true;
            _recalculateFov = AccessTools.Method(terminalType, "RecalculateFov");
            _navigatableUi = AccessTools.Field(terminalType, "navigatableUI");
            if (_recalculateFov == null || _navigatableUi == null)
            {
                Log.Warn("Some terminal screen internals were not found (the game may have been updated); terminals may not respond to keyboard navigation in VR.");
            }
        }

        private static void Run(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Failure(what, e);
            }
        }
    }
}
