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
    /// and mouse clicks are mapped onto it in a way that doesn't need it attached to the camera. While the terminal is
    /// in use, the VR UI panel (the mouse pointer and the button hints) sits on the terminal's screen, at its depth and
    /// size, so the pointer is on the screen itself instead of floating in front of or behind it.
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
        private static Type _worldSpaceUiType;
        private static Component _activeScreen;
        private static bool _playerLookupDone;
        private static PropertyInfo _localPlayer;
        private static PropertyInfo _isInTerminal;

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
            Run("Finding the terminal's screen", () => _activeScreen = FindScreen(terminal, "worldSpaceUI"));
        }

        /// <summary>
        /// The game's ship-terminal coroutine only attaches the screen to the camera, so all that is left is noting
        /// which screen is in use once the camera has arrived (the same 0.75 s).
        /// </summary>
        public static IEnumerator EnterShipTerminal(MonoBehaviour terminal)
        {
            Kept(terminal);
            yield return new WaitForSeconds(GameDelay);
            Run("Finding the ship terminal's screen", () => _activeScreen = FindScreen(terminal, "worldUiPanel"));
        }

        /// <summary>
        /// Once per frame, before rendering, with the game camera's own pose (no head rotation): while a terminal is in
        /// use, puts the VR UI panel on its screen; otherwise puts the panel back.
        /// </summary>
        public static void PlacePanel(Vector3 cameraPosition, Quaternion cameraRotation)
        {
            Transform screen = Settings.SnapUiToTerminalScreens.Value && Settings.Anchor.Value == PanelAnchor.GameCamera ? CurrentScreen() : null;
            if (screen == null)
            {
                if (PanelLayout.LeaveTerminal()) Log.Verbose("The VR UI panel is back where the settings put it.");
                return;
            }

            // The screen is a 1 x 1 quad scaled to its size; its corners are at local (+-0.5, +-0.5).
            Quaternion toCamera = Quaternion.Inverse(cameraRotation);
            Vector3 centre = toCamera * (screen.position - cameraPosition);
            float halfWidth = (screen.TransformPoint(0.5f, 0f, 0f) - screen.position).magnitude;
            float halfHeight = (screen.TransformPoint(0f, 0.5f, 0f) - screen.position).magnitude;
            if (centre.z < 0.15f || centre.z > 6f || halfWidth < 0.01f || halfHeight < 0.01f)
            {
                PanelLayout.LeaveTerminal();
                return;
            }

            // The panel has the game window's shape; make it cover the whole terminal screen.
            float tanHalfWidth = Mathf.Max(halfWidth / centre.z, halfHeight / centre.z * PanelLayout.Aspect);
            float widthDegrees = Mathf.Clamp(2f * Mathf.Atan(tanHalfWidth) * Mathf.Rad2Deg, 20f, 140f);
            if (PanelLayout.PlaceOnTerminal(centre.z, widthDegrees, centre.y))
            {
                Log.InfoOnce("terminal-panel", "While you use a terminal, the VR UI panel (mouse pointer and button hints) sits on the terminal's screen.");
                Log.Verbose(string.Format("VR UI panel moved onto the terminal screen: {0:0.00} m away, {1:0}° wide.", centre.z, widthDegrees));
            }
        }

        /// <summary>The screen of the terminal in use, or null once the player has left it.</summary>
        private static Transform CurrentScreen()
        {
            if (_activeScreen == null) return null;
            if (!_activeScreen.gameObject.activeInHierarchy || !PlayerIsInTerminal())
            {
                _activeScreen = null;
                return null;
            }
            return _activeScreen.transform;
        }

        /// <summary>The game's own "using a terminal" flag on the local player (read by name; the player class is networked).</summary>
        private static bool PlayerIsInTerminal()
        {
            if (!_playerLookupDone)
            {
                _playerLookupDone = true;
                Type localPlayer = AccessTools.TypeByName("CG.Game.Player.LocalPlayer");
                _localPlayer = localPlayer != null ? AccessTools.Property(localPlayer, "I") : null;
                _isInTerminal = localPlayer != null ? AccessTools.Property(localPlayer, "IsInTerminal") : null;
                if (_localPlayer == null || _isInTerminal == null)
                {
                    Log.Warn("The game's terminal flag was not found (the game may have been updated); the VR UI panel won't move onto terminal screens.");
                }
            }
            if (_localPlayer == null || _isInTerminal == null) return false;

            var player = _localPlayer.GetValue(null, null) as UnityEngine.Object;
            return player != null && (bool)_isInTerminal.GetValue(player, null);
        }

        /// <summary>The terminal's screen: the game's WorldSpaceUI component on (or under) the given field's object.</summary>
        private static Component FindScreen(MonoBehaviour terminal, string fieldName)
        {
            if (terminal == null) return null;
            if (_worldSpaceUiType == null) _worldSpaceUiType = AccessTools.TypeByName("CG.Client.UI.Terminals.WorldSpaceUI");
            FieldInfo field = AccessTools.Field(terminal.GetType(), fieldName);
            var holder = field != null ? field.GetValue(terminal) as Component : null;
            if (holder == null || _worldSpaceUiType == null) return null;
            Component screen = holder.GetComponentInChildren(_worldSpaceUiType, true);
            if (screen == null) Log.Verbose("Terminal '" + terminal.name + "' has no world-space screen; the VR UI panel stays where it is.");
            return screen;
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
