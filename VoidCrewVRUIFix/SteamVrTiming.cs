using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Valve.VR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// SteamVR's own frame timing, for the diagnostics report. When the game can't finish a frame within one headset
    /// refresh, SteamVR runs it at a half or a third of the refresh rate and shows each frame more than once, which is
    /// why the frame rate locks to steps like 45 or 30 FPS at 90 Hz. This shows which step it is on and why.
    /// Only called with OpenVR; kept in its own class so the OpenVR library is only loaded when it is used.
    /// </summary>
    internal static class SteamVrTiming
    {
        private const uint FramesToAverage = 30;

        // Compositor_FrameTiming.m_nReprojectionFlags bits, from openvr.h.
        private const uint ReprojectionReasonCpu = 0x01;
        private const uint ReprojectionReasonGpu = 0x02;
        private const uint ReprojectionMotion = 0x08;
        private const uint ThrottleMask = 0xF00;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Describe()
        {
            CVRCompositor compositor = OpenVR.Compositor;
            if (compositor == null) return "SteamVR's compositor is not available";

            float refreshRate = 0f;
            CVRSystem system = OpenVR.System;
            if (system != null)
            {
                ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
                refreshRate = system.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd, ETrackedDeviceProperty.Prop_DisplayFrequency_Float, ref error);
                if (error != ETrackedPropertyError.TrackedProp_Success) refreshRate = 0f;
            }

            var timing = new Compositor_FrameTiming { m_nSize = (uint)Marshal.SizeOf(typeof(Compositor_FrameTiming)) };
            int frames = 0;
            int cpuLate = 0;
            int gpuLate = 0;
            int motionSmoothed = 0;
            uint presents = 0;
            uint mostThrottled = 0;
            float gpuMs = 0f;
            float intervalMs = 0f;
            // 0 is the frame still being made, so start with the last finished one.
            for (uint framesAgo = 1; framesAgo <= FramesToAverage; framesAgo++)
            {
                if (!compositor.GetFrameTiming(ref timing, framesAgo)) break;
                frames++;
                presents += timing.m_nNumFramePresents;
                gpuMs += timing.m_flTotalRenderGpuMs;
                intervalMs += timing.m_flClientFrameIntervalMs;
                if ((timing.m_nReprojectionFlags & ReprojectionReasonCpu) != 0) cpuLate++;
                if ((timing.m_nReprojectionFlags & ReprojectionReasonGpu) != 0) gpuLate++;
                if ((timing.m_nReprojectionFlags & ReprojectionMotion) != 0) motionSmoothed++;
                uint throttled = (timing.m_nReprojectionFlags & ThrottleMask) >> 8;
                if (throttled > mostThrottled) mostThrottled = throttled;
            }

            string headset = refreshRate > 0f ? "headset " + refreshRate.ToString("0") + " Hz" : "headset refresh rate unknown";
            if (frames == 0) return headset + ", no frame timing from SteamVR yet";

            float shown = presents > 0 ? (float)presents / frames : 1f;
            float averageGpu = gpuMs / frames;
            string budget = refreshRate > 0f ? " (one refresh is " + (1000f / refreshRate).ToString("0.0") + " ms)" : "";
            string fps = refreshRate > 0f ? " = about " + (refreshRate / shown).ToString("0") + " FPS" : "";
            return headset +
                   " | each game frame shown " + shown.ToString("0.0") + " times" + fps +
                   (mostThrottled > 0 ? ", SteamVR throttling the game by up to " + mostThrottled + " refreshes" : "") +
                   " | game GPU time " + averageGpu.ToString("0.0") + " ms per frame" + budget +
                   ", time between game frames " + (intervalMs / frames).ToString("0.0") + " ms" +
                   " | late frames over the last " + frames + ": GPU " + gpuLate + ", CPU " + cpuLate +
                   ", motion smoothing used on " + motionSmoothed;
        }
    }
}
