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
        private RenderTexture _sink;

        public RenderTexture Texture { get; private set; }
        public bool Enabled;
        public float BackdropOpacity;
        public int LastCaptureFrame { get; private set; } = -1;
        public long CaptureCount { get; private set; }
        public Camera LastScreenCamera { get; private set; }

        /// <summary>Called when the texture is (re)created, before anything is drawn into it.</summary>
        public Action<RenderTexture> TextureCreated;

        /// <summary>Called inside the render context right after the UI was drawn, so displays can queue their submit.</summary>
        public Action<CommandBuffer, RenderTexture> AfterCapture;

        /// <summary>Called inside the render context once the frame's headset images are finished, before the capture.</summary>
        public Action<ScriptableRenderContext, Camera> FrameRendered;

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
            if (_sink != null)
            {
                _sink.Release();
                UnityEngine.Object.Destroy(_sink);
                _sink = null;
            }
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
            if (screenCamera == null) return;

            LastCaptureFrame = Time.frameCount;
            LastScreenCamera = screenCamera;

            if (FrameRendered != null)
            {
                try
                {
                    FrameRendered(context, screenCamera);
                }
                catch (Exception e)
                {
                    Log.Failure("Drawing the desktop view", e);
                }
            }

            if (!EnsureTexture())
            {
                BindSink(context);
                context.Submit(); // still send the desktop view
                return;
            }

            _commands.Clear();
            _commands.SetRenderTarget(Texture);
            _commands.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, Mathf.Clamp01(BackdropOpacity)));
            context.ExecuteCommandBuffer(_commands);
            _commands.Clear();

            // While VR is active HDRP reports that it does not render the UI overlay. Claim it for the duration of our
            // draw, then hand it back.
            SupportedRenderingFeatures features = SupportedRenderingFeatures.active;
            bool previous = features != null && features.rendersUIOverlay;
            if (features != null) features.rendersUIOverlay = true;
            try
            {
                context.DrawUIOverlay(screenCamera);
                if (AfterCapture != null) AfterCapture(_commands, Texture);
                context.ExecuteCommandBuffer(_commands);
                _commands.Clear();
                BindSink(context);
                context.Submit();
            }
            finally
            {
                if (features != null) features.rendersUIOverlay = previous;
            }
            CaptureCount++;
        }

        /// <summary>
        /// After the render pipeline finishes, Unity draws the screen-space UI once more into whatever image was used
        /// last. That has to go nowhere: on the game window it doubles the UI and cursor (the desktop picture already
        /// has them), and in an eye image it is the "UI duplicated in the right eye" seen with UUVR alone.
        /// </summary>
        private void BindSink(ScriptableRenderContext context)
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (_sink == null || _sink.width != width || _sink.height != height || !_sink.IsCreated())
            {
                if (_sink != null)
                {
                    _sink.Release();
                    UnityEngine.Object.Destroy(_sink);
                }
                _sink = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "VoidCrewVRUIFix_UnusedUiDraw",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _sink.Create();
            }

            _commands.Clear();
            _commands.SetRenderTarget(_sink);
            context.ExecuteCommandBuffer(_commands);
            _commands.Clear();
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
