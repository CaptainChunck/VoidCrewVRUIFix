using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VoidCrewVRUIFix
{
    /// <summary>
    /// The ping wheel (and the game's other wheels, like emotes) in VR. Holding the wheel key shows the game's mouse
    /// cursor, which the plugin otherwise takes as a menu being open: head aim paused and the view snapped back to the
    /// game camera, and the wheel with it. A wheel isn't a menu, so head aim carries on and the wheel stays where you
    /// look. The game works out what each ping would mark once, when the wheel opens; with head aim, the ping wheel's
    /// targets are worked out again from where you look while it is open and when you let go, so you can look around
    /// with the wheel open and ping what you look at. Options light up for what you look at, like when the wheel opens.
    /// </summary>
    internal static class PingWheel
    {
        private const float RefreshInterval = 0.1f;

        private static bool _cursorResolved;
        private static PropertyInfo _inputService;
        private static PropertyInfo _cursorControl;
        private static FieldInfo _cursorSources;
        private static Type _wheelAbility;

        private static bool _menuResolved;
        private static bool _menuBroken;
        private static FieldInfo _pings;
        private static PropertyInfo _asset;
        private static FieldInfo _targets;
        private static FieldInfo _pingDefs;
        private static FieldInfo _radialMenu;
        private static FieldInfo _manipulators;
        private static FieldInfo _selectedIndex;
        private static MethodInfo _setActive;
        private static MethodInfo _highlight;
        private static FieldInfo _enabled;
        private static MethodInfo _getTarget;
        private static Type _targetInfo;
        private static FieldInfo _infoItem;
        private static FieldInfo _infoPosition;
        private static FieldInfo _infoIndex;
        private static PropertyInfo _container;
        private static MethodInfo _findIndexOf;
        private static readonly object[] TargetArguments = new object[1];
        private static readonly object[] OneArgument = new object[1];

        private static UnityEngine.Object _openMenu;
        private static float _nextRefresh;
        private static int _refreshes;

        /// <summary>
        /// True when the game shows its cursor only because a wheel is held open (the cursor isn't for a menu). Read
        /// from the game's own list of what wants the cursor shown.
        /// </summary>
        public static bool CursorOnlyForWheel()
        {
            if (!ResolveCursor()) return false;
            try
            {
                object service = _inputService.GetValue(null, null);
                object control = service != null ? _cursorControl.GetValue(service, null) : null;
                var sources = control != null ? _cursorSources.GetValue(control) as IDictionary : null;
                if (sources == null || sources.Count == 0) return false;
                foreach (object source in sources.Keys)
                {
                    if (source == null || !_wheelAbility.IsInstanceOfType(source)) return false;
                }
                return true;
            }
            catch (Exception e)
            {
                _wheelAbility = null;
                Log.Failure("Checking whether a wheel or a menu shows the cursor (wheels now count as menus)", e);
                return false;
            }
        }

        public static string Describe()
        {
            if (_cursorResolved && _wheelAbility == null) return "the game's wheels count as menus (wheel internals not found)";
            if (_menuBroken) return "wheels keep head aim; ping targets are worked out when the wheel opens (ping wheel internals not found)";
            return "wheels keep head aim; ping wheel targets follow where you look" + (_openMenu != null ? " (open now)" : "") +
                   ", refreshed " + _refreshes + " time(s) this session";
        }

        /// <summary>The ping wheel just opened (after the game worked out its targets).</summary>
        public static void Opened(UnityEngine.Object menu)
        {
            _openMenu = menu;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
        }

        /// <summary>The ping wheel is about to hand over the chosen ping: its targets are taken from where you look now.</summary>
        public static void Closing(UnityEngine.Object menu)
        {
            if (HeadAim.Aiming) Refresh(menu);
            _openMenu = null;
        }

        /// <summary>Once per frame: while the ping wheel is open, its targets follow where you look.</summary>
        public static void Update()
        {
            if (_openMenu == null) return;
            if (Time.unscaledTime < _nextRefresh || !HeadAim.Aiming) return;
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh(_openMenu);
        }

        public static void Stop()
        {
            _openMenu = null;
        }

        /// <summary>The same work the game does when the wheel opens, without reopening it (no sound, the choice under the mouse stays).</summary>
        private static void Refresh(UnityEngine.Object menu)
        {
            if (menu == null || _menuBroken || !ResolveMenu(menu.GetType())) return;
            try
            {
                var pings = _pings.GetValue(menu) as Array;
                var targets = _targets.GetValue(menu) as IDictionary;
                var pingDefs = _pingDefs.GetValue(menu) as IList;
                object radialMenu = _radialMenu.GetValue(menu);
                var manipulators = radialMenu != null ? _manipulators.GetValue(radialMenu) as IList : null;
                if (pings == null || targets == null || pingDefs == null || manipulators == null) return;
                int selected = (int)_selectedIndex.GetValue(radialMenu);

                foreach (object reference in pings)
                {
                    if (reference == null) continue;
                    object asset = _asset.GetValue(reference, null);
                    if (asset == null) continue;

                    TargetArguments[0] = Vector3.zero;
                    object target = _getTarget.Invoke(asset, TargetArguments);
                    bool found = target as UnityEngine.Object != null;
                    if (found)
                    {
                        object info = targets.Contains(asset) ? targets[asset] : null;
                        if (info == null)
                        {
                            info = Activator.CreateInstance(_targetInfo);
                            OneArgument[0] = asset;
                            _infoIndex.SetValue(info, _findIndexOf.Invoke(_container.GetValue(null, null), OneArgument));
                        }
                        _infoItem.SetValue(info, target);
                        _infoPosition.SetValue(info, TargetArguments[0]);
                        targets[asset] = info;
                    }
                    else
                    {
                        targets[asset] = null;
                    }

                    int option = pingDefs.IndexOf(asset);
                    object manipulator = option >= 0 && option < manipulators.Count ? manipulators[option] : null;
                    if (manipulator == null || (bool)_enabled.GetValue(manipulator) == found) continue;
                    OneArgument[0] = found;
                    _setActive.Invoke(manipulator, OneArgument);
                    OneArgument[0] = option == selected;
                    _highlight.Invoke(manipulator, OneArgument);
                }
                _refreshes++;
            }
            catch (Exception e)
            {
                _menuBroken = true;
                Log.Failure("Updating the ping wheel's targets from where you look (they are worked out when the wheel opens from now on)", e);
            }
        }

        private static bool ResolveCursor()
        {
            if (_cursorResolved) return _wheelAbility != null;
            _cursorResolved = true;
            Type service = AccessTools.TypeByName("CG.Input.InputService");
            Type control = AccessTools.TypeByName("CG.Input.CursorVisibilityControl");
            _inputService = service != null ? AccessTools.Property(service, "Instance") : null;
            _cursorControl = service != null ? AccessTools.Property(service, "CursorVisibilityControl") : null;
            _cursorSources = control != null ? AccessTools.Field(control, "s_showSources") : null;
            _wheelAbility = AccessTools.TypeByName("Gameplay.WheelAbility");
            if (_inputService == null || _cursorControl == null || _cursorSources == null || _wheelAbility == null)
            {
                _wheelAbility = null;
                Log.Warn("The game's cursor sources were not found (the game may have been updated); holding a wheel (pings, emotes) pauses head aim like a menu.");
                return false;
            }
            return true;
        }

        private static bool ResolveMenu(Type menuType)
        {
            if (_menuResolved) return !_menuBroken;
            _menuResolved = true;

            var missing = new List<string>();
            try
            {
                _pings = Need(AccessTools.Field(menuType, "Pings"), "PingMenu.Pings", missing);
                Type reference = _pings != null && _pings.FieldType.IsArray ? _pings.FieldType.GetElementType() : null;
                _asset = Need(reference != null ? AccessTools.Property(reference, "Asset") : null, "PingDefinitionRef.Asset", missing);
                _targets = Need(AccessTools.Field(menuType, "targets"), "PingMenu.targets", missing);
                _pingDefs = Need(AccessTools.Field(menuType, "pingDefs"), "PingMenu.pingDefs", missing);
                _radialMenu = Need(AccessTools.Field(menuType, "radialMenu"), "PingMenu.radialMenu", missing);
                Type radial = _radialMenu != null ? _radialMenu.FieldType : null;
                _manipulators = Need(radial != null ? AccessTools.Field(radial, "manipulators") : null, "RadialSelectionMenuVE.manipulators", missing);
                _selectedIndex = Need(radial != null ? AccessTools.Field(radial, "selectedIndex") : null, "RadialSelectionMenuVE.selectedIndex", missing);
                Type manipulator = _manipulators != null && _manipulators.FieldType.IsGenericType ? _manipulators.FieldType.GetGenericArguments()[0] : null;
                _setActive = Need(manipulator != null ? AccessTools.Method(manipulator, "SetActive", new[] { typeof(bool) }) : null, "RadialOptionManipulator.SetActive", missing);
                _highlight = Need(manipulator != null ? AccessTools.Method(manipulator, "Highlight", new[] { typeof(bool) }) : null, "RadialOptionManipulator.Highlight", missing);
                _enabled = Need(manipulator != null ? AccessTools.Field(manipulator, "Enabled") : null, "RadialOptionManipulator.Enabled", missing);

                Type definition = AccessTools.TypeByName("UI.Ping.PingDefinition");
                _getTarget = Need(definition != null ? AccessTools.Method(definition, "GetTarget") : null, "PingDefinition.GetTarget", missing);
                _targetInfo = _targets != null && _targets.FieldType.IsGenericType ? _targets.FieldType.GetGenericArguments()[1] : null;
                if (_targetInfo == null) missing.Add("PingComponent.PingTargetInfo");
                _infoItem = Need(_targetInfo != null ? AccessTools.Field(_targetInfo, "Item") : null, "PingTargetInfo.Item", missing);
                _infoPosition = Need(_targetInfo != null ? AccessTools.Field(_targetInfo, "Position") : null, "PingTargetInfo.Position", missing);
                _infoIndex = Need(_targetInfo != null ? AccessTools.Field(_targetInfo, "PingDefinitionIndex") : null, "PingTargetInfo.PingDefinitionIndex", missing);

                Type container = AccessTools.TypeByName("ResourceAssets.PingDefinitionContainer");
                _container = Need(container != null ? AccessTools.Property(container, "Instance") : null, "PingDefinitionContainer.Instance", missing);
                _findIndexOf = Need(container != null && definition != null ? AccessTools.Method(container, "FindIndexOf", new[] { definition }) : null,
                    "PingDefinitionContainer.FindIndexOf", missing);

                if (_selectedIndex != null && _selectedIndex.FieldType != typeof(int)) missing.Add("RadialSelectionMenuVE.selectedIndex (not an int)");
                if (_enabled != null && _enabled.FieldType != typeof(bool)) missing.Add("RadialOptionManipulator.Enabled (not a bool)");
                if (_infoIndex != null && _infoIndex.FieldType != typeof(int)) missing.Add("PingTargetInfo.PingDefinitionIndex (not an int)");
                if (_infoPosition != null && _infoPosition.FieldType != typeof(Vector3)) missing.Add("PingTargetInfo.Position (not a Vector3)");
            }
            catch (Exception e)
            {
                missing.Add(e.GetType().Name + ": " + e.Message);
            }

            if (missing.Count > 0)
            {
                _menuBroken = true;
                Log.Warn("The ping wheel's targets are worked out when it opens, as in the game: its " + string.Join(", ", missing.ToArray()) +
                         " was not found (the game may have been updated).");
            }
            return !_menuBroken;
        }

        private static T Need<T>(T member, string name, List<string> missing) where T : MemberInfo
        {
            if (member == null) missing.Add(name);
            return member;
        }
    }
}
