using System;
using BepInEx.Configuration;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// Where one head-locked piece of the VR UI sits in your view: left/right and up/down (degrees), forwards and
    /// backwards (distance in metres), its size, and whether it follows your head at all. Shared by the HUD layers, the
    /// FPS counter and the Modify UI window.
    /// </summary>
    internal sealed class LayerPlacement
    {
        /// <summary>Pieces that aren't the one being adjusted are drawn this see-through while the Modify UI window is open.</summary>
        private const float EditingAlpha = 0.45f;

        public readonly string Title;
        public readonly string Description;

        /// <summary>Null for pieces that are always locked to the head (the FPS counter).</summary>
        public readonly ConfigEntry<bool> FollowsHead;

        public readonly ConfigEntry<float> LeftRight;
        public readonly ConfigEntry<float> UpDown;
        public readonly ConfigEntry<float> Distance;
        public readonly ConfigEntry<float> Size;

        /// <summary>True while the Modify UI window is open: every piece shows in the headset, even with the menu open.</summary>
        public static bool Editing;

        /// <summary>The piece chosen in the Modify UI window (null: the UI screen), drawn solid while the others are see-through.</summary>
        public static LayerPlacement Selected;

        public LayerPlacement(string title, string description, ConfigEntry<bool> followsHead, ConfigEntry<float> leftRight,
            ConfigEntry<float> upDown, ConfigEntry<float> distance, ConfigEntry<float> size)
        {
            Title = title;
            Description = description;
            FollowsHead = followsHead;
            LeftRight = leftRight;
            UpDown = upDown;
            Distance = distance;
            Size = size;
        }

        /// <summary>False when the piece stays on the UI screen.</summary>
        public bool Enabled { get { return FollowsHead == null || FollowsHead.Value; } }

        public float Scale { get { return Mathf.Clamp(Size.Value, 0.3f, 3f); } }

        /// <summary>How see-through to draw the piece right now.</summary>
        public float Alpha { get { return !Editing || Selected == this ? 1f : EditingAlpha; } }

        /// <summary>How far in front of the eyes: the setting, but always nearer than the UI screen so it is never behind it.</summary>
        public float EffectiveDistance()
        {
            return Mathf.Min(Mathf.Clamp(Distance.Value, 0.3f, 3f), 0.85f * PanelLayout.Distance);
        }

        /// <summary>The direction of its centre from the eyes (Unity camera space): turned left or right, then up or down.</summary>
        public Quaternion Rotation
        {
            get { return Quaternion.Euler(-Mathf.Clamp(UpDown.Value, -60f, 60f), Mathf.Clamp(LeftRight.Value, -60f, 60f), 0f); }
        }

        /// <summary>Where it sits relative to the eyes (Unity camera space, +Z forward), turned to face them.</summary>
        public void Place(out Vector3 position, out Quaternion rotation)
        {
            rotation = Rotation;
            position = rotation * new Vector3(0f, 0f, EffectiveDistance());
        }

        /// <summary>Calls back whenever one of its settings changes.</summary>
        public void OnChanged(Action changed)
        {
            if (FollowsHead != null) FollowsHead.SettingChanged += (sender, args) => changed();
            LeftRight.SettingChanged += (sender, args) => changed();
            UpDown.SettingChanged += (sender, args) => changed();
            Distance.SettingChanged += (sender, args) => changed();
            Size.SettingChanged += (sender, args) => changed();
        }

        public void ResetToDefaults()
        {
            if (FollowsHead != null) FollowsHead.Value = (bool)FollowsHead.DefaultValue;
            LeftRight.Value = (float)LeftRight.DefaultValue;
            UpDown.Value = (float)UpDown.DefaultValue;
            Distance.Value = (float)Distance.DefaultValue;
            Size.Value = (float)Size.DefaultValue;
        }

        public string Describe()
        {
            return string.Format("{0:0}° left/right, {1:0}° up/down, {2:0.00} m away, size x{3:0.0}", LeftRight.Value, UpDown.Value, EffectiveDistance(), Size.Value);
        }
    }
}
