using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Harmony patches on the game and on Unity. Every target is looked up by name: if a game update renames one,
    /// that single fix is skipped with a warning and the rest of the plugin keeps working.
    /// </summary>
    internal static class GamePatches
    {
        private static FieldInfo _cursorTextureField;
        private static MethodInfo _updateCursorGraphic;

        /// <summary>The game's own cursor image, once its input system has been initialised.</summary>
        public static Texture2D GameCursorTexture
        {
            get { return _cursorTextureField != null ? _cursorTextureField.GetValue(null) as Texture2D : null; }
        }

        public static void Apply(Harmony harmony)
        {
            var applied = new List<string>();
            var skipped = new List<string>();

            Patch(harmony, "SettingUpscaling", "SupportsDLSS", nameof(HideDlss), null, "DLSS option", applied, skipped);
            Patch(harmony, "SettingUpscaling", "SupportsFSR3", nameof(HideFsr3), null, "FSR3 option", applied, skipped);
            Patch(harmony, "VideoSwitcher", "SetupAndPlayVideo", null, nameof(RedirectIntroVideo), "intro video", applied, skipped);
            Patch(harmony, "CG.Input.MouseCursor", "UpdateCursorGraphic", nameof(HideHardwareCursorGraphic), null, "Windows cursor", applied, skipped);

            // Both overloads of each are patched: the one-argument versions are small enough to be inlined into game code.
            Type[] mono = { typeof(Vector3) };
            Type[] withEye = { typeof(Vector3), typeof(Camera.MonoOrStereoscopicEye) };
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "WorldToViewportPoint", mono), nameof(WorldToViewportMono), null, "marker alignment", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "WorldToViewportPoint", withEye), nameof(WorldToViewport), null, "marker alignment (eye)", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "WorldToScreenPoint", mono), nameof(WorldToScreenMono), null, "screen alignment", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "WorldToScreenPoint", withEye), nameof(WorldToScreen), null, "screen alignment (eye)", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "ScreenToWorldPoint", mono), nameof(ScreenToWorldMono), null, "terminal mouse aim", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "ScreenToWorldPoint", withEye), nameof(ScreenToWorld), null, "terminal mouse aim (eye)", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "ScreenPointToRay", mono), nameof(ScreenPointToRayMono), null, "mouse rays", applied, skipped);
            PatchMethod(harmony, AccessTools.Method(typeof(Camera), "ScreenPointToRay", withEye), nameof(ScreenPointToRay), null, "mouse rays (eye)", applied, skipped);

            Type mouseCursor = AccessTools.TypeByName("CG.Input.MouseCursor");
            if (mouseCursor != null)
            {
                _cursorTextureField = AccessTools.Field(mouseCursor, "_mouseCursorTexture");
                _updateCursorGraphic = AccessTools.Method(mouseCursor, "UpdateCursorGraphic");
            }

            Log.Info("Patched: " + string.Join(", ", applied.ToArray()) + ".");
            if (skipped.Count > 0)
            {
                Log.Warn("Could not apply: " + string.Join("; ", skipped.ToArray()) +
                         ". The game may have been updated; those fixes are inactive, everything else still works.");
            }
        }

        /// <summary>Re-applies the game's cursor image choice, so the Windows cursor is hidden or restored to match VR.</summary>
        public static void RefreshHardwareCursor()
        {
            if (_updateCursorGraphic == null || GameCursorTexture == null) return;
            try
            {
                _updateCursorGraphic.Invoke(null, new object[] { true, true });
            }
            catch (Exception e)
            {
                Log.Failure("Updating the Windows cursor", e);
            }
        }

        private static void Patch(Harmony harmony, string typeName, string methodName, string prefix, string postfix,
            string label, List<string> applied, List<string> skipped)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo target = type != null ? AccessTools.DeclaredMethod(type, methodName) : null;
            if (target == null)
            {
                skipped.Add(label + " (" + typeName + "." + methodName + " not found)");
                return;
            }
            PatchMethod(harmony, target, prefix, postfix, label, applied, skipped);
        }

        private static void PatchMethod(Harmony harmony, MethodBase target, string prefix, string postfix,
            string label, List<string> applied, List<string> skipped)
        {
            if (target == null)
            {
                skipped.Add(label + " (method not found)");
                return;
            }

            try
            {
                harmony.Patch(target,
                    prefix != null ? new HarmonyMethod(typeof(GamePatches), prefix) : null,
                    postfix != null ? new HarmonyMethod(typeof(GamePatches), postfix) : null);
                applied.Add(label);
            }
            catch (Exception e)
            {
                skipped.Add(label + " (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        private static bool HideDlss(ref bool __result)
        {
            if (!Settings.DisableDlss.Value || !UuvrCompat.Installed) return true;
            __result = false;
            Log.InfoOnce("dlss", "DLSS removed from the graphics options because it breaks VR rendering. A saved DLSS setting falls back to 'None'.");
            return false;
        }

        private static bool HideFsr3(ref bool __result)
        {
            if (!Settings.DisableFsr3.Value || !UuvrCompat.Installed) return true;
            __result = false;
            Log.InfoOnce("fsr3", "FSR3 removed from the graphics options because it is not built for VR rendering.");
            return false;
        }

        private static void RedirectIntroVideo(MonoBehaviour __instance)
        {
            if (!Settings.FixIntroVideo.Value || !XrState.IsActive) return;
            IntroVideo.Redirect(__instance.GetComponent<VideoPlayer>());
        }

        private static void HideHardwareCursorGraphic(ref bool showTexture)
        {
            if (XrState.IsActive && Settings.HideHardwareCursor.Value && Settings.ShowVrCursor.Value)
            {
                showTexture = false;
            }
        }

        // Camera projection prefixes: replace the result for the player camera while VR is on, otherwise run Unity's.

        private static bool WorldToViewportMono(Camera __instance, Vector3 position, ref Vector3 __result)
        {
            return WorldToViewport(__instance, position, Camera.MonoOrStereoscopicEye.Mono, ref __result);
        }

        private static bool WorldToViewport(Camera __instance, Vector3 position, Camera.MonoOrStereoscopicEye eye, ref Vector3 __result)
        {
            if (!MarkerAlignment.Applies(__instance, eye)) return true;
            __result = MarkerAlignment.WorldToViewport(__instance, position);
            return false;
        }

        private static bool WorldToScreenMono(Camera __instance, Vector3 position, ref Vector3 __result)
        {
            return WorldToScreen(__instance, position, Camera.MonoOrStereoscopicEye.Mono, ref __result);
        }

        private static bool WorldToScreen(Camera __instance, Vector3 position, Camera.MonoOrStereoscopicEye eye, ref Vector3 __result)
        {
            if (!MarkerAlignment.Applies(__instance, eye)) return true;
            __result = MarkerAlignment.WorldToScreen(__instance, position);
            return false;
        }

        private static bool ScreenToWorldMono(Camera __instance, Vector3 position, ref Vector3 __result)
        {
            return ScreenToWorld(__instance, position, Camera.MonoOrStereoscopicEye.Mono, ref __result);
        }

        private static bool ScreenToWorld(Camera __instance, Vector3 position, Camera.MonoOrStereoscopicEye eye, ref Vector3 __result)
        {
            if (!MarkerAlignment.Applies(__instance, eye)) return true;
            __result = MarkerAlignment.ScreenToWorld(__instance, position);
            return false;
        }

        private static bool ScreenPointToRayMono(Camera __instance, Vector3 pos, ref Ray __result)
        {
            return ScreenPointToRay(__instance, pos, Camera.MonoOrStereoscopicEye.Mono, ref __result);
        }

        private static bool ScreenPointToRay(Camera __instance, Vector3 pos, Camera.MonoOrStereoscopicEye eye, ref Ray __result)
        {
            if (!MarkerAlignment.Applies(__instance, eye)) return true;
            __result = MarkerAlignment.ScreenToRay(__instance, pos);
            return false;
        }
    }
}
