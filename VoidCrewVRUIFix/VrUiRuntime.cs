using System;
using System.Collections;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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

        // Camera-space HUD canvases are searched for every this many maintenance steps, and straight after a scene loads.
        private const int CanvasScanEvery = 3;

        private readonly UiCapture _capture = new UiCapture();
        private readonly DesktopView _desktop = new DesktopView();
        private readonly FullscreenFill _fill = new FullscreenFill();
        private readonly FpsCounter _fps = new FpsCounter();
        private readonly HudLayer _hud = new PlayerHudLayer();
        private readonly HudLayer _turretHud = new TurretHudLayer();
        private readonly CanvasMover _canvases = new CanvasMover();
        private readonly VrViewInput _vrViewInput = new VrViewInput();
        private CursorOverlay _cursor;
        private IUiDisplay _display;
        private Camera _mainCamera;
        private bool _vrActive;
        private float _vrStartedAt;
        private float _nextMaintenance;
        private float _nextDisplayAttempt;
        private Vector3 _cameraPosition;
        private Quaternion _cameraRotation = Quaternion.identity;
        private static bool _quitting;
        private bool _displayModeChanged;
        private int _maintenanceCount;
        private bool _canvasScanDue = true;
        private bool _pictureShown = true;
        private bool _pictureForHeadsetOff;
        private float _pictureOnAt;

        public UiCapture Capture { get { return _capture; } }
        public IUiDisplay Display { get { return _display; } }
        public Camera MainCamera { get { return _mainCamera; } }
        public CursorOverlay Cursor { get { return _cursor; } }
        public CanvasMover Canvases { get { return _canvases; } }
        public DesktopView Desktop { get { return _desktop; } }
        public FpsCounter Fps { get { return _fps; } }
        public HudLayer Hud { get { return _hud; } }
        public HudLayer TurretHud { get { return _turretHud; } }

        /// <summary>Whether the game window on the monitor shows the game right now.</summary>
        public bool PictureShown { get { return _pictureShown; } }

        private void Awake()
        {
            _cursor = new CursorOverlay(transform);
            Application.quitting += () => _quitting = true;
            Settings.DisplayMode.SettingChanged += (sender, args) => _displayModeChanged = true;
            Settings.DisableUuvrUi.SettingChanged += (sender, args) => UuvrCompat.RequestScan();
            Settings.DisableUuvrHelperCameras.SettingChanged += (sender, args) => UuvrCompat.RequestScan();
            SceneManager.sceneLoaded += (scene, mode) => _canvasScanDue = true;

            // Changing a HUD setting shows that HUD for a few seconds, even with the Settings menu open.
            _hud.PreviewOnChange();
            _turretHud.PreviewOnChange();

            // Order inside each frame: headset image into the desktop picture, capture the UI, then put the UI on
            // the desktop picture and send it to the headset display. With the monitor picture off, the desktop
            // picture is skipped and the window stays black.
            _capture.TextureCreated = texture => { if (_display != null) _display.SetTexture(texture); };
            _capture.FrameRendered = (context, camera) =>
            {
                if (_pictureShown && Settings.DesktopView.Value != DesktopViewMode.HeadsetMirror) _desktop.DrawScene(context, camera);
            };
            _capture.AfterCapture = (commands, ui) =>
            {
                _hud.Extract(commands, ui);
                _fill.Sample(commands, ui);
                _desktop.Fill = _fill.Current;
                if (_pictureShown && Settings.DesktopView.Value != DesktopViewMode.HeadsetMirror)
                {
                    _desktop.Composite(commands, ui, _hud.Extracting ? _hud.CopyTexture : null, _hud.UvRect);
                }
                // After the monitor picture, so the monitor shows the turret's readouts where the game draws them.
                _turretHud.Extract(commands, ui);
                if (_display != null) _display.AppendSubmit(commands);
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
            _desktop.Release();
            _fill.Release();
            _fps.Release();
            _hud.Release();
            _turretHud.Release();
        }

        private void Update()
        {
            Log.FlushDebounced();
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
            // The VR tab can wait for a key press even while VR is off.
            HotkeyCapture.Update();

            bool vrActive = XrState.IsActive;
            if (vrActive != _vrActive)
            {
                if (vrActive) OnVrStarted();
                else OnVrStopped();
            }
            if (!vrActive) return;

            UpdateMonitorPicture();
            _fps.Update(Settings.ShowFpsCounter.Value);

            // Unity's own mirror is drawn over the screen-space UI, so it is only used when chosen explicitly.
            bool unityMirror = _pictureShown && Settings.DesktopView.Value == DesktopViewMode.HeadsetMirror;
            if (XRSettings.showDeviceView != unityMirror) XRSettings.showDeviceView = unityMirror;

            if (Time.unscaledTime >= _nextMaintenance)
            {
                _nextMaintenance = Time.unscaledTime + MaintenanceInterval;
                Maintain();
            }

            if (_displayModeChanged)
            {
                _displayModeChanged = false;
                StopDisplay("the UI display setting changed to " + Settings.DisplayMode.Value);
                _nextDisplayAttempt = 0f;
            }
            if (_display != null && !_display.IsAlive) StopDisplay("it stopped working (was VR restarted?)");
            if (_display == null && Time.unscaledTime >= _nextDisplayAttempt) StartDisplay();

            // The game shows the cursor exactly when a menu, popup or terminal wants mouse input.
            bool menuOpen = UnityEngine.Cursor.visible && UnityEngine.Cursor.lockState != CursorLockMode.Locked;
            // A terminal's screen is part of the world, so it isn't darkened like a menu.
            _capture.BackdropOpacity = menuOpen && !PanelLayout.OnTerminalScreen ? Settings.MenuBackdropOpacity.Value : 0f;
            _hud.Update(menuOpen, _fill.Current.a > 0.003f);
            _turretHud.Update(menuOpen, _fill.Current.a > 0.003f);
            HeadAim.Update(true, menuOpen);
            _desktop.UiInSceneImage = _display is InGamePanelDisplay;
            _cursor.Update(Settings.ShowVrCursor.Value && menuOpen);
            IntroVideo.Update();

            MarkerAlignment.PlayerCamera = _mainCamera;
            MarkerAlignment.Active = Settings.AlignMarkersToPanel.Value;

            _vrViewInput.Update();
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
                // With the monitor picture off nothing else draws to the window, so it is always cleared to black.
                if (Settings.ClearDesktopEachFrame.Value || !_pictureShown) ClearDesktop();

                // Captured before UUVR applies the head rotation for rendering, so this is the in-game view direction.
                if (_mainCamera != null)
                {
                    Transform cameraTransform = _mainCamera.transform;
                    _cameraPosition = cameraTransform.position;
                    _cameraRotation = cameraTransform.rotation;
                    TerminalScreens.PlacePanel(_cameraPosition, _cameraRotation);
                }

                if (_display != null)
                {
                    _display.Update(new PanelState
                    {
                        Visible = _capture.Texture != null,
                        Camera = _mainCamera,
                        CameraPosition = _cameraPosition,
                        CameraRotation = _cameraRotation,
                        Fill = _fill.Current,
                        FpsTexture = _fps.Texture,
                        Hud = _hud.Picture(),
                        TurretHud = _turretHud.Picture()
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
            _canvasScanDue = true;
            _capture.Enabled = true;
            HeadsetPresence.Reset();
            UuvrCompat.RequestScan();
            Log.Info("VR started (" + XrState.DeviceName + "). Game window shows: " +
                     (Settings.DesktopViewOn.Value ? Settings.DesktopView.Value.ToString() : "nothing (monitor picture off, " + Settings.KeyDesktopView.Value + " turns it on)") + ".");
            GamePatches.RefreshHardwareCursor();
        }

        private void OnVrStopped()
        {
            _vrActive = false;
            _capture.Enabled = false;
            StopDisplay(null);
            MarkerAlignment.Active = false;
            _vrViewInput.Shutdown();
            HeadsetPresence.Reset();
            _pictureShown = true;
            _pictureForHeadsetOff = false;
            VrPerformance.OnVrStopped();
            PanelLayout.LeaveTerminal();
            _hud.Stop();
            _turretHud.Stop();
            HeadAim.Stop();

            // While the game is closing, Steam and the game's UI are already shutting down; leave them alone.
            if (_quitting) return;

            _canvases.RestoreAll();
            _cursor.Update(false);
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
                _canvasScanDue = true;
                if (camera != null) Log.Info("Player camera is now '" + camera.name + "'.");
                else Log.Verbose("No player camera right now (loading?).");
            }

            if (_canvasScanDue || ++_maintenanceCount % CanvasScanEvery == 0)
            {
                _canvasScanDue = false;
                _canvases.Scan();
            }
            HdrpTweaks.DisableDlssOnCameras(_mainCamera);
            VrPerformance.Maintain();
        }

        /// <summary>
        /// The monitor picture is on when the setting says so, or while the headset is taken off (if that option is on).
        /// </summary>
        private void UpdateMonitorPicture()
        {
            HeadsetPresence.Update();
            bool forHeadsetOff = !Settings.DesktopViewOn.Value && Settings.DesktopViewWhenHeadsetOff.Value && HeadsetPresence.Removed;
            if (forHeadsetOff != _pictureForHeadsetOff)
            {
                _pictureForHeadsetOff = forHeadsetOff;
                if (forHeadsetOff) Log.Info("Headset taken off: the game shows on the monitor until it is put back on.");
                else if (!HeadsetPresence.Removed && !Settings.DesktopViewOn.Value) Log.Info("Headset back on: the monitor picture is off again.");
            }

            bool shown = Settings.DesktopViewOn.Value || forHeadsetOff;
            if (shown && !_pictureShown) _pictureOnAt = Time.unscaledTime;
            _pictureShown = shown;
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

            // While the VR tab waits for a new hotkey, or in the frame it got one, keys are not hotkeys.
            if (HotkeyCapture.Waiting || HotkeyCapture.CapturedFrame == Time.frameCount) return;

            if (Pressed(keyboard, Settings.KeyDesktopView))
            {
                Settings.DesktopViewOn.Value = !Settings.DesktopViewOn.Value;
                Log.Info("[" + Settings.KeyDesktopView.Value + "] Game picture on the monitor turned " + (Settings.DesktopViewOn.Value ? "on" : "off") +
                         " (saved to the config).");
            }

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
                // The setting's change event restarts the display.
                Settings.DisplayMode.Value = _display is InGamePanelDisplay ? UiDisplayMode.SteamVROverlay : UiDisplayMode.InGamePanel;
                Log.Info("[" + Settings.KeyDisplayMode.Value + "] Switching the VR UI display to " + Settings.DisplayMode.Value + " (saved to the config).");
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

            if (_pictureShown && Time.unscaledTime - _pictureOnAt > HealthCheckDelay &&
                Settings.DesktopView.Value == DesktopViewMode.PanelView && _desktop.LastDrawnFrame < Time.frameCount - 120)
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
