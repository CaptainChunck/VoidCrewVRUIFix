using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// BepInEx entry point. Loads after UUVR (when it is installed), applies the game patches and starts the runtime.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(UuvrCompat.UuvrGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.user.voidcrew.vruifix";
        public const string Name = "Void Crew VR UI Fix";
        public const string Version = "3.3.0";

        private GameObject _runtime;

        private void Awake()
        {
            Log.Init(Logger);
            Settings.Bind(Config);
            if (!Settings.Enabled.Value)
            {
                Log.Info(Name + " " + Version + " is turned off in its config (General > Enabled).");
                return;
            }

            UuvrCompat.Resolve();
            Log.Info(Name + " " + Version + " | Unity " + Application.unityVersion + " | " + SystemInfo.graphicsDeviceType +
                     " | UUVR " + UuvrCompat.Version + (UuvrCompat.Installed ? " (VR API " + UuvrCompat.VrApi + ", camera tracking " + UuvrCompat.CameraTracking + ")" : ""));
            if (!UuvrCompat.Installed)
            {
                Log.Warn("UUVR is not installed, so the game will not run in VR and this plugin will stay idle. Install UUVR for Void Crew with Rai Pal.");
            }

            GamePatches.Apply(new Harmony(Guid));
            Log.Info(string.Format("Hotkeys: {0} recentre UI, {1} diagnostics, {2} switch UI display, {3} flip UI.",
                Settings.KeyRecenter.Value, Settings.KeyDiagnostics.Value, Settings.KeyDisplayMode.Value, Settings.KeyFlip.Value));

            CreateRuntime();
        }

        private void Update()
        {
            // UUVR's log shows persistent objects being destroyed once during start-up; if that hits ours, rebuild it.
            if (_runtime == null && Settings.Enabled.Value)
            {
                Log.Warn("The plugin's runtime object was destroyed by the game; recreating it.");
                CreateRuntime();
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
