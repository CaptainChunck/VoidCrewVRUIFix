using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Spots full-screen UI layers (the death screen, the black boot/spawn screen, fades to black) and works out the
    /// colour that should fill the rest of the player's view around the UI panel. Each frame the UI image is shrunk to
    /// 16 x 9 samples on the GPU and read back; if its border is one even colour and opacity, the UI is covering the
    /// whole screen, so the view outside the panel is filled with that colour at that opacity. Fades therefore fade
    /// the whole view, and opaque screens fill it completely.
    /// </summary>
    internal sealed class FullscreenFill
    {
        private const int Columns = 16;
        private const int Rows = 9;
        private const float AlphaTolerance = 0.08f;
        private const float RequiredShare = 0.85f;

        private readonly List<Color32> _border = new List<Color32>(Columns * 2 + Rows * 2);
        private readonly List<float> _sorted = new List<float>(Columns * 2 + Rows * 2);
        private RenderTexture _samples;
        private bool _readbackPending;

        /// <summary>The fill colour (gamma space, straight alpha). Alpha 0 means no full-screen UI right now.</summary>
        public Color Current { get; private set; } = Color.clear;

        public void Sample(CommandBuffer commands, RenderTexture ui)
        {
            if (_readbackPending || ui == null || !SystemInfo.supportsAsyncGPUReadback) return;
            if (_samples == null)
            {
                // Explicit RGBA byte order, read back as stored (no format conversion), so the bytes are exactly the UI's.
                _samples = new RenderTexture(Columns, Rows, 0, GraphicsFormat.R8G8B8A8_SRGB)
                {
                    name = "VoidCrewVRUIFix_EdgeSamples",
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
                _samples.Create();
            }

            commands.Blit(ui, _samples);
            commands.RequestAsyncReadback(_samples, OnReadback);
            _readbackPending = true;
        }

        public void Release()
        {
            if (_samples == null) return;
            _samples.Release();
            Object.Destroy(_samples);
            _samples = null;
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            if (request.hasError) return;

            NativeArray<byte> data = request.GetData<byte>();
            if (data.Length < Columns * Rows * 4) return;

            _border.Clear();
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (y != 0 && y != Rows - 1 && x != 0 && x != Columns - 1) continue;
                    int i = (y * Columns + x) * 4;
                    _border.Add(new Color32(data[i], data[i + 1], data[i + 2], data[i + 3]));
                }
            }

            float alpha = Median(c => c.a / 255f);
            int even = 0;
            foreach (Color32 sample in _border)
            {
                if (Mathf.Abs(sample.a / 255f - alpha) <= AlphaTolerance) even++;
            }

            if (alpha < 0.03f || even < _border.Count * RequiredShare)
            {
                Current = Color.clear;
                return;
            }

            // The UI image stores colours already multiplied by their opacity; undo that for the fill colour.
            float divide = Mathf.Max(alpha, 0.001f);
            Current = new Color(
                Mathf.Clamp01(Median(c => c.r / 255f) / divide),
                Mathf.Clamp01(Median(c => c.g / 255f) / divide),
                Mathf.Clamp01(Median(c => c.b / 255f) / divide),
                alpha > 0.97f ? 1f : alpha);
        }

        private float Median(System.Func<Color32, float> channel)
        {
            _sorted.Clear();
            foreach (Color32 sample in _border) _sorted.Add(channel(sample));
            _sorted.Sort();
            return _sorted.Count == 0 ? 0f : _sorted[_sorted.Count / 2];
        }
    }
}
