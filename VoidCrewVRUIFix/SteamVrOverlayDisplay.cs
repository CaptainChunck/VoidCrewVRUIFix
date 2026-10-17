using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Valve.VR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Shows the UI through a SteamVR overlay. SteamVR draws it on top of the game image, so it is sharp, never
    /// affected by HDRP post-processing (bloom, depth of field, exposure) and is drawn once for both eyes.
    /// Needs UUVR to use OpenVR and the game to run on Direct3D 11. The OpenVR API comes from UUVR's own
    /// Uuvr.XR.OpenVR.dll, so players don't need any extra files.
    /// </summary>
    internal sealed class SteamVrOverlayDisplay : IUiDisplay
    {
        private const string OverlayKey = "voidcrew.vruifix.ui";
        private const string FillKey = "voidcrew.vruifix.fill";
        private const string FpsKey = "voidcrew.vruifix.fps";
        private const string HudKey = "voidcrew.vruifix.hud";
        private const string TurretHudKey = "voidcrew.vruifix.turrethud";

        // The fill is locked to the head and wide enough to cover the whole view. SteamVR draws overlays at their real
        // depth, so it has to sit behind every part of the UI panel: when it sat just behind the panel's centre, it cut
        // through the panel's far edge as soon as you looked to the side (a dark box over the edge of the menus).
        // It now sits behind the panel's farthest corner, with this much room for leaning.
        private const float FillHeadroom = 1f;
        private const float FillHalfAngle = 80f;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RenderEventCallback(int eventId, IntPtr data);

        // Shared with the render-thread callback. Written on the main thread under SubmitLock.
        private static readonly object SubmitLock = new object();
        private static CVROverlay _submitOverlay;
        private static ulong _submitHandle;
        private static Texture_t _submitTexture;
        private static ulong _submitFillHandle;
        private static Texture_t _submitFillTexture;
        private static bool _submitFillActive;
        private static ulong _submitFpsHandle;
        private static Texture_t _submitFpsTexture;
        private static bool _submitFpsActive;
        // The pictures locked to the head: the player HUD and the turret HUD.
        private static readonly SubmitSlot[] HeadLockedSlots = new SubmitSlot[2];
        private static uint _submitInitToken;
        private static volatile int _submitError;
        private static volatile string _submitException;
        private static long _submitCount;

        // The delegate must stay referenced for as long as Unity may call it.
        private static readonly RenderEventCallback SubmitCallback = SubmitOnRenderThread;
        private static readonly IntPtr SubmitCallbackPtr = Marshal.GetFunctionPointerForDelegate(SubmitCallback);
        private static readonly TrackedDevicePose_t[] Poses = new TrackedDevicePose_t[1];

        private readonly CVROverlay _overlay;
        private readonly ulong _handle;
        private readonly ulong _fillHandle;
        private readonly ulong _fpsHandle;
        private Texture _fpsTexture;
        private bool _fpsShown;
        private float _appliedFpsDistance = -1f;
        private readonly HeadLockedOverlay _hud;
        private readonly HeadLockedOverlay _turretHud;
        private readonly RenderTexture _fillTexture;
        private readonly uint _initToken;
        private bool _shown;
        private bool _fillShown;
        private Color _fillColour;
        private float _appliedFillAlpha = -1f;
        private float _appliedFillDistance = -1f;
        private bool _needsRecenter = true;
        private Vector3 _center;

        // Which way the game's forward points in SteamVR's tracking space (OpenVR convention, rotation only), measured
        // when the panel is centred. Identity when SteamVR's tracking space and UUVR's head tracking agree.
        private Matrix4x4 _frame = Matrix4x4.identity;
        private bool _frameMeasured;
        private float _frameYaw;
        private float _frameTilt;
        private Camera _camera;
        private Matrix4x4 _appliedRotation = Matrix4x4.identity;
        private ETrackingUniverseOrigin _origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
        private float _centerDistance;
        private float _centerOffset;
        private float _appliedWidth = -1f;
        private int _appliedFlip = -1;
        private PanelAnchor _appliedAnchor;
        private Vector3 _appliedPosition = new Vector3(float.NaN, 0f, 0f);
        private int _reportedError;
        private string _reportedException;

        private SteamVrOverlayDisplay(CVROverlay overlay, ulong handle, ulong fillHandle, ulong fpsHandle, ulong hudHandle, ulong turretHudHandle)
        {
            _overlay = overlay;
            _handle = handle;
            _fillHandle = fillHandle;
            _fpsHandle = fpsHandle;
            _initToken = OpenVR.GetInitToken();

            Check(_overlay.SetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, true), "SetOverlayFlag(IsPremultiplied)");
            Check(_overlay.SetOverlaySortOrder(handle, 1), "SetOverlaySortOrder");
            _overlay.HideOverlay(handle);

            if (fillHandle != 0)
            {
                // A tiny texture cleared to the fill colour; its opacity is set on the overlay itself.
                _fillTexture = new RenderTexture(4, 4, 0, GraphicsFormat.R8G8B8A8_SRGB)
                {
                    name = "VoidCrewVRUIFix_Fill",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _fillTexture.Create();
                Check(_overlay.SetOverlaySortOrder(fillHandle, 0), "SetOverlaySortOrder(fill)");
                _overlay.HideOverlay(fillHandle);
                IntPtr nativeFill = _fillTexture.GetNativeTexturePtr();
                lock (SubmitLock)
                {
                    _submitFillHandle = fillHandle;
                    _submitFillTexture = new Texture_t { handle = nativeFill, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
                    _submitFillActive = false;
                }
            }

            if (fpsHandle != 0)
            {
                // The frame-rate counter: drawn above the UI, and flipped like the UI (Unity textures are stored bottom row first).
                Check(_overlay.SetOverlaySortOrder(fpsHandle, 2), "SetOverlaySortOrder(fps)");
                var bounds = new VRTextureBounds_t { uMin = 0f, uMax = 1f, vMin = 1f, vMax = 0f };
                Check(_overlay.SetOverlayTextureBounds(fpsHandle, ref bounds), "SetOverlayTextureBounds(fps)");
                _overlay.HideOverlay(fpsHandle);
                lock (SubmitLock)
                {
                    _submitFpsHandle = fpsHandle;
                    _submitFpsTexture = default(Texture_t);
                    _submitFpsActive = false;
                }
            }

            if (hudHandle != 0) _hud = new HeadLockedOverlay(this, hudHandle, 0, "hud");
            if (turretHudHandle != 0) _turretHud = new HeadLockedOverlay(this, turretHudHandle, 1, "turret hud");
        }

        public string Name { get { return "SteamVR overlay"; } }

        public bool IsAlive { get { return OpenVR.GetInitToken() == _initToken; } }

        public string Status
        {
            get
            {
                return string.Format(
                    "handle 0x{0:X}, shown {1}, tracking space {2}, centre {3}{9}, frames submitted {4} ({5}), last SteamVR error {6}, background fill {7}{8}",
                    _handle, _shown, _origin, _center, Interlocked.Read(ref _submitCount),
                    Settings.SubmitOverlayOnRenderThread.Value ? "render thread" : "main thread",
                    (EVROverlayError)_submitError,
                    _fillShown ? "shown " + _appliedFillDistance.ToString("0.0") + " m away" : "hidden",
                    _submitException != null ? ", last exception " + _submitException : "",
                    _frameMeasured ? string.Format(", game forward {0:0}° to the {1} and {2:0}° tilted from the space's forward", Mathf.Abs(_frameYaw),
                        _frameYaw < 0f ? "left" : "right", _frameTilt) : ", game forward not measured yet");
            }
        }

        public string CheckHealth()
        {
            if (_shown && Interlocked.Read(ref _submitCount) == 0)
            {
                return "No UI frames have reached SteamVR yet. If the headset shows no UI, set 'Submit Overlay On Render Thread' " +
                       "to false, or 'UI Display Mode' to InGamePanel, in the config and restart the game.";
            }
            return null;
        }

        public static IUiDisplay TryCreate(out string reason)
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                reason = "the game is running on " + SystemInfo.graphicsDeviceType + " and the overlay supports Direct3D 11 only";
                return null;
            }
            if (!XrState.IsOpenVr)
            {
                reason = "UUVR is not using OpenVR (VR device: '" + XrState.DeviceName + "')";
                return null;
            }

            CVROverlay overlay = OpenVR.Overlay;
            if (overlay == null)
            {
                reason = "the OpenVR overlay interface is not available";
                return null;
            }

            ulong handle = 0;
            if (overlay.FindOverlay(OverlayKey, ref handle) != EVROverlayError.None || handle == 0)
            {
                handle = 0;
                EVROverlayError error = overlay.CreateOverlay(OverlayKey, "Void Crew UI", ref handle);
                if (error != EVROverlayError.None)
                {
                    reason = "SteamVR refused to create the overlay (" + overlay.GetOverlayErrorNameFromEnum(error) + ")";
                    return null;
                }
            }

            // Optional second overlay that fills the view behind full-screen UI (death screen, fades).
            ulong fillHandle = 0;
            if (overlay.FindOverlay(FillKey, ref fillHandle) != EVROverlayError.None || fillHandle == 0)
            {
                fillHandle = 0;
                if (overlay.CreateOverlay(FillKey, "Void Crew UI background", ref fillHandle) != EVROverlayError.None) fillHandle = 0;
            }

            // Optional third overlay for the frame-rate counter.
            ulong fpsHandle = 0;
            if (overlay.FindOverlay(FpsKey, ref fpsHandle) != EVROverlayError.None || fpsHandle == 0)
            {
                fpsHandle = 0;
                if (overlay.CreateOverlay(FpsKey, "Void Crew FPS counter", ref fpsHandle) != EVROverlayError.None) fpsHandle = 0;
            }

            // Optional overlays for the status HUD and the turret HUD, locked to the head.
            ulong hudHandle = FindOrCreate(overlay, HudKey, "Void Crew HUD");
            ulong turretHudHandle = FindOrCreate(overlay, TurretHudKey, "Void Crew turret HUD");

            reason = null;
            return new SteamVrOverlayDisplay(overlay, handle, fillHandle, fpsHandle, hudHandle, turretHudHandle);
        }

        /// <summary>An optional overlay: the one left from an earlier VR start, or a new one; 0 if SteamVR refuses.</summary>
        private static ulong FindOrCreate(CVROverlay overlay, string key, string name)
        {
            ulong found = 0;
            if (overlay.FindOverlay(key, ref found) == EVROverlayError.None && found != 0) return found;
            ulong created = 0;
            return overlay.CreateOverlay(key, name, ref created) == EVROverlayError.None ? created : 0;
        }

        public void SetTexture(RenderTexture texture)
        {
            // Called right after the texture is created, before anything is drawn into it, so the render thread
            // never submits a texture that has been released.
            IntPtr nativeTexture = texture != null ? texture.GetNativeTexturePtr() : IntPtr.Zero;
            lock (SubmitLock)
            {
                _submitOverlay = _overlay;
                _submitHandle = _handle;
                _submitInitToken = _initToken;
                _submitTexture = new Texture_t
                {
                    handle = nativeTexture,
                    eType = ETextureType.DirectX,
                    eColorSpace = EColorSpace.Auto
                };
            }
        }

        public void Update(PanelState state)
        {
            if (!IsAlive) return;
            _camera = state.Camera;

            int flip = Settings.FlipVertically.Value ? 1 : 0;
            if (flip != _appliedFlip)
            {
                // Unity stores render textures bottom row first on Direct3D, SteamVR reads top row first.
                var bounds = new VRTextureBounds_t { uMin = 0f, uMax = 1f, vMin = flip == 1 ? 0f : 1f, vMax = flip == 1 ? 1f : 0f };
                Check(_overlay.SetOverlayTextureBounds(_handle, ref bounds), "SetOverlayTextureBounds");
                _appliedFlip = flip;
            }

            float width = PanelLayout.WidthMeters;
            if (Mathf.Abs(width - _appliedWidth) > 0.0001f)
            {
                Check(_overlay.SetOverlayWidthInMeters(_handle, width), "SetOverlayWidthInMeters");
                _appliedWidth = width;
            }

            ApplyPlacement();

            bool show = state.Visible && _submitTexture.handle != IntPtr.Zero;
            if (show != _shown)
            {
                Check(show ? _overlay.ShowOverlay(_handle) : _overlay.HideOverlay(_handle), show ? "ShowOverlay" : "HideOverlay");
                _shown = show;
            }

            UpdateFill(show ? state.Fill : Color.clear);
            UpdateFps(show ? state.FpsTexture : null);
            if (_hud != null) _hud.Update(state.Hud);
            if (_turretHud != null) _turretHud.Update(state.TurretHud);
            ReportRenderThreadProblems();
        }

        /// <summary>The frame-rate counter: locked to the head, up and to the left, in front of the UI panel.</summary>
        private void UpdateFps(Texture texture)
        {
            if (_fpsHandle == 0) return;

            bool show = texture != null;
            if (show)
            {
                if (!ReferenceEquals(texture, _fpsTexture))
                {
                    // Asking for the native pointer waits for the render thread, so only when the texture changes.
                    _fpsTexture = texture;
                    IntPtr native = texture.GetNativeTexturePtr();
                    lock (SubmitLock) _submitFpsTexture = new Texture_t { handle = native, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
                }

                float distance = FpsCounter.Distance(PanelLayout.Distance);
                if (Mathf.Abs(distance - _appliedFpsDistance) > 0.001f)
                {
                    var transform = new HmdMatrix34_t
                    {
                        m0 = 1f, m5 = 1f, m10 = 1f,
                        m3 = FpsCounter.OffsetAtOneMetre.x * distance,
                        m7 = FpsCounter.OffsetAtOneMetre.y * distance,
                        m11 = -distance
                    };
                    Check(_overlay.SetOverlayTransformTrackedDeviceRelative(_fpsHandle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform),
                        "SetOverlayTransformTrackedDeviceRelative(fps)");
                    Check(_overlay.SetOverlayWidthInMeters(_fpsHandle, FpsCounter.WidthAtOneMetre * distance), "SetOverlayWidthInMeters(fps)");
                    _appliedFpsDistance = distance;
                }
            }

            if (show != _fpsShown)
            {
                Check(show ? _overlay.ShowOverlay(_fpsHandle) : _overlay.HideOverlay(_fpsHandle), show ? "ShowOverlay(fps)" : "HideOverlay(fps)");
                _fpsShown = show;
                lock (SubmitLock) _submitFpsActive = show;
            }
        }

        private void UpdateFill(Color fill)
        {
            if (_fillHandle == 0) return;

            bool show = fill.a > 0.003f;
            if (show)
            {
                float distance = FillDistance();
                if (Mathf.Abs(distance - _appliedFillDistance) > 0.001f)
                {
                    var transform = new HmdMatrix34_t { m0 = 1f, m5 = 1f, m10 = 1f, m11 = -distance };
                    Check(_overlay.SetOverlayTransformTrackedDeviceRelative(_fillHandle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform),
                        "SetOverlayTransformTrackedDeviceRelative(fill)");
                    Check(_overlay.SetOverlayWidthInMeters(_fillHandle, 2f * distance * Mathf.Tan(FillHalfAngle * Mathf.Deg2Rad)),
                        "SetOverlayWidthInMeters(fill)");
                    _appliedFillDistance = distance;
                }
                if (Mathf.Abs(fill.a - _appliedFillAlpha) > 0.002f)
                {
                    Check(_overlay.SetOverlayAlpha(_fillHandle, fill.a), "SetOverlayAlpha(fill)");
                    _appliedFillAlpha = fill.a;
                }
                _fillColour = fill;
            }

            if (show != _fillShown)
            {
                Check(show ? _overlay.ShowOverlay(_fillHandle) : _overlay.HideOverlay(_fillHandle), show ? "ShowOverlay(fill)" : "HideOverlay(fill)");
                _fillShown = show;
                lock (SubmitLock) _submitFillActive = show;
            }
        }

        /// <summary>How far in front of the eyes the fill sits: behind the panel's farthest corner, plus room for leaning.</summary>
        private static float FillDistance()
        {
            float distance = PanelLayout.Distance;
            float halfWidth = PanelLayout.WidthMeters * 0.5f;
            float halfHeight = PanelLayout.HeightMeters * 0.5f + Mathf.Abs(PanelLayout.HeightOffset);
            return Mathf.Sqrt(distance * distance + halfWidth * halfWidth + halfHeight * halfHeight) + FillHeadroom;
        }

        public void AppendSubmit(CommandBuffer commands)
        {
            if (_fillShown && _fillTexture != null)
            {
                // Clear colours are linear; the fill colour was measured from the gamma-encoded UI image.
                Color linear = _fillColour.linear;
                commands.SetRenderTarget(_fillTexture);
                commands.ClearRenderTarget(false, true, new Color(linear.r, linear.g, linear.b, 1f));
            }
            if (Settings.SubmitOverlayOnRenderThread.Value && _shown)
            {
                commands.IssuePluginEventAndData(SubmitCallbackPtr, 0, IntPtr.Zero);
            }
        }

        public void SubmitFromMainThread()
        {
            if (Settings.SubmitOverlayOnRenderThread.Value || !_shown || !IsAlive) return;
            Texture_t texture = _submitTexture;
            EVROverlayError error = _overlay.SetOverlayTexture(_handle, ref texture);
            if (error != EVROverlayError.None) _submitError = (int)error;
            if (_fillShown)
            {
                Texture_t fill = _submitFillTexture;
                _overlay.SetOverlayTexture(_fillHandle, ref fill);
            }
            if (_fpsShown && _submitFpsTexture.handle != IntPtr.Zero)
            {
                Texture_t fps = _submitFpsTexture;
                _overlay.SetOverlayTexture(_fpsHandle, ref fps);
            }
            if (_hud != null) _hud.SubmitFromMainThread();
            if (_turretHud != null) _turretHud.SubmitFromMainThread();
            Interlocked.Increment(ref _submitCount);
        }

        public void Recenter()
        {
            _needsRecenter = true;
        }

        public void Shutdown()
        {
            lock (SubmitLock)
            {
                _submitHandle = 0;
                _submitOverlay = null;
                _submitTexture = default(Texture_t);
                _submitFillHandle = 0;
                _submitFillTexture = default(Texture_t);
                _submitFillActive = false;
                _submitFpsHandle = 0;
                _submitFpsTexture = default(Texture_t);
                _submitFpsActive = false;
                HeadLockedSlots[0] = default(SubmitSlot);
                HeadLockedSlots[1] = default(SubmitSlot);
            }

            // If VR was already turned off, OpenVR is gone and the overlays with it. Calling into it would crash.
            if (IsAlive)
            {
                _overlay.HideOverlay(_handle);
                _overlay.DestroyOverlay(_handle);
                if (_fillHandle != 0)
                {
                    _overlay.HideOverlay(_fillHandle);
                    _overlay.DestroyOverlay(_fillHandle);
                }
                if (_fpsHandle != 0)
                {
                    _overlay.HideOverlay(_fpsHandle);
                    _overlay.DestroyOverlay(_fpsHandle);
                }
                if (_hud != null) _hud.Destroy();
                if (_turretHud != null) _turretHud.Destroy();
            }
            if (_fillTexture != null)
            {
                _fillTexture.Release();
                UnityEngine.Object.Destroy(_fillTexture);
            }
        }

        private void ApplyPlacement()
        {
            PanelAnchor anchor = HeadAim.PanelFollowsHead ? PanelAnchor.Head : PanelAnchor.GameCamera;
            Vector3 position;
            Matrix4x4 rotation = Matrix4x4.identity;
            if (anchor == PanelAnchor.Head)
            {
                position = new Vector3(0f, PanelLayout.HeightOffset, -PanelLayout.Distance);
            }
            else
            {
                if (_centerDistance != PanelLayout.Distance || _centerOffset != PanelLayout.HeightOffset) _needsRecenter = true;
                if (_needsRecenter) RecenterNow();
                position = _center;
                rotation = _frame;
            }

            if (anchor == _appliedAnchor && position == _appliedPosition && rotation == _appliedRotation) return;

            // OpenVR looks down -Z and an overlay faces +Z, so with the frame's rotation the panel faces the player
            // looking along the game's forward.
            var transform = new HmdMatrix34_t
            {
                m0 = rotation.m00, m1 = rotation.m01, m2 = rotation.m02, m3 = position.x,
                m4 = rotation.m10, m5 = rotation.m11, m6 = rotation.m12, m7 = position.y,
                m8 = rotation.m20, m9 = rotation.m21, m10 = rotation.m22, m11 = position.z
            };
            if (anchor == PanelAnchor.Head)
            {
                Check(_overlay.SetOverlayTransformTrackedDeviceRelative(_handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform),
                    "SetOverlayTransformTrackedDeviceRelative");
            }
            else
            {
                Check(_overlay.SetOverlayTransformAbsolute(_handle, _origin, ref transform), "SetOverlayTransformAbsolute");
            }
            _appliedAnchor = anchor;
            _appliedPosition = position;
            _appliedRotation = rotation;
        }

        /// <summary>
        /// Places the panel straight ahead along the game's forward, at the headset's current position, so the panel
        /// (and the markers on it) lines up with the world when you turn your head. With UUVR's RelativeTransform
        /// tracking the game's forward is normally the tracking space's own forward axis, but not when SteamVR's saved
        /// forward (its seated or standing zero) differs from the one UUVR's head tracking uses: then the panel showed
        /// far off to one side. So the game's forward is measured in SteamVR's space each time the panel is centred.
        /// </summary>
        private void RecenterNow()
        {
            CVRCompositor compositor = OpenVR.Compositor;
            _origin = compositor != null ? compositor.GetTrackingSpace() : ETrackingUniverseOrigin.TrackingUniverseStanding;

            Vector3 head;
            Matrix4x4 headRotation;
            bool valid = TryGetHeadPose(_origin, out head, out headRotation);
            if (!valid)
            {
                head = new Vector3(0f, _origin == ETrackingUniverseOrigin.TrackingUniverseStanding ? 1.6f : 0f, 0f);
            }

            Matrix4x4 frame;
            if (valid && TryMeasureGameForward(headRotation, out frame))
            {
                _frame = frame;
                _frameMeasured = true;
                if (Mathf.Abs(_frameYaw) > 5f || _frameTilt > 5f)
                {
                    Log.InfoOnce("overlay-frame", string.Format("SteamVR's tracking space faces {0:0}° to the {1} of the game's forward (UUVR's head tracking); " +
                        "the UI panel is turned to match. Press the recentre key ({2}) to measure it again.", Mathf.Abs(_frameYaw), _frameYaw < 0f ? "left" : "right",
                        Settings.KeyRecenter.Value));
                }
            }

            _centerDistance = PanelLayout.Distance;
            _centerOffset = PanelLayout.HeightOffset;
            _center = head + _frame.MultiplyVector(new Vector3(0f, _centerOffset, -_centerDistance));
            _needsRecenter = !valid;
            Log.Verbose("SteamVR overlay centred at " + _center + " in " + _origin + (valid ? "" : " (headset pose not available yet, using a default height)") +
                        (_frameMeasured ? string.Format(", game forward {0:0.0}° yaw, {1:0.0}° tilt from the space's forward", _frameYaw, _frameTilt) : ""));
        }

        /// <summary>
        /// The game's forward in SteamVR's tracking space: the headset's rotation there, undone by its rotation relative
        /// to the game camera (what UUVR turns the game camera by). SteamVR's spaces are level, so a small tilt is
        /// measurement noise (the two rotations are read a moment apart) and is dropped, and so is a turn under 2°.
        /// </summary>
        private bool TryMeasureGameForward(Matrix4x4 headRotation, out Matrix4x4 frame)
        {
            frame = Matrix4x4.identity;
            Quaternion relative;
            if (!HeadAim.TryGetHeadRelativeToGameCamera(_camera, out relative)) return false;

            // Unity looks along +Z (left-handed), OpenVR along -Z (right-handed): mirror Z.
            Matrix4x4 relativeVr = MirrorZ(Matrix4x4.Rotate(relative));
            Matrix4x4 measured = headRotation * relativeVr.transpose;

            Vector3 forward = measured.MultiplyVector(new Vector3(0f, 0f, -1f));
            _frameYaw = Mathf.Atan2(forward.x, -forward.z) * Mathf.Rad2Deg;
            _frameTilt = Mathf.Acos(Mathf.Clamp(measured.m11, -1f, 1f)) * Mathf.Rad2Deg;
            if (_frameTilt > 10f)
            {
                frame = measured;
                return true;
            }

            var level = new Vector3(forward.x, 0f, forward.z);
            if (level.sqrMagnitude < 1e-6f || Mathf.Abs(_frameYaw) < 2f) return true;
            Vector3 back = -level.normalized;
            Vector3 up = Vector3.up;
            Vector3 right = Vector3.Cross(up, back);
            frame.SetColumn(0, new Vector4(right.x, right.y, right.z, 0f));
            frame.SetColumn(1, new Vector4(0f, 1f, 0f, 0f));
            frame.SetColumn(2, new Vector4(back.x, back.y, back.z, 0f));
            return true;
        }

        private static Matrix4x4 MirrorZ(Matrix4x4 m)
        {
            m.m02 = -m.m02;
            m.m12 = -m.m12;
            m.m20 = -m.m20;
            m.m21 = -m.m21;
            return m;
        }

        private static bool TryGetHeadPose(ETrackingUniverseOrigin origin, out Vector3 position, out Matrix4x4 rotation)
        {
            position = Vector3.zero;
            rotation = Matrix4x4.identity;
            CVRSystem system = OpenVR.System;
            if (system == null) return false;

            system.GetDeviceToAbsoluteTrackingPose(origin, 0f, Poses);
            if (!Poses[0].bPoseIsValid) return false;

            HmdMatrix34_t pose = Poses[0].mDeviceToAbsoluteTracking;
            position = new Vector3(pose.m3, pose.m7, pose.m11);
            rotation.m00 = pose.m0; rotation.m01 = pose.m1; rotation.m02 = pose.m2;
            rotation.m10 = pose.m4; rotation.m11 = pose.m5; rotation.m12 = pose.m6;
            rotation.m20 = pose.m8; rotation.m21 = pose.m9; rotation.m22 = pose.m10;
            return true;
        }

        private void ReportRenderThreadProblems()
        {
            int error = _submitError;
            if (error != 0 && error != _reportedError)
            {
                _reportedError = error;
                Log.Warn("SteamVR rejected the UI image: " + _overlay.GetOverlayErrorNameFromEnum((EVROverlayError)error));
            }

            string exception = _submitException;
            if (exception != null && exception != _reportedException)
            {
                _reportedException = exception;
                Log.Error("Sending the UI image to SteamVR threw " + exception);
            }
        }

        private void Check(EVROverlayError error, string call)
        {
            if (error != EVROverlayError.None)
            {
                Log.WarnOnce("overlay-" + call + "-" + error, "SteamVR overlay call " + call + " failed: " + _overlay.GetOverlayErrorNameFromEnum(error));
            }
        }

        private static void SubmitOnRenderThread(int eventId, IntPtr data)
        {
            try
            {
                CVROverlay overlay;
                ulong handle;
                Texture_t texture;
                ulong fillHandle;
                Texture_t fillTexture;
                bool fillActive;
                ulong fpsHandle;
                Texture_t fpsTexture;
                bool fpsActive;
                SubmitSlot hud;
                SubmitSlot turretHud;
                uint initToken;
                lock (SubmitLock)
                {
                    overlay = _submitOverlay;
                    handle = _submitHandle;
                    texture = _submitTexture;
                    fillHandle = _submitFillHandle;
                    fillTexture = _submitFillTexture;
                    fillActive = _submitFillActive;
                    fpsHandle = _submitFpsHandle;
                    fpsTexture = _submitFpsTexture;
                    fpsActive = _submitFpsActive;
                    hud = HeadLockedSlots[0];
                    turretHud = HeadLockedSlots[1];
                    initToken = _submitInitToken;
                }
                if (overlay == null || handle == 0 || texture.handle == IntPtr.Zero) return;

                // VR may have been turned off (UUVR's F3) after this was queued; OpenVR would no longer exist.
                if (OpenVR.GetInitToken() != initToken) return;

                EVROverlayError error = overlay.SetOverlayTexture(handle, ref texture);
                if (error != EVROverlayError.None) _submitError = (int)error;
                if (fillActive && fillHandle != 0 && fillTexture.handle != IntPtr.Zero)
                {
                    overlay.SetOverlayTexture(fillHandle, ref fillTexture);
                }
                if (fpsActive && fpsHandle != 0 && fpsTexture.handle != IntPtr.Zero)
                {
                    overlay.SetOverlayTexture(fpsHandle, ref fpsTexture);
                }
                if (hud.Active && hud.Handle != 0 && hud.Texture.handle != IntPtr.Zero)
                {
                    overlay.SetOverlayTexture(hud.Handle, ref hud.Texture);
                }
                if (turretHud.Active && turretHud.Handle != 0 && turretHud.Texture.handle != IntPtr.Zero)
                {
                    overlay.SetOverlayTexture(turretHud.Handle, ref turretHud.Texture);
                }
                Interlocked.Increment(ref _submitCount);
            }
            catch (Exception e)
            {
                // Never let an exception escape onto Unity's render thread.
                _submitException = e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>What the render thread needs to send one head-locked picture to SteamVR.</summary>
        private struct SubmitSlot
        {
            public ulong Handle;
            public Texture_t Texture;
            public bool Active;
        }

        /// <summary>
        /// One picture locked to the head (the player HUD or the turret HUD) on its own overlay: above the UI, below the
        /// frame-rate counter, and flipped like the UI, since it is a piece of the same picture.
        /// </summary>
        private sealed class HeadLockedOverlay
        {
            private readonly SteamVrOverlayDisplay _display;
            private readonly ulong _handle;
            private readonly int _slot;
            private readonly string _transformCall;
            private readonly string _widthCall;
            private readonly string _boundsCall;
            private readonly string _showCall;
            private readonly string _hideCall;
            private Texture _texture;
            private bool _shown;
            private Vector3 _appliedPosition = new Vector3(float.NaN, 0f, 0f);
            private Quaternion _appliedRotation;
            private float _appliedWidth = -1f;
            private int _appliedFlip;

            public HeadLockedOverlay(SteamVrOverlayDisplay display, ulong handle, int slot, string label)
            {
                _display = display;
                _handle = handle;
                _slot = slot;
                _transformCall = "SetOverlayTransformTrackedDeviceRelative(" + label + ")";
                _widthCall = "SetOverlayWidthInMeters(" + label + ")";
                _boundsCall = "SetOverlayTextureBounds(" + label + ")";
                _showCall = "ShowOverlay(" + label + ")";
                _hideCall = "HideOverlay(" + label + ")";

                CVROverlay overlay = display._overlay;
                display.Check(overlay.SetOverlaySortOrder(handle, 2), "SetOverlaySortOrder(" + label + ")");
                var bounds = new VRTextureBounds_t { uMin = 0f, uMax = 1f, vMin = 1f, vMax = 0f };
                display.Check(overlay.SetOverlayTextureBounds(handle, ref bounds), _boundsCall);
                overlay.HideOverlay(handle);
                lock (SubmitLock) HeadLockedSlots[slot] = new SubmitSlot { Handle = handle };
            }

            /// <summary>Locked to the head where its settings put it, turned to face the eyes.</summary>
            public void Update(HeadLockedPicture picture)
            {
                CVROverlay overlay = _display._overlay;
                bool show = picture.Texture != null && picture.Size.x > 0.001f;
                if (show)
                {
                    if (!ReferenceEquals(picture.Texture, _texture))
                    {
                        // Asking for the native pointer waits for the render thread, so only when the texture changes.
                        _texture = picture.Texture;
                        IntPtr native = picture.Texture.GetNativeTexturePtr();
                        lock (SubmitLock) HeadLockedSlots[_slot].Texture = new Texture_t { handle = native, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
                    }

                    if (picture.Position != _appliedPosition || picture.Rotation != _appliedRotation)
                    {
                        // Unity's camera space looks along +Z, SteamVR's head space along -Z: mirror Z in the position and rotation.
                        Matrix4x4 r = Matrix4x4.Rotate(picture.Rotation);
                        var transform = new HmdMatrix34_t
                        {
                            m0 = r.m00, m1 = r.m01, m2 = -r.m02, m3 = picture.Position.x,
                            m4 = r.m10, m5 = r.m11, m6 = -r.m12, m7 = picture.Position.y,
                            m8 = -r.m20, m9 = -r.m21, m10 = r.m22, m11 = -picture.Position.z
                        };
                        _display.Check(overlay.SetOverlayTransformTrackedDeviceRelative(_handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform), _transformCall);
                        _appliedPosition = picture.Position;
                        _appliedRotation = picture.Rotation;
                    }
                    if (Mathf.Abs(picture.Size.x - _appliedWidth) > 0.0005f)
                    {
                        _display.Check(overlay.SetOverlayWidthInMeters(_handle, picture.Size.x), _widthCall);
                        _appliedWidth = picture.Size.x;
                    }
                    int flip = Settings.FlipVertically.Value ? 1 : 0;
                    if (flip != _appliedFlip)
                    {
                        var bounds = new VRTextureBounds_t { uMin = 0f, uMax = 1f, vMin = flip == 1 ? 0f : 1f, vMax = flip == 1 ? 1f : 0f };
                        _display.Check(overlay.SetOverlayTextureBounds(_handle, ref bounds), _boundsCall);
                        _appliedFlip = flip;
                    }
                }

                if (show != _shown)
                {
                    _display.Check(show ? overlay.ShowOverlay(_handle) : overlay.HideOverlay(_handle), show ? _showCall : _hideCall);
                    _shown = show;
                    lock (SubmitLock) HeadLockedSlots[_slot].Active = show;
                }
            }

            public void SubmitFromMainThread()
            {
                if (!_shown) return;
                Texture_t texture;
                lock (SubmitLock) texture = HeadLockedSlots[_slot].Texture;
                if (texture.handle != IntPtr.Zero) _display._overlay.SetOverlayTexture(_handle, ref texture);
            }

            /// <summary>Only while OpenVR is still running.</summary>
            public void Destroy()
            {
                _display._overlay.HideOverlay(_handle);
                _display._overlay.DestroyOverlay(_handle);
            }
        }
    }
}
