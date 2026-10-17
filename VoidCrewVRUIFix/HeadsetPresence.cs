using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Whether someone is wearing the headset. SteamVR reports this from the headset's proximity sensor (and
    /// movement); OpenXR runtimes report it when they support it. A headset that says "not worn" only counts as taken
    /// off after a short delay, so a moment of stillness on a headset without a sensor doesn't count.
    /// </summary>
    internal static class HeadsetPresence
    {
        private const float RemovedAfter = 2f;

        private static readonly List<UnityEngine.XR.InputDevice> Heads = new List<UnityEngine.XR.InputDevice>();
        private static float _notWornSince = -1f;

        /// <summary>False when the headset can't tell (no sensor support), so nothing should depend on it.</summary>
        public static bool Supported { get; private set; }

        /// <summary>True once the headset has reported "not worn" for a couple of seconds.</summary>
        public static bool Removed { get; private set; }

        public static void Update()
        {
            bool worn;
            if (!TryReadWorn(out worn))
            {
                Supported = false;
                Removed = false;
                _notWornSince = -1f;
                return;
            }

            Supported = true;
            if (worn)
            {
                _notWornSince = -1f;
                Removed = false;
                return;
            }

            if (_notWornSince < 0f) _notWornSince = Time.unscaledTime;
            if (Time.unscaledTime - _notWornSince >= RemovedAfter) Removed = true;
        }

        public static void Reset()
        {
            Supported = false;
            Removed = false;
            _notWornSince = -1f;
        }

        public static string Describe()
        {
            if (!Supported) return "unknown (the headset or VR runtime doesn't report it)";
            return Removed ? "taken off" : "worn";
        }

        private static bool TryReadWorn(out bool worn)
        {
            worn = true;
            Heads.Clear();
            InputDevices.GetDevicesAtXRNode(XRNode.Head, Heads);
            foreach (UnityEngine.XR.InputDevice head in Heads)
            {
                if (head.isValid && head.TryGetFeatureValue(CommonUsages.userPresence, out worn)) return true;
            }
            return false;
        }
    }
}
