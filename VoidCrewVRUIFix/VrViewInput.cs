using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Testing aid for SteamVR's null (no headset) driver: lets you play while SteamVR's "VR View" window is the
    /// focused window. Windows only sends keyboard and mouse input to the focused window, so while VR View is in
    /// front the plugin reads the keys, mouse buttons and pointer itself and passes them to the game. When the game
    /// wants mouse-look, the mouse is turned into look movement and kept inside the VR View window.
    /// Two parts of the game stop listening the moment its window loses focus, so both are kept going while this
    /// setting is on: Unity's input system switches the keyboard and mouse off, and the character controller
    /// (Opsive) stops reading mouse-look. Does nothing while the setting is off.
    /// </summary>
    internal sealed class VrViewInput
    {
        private const int VkLeftButton = 0x01;
        private const int VkRightButton = 0x02;
        private const int VkMiddleButton = 0x04;
        private const float CharacterInputSearchInterval = 1f;

        private static readonly KeyValuePair<Key, int>[] KeyMap = BuildKeyMap();
        private static readonly object BoxedTrue = true;

        private IntPtr _checkedWindow;
        private bool _checkedWindowIsVrView;
        private bool _active;
        private bool _keepingDevices;
        private InputSettings.BackgroundBehavior _previousBehaviour;
        private bool _wasFocused = true;
        private bool _lookCaptured;
        private IntPtr _gameWindow;

        private bool _characterInputResolved;
        private Type _characterInputType;
        private FieldInfo _characterInputFocus;
        private UnityEngine.Object[] _characterInputs = new UnityEngine.Object[0];
        private float _nextCharacterInputSearch;

        public bool Active { get { return _active; } }

        public void Update()
        {
            bool enabled = Settings.ControlFromVrView.Value;
            if (enabled != _keepingDevices) KeepDevicesWhileUnfocused(enabled);
            if (!enabled)
            {
                if (_active) Stop();
                return;
            }

            bool focused = Application.isFocused;
            if (focused) _gameWindow = GetForegroundWindow();

            bool shouldRun = !focused && IsVrViewInFront();
            if (shouldRun != _active)
            {
                if (shouldRun) Start();
                else Stop();
            }
            else if (_wasFocused && !focused && !_active)
            {
                // The game lost focus to another program. Unity no longer clears held keys by itself while this
                // setting is on, so let go of everything rather than leave a key stuck down.
                ReleaseAll();
            }
            _wasFocused = focused;
            if (!_active) return;

            try
            {
                EnableDevice(Keyboard.current);
                EnableDevice(Mouse.current);
                KeepCharacterLookActive();
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
            if (_keepingDevices) KeepDevicesWhileUnfocused(false);
        }

        private void KeepDevicesWhileUnfocused(bool keep)
        {
            _keepingDevices = keep;
            if (keep)
            {
                // Unity normally switches the keyboard and mouse off as soon as the game window loses focus, and
                // switching the setting afterwards doesn't turn them back on. So it is changed before that happens.
                _previousBehaviour = InputSystem.settings.backgroundBehavior;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            }
            else
            {
                InputSystem.settings.backgroundBehavior = _previousBehaviour;
            }
        }

        private void Start()
        {
            _active = true;
            Log.Info("SteamVR's VR View window is focused: passing your keyboard and mouse to the game (Testing > Control From VR View).");
        }

        private void Stop()
        {
            _active = false;
            _lookCaptured = false;
            ReleaseAll();
            Log.Verbose("Stopped passing VR View input to the game.");
        }

        /// <summary>Releases every key and button, so nothing stays stuck down.</summary>
        private void ReleaseAll()
        {
            try
            {
                SendKeyboard(true);
                SendMouse(true);
            }
            catch (Exception e)
            {
                Log.Failure("Releasing VR View input", e);
            }
        }

        private static void EnableDevice(InputDevice device)
        {
            // If the window lost focus before this setting was on, Unity has already switched the device off.
            if (device != null && !device.enabled) InputSystem.EnableDevice(device);
        }

        /// <summary>
        /// The character controller ignores mouse-look while the game window isn't focused, and only listens again
        /// when a menu closes. That was why turning the camera stopped working at random.
        /// </summary>
        private void KeepCharacterLookActive()
        {
            if (!_characterInputResolved)
            {
                _characterInputResolved = true;
                _characterInputType = AccessTools.TypeByName("Opsive.Shared.Input.PlayerInput");
                _characterInputFocus = _characterInputType != null ? AccessTools.Field(_characterInputType, "m_Focus") : null;
                if (_characterInputFocus == null || _characterInputFocus.FieldType != typeof(bool))
                {
                    _characterInputFocus = null;
                    Log.WarnOnce("vrview-look", "Control From VR View: the game's character input was not found, so mouse-look may stop while VR View is focused.");
                }
            }
            if (_characterInputFocus == null) return;

            if (Time.unscaledTime >= _nextCharacterInputSearch)
            {
                _nextCharacterInputSearch = Time.unscaledTime + CharacterInputSearchInterval;
                _characterInputs = UnityEngine.Object.FindObjectsOfType(_characterInputType);
            }
            foreach (UnityEngine.Object input in _characterInputs)
            {
                if (input != null) _characterInputFocus.SetValue(input, BoxedTrue);
            }
        }

        private static void SendKeyboard(bool releaseAll)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.enabled) return;

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
            if (mouse == null || !mouse.enabled) return;

            bool mouseLook = !releaseAll && UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            Vector2 look = mouseLook ? CaptureLookMovement() : Vector2.zero;
            if (!mouseLook) _lookCaptured = false;

            InputEventPtr eventPtr;
            using (StateEvent.From(mouse, out eventPtr))
            {
                // Windows sends pointer moves to the window under the pointer (VR View), so the game would never
                // see the pointer move over its menus. Pass where it is, relative to the game window.
                Vector2 position;
                if (!releaseAll && !mouseLook && TryGetPointerInGameWindow(out position))
                {
                    mouse.position.WriteValueIntoEvent(position, eventPtr);
                }
                mouse.leftButton.WriteValueIntoEvent(!releaseAll && IsDown(VkLeftButton) ? 1f : 0f, eventPtr);
                mouse.rightButton.WriteValueIntoEvent(!releaseAll && IsDown(VkRightButton) ? 1f : 0f, eventPtr);
                mouse.middleButton.WriteValueIntoEvent(!releaseAll && IsDown(VkMiddleButton) ? 1f : 0f, eventPtr);
                mouse.delta.WriteValueIntoEvent(look, eventPtr);
                mouse.scroll.WriteValueIntoEvent(Vector2.zero, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        /// <summary>The pointer's position in the game's own screen coordinates (origin bottom left).</summary>
        private bool TryGetPointerInGameWindow(out Vector2 position)
        {
            position = Vector2.zero;
            if (_gameWindow == IntPtr.Zero)
            {
                using (Process process = Process.GetCurrentProcess()) _gameWindow = process.MainWindowHandle;
            }

            NativePoint pointer;
            NativeRect client;
            if (_gameWindow == IntPtr.Zero || !GetCursorPos(out pointer) || !ScreenToClient(_gameWindow, ref pointer) ||
                !GetClientRect(_gameWindow, out client))
            {
                return false;
            }

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0) return false;

            float x = Mathf.Clamp(pointer.X, 0, width - 1) * (float)Screen.width / width;
            float y = (height - 1 - Mathf.Clamp(pointer.Y, 0, height - 1)) * (float)Screen.height / height;
            position = new Vector2(x, y);
            return true;
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

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    }
}
