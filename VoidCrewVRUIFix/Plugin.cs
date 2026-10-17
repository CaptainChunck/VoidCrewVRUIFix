using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// BepInEx entry point. Loads after UUVR (when it is installed), applies the game patches and starts the runtime.
    /// Each start-up step is guarded: if one fails on a player's PC (a different BepInEx, UUVR or game version), the
    /// error is logged with what failed and the rest of the plugin still starts.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(UuvrCompat.UuvrGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.user.voidcrew.vruifix";
        public const string Name = "Void Crew VR UI Fix";
        public const string Version = "3.10.0";

        private GameObject _runtime;
        private bool _running;

        private void Awake()
        {
            Log.Init(Logger);
            try
            {
                Settings.Bind(Config);
            }
            catch (Exception e)
            {
                Log.Error(Name + " " + Version + " could not read its settings file (" + Config.ConfigFilePath + "), so it is not running. " +
                          "Delete that file and start the game again. Details: " + e);
                return;
            }

            if (!Settings.Enabled.Value)
            {
                Log.Info(Name + " " + Version + " is turned off in its config (General > Enabled).");
                return;
            }

            Step("Reading UUVR and the plugin settings", () =>
            {
                UuvrCompat.Resolve();
                Log.Info(Name + " " + Version + " | Unity " + Application.unityVersion + " | " + SystemInfo.graphicsDeviceType +
                         " | UUVR " + UuvrCompat.Version + (UuvrCompat.Installed ? " (VR API " + UuvrCompat.VrApi + ", camera tracking " + UuvrCompat.CameraTracking + ")" : "") +
                         " | BepInEx " + typeof(BaseUnityPlugin).Assembly.GetName().Version + ", Harmony " + typeof(Harmony).Assembly.GetName().Version);
                Log.Info("VR settings loaded from " + Config.ConfigFilePath + ": " + Settings.Describe() + ".");
                if (!UuvrCompat.Installed)
                {
                    Log.Warn("UUVR is not installed, so the game will not run in VR and this plugin will stay idle. Install UUVR for Void Crew with Rai Pal.");
                }
            });

            Step("Applying the game patches", () => GamePatches.Apply(new Harmony(Guid)));
            Step("Listing the hotkeys", () => Log.Info(string.Format("Hotkeys: {0} recentre UI, {1} diagnostics, {2} switch UI display, {3} flip UI, {4} monitor picture on/off.",
                Settings.KeyRecenter.Value, Settings.KeyDiagnostics.Value, Settings.KeyDisplayMode.Value, Settings.KeyFlip.Value, Settings.KeyDesktopView.Value)));

            _running = Step("Starting the VR UI runtime", CreateRuntime);
            if (_running) Log.Info(Name + " " + Version + " is running.");
        }

        private void Update()
        {
            // UUVR's log shows persistent objects being destroyed once during start-up; if that hits ours, rebuild it.
            if (_running && _runtime == null)
            {
                Log.Warn("The plugin's runtime object was destroyed by the game; recreating it.");
                _running = Step("Restarting the VR UI runtime", CreateRuntime);
            }
        }

        /// <summary>Runs one start-up step; a failure is logged in full and the next steps still run.</summary>
        private static bool Step(string what, Action step)
        {
            try
            {
                step();
                return true;
            }
            catch (Exception e)
            {
                Log.Error(what + " failed, so part of the plugin may not work. Please send BepInEx\\LogOutput.log with a bug report. Details: " + e);
                return false;
            }
        }

        private void CreateRuntime()
        {
            _runtime = new GameObject("VoidCrewVRUIFix");
            DontDestroyOnLoad(_runtime);
            _runtime.hideFlags = HideFlags.HideAndDontSave;
            _runtime.AddComponent<VrUiRuntime>();
        }
    }
}
