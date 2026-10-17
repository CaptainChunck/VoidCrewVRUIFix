using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidCrewVRUIFix
{
    /// <summary>Per-frame inputs for placing the UI panel, gathered just before the frame renders.</summary>
    internal struct PanelState
    {
        public bool Visible;
        public Camera Camera;
        public Vector3 CameraPosition;
        public Quaternion CameraRotation;

        /// <summary>Colour to fill the rest of the view with while a full-screen UI layer shows; alpha 0 = none.</summary>
        public Color Fill;

        /// <summary>The frame-rate counter's picture, shown locked to the head; null when it is turned off.</summary>
        public Texture FpsTexture;

        /// <summary>The player status HUD, shown locked to the head; its texture is null when it isn't shown.</summary>
        public HeadLockedPicture Hud;

        /// <summary>The turret's readouts while you control a turret, shown locked to the head.</summary>
        public HeadLockedPicture TurretHud;
    }

    /// <summary>A way of showing the captured UI texture in the headset.</summary>
    internal interface IUiDisplay
    {
        string Name { get; }

        /// <summary>False once the display can no longer work (for example VR was turned off underneath it).</summary>
        bool IsAlive { get; }

        /// <summary>One line of live status for the diagnostics report.</summary>
        string Status { get; }

        void SetTexture(RenderTexture texture);

        /// <summary>Main thread, just before rendering.</summary>
        void Update(PanelState state);

        /// <summary>Inside the render context, right after the UI was captured.</summary>
        void AppendSubmit(CommandBuffer commands);

        /// <summary>End of frame on the main thread, used when render-thread submission is turned off.</summary>
        void SubmitFromMainThread();

        void Recenter();

        /// <summary>A player-readable description of a problem if the display is silently not working, otherwise null.</summary>
        string CheckHealth();

        void Shutdown();
    }

    internal static class SteamVrOverlayFactory
    {
        /// <summary>
        /// Kept in its own non-inlined method so that a missing or changed UUVR OpenVR assembly surfaces as an
        /// exception the caller can catch, rather than a failure to load the calling code.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static IUiDisplay TryCreate(out string reason)
        {
            return SteamVrOverlayDisplay.TryCreate(out reason);
        }
    }
}
