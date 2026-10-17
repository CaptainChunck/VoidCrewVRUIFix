using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
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

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void RenderEventCallback(int eventId, IntPtr data);

        // Shared with the render-thread callback. Written on the main thread under SubmitLock.
        private static readonly object SubmitLock = new object();
        private static CVROverlay _submitOverlay;
        private static ulong _submitHandle;
        private static Texture_t _submitTexture;
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
        private readonly uint _initToken;
        private bool _shown;
        private bool _needsRecenter = true;
        private Vector3 _center;
        private ETrackingUniverseOrigin _origin = ETrackingUniverseOrigin.TrackingUniverseStanding;
        private float _centerDistance;
        private float _centerOffset;
        private float _appliedWidth = -1f;
        private int _appliedFlip = -1;
        private PanelAnchor _appliedAnchor;
        private Vector3 _appliedPosition = new Vector3(float.NaN, 0f, 0f);
        private int _reportedError;
        private string _reportedException;

        private SteamVrOverlayDisplay(CVROverlay overlay, ulong handle)
        {
            _overlay = overlay;
            _handle = handle;
            _initToken = OpenVR.GetInitToken();

            Check(_overlay.SetOverlayFlag(handle, VROverlayFlags.IsPremultiplied, true), "SetOverlayFlag(IsPremultiplied)");
            Check(_overlay.SetOverlaySortOrder(handle, 0), "SetOverlaySortOrder");
            _overlay.HideOverlay(handle);
        }

        public string Name { get { return "SteamVR overlay"; } }

        public bool IsAlive { get { return OpenVR.GetInitToken() == _initToken; } }

        public string Status
        {
            get
            {
                return string.Format(
                    "handle 0x{0:X}, shown {1}, tracking space {2}, centre {3}, frames submitted {4} ({5}), last SteamVR error {6}{7}",
                    _handle, _shown, _origin, _center, Interlocked.Read(ref _submitCount),
                    Settings.SubmitOverlayOnRenderThread.Value ? "render thread" : "main thread",
                    (EVROverlayError)_submitError,
                    _submitException != null ? ", last exception " + _submitException : "");
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

            reason = null;
            return new SteamVrOverlayDisplay(overlay, handle);
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

            ReportRenderThreadProblems();
        }

        public void AppendSubmit(CommandBuffer commands)
        {
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
            }

            // If VR was already turned off, OpenVR is gone and the overlay with it. Calling into it would crash.
            if (!IsAlive) return;
            _overlay.HideOverlay(_handle);
            _overlay.DestroyOverlay(_handle);
        }

        private void ApplyPlacement()
        {
            PanelAnchor anchor = Settings.Anchor.Value;
            Vector3 position;
            if (anchor == PanelAnchor.Head)
            {
                position = new Vector3(0f, PanelLayout.HeightOffset, -PanelLayout.Distance);
            }
            else
            {
                if (_centerDistance != PanelLayout.Distance || _centerOffset != PanelLayout.HeightOffset) _needsRecenter = true;
                if (_needsRecenter) RecenterNow();
                position = _center;
            }

            if (anchor == _appliedAnchor && position == _appliedPosition) return;

            // Identity rotation: OpenVR looks down -Z and an overlay faces +Z, so the panel faces the player.
            var transform = new HmdMatrix34_t { m0 = 1f, m5 = 1f, m10 = 1f, m3 = position.x, m7 = position.y, m11 = position.z };
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
        }

        /// <summary>
        /// Places the panel straight ahead along the tracking space's forward axis, at the headset's current position.
        /// With UUVR's RelativeTransform tracking, tracking-space forward is the in-game view direction, which keeps
        /// the panel (and the markers on it) lined up with the world when you turn your head.
        /// </summary>
        private void RecenterNow()
        {
            CVRCompositor compositor = OpenVR.Compositor;
            _origin = compositor != null ? compositor.GetTrackingSpace() : ETrackingUniverseOrigin.TrackingUniverseStanding;

            Vector3 head;
            bool valid = TryGetHeadPosition(_origin, out head);
            if (!valid)
            {
                head = new Vector3(0f, _origin == ETrackingUniverseOrigin.TrackingUniverseStanding ? 1.6f : 0f, 0f);
            }

            _centerDistance = PanelLayout.Distance;
            _centerOffset = PanelLayout.HeightOffset;
            _center = new Vector3(head.x, head.y + _centerOffset, head.z - _centerDistance);
            _needsRecenter = !valid;
            Log.Verbose("SteamVR overlay centred at " + _center + " in " + _origin + (valid ? "" : " (headset pose not available yet, using a default height)"));
        }

        private static bool TryGetHeadPosition(ETrackingUniverseOrigin origin, out Vector3 position)
        {
            position = Vector3.zero;
            CVRSystem system = OpenVR.System;
            if (system == null) return false;

            system.GetDeviceToAbsoluteTrackingPose(origin, 0f, Poses);
            if (!Poses[0].bPoseIsValid) return false;

            HmdMatrix34_t pose = Poses[0].mDeviceToAbsoluteTracking;
            position = new Vector3(pose.m3, pose.m7, pose.m11);
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
                uint initToken;
                lock (SubmitLock)
                {
                    overlay = _submitOverlay;
                    handle = _submitHandle;
                    texture = _submitTexture;
                    initToken = _submitInitToken;
                }
                if (overlay == null || handle == 0 || texture.handle == IntPtr.Zero) return;

                // VR may have been turned off (UUVR's F3) after this was queued; OpenVR would no longer exist.
                if (OpenVR.GetInitToken() != initToken) return;

                EVROverlayError error = overlay.SetOverlayTexture(handle, ref texture);
                if (error != EVROverlayError.None) _submitError = (int)error;
                Interlocked.Increment(ref _submitCount);
            }
            catch (Exception e)
            {
                // Never let an exception escape onto Unity's render thread.
                _submitException = e.GetType().Name + ": " + e.Message;
            }
        }
    }
}
