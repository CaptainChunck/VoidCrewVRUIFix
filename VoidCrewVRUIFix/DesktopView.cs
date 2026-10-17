using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Draws the game window's picture while in VR. Unity's own headset mirror lands on the window after the
    /// screen-space UI, so the UI and cursor never show on the desktop, and it is zoomed in to fill the window.
    /// Instead, the headset's eye image is copied to the window here, cropped to exactly the area the UI panel covers,
    /// before Unity draws the UI on top. The game window then looks and plays like the flat game, with markers lined up.
    /// </summary>
    internal sealed class DesktopView
    {
        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();
        private readonly CommandBuffer _commands = new CommandBuffer { name = "VoidCrewVRUIFix desktop view" };

        public int LastDrawnFrame { get; private set; } = -1;
        public string Problem { get; private set; }
        public Rect Crop { get; private set; }

        public string Describe()
        {
            return string.Format("mode {0}, last drawn frame {1}, eye image crop u {2:0.000}-{3:0.000} v {4:0.000}-{5:0.000}{6}",
                Settings.DesktopView.Value, LastDrawnFrame, Crop.xMin, Crop.xMax, Crop.yMin, Crop.yMax,
                Problem != null ? ", problem: " + Problem : "");
        }

        /// <summary>Called inside the render context once the headset images for this frame are finished.</summary>
        public void Draw(ScriptableRenderContext context, Camera camera)
        {
            XRDisplaySubsystem display = GetRunningDisplay();
            if (display == null)
            {
                Problem = "no running VR display";
                return;
            }

            int passCount = display.GetRenderPassCount();
            if (passCount == 0)
            {
                Problem = "the VR display has no render passes";
                return;
            }

            // UUVR renders multi-pass (one pass per eye); single-pass has one pass with one parameter per eye.
            // The right eye is used, like SteamVR's own mirror.
            XRDisplaySubsystem.XRRenderPass pass;
            display.GetRenderPass(passCount - 1, out pass);
            int parameterCount = pass.GetRenderParameterCount();
            if (parameterCount == 0)
            {
                Problem = "the VR render pass has no eye parameters";
                return;
            }

            XRDisplaySubsystem.XRRenderParameter eye;
            pass.GetRenderParameter(camera, parameterCount - 1, out eye);

            // Where the panel's edges fall in the eye image. For a view direction with slope t (one metre ahead),
            // the projection gives ndc = m00 * t - m02 horizontally and m11 * t - m12 vertically.
            Matrix4x4 projection = eye.projection;
            float halfWidth = PanelLayout.TanHalfWidth;
            float halfHeight = PanelLayout.TanHalfHeight;
            float centre = PanelLayout.HeightOffset / PanelLayout.Distance;
            float uMin = 0.5f + 0.5f * (projection.m00 * -halfWidth - projection.m02);
            float uMax = 0.5f + 0.5f * (projection.m00 * halfWidth - projection.m02);
            float vMin = 0.5f + 0.5f * (projection.m11 * (centre - halfHeight) - projection.m12);
            float vMax = 0.5f + 0.5f * (projection.m11 * (centre + halfHeight) - projection.m12);
            Crop = Rect.MinMaxRect(uMin, vMin, uMax, vMax);

            _commands.Clear();
            _commands.Blit(pass.renderTarget, new RenderTargetIdentifier(Display.main.colorBuffer),
                new Vector2(uMax - uMin, vMax - vMin), new Vector2(uMin, vMin), Mathf.Max(0, eye.textureArraySlice), 0);
            context.ExecuteCommandBuffer(_commands);
            _commands.Clear();

            LastDrawnFrame = Time.frameCount;
            Problem = null;
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
