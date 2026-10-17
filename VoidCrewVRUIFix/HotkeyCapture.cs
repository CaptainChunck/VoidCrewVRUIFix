using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Lets a hotkey row in the VR tab wait for the next key press. Escape cancels; Backspace or Delete clears the key.
    /// Checked once per frame by the runtime, before the hotkeys, so the key that was just bound doesn't also fire.
    /// </summary>
    internal static class HotkeyCapture
    {
        private const float GiveUpAfter = 10f;

        private static ConfigEntry<Key> _entry;
        private static Action _finished;
        private static Func<bool> _stillWanted;
        private static float _giveUpAt;

        public static bool Waiting { get { return _entry != null; } }

        /// <summary>The frame a key was captured on; hotkeys ignore that frame.</summary>
        public static int CapturedFrame { get; private set; } = -1;

        public static bool IsWaitingFor(ConfigEntry<Key> entry)
        {
            return _entry != null && _entry == entry;
        }

        /// <summary>
        /// Starts waiting for a key for this setting. <paramref name="finished"/> runs when it is set or cancelled;
        /// waiting stops by itself once <paramref name="stillWanted"/> says no (the menu was closed).
        /// </summary>
        public static void Begin(ConfigEntry<Key> entry, Action finished, Func<bool> stillWanted)
        {
            Cancel();
            _entry = entry;
            _finished = finished;
            _stillWanted = stillWanted;
            _giveUpAt = Time.unscaledTime + GiveUpAfter;
        }

        public static void Cancel()
        {
            if (_entry == null) return;
            Finish();
        }

        public static void Update()
        {
            if (_entry == null) return;
            if (Time.unscaledTime > _giveUpAt || !StillWanted())
            {
                Finish();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            foreach (KeyControl key in keyboard.allKeys)
            {
                if (key == null || !key.wasPressedThisFrame) continue;

                Key pressed = key.keyCode;
                if (pressed != Key.Escape)
                {
                    _entry.Value = pressed == Key.Backspace || pressed == Key.Delete ? Key.None : pressed;
                    CapturedFrame = Time.frameCount;
                    Log.Info("Hotkey '" + _entry.Definition.Key + "' set to " + _entry.Value + " (saved to the config).");
                }
                Finish();
                return;
            }
        }

        public static string KeyName(Key key)
        {
            return key == Key.None ? "NONE" : key.ToString().ToUpperInvariant();
        }

        private static bool StillWanted()
        {
            try
            {
                return _stillWanted == null || _stillWanted();
            }
            catch
            {
                return false;
            }
        }

        private static void Finish()
        {
            Action finished = _finished;
            _entry = null;
            _finished = null;
            _stillWanted = null;
            if (finished == null) return;
            try
            {
                finished();
            }
            catch (Exception e)
            {
                Log.Failure("Updating the VR tab hotkey row", e);
            }
        }
    }
}
