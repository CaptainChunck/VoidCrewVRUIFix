using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Testing aid: lets you play while SteamVR's "VR View" window is the focused window (handy with no headset).
    /// Windows only sends keyboard and mouse clicks to the focused window, so while VR View is in front the plugin
    /// reads the keys and mouse buttons itself and passes them to the game. When the game wants mouse-look, the
    /// mouse is also turned into look movement and kept inside the VR View window. Does nothing in any other case.
    /// </summary>
    internal sealed class VrViewInput
    {
        private const int VkLeftButton = 0x01;
        private const int VkRightButton = 0x02;
        private const int VkMiddleButton = 0x04;

        private static readonly KeyValuePair<Key, int>[] KeyMap = BuildKeyMap();

        private IntPtr _checkedWindow;
        private bool _checkedWindowIsVrView;
        private bool _active;
        private InputSettings.BackgroundBehavior _previousBehaviour;
        private bool _lookCaptured;

        public bool Active { get { return _active; } }

        public void Update()
        {
            bool shouldRun = Settings.ControlFromVrView.Value && !Application.isFocused && IsVrViewInFront();
            if (shouldRun != _active)
            {
                if (shouldRun) Start();
                else Stop();
            }
            if (!_active) return;

            try
            {
                SendKeyboard(false);
                SendMouse(false);
            }
            catch (Exception e)
            {
                Log.Failure("Passing VR View input to the game", e);
            }
        }

        /// <summary>Stops passing input and restores Unity's normal focus handling (VR stopped or plugin unloading).</summary>
        public void Shutdown()
        {
            if (_active) Stop();
        }

        private void Start()
        {
            _active = true;
            // Unity normally ignores keyboard and mouse while the game window isn't focused.
            _previousBehaviour = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Log.Info("SteamVR's VR View window is focused: passing your keyboard and mouse to the game (Testing > Control From VR View).");
        }

        private void Stop()
        {
            _active = false;
            _lookCaptured = false;
            try
            {
                // Release everything that was held, so no key or button stays stuck down.
                SendKeyboard(true);
                SendMouse(true);
            }
            catch (Exception e)
            {
                Log.Failure("Releasing VR View input", e);
            }
            InputSystem.settings.backgroundBehavior = _previousBehaviour;
            Log.Verbose("Stopped passing VR View input to the game.");
        }

        private static void SendKeyboard(bool releaseAll)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            InputEventPtr eventPtr;
            using (StateEvent.From(keyboard, out eventPtr))
            {
                foreach (KeyValuePair<Key, int> mapping in KeyMap)
                {
                    bool down = !releaseAll && IsDown(mapping.Value);
                    keyboard[mapping.Key].WriteValueIntoEvent(down ? 1f : 0f, eventPtr);
                }
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private void SendMouse(bool releaseAll)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 look = Vector2.zero;
            if (!releaseAll && UnityEngine.Cursor.lockState == CursorLockMode.Locked)
            {
                look = CaptureLookMovement();
            }
            else
            {
                _lookCaptured = false;
            }

            InputEventPtr eventPtr;
            using (StateEvent.From(mouse, out eventPtr))
            {
                mouse.leftButton.WriteValueIntoEvent(!releaseAll && IsDown(VkLeftButton) ? 1f : 0f, eventPtr);
                mouse.rightButton.WriteValueIntoEvent(!releaseAll && IsDown(VkRightButton) ? 1f : 0f, eventPtr);
                mouse.middleButton.WriteValueIntoEvent(!releaseAll && IsDown(VkMiddleButton) ? 1f : 0f, eventPtr);
                mouse.delta.WriteValueIntoEvent(look, eventPtr);
                mouse.scroll.WriteValueIntoEvent(Vector2.zero, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        /// <summary>
        /// Mouse-look: measures how far the pointer moved since last frame, then puts it back in the middle of the
        /// VR View window so it never runs into the edge of the screen.
        /// </summary>
        private Vector2 CaptureLookMovement()
        {
            NativeRect window;
            NativePoint pointer;
            if (!GetWindowRect(GetForegroundWindow(), out window) || !GetCursorPos(out pointer)) return Vector2.zero;

            int centreX = (window.Left + window.Right) / 2;
            int centreY = (window.Top + window.Bottom) / 2;
            Vector2 movement = Vector2.zero;
            if (_lookCaptured)
            {
                // Screen y grows downwards; Unity's mouse y grows upwards.
                movement = new Vector2(pointer.X - centreX, centreY - pointer.Y);
            }
            SetCursorPos(centreX, centreY);
            _lookCaptured = true;
            return movement;
        }

        private bool IsVrViewInFront()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            if (window == _checkedWindow) return _checkedWindowIsVrView;

            _checkedWindow = window;
            _checkedWindowIsVrView = false;
            try
            {
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                using (Process process = Process.GetProcessById((int)processId))
                {
                    _checkedWindowIsVrView = string.Equals(process.ProcessName, "vrcompositor", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                // The process may have exited between the two calls; treat it as not VR View.
            }
            return _checkedWindowIsVrView;
        }

        private static bool IsDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private static KeyValuePair<Key, int>[] BuildKeyMap()
        {
            var map = new List<KeyValuePair<Key, int>>();
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                map.Add(new KeyValuePair<Key, int>((Key)Enum.Parse(typeof(Key), letter.ToString()), letter));
            }
            for (int digit = 0; digit <= 9; digit++)
            {
                map.Add(new KeyValuePair<Key, int>((Key)Enum.Parse(typeof(Key), "Digit" + digit), 0x30 + digit));
                map.Add(new KeyValuePair<Key, int>((Key)Enum.Parse(typeof(Key), "Numpad" + digit), 0x60 + digit));
            }
            for (int function = 1; function <= 12; function++)
            {
                map.Add(new KeyValuePair<Key, int>((Key)Enum.Parse(typeof(Key), "F" + function), 0x6F + function));
            }

            map.Add(new KeyValuePair<Key, int>(Key.Space, 0x20));
            map.Add(new KeyValuePair<Key, int>(Key.Enter, 0x0D));
            map.Add(new KeyValuePair<Key, int>(Key.Escape, 0x1B));
            map.Add(new KeyValuePair<Key, int>(Key.Tab, 0x09));
            map.Add(new KeyValuePair<Key, int>(Key.Backspace, 0x08));
            map.Add(new KeyValuePair<Key, int>(Key.LeftShift, 0xA0));
            map.Add(new KeyValuePair<Key, int>(Key.RightShift, 0xA1));
            map.Add(new KeyValuePair<Key, int>(Key.LeftCtrl, 0xA2));
            map.Add(new KeyValuePair<Key, int>(Key.RightCtrl, 0xA3));
            map.Add(new KeyValuePair<Key, int>(Key.LeftAlt, 0xA4));
            map.Add(new KeyValuePair<Key, int>(Key.RightAlt, 0xA5));
            map.Add(new KeyValuePair<Key, int>(Key.LeftArrow, 0x25));
            map.Add(new KeyValuePair<Key, int>(Key.UpArrow, 0x26));
            map.Add(new KeyValuePair<Key, int>(Key.RightArrow, 0x27));
            map.Add(new KeyValuePair<Key, int>(Key.DownArrow, 0x28));
            map.Add(new KeyValuePair<Key, int>(Key.Insert, 0x2D));
            map.Add(new KeyValuePair<Key, int>(Key.Delete, 0x2E));
            map.Add(new KeyValuePair<Key, int>(Key.Home, 0x24));
            map.Add(new KeyValuePair<Key, int>(Key.End, 0x23));
            map.Add(new KeyValuePair<Key, int>(Key.PageUp, 0x21));
            map.Add(new KeyValuePair<Key, int>(Key.PageDown, 0x22));
            map.Add(new KeyValuePair<Key, int>(Key.CapsLock, 0x14));
            map.Add(new KeyValuePair<Key, int>(Key.Backquote, 0xC0));
            map.Add(new KeyValuePair<Key, int>(Key.Minus, 0xBD));
            map.Add(new KeyValuePair<Key, int>(Key.Equals, 0xBB));
            map.Add(new KeyValuePair<Key, int>(Key.LeftBracket, 0xDB));
            map.Add(new KeyValuePair<Key, int>(Key.RightBracket, 0xDD));
            map.Add(new KeyValuePair<Key, int>(Key.Backslash, 0xDC));
            map.Add(new KeyValuePair<Key, int>(Key.Semicolon, 0xBA));
            map.Add(new KeyValuePair<Key, int>(Key.Quote, 0xDE));
            map.Add(new KeyValuePair<Key, int>(Key.Comma, 0xBC));
            map.Add(new KeyValuePair<Key, int>(Key.Period, 0xBE));
            map.Add(new KeyValuePair<Key, int>(Key.Slash, 0xBF));
            map.Add(new KeyValuePair<Key, int>(Key.NumpadPlus, 0x6B));
            map.Add(new KeyValuePair<Key, int>(Key.NumpadMinus, 0x6D));
            map.Add(new KeyValuePair<Key, int>(Key.NumpadMultiply, 0x6A));
            map.Add(new KeyValuePair<Key, int>(Key.NumpadDivide, 0x6F));
            map.Add(new KeyValuePair<Key, int>(Key.NumpadPeriod, 0x6E));
            return map.ToArray();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    }
}
