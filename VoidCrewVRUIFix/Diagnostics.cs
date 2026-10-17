using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UIElements;
using UnityEngine.Video;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// The on-demand diagnostics report (Write Diagnostics hotkey, F9 by default). Writes one snapshot of everything
    /// relevant to the VR UI into the BepInEx log and saves the current VR UI texture as a PNG, so a player's bug
    /// report contains what is needed to diagnose it.
    /// </summary>
    internal static class Diagnostics
    {
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();
        private static Type _stateMachineType;
        private static PropertyInfo _currentStateProperty;
        private static UnityEngine.Object _stateMachine;

        public static string SnapshotPath
        {
            get { return Path.Combine(Path.Combine(Application.persistentDataPath, "VoidCrewVRUIFix"), "vr-ui-snapshot.png"); }
        }

        public static void Write(VrUiRuntime runtime)
        {
            var report = new StringBuilder(8192);
            report.AppendLine();
            report.AppendLine("==================== Void Crew VR UI Fix diagnostics ====================");

            Section(report, "Environment");
            Line(report, "Plugin", Plugin.Version + " | Unity " + Application.unityVersion + " | " + SystemInfo.graphicsDeviceType + " | " + SystemInfo.graphicsDeviceName);
            Line(report, "Window", Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode + " | frame " + Time.frameCount +
                                  " | " + (1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f)).ToString("0") + " FPS");
            Line(report, "VR", "active " + XrState.IsActive + ", device '" + XrState.DeviceName + "', eye texture " + XRSettings.eyeTextureWidth + "x" +
                              XRSettings.eyeTextureHeight + ", desktop shows VR view " + XRSettings.showDeviceView);
            Line(report, "UUVR", UuvrCompat.Installed
                ? UuvrCompat.Version + ", VR API " + UuvrCompat.VrApi + ", camera tracking " + UuvrCompat.CameraTracking + ", UUVR UI mode " + UuvrCompat.UiPatchMode +
                  " | UI handling turned off " + UuvrCompat.DisabledUiManagers + "x, helper cameras disabled " + UuvrCompat.DisabledHelperCameras + " (" + UuvrCompat.Mode + ")"
                : "not installed");
            Line(report, "Game state", GameStateName());

            Section(report, "Performance");
            Line(report, "Game", (1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f)).ToString("0") + " FPS (" + (Time.smoothDeltaTime * 1000f).ToString("0.0") +
                                " ms per frame) | VR render scale " + XRSettings.eyeTextureResolutionScale.ToString("0.00"));
            Line(report, "Monitor sync", VrPerformance.DescribeMonitorSync());
            Line(report, "FPS counter", Settings.ShowFpsCounter.Value ? "on, last shown " + runtime.Fps.LastFps + " FPS" : "off");
            Line(report, "Monitor picture", (runtime.PictureShown ? "on" : "off (window left black)") + " | setting " + (Settings.DesktopViewOn.Value ? "on" : "off") +
                                           ", hotkey " + Settings.KeyDesktopView.Value + ", headset " + HeadsetPresence.Describe() +
                                           (Settings.DesktopViewWhenHeadsetOff.Value ? " (picture comes back while it is off)" : ""));
            if (XrState.IsActive && XrState.IsOpenVr) Line(report, "SteamVR", SteamVrTimingReport());
            Line(report, "HDRP VR", VrPerformance.DescribeHdrpXr(runtime.MainCamera));
            Line(report, "DLSS", Settings.DisableDlss.Value ? "removed from the options (VR tab > Allow DLSS is off)" : "allowed, " + DlssEyes.Describe());

            Section(report, "VR UI");
            IUiDisplay display = runtime.Display;
            Line(report, "Display", display == null ? "none (VR inactive or still starting)" : display.Name + ": " + display.Status);
            Line(report, "Panel", PanelLayout.Describe() + ", flipped " + Settings.FlipVertically.Value);
            UiCapture capture = runtime.Capture;
            Line(report, "Capture", (capture.Texture == null ? "no texture" : "texture " + capture.Texture.width + "x" + capture.Texture.height) +
                                   ", last captured frame " + capture.LastCaptureFrame + ", total captures " + capture.CaptureCount +
                                   ", screen camera '" + (capture.LastScreenCamera != null ? capture.LastScreenCamera.name : "none") +
                                   "', backdrop " + capture.BackdropOpacity.ToString("0.00"));
            Line(report, "Player camera", runtime.MainCamera != null ? Describe(runtime.MainCamera) : "none");
            Line(report, "Player HUD", runtime.Hud.Describe());
            Line(report, "Cursor", runtime.Cursor.Describe() + " | game wants cursor " + UnityEngine.Cursor.visible + ", lock " + UnityEngine.Cursor.lockState);
            Line(report, "Desktop view", runtime.Desktop.Describe() + ", Unity mirror " + XRSettings.showDeviceView);

            Section(report, "Input");
            Line(report, "Game window focused", Application.isFocused + " (clicks only reach the game while its window is focused)");
            EventSystem eventSystem = EventSystem.current;
            Line(report, "Event system", eventSystem == null ? "none" : "'" + eventSystem.name + "' using " +
                (eventSystem.currentInputModule != null ? eventSystem.currentInputModule.GetType().Name : "no input module"));
            Line(report, "Mouse position", Mouse.current != null ? Mouse.current.position.ReadValue().ToString() : "no mouse");
            Line(report, "uGUI/panels under mouse", DescribeUiUnderMouse(5));
            Line(report, "UI Toolkit under mouse", DescribeUiToolkitUnderMouse());
            var devices = new List<string>();
            foreach (UnityEngine.InputSystem.InputDevice device in InputSystem.devices) devices.Add(device.displayName ?? device.name);
            Line(report, "Input devices", string.Join(", ", devices.ToArray()));
            Line(report, "Canvases moved to panel", runtime.Canvases.MovedCount.ToString());
            Line(report, "Intro video", IntroVideo.Describe());
            Line(report, "Terminal screens", (Settings.KeepTerminalScreensInWorld.Value ? "kept on their terminals in VR" : "attached to the camera (game default)") +
                                            ", kept " + TerminalScreens.KeptInWorld + " time(s) this session" +
                                            (Settings.SnapUiToTerminalScreens.Value ? ", UI panel moves onto them" : "") +
                                            (PanelLayout.OnTerminalScreen ? " (it is on one now)" : ""));
            Line(report, "Marker alignment", MarkerAlignment.Active ? "on (camera '" + (MarkerAlignment.PlayerCamera != null ? MarkerAlignment.PlayerCamera.name : "none") + "')" : "off");
            SupportedRenderingFeatures features = SupportedRenderingFeatures.active;
            Line(report, "UI overlay owner", features != null && features.rendersUIOverlay ? "render pipeline" : "Unity (normal for VR)");

            Section(report, "Cameras (enabled first)");
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera.gameObject.scene.IsValid() && camera.isActiveAndEnabled) Line(report, " +", Describe(camera));
            }
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera.gameObject.scene.IsValid() && !camera.isActiveAndEnabled) Line(report, " -", Describe(camera));
            }

            Section(report, "Canvases (root)");
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>(true))
            {
                if (!canvas.isRootCanvas) continue;
                Line(report, canvas.isActiveAndEnabled ? " +" : " -", PathOf(canvas.transform) + " | " + canvas.renderMode + " | order " + canvas.sortingOrder +
                                                                     (canvas.worldCamera != null ? " | camera '" + canvas.worldCamera.name + "'" : ""));
            }

            Section(report, "UI Toolkit documents");
            foreach (UIDocument document in UnityEngine.Object.FindObjectsOfType<UIDocument>(true))
            {
                PanelSettings panel = document.panelSettings;
                Line(report, document.isActiveAndEnabled ? " +" : " -", PathOf(document.transform) + " | panel '" + (panel != null ? panel.name : "none") + "'" +
                    (panel != null ? " order " + panel.sortingOrder + (panel.targetTexture != null ? " -> texture '" + panel.targetTexture.name + "'" : " -> screen") : ""));
            }

            Section(report, "Video players");
            foreach (VideoPlayer player in UnityEngine.Object.FindObjectsOfType<VideoPlayer>(true))
            {
                Line(report, player.isActiveAndEnabled ? " +" : " -", PathOf(player.transform) + " | " + player.renderMode + " | playing " + player.isPlaying +
                    (player.targetCamera != null ? " | camera '" + player.targetCamera.name + "'" : ""));
            }

            Section(report, "HDRP");
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            if (asset != null)
            {
                RenderPipelineSettings settings = asset.currentPlatformRenderPipelineSettings;
                Line(report, "Asset", asset.name + ", colour buffer " + settings.colorBufferFormat + ", custom passes " + settings.supportCustomPass +
                                     ", dynamic resolution " + settings.dynamicResolutionSettings.enabled + ", DLSS hardware " + HDDynamicResolutionPlatformCapabilities.DLSSDetected);
            }
            else
            {
                Line(report, "Asset", UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name : "none");
            }

            Section(report, "Recent warnings and errors");
            bool anyProblem = false;
            foreach (string problem in Log.RecentProblems)
            {
                report.AppendLine("  " + problem);
                anyProblem = true;
            }
            foreach (var failure in Log.FailureTotals)
            {
                report.AppendLine("  " + failure.Key + ": failed " + failure.Value + " time(s)");
                anyProblem = true;
            }
            if (!anyProblem) report.AppendLine("  none");

            report.Append("==================== end of diagnostics ====================");
            Log.Info(report.ToString());

            SaveSnapshot(capture.Texture, capture.BackdropOpacity);
        }

        private static void SaveSnapshot(RenderTexture texture, float backdrop)
        {
            if (texture == null)
            {
                Log.Info("No VR UI texture exists yet, so no snapshot was saved.");
                return;
            }
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Log.Info("This system can't read textures back from the GPU, so no snapshot was saved.");
                return;
            }

            int width = texture.width;
            int height = texture.height;
            byte backdropAlpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(backdrop) * 255f);
            AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    if (request.hasError)
                    {
                        Log.Warn("Reading the VR UI texture back from the GPU failed, so no snapshot was saved.");
                        return;
                    }

                    NativeArray<byte> data = request.GetData<byte>();
                    int uiPixels = 0;
                    for (int i = 3; i < data.Length; i += 4)
                    {
                        if (data[i] > backdropAlpha + 8) uiPixels++;
                    }

                    var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    image.LoadRawTextureData(data);
                    image.Apply(false);
                    byte[] png = image.EncodeToPNG();
                    UnityEngine.Object.Destroy(image);

                    Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));
                    File.WriteAllBytes(SnapshotPath, png);
                    Log.Info(string.Format("Saved the VR UI snapshot ({0:0.0}% of pixels contain UI) to {1}",
                        100.0 * uiPixels / Math.Max(1, width * height), SnapshotPath));
                }
                catch (Exception e)
                {
                    Log.Failure("Saving the VR UI snapshot", e);
                }
            });
        }

        /// <summary>The uGUI raycast hits under the mouse, top first. UI Toolkit panels show up as their panel object.</summary>
        public static string DescribeUiUnderMouse(int max)
        {
            EventSystem eventSystem = EventSystem.current;
            Mouse mouse = Mouse.current;
            if (eventSystem == null) return "no event system";
            if (mouse == null) return "no mouse";

            var pointer = new PointerEventData(eventSystem) { position = mouse.position.ReadValue() };
            Hits.Clear();
            eventSystem.RaycastAll(pointer, Hits);
            if (Hits.Count == 0) return "nothing";

            var text = new StringBuilder();
            for (int i = 0; i < Hits.Count && i < max; i++)
            {
                RaycastResult hit = Hits[i];
                if (i > 0) text.Append(" > ");
                text.Append('\'').Append(hit.gameObject != null ? PathOf(hit.gameObject.transform) : "?").Append("' (")
                    .Append(hit.module != null ? hit.module.GetType().Name : "?").Append(')');
            }
            if (Hits.Count > max) text.Append(" and ").Append(Hits.Count - max).Append(" more");
            return text.ToString();
        }

        /// <summary>The UI Toolkit element under the mouse in each visible document.</summary>
        private static string DescribeUiToolkitUnderMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return "no mouse";
            Vector2 screen = mouse.position.ReadValue();

            var found = new List<string>();
            foreach (UIDocument document in UnityEngine.Object.FindObjectsOfType<UIDocument>())
            {
                VisualElement root = document.rootVisualElement;
                IPanel panel = root != null ? root.panel : null;
                if (panel == null) continue;

                // UI Toolkit measures from the top-left corner, the mouse from the bottom-left.
                Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                VisualElement picked = panel.Pick(panelPosition);
                if (picked == null || picked == root) continue;
                found.Add(document.name + ": " + picked.GetType().Name + (string.IsNullOrEmpty(picked.name) ? "" : " '" + picked.name + "'"));
            }
            return found.Count == 0 ? "nothing" : string.Join(" | ", found.ToArray());
        }

        private static string Describe(Camera camera)
        {
            var text = new StringBuilder();
            text.Append(PathOf(camera.transform));
            text.Append(" | depth ").Append(camera.depth);
            text.Append(" | ").Append(camera.targetTexture != null
                ? "texture '" + camera.targetTexture.name + "' " + camera.targetTexture.width + "x" + camera.targetTexture.height
                : "screen/headset");
            text.Append(" | eyes ").Append(camera.stereoTargetEye);
            text.Append(" | mask 0x").Append(camera.cullingMask.ToString("X8"));

            var hd = camera.GetComponent<HDAdditionalCameraData>();
            if (hd != null)
            {
                text.Append(" | clear ").Append(hd.clearColorMode);
                text.Append(", AA ").Append(hd.antialiasing);
                text.Append(", DLSS ").Append(hd.allowDeepLearningSuperSampling);
                text.Append(", dyn-res ").Append(hd.allowDynamicResolution);
            }

            foreach (MonoBehaviour behaviour in camera.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Namespace != null && behaviour.GetType().Namespace.StartsWith("Uuvr"))
                {
                    text.Append(" | UUVR ").Append(behaviour.GetType().Name);
                }
            }
            return text.ToString();
        }

        private static string SteamVrTimingReport()
        {
            try
            {
                return SteamVrTiming.Describe();
            }
            catch (Exception e)
            {
                return "unavailable (" + e.GetType().Name + ": " + e.Message + ")";
            }
        }

        private static string GameStateName()
        {
            try
            {
                if (_stateMachineType == null)
                {
                    _stateMachineType = AccessTools.TypeByName("CG.GameLoopStateMachine.GameStateMachine");
                    if (_stateMachineType == null) return "unknown";
                    _currentStateProperty = AccessTools.Property(_stateMachineType, "CurrentState");
                }
                if (_stateMachine == null) _stateMachine = UnityEngine.Object.FindObjectOfType(_stateMachineType);
                object state = _stateMachine != null && _currentStateProperty != null ? _currentStateProperty.GetValue(_stateMachine, null) : null;
                return state != null ? state.GetType().Name : "none";
            }
            catch (Exception e)
            {
                return "unknown (" + e.GetType().Name + ")";
            }
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }
            return path;
        }

        private static void Section(StringBuilder report, string title)
        {
            report.AppendLine("--- " + title + " ---");
        }

        private static void Line(StringBuilder report, string label, string value)
        {
            report.Append("  ").Append(label).Append(": ").AppendLine(value);
        }
    }
}
