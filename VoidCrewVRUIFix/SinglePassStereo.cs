using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Experimental single-pass stereo for SteamVR: HDRP draws both eyes in one pass instead of rendering the whole
    /// game twice. SteamVR's Unity plugin (shipped with UUVR) reads its settings from UUVR's
    /// StreamingAssets/SteamVR/OpenVRSettings.asset when VR starts, and that file asks for multi-pass. Unless it was
    /// given settings already, so just before UUVR starts SteamVR, the plugin is handed the same settings as that file
    /// with only the stereo mode changed. Nothing on disk is modified.
    /// </summary>
    internal static class SinglePassStereo
    {
        // StereoRenderingModes in SteamVR's Unity plugin: 0 multi-pass, 1 single-pass instanced.
        private const ushort SinglePassInstanced = 1;

        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();
        private static bool _attempted;
        private static bool _reported;

        /// <summary>True when the settings were handed to SteamVR's plugin this session.</summary>
        public static bool Requested { get; private set; }

        public static string Problem { get; private set; }

        // Must match SteamVR's Unity plugin (UserProjectSettings.cpp in ValveSoftware/unity-xr-plugin).
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct UserDefinedSettings
        {
            public ushort stereoRenderingMode;
            public ushort initializationType;
            public ushort mirrorViewMode;
            [MarshalAs(UnmanagedType.LPStr)] public string editorAppKey;
            [MarshalAs(UnmanagedType.LPStr)] public string actionManifestPath;
            [MarshalAs(UnmanagedType.LPStr)] public string applicationName;
        }

        [DllImport("XRSDKOpenVR", CharSet = CharSet.Auto)]
        private static extern void SetUserDefinedSettings(UserDefinedSettings settings);

        /// <summary>Called just before UUVR starts SteamVR (each time; only the first one counts).</summary>
        public static void BeforeSteamVrStarts()
        {
            if (_attempted || !Settings.SinglePassStereo.Value) return;
            _attempted = true;
            try
            {
                Request();
            }
            catch (Exception e)
            {
                Problem = e.GetType().Name + ": " + e.Message;
                Log.Warn("Could not switch SteamVR to single-pass stereo (" + Problem + "). VR starts normally (multi-pass).");
            }
        }

        private static void Request()
        {
            string path = Path.Combine(Path.Combine(Application.streamingAssetsPath, "SteamVR"), "OpenVRSettings.asset");
            if (!File.Exists(path))
            {
                Problem = "UUVR's SteamVR settings file was not found at " + path;
                Log.Warn("Single-pass stereo is on, but " + Problem + ". VR starts normally (multi-pass).");
                return;
            }

            // Read the file the way SteamVR's plugin does: "Key: value" lines, the manifest path relative to the data folder.
            var settings = new UserDefinedSettings { stereoRenderingMode = SinglePassInstanced, initializationType = 1, mirrorViewMode = 2 };
            foreach (string line in File.ReadAllLines(path))
            {
                string value;
                if (TryRead(line, "InitializationType:", out value)) settings.initializationType = ushort.Parse(value);
                else if (TryRead(line, "MirrorView:", out value)) settings.mirrorViewMode = ushort.Parse(value);
                else if (TryRead(line, "ActionManifestFileRelativeFilePath:", out value))
                {
                    settings.actionManifestPath = Application.dataPath.Replace('/', '\\') + "\\" + value;
                }
            }

            SetUserDefinedSettings(settings);
            Requested = true;
            Log.Info("Single-pass stereo (experimental) requested from SteamVR. If one eye is black or effects look wrong, " +
                     "turn off 'Single-pass stereo' in the VR tab and restart the game.");
        }

        private static bool TryRead(string line, string key, out string value)
        {
            value = null;
            int index = line.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) return false;
            value = line.Substring(index + key.Length).Trim();
            return value.Length > 0;
        }

        /// <summary>Once VR runs: logs whether HDRP really draws both eyes in one pass.</summary>
        public static void ReportOnce(Camera camera)
        {
            if (_reported || !Requested || camera == null) return;
            string mode = DescribeMode(camera);
            if (mode == null) return;
            _reported = true;
            Log.Info("Stereo rendering: " + mode + ".");
        }

        /// <summary>How the running VR display draws the eyes, or null if there is no running display yet.</summary>
        public static string DescribeMode(Camera camera)
        {
            Displays.Clear();
            SubsystemManager.GetInstances(Displays);
            foreach (XRDisplaySubsystem display in Displays)
            {
                if (!display.running) continue;
                int passes = display.GetRenderPassCount();
                if (passes == 0) return null;
                XRDisplaySubsystem.XRRenderPass pass;
                display.GetRenderPass(0, out pass);
                int eyes = pass.GetRenderParameterCount();
                if (passes == 1 && eyes == 2) return "single-pass (both eyes in one pass)" + (Requested ? "" : ", chosen by SteamVR's plugin");
                return "multi-pass (" + passes + " passes, " + eyes + " eye per pass)" +
                       (Requested ? "; single-pass was requested but SteamVR's plugin did not use it" : "");
            }
            return null;
        }
    }
}
