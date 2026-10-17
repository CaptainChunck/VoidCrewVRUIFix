using System;
using System.Collections;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Runs the plugin while the game plays: follows VR starting and stopping, keeps UUVR's conflicting pieces off,
    /// captures the UI every frame, hands it to the active display and handles the hotkeys.
    /// Does nothing while VR is not active, so the flat game is unaffected.
    /// </summary>
    internal sealed class VrUiRuntime : MonoBehaviour
    {
        private const float MaintenanceInterval = 0.5f;
        private const float HealthCheckDelay = 5f;

        private readonly UiCapture _capture = new UiCapture();
        private readonly DesktopView _desktop = new DesktopView();
        private readonly CanvasMover _canvases = new CanvasMover();
        private CursorOverlay _cursor;
        private IUiDisplay _display;
        private Camera _mainCamera;
        private bool _vrActive;
        private float _vrStartedAt;
        private float _nextMaintenance;
        private float _nextDisplayAttempt;
        private Vector3 _cameraPosition;
        private Quaternion _cameraRotation = Quaternion.identity;

        public UiCapture Capture { get { return _capture; } }
        public IUiDisplay Display { get { return _display; } }
        public Camera MainCamera { get { return _mainCamera; } }
        public CursorOverlay Cursor { get { return _cursor; } }
        public CanvasMover Canvases { get { return _canvases; } }
        public DesktopView Desktop { get { return _desktop; } }

        private void Awake()
        {
            _cursor = new CursorOverlay(transform);
            _capture.TextureCreated = texture => { if (_display != null) _display.SetTexture(texture); };
            _capture.AfterCapture = commands => { if (_display != null) _display.AppendSubmit(commands); };
            _capture.FrameRendered = (context, camera) =>
            {
                if (Settings.DesktopView.Value == DesktopViewMode.PanelView) _desktop.Draw(context, camera);
            };
        }

        private void OnEnable()
        {
            _capture.Start();
            Application.onBeforeRender += OnBeforeRender;
            StartCoroutine(EndOfFrameLoop());
        }

        private void OnDisable()
        {
            _capture.Stop();
            Application.onBeforeRender -= OnBeforeRender;
            if (_vrActive) OnVrStopped();
        }

        private void OnDestroy()
        {
            _capture.Release();
        }

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Log.Failure("Per-frame update", e);
            }
        }

        private void Tick()
        {
            bool vrActive = XrState.IsActive;
            if (vrActive != _vrActive)
            {
                if (vrActive) OnVrStarted();
                else OnVrStopped();
            }
            if (!vrActive) return;

            // Unity's own mirror is drawn over the screen-space UI, so it is only used when chosen explicitly.
            bool unityMirror = Settings.DesktopView.Value == DesktopViewMode.HeadsetMirror;
            if (XRSettings.showDeviceView != unityMirror) XRSettings.showDeviceView = unityMirror;

            if (Time.unscaledTime >= _nextMaintenance)
            {
                _nextMaintenance = Time.unscaledTime + MaintenanceInterval;
                Maintain();
            }

            if (_display != null && !_display.IsAlive) StopDisplay("it stopped working (was VR restarted?)");
            if (_display == null && Time.unscaledTime >= _nextDisplayAttempt) StartDisplay();

            // The game shows the cursor exactly when a menu, popup or terminal wants mouse input.
            bool menuOpen = UnityEngine.Cursor.visible && UnityEngine.Cursor.lockState != CursorLockMode.Locked;
            _capture.BackdropOpacity = menuOpen ? Settings.MenuBackdropOpacity.Value : 0f;
            _cursor.Update(Settings.ShowVrCursor.Value && menuOpen);
            IntroVideo.Update();

            MarkerAlignment.PlayerCamera = _mainCamera;
            MarkerAlignment.Active = Settings.AlignMarkersToPanel.Value;

            HandleHotkeys();
            CheckHealth();
            if (Settings.VerboseLogging.Value) LogClicks();
        }

        /// <summary>Verbose logging only: where each click lands, to diagnose buttons that don't respond.</summary>
        private static void LogClicks()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            Log.Verbose("Click at " + mouse.position.ReadValue() + ", game window focused " + Application.isFocused +
                        ", UI under the mouse: " + Diagnostics.DescribeUiUnderMouse(3));
        }

        private void OnBeforeRender()
        {
            if (!_vrActive) return;
            try
            {
                if (Settings.ClearDesktopEachFrame.Value) ClearDesktop();

                // Captured before UUVR applies the head rotation for rendering, so this is the in-game view direction.
                if (_mainCamera != null)
                {
                    Transform cameraTransform = _mainCamera.transform;
                    _cameraPosition = cameraTransform.position;
                    _cameraRotation = cameraTransform.rotation;
                }

                if (_display != null)
                {
                    _display.Update(new PanelState
                    {
                        Visible = _capture.Texture != null,
                        Camera = _mainCamera,
                        CameraPosition = _cameraPosition,
                        CameraRotation = _cameraRotation
                    });
                }
            }
            catch (Exception e)
            {
                Log.Failure("Updating the VR UI panel", e);
            }
        }

        private IEnumerator EndOfFrameLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                if (!_vrActive || _display == null) continue;
                try
                {
                    _display.SubmitFromMainThread();
                }
                catch (Exception e)
                {
                    Log.Failure("Sending the UI image to SteamVR", e);
                }
            }
        }

        private void OnVrStarted()
        {
            _vrActive = true;
            _vrStartedAt = Time.unscaledTime;
            _nextMaintenance = 0f;
            _nextDisplayAttempt = 0f;
            _capture.Enabled = true;
            Log.Info("VR started (" + XrState.DeviceName + "). Game window shows: " + Settings.DesktopView.Value + ".");
            GamePatches.RefreshHardwareCursor();
        }

        private void OnVrStopped()
        {
            _vrActive = false;
            _capture.Enabled = false;
            StopDisplay(null);
            _canvases.RestoreAll();
            _cursor.Update(false);
            MarkerAlignment.Active = false;
            GamePatches.RefreshHardwareCursor();
            Log.Info("VR stopped. The normal desktop UI is restored.");
        }

        private void Maintain()
        {
            UuvrCompat.Maintain();

            Camera camera = XrState.FindMainCamera();
            if (camera != _mainCamera)
            {
                _mainCamera = camera;
                if (camera != null) Log.Info("Player camera is now '" + camera.name + "'.");
                else Log.Verbose("No player camera right now (loading?).");
            }

            _canvases.Scan();
            HdrpTweaks.DisableDlssOnCameras();
        }

        private void StartDisplay()
        {
            _nextDisplayAttempt = Time.unscaledTime + 5f;
            UiDisplayMode mode = Settings.DisplayMode.Value;

            if (mode != UiDisplayMode.InGamePanel)
            {
                string reason;
                try
                {
                    _display = SteamVrOverlayFactory.TryCreate(out reason);
                }
                catch (Exception e)
                {
                    reason = "UUVR's OpenVR library could not be used (" + e.GetType().Name + ": " + e.Message + ")";
                }

                if (_display == null)
                {
                    if (mode == UiDisplayMode.SteamVROverlay) Log.Warn("SteamVR overlay unavailable: " + reason + ". Using the in-game panel instead.");
                    else Log.Info("SteamVR overlay not used: " + reason + ". Using the in-game panel.");
                }
            }

            if (_display == null)
            {
                try
                {
                    _display = new InGamePanelDisplay();
                }
                catch (Exception e)
                {
                    Log.Failure("Creating the in-game UI panel", e);
                    return;
                }
            }

            if (_capture.Texture != null) _display.SetTexture(_capture.Texture);
            Log.Info("VR UI is shown with the " + _display.Name + ": " + PanelLayout.Describe() + ".");
        }

        private void StopDisplay(string reason)
        {
            if (_display == null) return;
            if (reason != null) Log.Info("Restarting the " + _display.Name + " because " + reason + ".");
            try
            {
                _display.Shutdown();
            }
            catch (Exception e)
            {
                Log.Failure("Shutting down the " + _display.Name, e);
            }
            _display = null;
        }

        private void HandleHotkeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (Pressed(keyboard, Settings.KeyRecenter))
            {
                if (_display != null) _display.Recenter();
                Log.Info("[" + Settings.KeyRecenter.Value + "] VR UI panel recentred.");
            }

            if (Pressed(keyboard, Settings.KeyFlip))
            {
                Settings.FlipVertically.Value = !Settings.FlipVertically.Value;
                Log.Info("[" + Settings.KeyFlip.Value + "] VR UI flip is now " + (Settings.FlipVertically.Value ? "on" : "off") + " (saved to the config).");
            }

            if (Pressed(keyboard, Settings.KeyDisplayMode))
            {
                Settings.DisplayMode.Value = _display is InGamePanelDisplay ? UiDisplayMode.SteamVROverlay : UiDisplayMode.InGamePanel;
                Log.Info("[" + Settings.KeyDisplayMode.Value + "] Switching the VR UI display to " + Settings.DisplayMode.Value + " (saved to the config).");
                StopDisplay(null);
                _nextDisplayAttempt = 0f;
            }

            if (Pressed(keyboard, Settings.KeyDiagnostics))
            {
                Diagnostics.Write(this);
            }
        }

        private static bool Pressed(Keyboard keyboard, ConfigEntry<Key> entry)
        {
            Key key = entry.Value;
            if (key == Key.None) return false;
            try
            {
                return keyboard[key].wasPressedThisFrame;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>One-time warnings when a part of the pipeline is silently not running, with what to do about it.</summary>
        private void CheckHealth()
        {
            if (Time.unscaledTime - _vrStartedAt < HealthCheckDelay || _mainCamera == null) return;

            if (_capture.LastCaptureFrame < Time.frameCount - 120)
            {
                Log.WarnOnce("health-capture", "The UI has not been captured for a while, so the VR UI panel is not updating. " +
                                               "Press F9 and include the diagnostics in a bug report.");
            }

            string problem = _display != null ? _display.CheckHealth() : null;
            if (problem != null) Log.WarnOnce("health-" + _display.Name, problem);

            if (Settings.DesktopView.Value == DesktopViewMode.PanelView && _desktop.LastDrawnFrame < Time.frameCount - 120)
            {
                Log.WarnOnce("health-desktop", "The game window isn't showing the VR view (" + (_desktop.Problem ?? "unknown reason") +
                                               "). Set 'Desktop View' to HeadsetMirror in the config as a workaround, and press F9 for a bug report.");
            }
        }

        private static void ClearDesktop()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = null;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }
    }
}
