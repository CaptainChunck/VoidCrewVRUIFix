using UnityEngine;
using UnityEngine.Rendering;

namespace VoidCrewVRUIFix
{
    /// <summary>Small rendering helpers shared by the in-game panel and the desktop view.</summary>
    internal static class RenderUtil
    {
        /// <summary>A 1 x 1 quad centred on the origin with white vertex colours, optionally with its texture flipped.</summary>
        public static Mesh CreateQuad(bool flipped)
        {
            float bottom = flipped ? 1f : 0f;
            float top = flipped ? 0f : 1f;
            var mesh = new Mesh
            {
                name = flipped ? "VoidCrewVRUIFix Quad (flipped)" : "VoidCrewVRUIFix Quad",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
                },
                uv = new[]
                {
                    new Vector2(0f, bottom), new Vector2(1f, bottom),
                    new Vector2(1f, top), new Vector2(0f, top)
                },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// A material for drawing the captured UI texture. uGUI's default material is always in the build, needs no
        /// HDRP setup, and can be told to ignore depth so the UI is always on top.
        /// </summary>
        public static Material CreateUiMaterial(string name)
        {
            var material = new Material(Canvas.GetDefaultCanvasMaterial())
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            material.SetVector("_TextureSampleAdd", Vector4.zero);
            return material;
        }
    }
}
