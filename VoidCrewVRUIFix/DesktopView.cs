using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Builds the game window's picture while in VR. With only VR cameras rendering, Unity draws no screen-space UI
    /// onto the window, and its own headset mirror is zoomed in. So the picture is assembled here instead: the
    /// headset's right-eye image, then the captured UI (with the cursor) placed where the UI panel appears in the
    /// headset, then one copy onto the window. The game window shows what the player sees, and can be played from.
    /// </summary>
    internal sealed class DesktopView
    {
        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();
        private readonly CommandBuffer _commands = new CommandBuffer { name = "VoidCrewVRUIFix desktop view" };
        private RenderTexture _frame;
        private Material _uiMaterial;
        private Mesh _quad;
        private Rect _uiRect = new Rect(0f, 0f, 1f, 1f);
        private bool _sceneReady;

        private Material _fillMaterial;

        public int LastDrawnFrame { get; private set; } = -1;
        public string Problem { get; private set; }
        public float ViewWidthDegrees { get; private set; }
        public float ViewAimDegrees { get; private set; }

        /// <summary>Colour to fill the whole picture with behind the UI while a full-screen UI layer shows.</summary>
        public Color Fill;

        public string Describe()
        {
            return string.Format("mode {0}, last drawn frame {1}, view {2:0}° wide aimed {3:0.0}° up, UI area x {4:0.00}-{5:0.00} y {6:0.00}-{7:0.00}{8}",
                Settings.DesktopView.Value, LastDrawnFrame, ViewWidthDegrees, ViewAimDegrees, _uiRect.xMin, _uiRect.xMax, _uiRect.yMin, _uiRect.yMax,
                Problem != null ? ", problem: " + Problem : "");
        }

        /// <summary>Step 1, once the frame's headset images are finished: the scene (or black) into the window picture.</summary>
        public void DrawScene(ScriptableRenderContext context, Camera camera)
        {
            if (!EnsureFrame()) return;

            _commands.Clear();
            _uiRect = new Rect(0f, 0f, 1f, 1f);
            bool sceneDrawn = Settings.DesktopView.Value == DesktopViewMode.PanelView && TryAddEyeImage(camera);
            if (!sceneDrawn)
            {
                _commands.SetRenderTarget(_frame);
                _commands.ClearRenderTarget(false, true, Color.black);
            }
            context.ExecuteCommandBuffer(_commands);
            _commands.Clear();
            _sceneReady = true;
        }

        /// <summary>Step 2, after the UI was captured: the UI over the scene, then the finished picture onto the window.</summary>
        public void Composite(CommandBuffer commands, RenderTexture ui)
        {
            if (!_sceneReady || _frame == null) return;
            _sceneReady = false;

            if (_uiMaterial == null) _uiMaterial = RenderUtil.CreateUiMaterial("VoidCrewVRUIFix Desktop UI");
            if (_quad == null) _quad = RenderUtil.CreateQuad(false);

            commands.SetRenderTarget(_frame);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f));
            if (Fill.a > 0.003f)
            {
                if (_fillMaterial == null)
                {
                    _fillMaterial = RenderUtil.CreateUiMaterial("VoidCrewVRUIFix Desktop Fill");
                    _fillMaterial.mainTexture = Texture2D.whiteTexture;
                }
                _fillMaterial.color = Fill;
                commands.DrawMesh(_quad, Matrix4x4.TRS(new Vector3(0.5f, 0.5f, 0f), Quaternion.identity, Vector3.one), _fillMaterial, 0, 0);
            }

            if (ui != null)
            {
                _uiMaterial.mainTexture = ui;
                commands.DrawMesh(_quad,
                    Matrix4x4.TRS(new Vector3(_uiRect.center.x, _uiRect.center.y, 0f), Quaternion.identity, new Vector3(_uiRect.width, _uiRect.height, 1f)),
                    _uiMaterial, 0, 0);
            }

            commands.Blit(_frame, new RenderTargetIdentifier(Display.main.colorBuffer));
            LastDrawnFrame = Time.frameCount;
        }

        public void Release()
        {
            if (_frame != null)
            {
                _frame.Release();
                Object.Destroy(_frame);
                _frame = null;
            }
            if (_uiMaterial != null) Object.Destroy(_uiMaterial);
            if (_fillMaterial != null) Object.Destroy(_fillMaterial);
            if (_quad != null) Object.Destroy(_quad);
            _uiMaterial = null;
            _fillMaterial = null;
            _quad = null;
        }

        private bool TryAddEyeImage(Camera camera)
        {
            XRDisplaySubsystem display = GetRunningDisplay();
            if (display == null)
            {
                Problem = "no running VR display";
                return false;
            }

            int passCount = display.GetRenderPassCount();
            if (passCount == 0)
            {
                Problem = "the VR display has no render passes";
                return false;
            }

            // UUVR renders multi-pass (one pass per eye); single-pass has one pass with one parameter per eye.
            // The right eye is used, like SteamVR's own mirror.
            XRDisplaySubsystem.XRRenderPass pass;
            display.GetRenderPass(passCount - 1, out pass);
            int parameterCount = pass.GetRenderParameterCount();
            if (parameterCount == 0)
            {
                Problem = "the VR render pass has no eye parameters";
                return false;
            }

            XRDisplaySubsystem.XRRenderParameter eye;
            pass.GetRenderParameter(camera, parameterCount - 1, out eye);
            Matrix4x4 projection = eye.projection;

            // A view direction with slope t (one metre ahead) lands at ndc = m00 * t - m02 across, m11 * t - m12 up.
            // How far the eye image reaches in each direction:
            float maxX = Mathf.Min((1f + projection.m02) / projection.m00, (1f - projection.m02) / projection.m00);
            float maxUp = (1f + projection.m12) / projection.m11;
            float maxDown = (1f - projection.m12) / projection.m11;

            // The view is centred on the UI panel (which "UI screen height" can raise or lower), then aimed up or down by
            // Desktop View Height. Otherwise a raised panel sits high in the window and its top is cut off when the
            // view is narrowed to fit the panel.
            float aim = PanelLayout.HeightOffset / PanelLayout.Distance + Mathf.Tan(Mathf.Clamp(Settings.DesktopViewHeight.Value, -30f, 30f) * Mathf.Deg2Rad);
            aim = Mathf.Clamp(aim, -0.8f * maxDown, 0.8f * maxUp);
            float maxY = Mathf.Min(maxUp - aim, maxDown + aim);

            float aspect = (float)_frame.width / _frame.height;
            float tanX = Mathf.Tan(Mathf.Clamp(Settings.DesktopViewWidth.Value, 30f, 170f) * 0.5f * Mathf.Deg2Rad);
            float tanY = tanX / aspect;
            float fit = Mathf.Min(1f, Mathf.Min(maxX / tanX, maxY / tanY));
            tanX *= fit;
            tanY *= fit;
            ViewWidthDegrees = 2f * Mathf.Atan(tanX) * Mathf.Rad2Deg;
            ViewAimDegrees = Mathf.Atan(aim) * Mathf.Rad2Deg;

            float uMin = 0.5f + 0.5f * (projection.m00 * -tanX - projection.m02);
            float uMax = 0.5f + 0.5f * (projection.m00 * tanX - projection.m02);
            float vMin = 0.5f + 0.5f * (projection.m11 * (aim - tanY) - projection.m12);
            float vMax = 0.5f + 0.5f * (projection.m11 * (aim + tanY) - projection.m12);

            // HDRP writes headset images top row first (the way the headset displays them), while Unity's textures
            // count rows from the bottom, so image height h is texture coordinate 1 - h.
            _commands.Blit(pass.renderTarget, _frame,
                new Vector2(uMax - uMin, vMin - vMax), new Vector2(uMin, 1f - vMin),
                Mathf.Max(0, eye.textureArraySlice), 0);

            // Where the UI panel sits within this view, so the UI lines up with the scene like it does in the headset.
            float centre = (PanelLayout.HeightOffset / PanelLayout.Distance - aim) / tanY;
            float width = PanelLayout.TanHalfWidth / tanX;
            float height = PanelLayout.TanHalfHeight / tanY;
            _uiRect = Rect.MinMaxRect(0.5f - 0.5f * width, 0.5f + 0.5f * (centre - height), 0.5f + 0.5f * width, 0.5f + 0.5f * (centre + height));

            Problem = null;
            return true;
        }

        private bool EnsureFrame()
        {
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0) return false;
            if (_frame != null && _frame.width == width && _frame.height == height && _frame.IsCreated()) return true;

            if (_frame != null)
            {
                _frame.Release();
                Object.Destroy(_frame);
            }
            _frame = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "VoidCrewVRUIFix_Desktop",
                hideFlags = HideFlags.HideAndDontSave
            };
            _frame.Create();
            return true;
        }

        private static XRDisplaySubsystem GetRunningDisplay()
        {
            Displays.Clear();
            SubsystemManager.GetInstances(Displays);
            foreach (XRDisplaySubsystem display in Displays)
            {
                if (display.running) return display;
            }
            return null;
        }
    }
}
