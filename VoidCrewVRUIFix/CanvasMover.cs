using System.Collections.Generic;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Some HUD canvases are "Screen Space - Camera": the game renders them as a flat sheet a short distance in front
    /// of the main camera. In VR that sheet sits in the 3D scene right in front of your eyes, where it is doubled,
    /// blurred by depth of field and hard to focus on. Switching them to "Screen Space - Overlay" puts them on the
    /// VR UI panel with everything else. They are switched back if VR is turned off.
    /// </summary>
    internal sealed class CanvasMover
    {
        private readonly List<Canvas> _moved = new List<Canvas>();
        private readonly HashSet<string> _reportedNames = new HashSet<string>();
        private readonly List<string> _newlyReported = new List<string>();

        public int MovedCount { get { return _moved.Count; } }

        public void Scan()
        {
            if (!Settings.MoveCameraCanvasesToPanel.Value) return;

            _moved.RemoveAll(c => c == null);
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceCamera) continue;

                // Canvases drawn by a camera that renders into a texture (icons, in-world screens) are left alone.
                Camera camera = canvas.worldCamera;
                if (camera != null && (camera.targetTexture != null || camera.stereoTargetEye == StereoTargetEyeMask.None)) continue;

                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _moved.Add(canvas);
                if (_reportedNames.Add(canvas.name)) _newlyReported.Add(canvas.name);
                else Log.Verbose("Moved canvas '" + canvas.name + "' onto the VR UI panel again.");
            }

            if (_newlyReported.Count > 0)
            {
                Log.Info("Moved " + _newlyReported.Count + " camera-space canvas(es) onto the VR UI panel: " + string.Join(", ", _newlyReported.ToArray()));
                _newlyReported.Clear();
            }
        }

        public void RestoreAll()
        {
            foreach (Canvas canvas in _moved)
            {
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                }
            }
            _moved.Clear();
        }
    }
}
