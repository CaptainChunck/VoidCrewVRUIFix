using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Keeps terminal screens on their terminals while you use them. 0.75 s after you start using a terminal, the game
    /// attaches the terminal's screen to the camera, so it stays perfectly still on a monitor. In VR, UUVR turns the
    /// camera with your head, so the attached screen turned with it: it floated in front of your eyes wherever you
    /// looked and slid through the terminal and the walls around it. While VR is on, the screen now stays where it is.
    /// </summary>
    internal static class TerminalScreens
    {
        private const float GameDelay = 0.75f;

        private static bool _resolved;
        private static MethodInfo _recalculateFov;
        private static FieldInfo _navigatableUi;

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
