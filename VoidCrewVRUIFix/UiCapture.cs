using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Captures all screen-space UI (UI Toolkit panels and uGUI overlay canvases) into one texture every frame.
    /// While VR is on, HDRP leaves the screen-space UI to Unity, which draws it only onto the desktop window. After HDRP
    /// has rendered the frame we ask Unity to draw that UI once more, into our texture. Unity still draws it onto the
    /// desktop window afterwards as normal. The texture is cleared every frame, so closed menus and moving markers
    /// can't leave ghost images behind.
    /// </summary>
    internal sealed class UiCapture
    {
        private readonly CommandBuffer _commands = new CommandBuffer { name = "VoidCrewVRUIFix UI capture" };

        public RenderTexture Texture { get; private set; }
        public bool Enabled;
        public float BackdropOpacity;
        public int LastCaptureFrame { get; private set; } = -1;
        public long CaptureCount { get; private set; }
        public Camera LastScreenCamera { get; private set; }

        /// <summary>Called when the texture is (re)created, before anything is drawn into it.</summary>
        public Action<RenderTexture> TextureCreated;

        /// <summary>Called inside the render context right after the UI was drawn, so displays can queue their submit.</summary>
        public Action<CommandBuffer> AfterCapture;

        public void Start()
        {
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
        }

        public void Stop()
        {
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
        }

        public void Release()
        {
            if (Texture == null) return;
            Texture.Release();
            UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }

        private void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (!Enabled || LastCaptureFrame == Time.frameCount) return;
            try
            {
                Capture(context, cameras);
            }
            catch (Exception e)
            {
                Log.Failure("Capturing the UI", e);
            }
        }

        private void Capture(ScriptableRenderContext context, List<Camera> cameras)
        {
            // HDRP also renders for icon cameras and reflection probes. Only the render that draws to the screen counts.
            Camera screenCamera = null;
            for (int i = 0; i < cameras.Count; i++)
            {
                Camera camera = cameras[i];
                if (camera != null && camera.cameraType == CameraType.Game && camera.targetTexture == null)
                {
                    screenCamera = camera;
                    break;
                }
            }
            if (screenCamera == null || !EnsureTexture()) return;

            LastCaptureFrame = Time.frameCount;
            LastScreenCamera = screenCamera;

            _commands.Clear();
            _commands.SetRenderTarget(Texture);
            _commands.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, Mathf.Clamp01(BackdropOpacity)));
            context.ExecuteCommandBuffer(_commands);
            _commands.Clear();

            // While VR is active HDRP reports that it does not render the UI overlay. Claim it for the duration of our
            // draw, then hand it back so Unity still draws the UI onto the desktop window.
            SupportedRenderingFeatures features = SupportedRenderingFeatures.active;
            bool previous = features != null && features.rendersUIOverlay;
            if (features != null) features.rendersUIOverlay = true;
            try
            {
                context.DrawUIOverlay(screenCamera);
                if (AfterCapture != null) AfterCapture(_commands);
                context.ExecuteCommandBuffer(_commands);
                _commands.Clear();
                context.Submit();
            }
            finally
            {
                if (features != null) features.rendersUIOverlay = previous;
            }
            CaptureCount++;
        }

        private bool EnsureTexture()
        {
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0) return false;
            if (Texture != null && Texture.width == width && Texture.height == height && Texture.IsCreated()) return true;

            Release();
            Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "VoidCrewVRUIFix_UI",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            Texture.Create();
            Log.Info("Created the VR UI texture (" + width + "x" + height + ", the size of the game window).");
            if (TextureCreated != null) TextureCreated(Texture);
            return true;
        }
    }
}
