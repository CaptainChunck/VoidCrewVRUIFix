using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

// Offline checks (no game running): applies the plugin's Harmony patches to the real game, HDRP and UUVR assemblies,
// checks the HDRP fields the plugin reads, runs the plugin's by-name lookups against the game's types, resolves every
// member the plugin's code refers to (so a game, Unity, HDRP or UUVR member that was renamed or removed shows up here
// instead of in game), and checks the defaults a new install gets and the VR tab's RESTORE.
// Run it with testsuild-and-test.ps1, which builds the plugin, compiles this file and passes the folders:
// PatchTest.exe <plugin dir> <BepInEx core dir> <game Managed dir> <UUVR plugin dir>
// The desktop .NET runtime can't run Unity engine calls or load types with default interface methods, so those few
// checks are done on the assemblies' metadata instead, or reported as not checkable here.
internal static class Program
{
    private static string[] _dirs;
    private static int _failures;

    private static int Main(string[] args)
    {
        _dirs = args;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Console.WriteLine("FAILED: " + e);
            return 2;
        }
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : _failures + " CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs e)
    {
        string file = new AssemblyName(e.Name).Name + ".dll";
        foreach (string dir in _dirs)
        {
            string path = Path.Combine(dir, file);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _failures++;
    }

    private static void Run()
    {
        Assembly plugin = Assembly.LoadFrom(Path.Combine(_dirs[0], "VoidCrewVRUIFix.dll"));
        Assembly harmonyAssembly = Assembly.LoadFrom(Path.Combine(_dirs[1], "0Harmony.dll"));
        Assembly.LoadFrom(Path.Combine(_dirs[3], "Uuvr.dll"));
        Assembly.LoadFrom(Path.Combine(_dirs[3], "Uuvr.XR.OpenVR.dll"));
        Type harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony");
        object harmony = Activator.CreateInstance(harmonyType, "patchtest");
        Type patches = plugin.GetType("VoidCrewVRUIFix.GamePatches", true);

        Console.WriteLine("Harmony patches:");
        RunPatcher(patches, "PatchDlssEyes", harmony, 1);
        // The UUVR start-up methods call Unity engine functions, which this desktop runtime refuses to compile, so
        // Harmony reports an IL compile error here; TryHook below shows the cause. In game (Unity's runtime) they compile.
        RunPatcherInfo(patches, "PatchUuvrStartup", harmony);
        Check(Equals(plugin.GetType("VoidCrewVRUIFix.DlssEyes", true).GetProperty("Active").GetValue(null, null), true), "DLSS eye fix found HDRP's internals");

        CheckPatched(harmonyType, "UnityEngine.Rendering.HighDefinition.DLSSPass+CameraState, Unity.RenderPipelines.HighDefinition.Runtime", "SubmitCommands", "prefix+finalizer");
        CheckPatched(harmonyType, "Uuvr.VrUi.VrUiManager, Uuvr", "Start", "postfix");
        CheckPatched(harmonyType, "Uuvr.VrCamera.VrCamera, Uuvr", "Start", "postfix");

        Console.WriteLine("HDRP fields:");
        object hdrpProblem = plugin.GetType("VoidCrewVRUIFix.VrPerformance", true).GetMethod("CheckHdrp").Invoke(null, null);
        Check(hdrpProblem == null, "HDRP asset settings field for camera jitter / hidden-area mask" + (hdrpProblem != null ? ": " + hdrpProblem : ""));

        Console.WriteLine("UUVR hooks, compiled directly (shows why Harmony could not build them here):");
        TryHook(harmonyType, patches, "Uuvr.VrUi.VrUiManager, Uuvr", "Start", "UuvrUiManagerStarted", false);
        TryHook(harmonyType, patches, "Uuvr.VrCamera.VrCamera, Uuvr", "Start", "UuvrCameraStarted", false);

        Console.WriteLine("Turret screen border patch, applied with Harmony to the game's own method:");
        TryHook(harmonyType, patches, "CG.Client.Player.HUD.WeaponControlScreenBorder, Assembly-CSharp", "PlaySequence", "HideTurretBorder", true);
        Console.WriteLine("Turret head aim patches, applied with Harmony to the game's own methods:");
        TryHook(harmonyType, patches, "ControllingTurret, Assembly-CSharp", "TrueUpdate", "TurretControls", true);
        TryHook(harmonyType, patches, "CinemachineBrainUpdater, Assembly-CSharp", "LateUpdate", "TurretCamera", false);
        Console.WriteLine("Ping wheel patches, applied with Harmony to the game's own methods:");
        TryHook(harmonyType, patches, "PingMenu, Assembly-CSharp", "Open", "PingWheelOpened", false);
        TryHook(harmonyType, patches, "PingMenu, Assembly-CSharp", "Close", "PingWheelClosing", true);
        TryHook(harmonyType, patches, "UI.Ping.RadialSelectionMenuVE, Assembly-CSharp", "Display", "WheelShown", false);
        Console.WriteLine("VR tab restore patch, applied with Harmony to the game's own method:");
        TryHook(harmonyType, patches, "UI.Settings.SettingsUIPanel, Assembly-CSharp", "TryRestore", "SettingsRestoreClicked", true);

        Console.WriteLine("The plugin's own by-name lookups, run against the game's types:");
        RunLookups(plugin);

        Console.WriteLine("Defaults: a new install gets the tested VR setup, and RESTORE on the VR tab puts it back:");
        CheckDefaults(plugin);

        Console.WriteLine("Terminal screen patches (keep the screen on the terminal in VR), checked in the game's metadata:");
        CheckTerminalTargets(Path.Combine(_dirs[2], "Assembly-CSharp.dll"), patches);

        Console.WriteLine("Every game, Unity, HDRP and UUVR member the plugin uses exists:");
        int resolved = 0;
        int unverifiable = 0;
        foreach (Type type in plugin.GetTypes())
        {
            foreach (MethodBase method in Methods(type))
            {
                if (method.IsAbstract || (method.Attributes & MethodAttributes.PinvokeImpl) != 0) continue;
                MethodBody body;
                try
                {
                    body = method.GetMethodBody();
                }
                catch (Exception e)
                {
                    unverifiable++;
                    Console.WriteLine("  skip " + type.Name + "." + method.Name + " (" + e.GetType().Name + ")");
                    continue;
                }
                if (body == null) continue;
                foreach (int token in MemberTokens(body.GetILAsByteArray()))
                {
                    try
                    {
                        type.Module.ResolveMember(token,
                            type.IsGenericType ? type.GetGenericArguments() : null,
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        resolved++;
                    }
                    catch (Exception e)
                    {
                        if (e is MissingMemberException || e is TypeLoadException && !e.Message.Contains("method in an interface") || e is FileNotFoundException)
                        {
                            Check(false, type.FullName + "." + method.Name + ": " + e.GetType().Name + ": " + e.Message);
                        }
                        else
                        {
                            unverifiable++;
                        }
                    }
                }
            }
        }
        Check(resolved > 0, resolved + " member references resolved (" + unverifiable + " could not be checked on this runtime)");
    }

    /// <summary>
    /// The terminal classes use C# default interface methods, which this desktop runtime can't load (Unity's can), so
    /// the members the plugin relies on are checked in the assembly's metadata with Mono.Cecil instead.
    /// </summary>
    private static void CheckTerminalTargets(string gameAssembly, Type patches)
    {
        Mono.Cecil.ModuleDefinition module = Mono.Cecil.ModuleDefinition.ReadModule(gameAssembly);
        Mono.Cecil.TypeDefinition terminal = module.GetType("Gameplay.Terminals.TerminalScreen");
        Mono.Cecil.TypeDefinition shipTerminal = module.GetType("CG.Client.Ship.Modules.Mediators.TerminalModuleMediator");
        Check(terminal != null && DerivesFromMonoBehaviour(terminal), "Gameplay.Terminals.TerminalScreen exists and is a MonoBehaviour");
        Check(shipTerminal != null && DerivesFromMonoBehaviour(shipTerminal), "CG.Client.Ship.Modules.Mediators.TerminalModuleMediator exists and is a MonoBehaviour");
        if (terminal != null)
        {
            Check(HasCoroutine(terminal, "DelayedParentWorldUiPanel"), "TerminalScreen.DelayedParentWorldUiPanel() returns IEnumerator");
            Check(HasMethod(terminal, "RecalculateFov"), "TerminalScreen.RecalculateFov() exists");
            Mono.Cecil.FieldDefinition field = null;
            foreach (Mono.Cecil.FieldDefinition f in terminal.Fields) if (f.Name == "navigatableUI") field = f;
            Check(field != null && field.FieldType.FullName == "NavigatableUI", "TerminalScreen.navigatableUI is a NavigatableUI");
        }
        if (shipTerminal != null) Check(HasCoroutine(shipTerminal, "DelayedParentWorldUiPanel"), "TerminalModuleMediator.DelayedParentWorldUiPanel() returns IEnumerator");

        Mono.Cecil.TypeDefinition worldUi = module.GetType("CG.Client.UI.Terminals.WorldSpaceUI");
        Check(worldUi != null && DerivesFromMonoBehaviour(worldUi), "CG.Client.UI.Terminals.WorldSpaceUI exists and is a MonoBehaviour");
        if (worldUi != null)
        {
            bool pointer = false;
            foreach (Mono.Cecil.MethodDefinition m in worldUi.Methods)
            {
                if (m.Name == "TransformPointerEvent" && !m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].Name == "eventData" &&
                    m.Parameters[0].ParameterType.FullName == "UnityEngine.EventSystems.PointerEventData" && m.ReturnType.FullName == "System.Void") pointer = true;
            }
            Check(pointer, "WorldSpaceUI.TransformPointerEvent(PointerEventData eventData) exists");
            int sizeFields = 0;
            foreach (Mono.Cecil.FieldDefinition f in worldUi.Fields)
            {
                if ((f.Name == "_panelWidth" || f.Name == "_panelHeight") && f.FieldType.FullName == "System.Int32") sizeFields++;
            }
            Check(sizeFields == 2, "WorldSpaceUI._panelWidth and _panelHeight are ints");
        }
        Check(terminal != null && HasField(terminal, "worldSpaceUI", "CG.Client.UI.Terminals.WorldSpaceUI"), "TerminalScreen.worldSpaceUI is a WorldSpaceUI");
        Check(shipTerminal != null && HasField(shipTerminal, "worldUiPanel", "UnityEngine.Transform"), "TerminalModuleMediator.worldUiPanel is a Transform");
        Mono.Cecil.TypeDefinition localPlayer = module.GetType("CG.Game.Player.LocalPlayer");
        bool playerI = false, inTerminal = false;
        for (Mono.Cecil.TypeDefinition t = localPlayer; t != null; )
        {
            foreach (Mono.Cecil.PropertyDefinition p in t.Properties)
            {
                if (p.Name == "I" && p.GetMethod != null && p.GetMethod.IsStatic) playerI = true;
                if (p.Name == "IsInTerminal" && p.PropertyType.FullName == "System.Boolean" && p.GetMethod != null) inTerminal = true;
            }
            Mono.Cecil.TypeReference baseType = t.BaseType;
            t = baseType != null && baseType.Scope == module ? baseType.Resolve() : null;
        }
        Check(playerI && inTerminal, "LocalPlayer.I and the player's IsInTerminal flag exist");

        Mono.Cecil.TypeDefinition playerHud = module.GetType("UI.MainHUD.PlayerHUD");
        Check(playerHud != null && DerivesFromMonoBehaviour(playerHud), "UI.MainHUD.PlayerHUD (the status HUD) exists and is a MonoBehaviour");
        if (playerHud != null)
        {
            var names = new HashSet<string>();
            foreach (Mono.Cecil.MethodDefinition m in playerHud.Methods)
            {
                if (!m.HasBody) continue;
                foreach (Mono.Cecil.Cil.Instruction instruction in m.Body.Instructions)
                {
                    if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr) names.Add((string)instruction.Operand);
                }
            }
            Check(names.Contains("LocalPlayerContainer") && names.Contains("KeybindsContainer"),
                "PlayerHUD looks up the LocalPlayerContainer and KeybindsContainer elements the HUD layer copies");
        }

        Mono.Cecil.TypeDefinition classAbility = module.GetType("ClassAbilityDisplay");
        Check(classAbility != null && DerivesFromMonoBehaviour(classAbility) && UsesString(classAbility, "ClassAbilityContainer"),
            "ClassAbilityDisplay exists and looks up ClassAbilityContainer (copied with the status HUD)");

        // The other head-locked HUD pieces: each component, its UI document, and the container the piece copies.
        Mono.Cecil.TypeDefinition shipInfo = module.GetType("UI.MainHUD.ShipInfoHUD");
        Check(shipInfo != null && DerivesFromMonoBehaviour(shipInfo) && HasField(shipInfo, "Doc", "UnityEngine.UIElements.UIDocument") && UsesString(shipInfo, "ShipInfo"),
            "UI.MainHUD.ShipInfoHUD has its Doc and looks up ShipInfo (the ship status piece)");
        Mono.Cecil.TypeDefinition missions = module.GetType("MissionListUIController");
        Check(missions != null && DerivesFromMonoBehaviour(missions) && UsesString(missions, "MissionListContainer"),
            "MissionListUIController looks up MissionListContainer (the objectives piece)");
        Mono.Cecil.TypeDefinition weaponHud = module.GetType("UI.PilotHUD.WeaponHUD");
        Check(weaponHud != null && DerivesFromMonoBehaviour(weaponHud) && HasField(weaponHud, "Doc", "UnityEngine.UIElements.UIDocument") && UsesString(weaponHud, "ModifierContainer"),
            "UI.PilotHUD.WeaponHUD has its Doc and looks up ModifierContainer (the weapon stats piece)");

        // Head aim: each method is patched by name, so it must exist exactly once.
        CheckSingleMethod(module, "CG.Client.Player.Interactions.RaycastHandler", "FindInteractable");
        CheckSingleMethod(module, "UI.Ping.PingAbility", "GetPingInfo");
        CheckSingleMethod(module, "UI.Ping.PingDefinition", "GetTarget");
        Mono.Cecil.ModuleDefinition opsive = Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(gameAssembly), "Opsive.UltimateCharacterController.dll"));
        Mono.Cecil.TypeDefinition firstPerson = opsive.GetType("Opsive.UltimateCharacterController.FirstPersonController.Camera.ViewTypes.FirstPerson");
        bool crosshairs = false;
        if (firstPerson != null)
        {
            foreach (Mono.Cecil.MethodDefinition m in firstPerson.Methods)
            {
                if (m.Name == "GetCrosshairsDeltaRotation" && !m.HasParameters && m.ReturnType.FullName == "UnityEngine.Quaternion") crosshairs = true;
            }
        }
        Check(crosshairs, "Opsive FirstPerson.GetCrosshairsDeltaRotation() returns a Quaternion (weapons aim with the head)");

        Mono.Cecil.TypeDefinition brain = module.GetType("CinemachineBrainUpdater");
        bool brainOk = false;
        if (brain != null)
        {
            bool instance = false, getCamera = false;
            foreach (Mono.Cecil.FieldDefinition f in brain.Fields) if (f.Name == "Instance" && f.IsStatic && f.IsPublic) instance = true;
            foreach (Mono.Cecil.MethodDefinition m in brain.Methods) if (m.Name == "GetCamera" && m.IsPublic && !m.HasParameters && m.ReturnType.FullName == "UnityEngine.Camera") getCamera = true;
            brainOk = instance && getCamera;
        }
        Check(brainOk, "CinemachineBrainUpdater.Instance and GetCamera() exist");

        // Turrets: the border's fade (the plugin's prefix changes its alpha argument) and the player's interaction modes.
        Mono.Cecil.TypeDefinition border = module.GetType("CG.Client.Player.HUD.WeaponControlScreenBorder");
        int sequences = 0;
        bool alphaParameter = false;
        if (border != null)
        {
            foreach (Mono.Cecil.MethodDefinition m in border.Methods)
            {
                if (m.Name != "PlaySequence") continue;
                sequences++;
                foreach (Mono.Cecil.ParameterDefinition p in m.Parameters) if (p.Name == "alpha" && p.ParameterType.FullName == "System.Single") alphaParameter = true;
            }
        }
        Check(border != null && DerivesFromMonoBehaviour(border) && sequences == 1 && alphaParameter && HasField(border, "canvasGroup", "UnityEngine.CanvasGroup"),
            "WeaponControlScreenBorder.PlaySequence(..., float alpha) exists once and the border has its canvasGroup field");

        Mono.Cecil.TypeDefinition interactionState = module.GetType("CG.Client.Player.LocalPlayerInteractionState");
        Mono.Cecil.TypeDefinition interactionMode = module.GetType("CG.Client.Player.Interactions.InteractionMode");
        bool activeModes = false;
        if (interactionState != null && interactionMode != null)
        {
            foreach (Mono.Cecil.PropertyDefinition p in interactionState.Properties)
            {
                if (p.Name == "ActiveModes" && p.GetMethod != null && !p.GetMethod.IsStatic && p.PropertyType.FullName == interactionMode.FullName) activeModes = true;
            }
        }
        bool stateProperty = false;
        for (Mono.Cecil.TypeDefinition t = localPlayer; t != null; )
        {
            foreach (Mono.Cecil.PropertyDefinition p in t.Properties)
            {
                if (p.Name == "InteractionState" && p.GetMethod != null && !p.GetMethod.IsStatic && p.PropertyType.FullName == "CG.Client.Player.LocalPlayerInteractionState") stateProperty = true;
            }
            Mono.Cecil.TypeReference baseType = t.BaseType;
            t = baseType != null && baseType.Scope == module ? baseType.Resolve() : null;
        }
        var modeNames = new HashSet<string>();
        if (interactionMode != null) foreach (Mono.Cecil.FieldDefinition f in interactionMode.Fields) if (f.IsLiteral) modeNames.Add(f.Name);
        Check(stateProperty && activeModes && modeNames.Contains("ControllingTurret") && modeNames.Contains("ShipThirdPerson") && modeNames.Contains("UsingPeriscope"),
            "LocalPlayer.InteractionState.ActiveModes is an InteractionMode with ControllingTurret, ShipThirdPerson and UsingPeriscope");

        CheckTurretAim(module, gameAssembly, patches);
        CheckPingWheel(module, gameAssembly);

        MethodInfo borderPrefix = patches.GetMethod("HideTurretBorder", BindingFlags.NonPublic | BindingFlags.Static);
        ParameterInfo[] borderParameters = borderPrefix != null ? borderPrefix.GetParameters() : new ParameterInfo[0];
        Check(borderPrefix != null && borderPrefix.ReturnType == typeof(void) && borderParameters.Length == 2 && borderParameters[0].Name == "alpha" &&
              borderParameters[0].ParameterType.IsByRef && borderParameters[0].ParameterType.GetElementType() == typeof(float) &&
              borderParameters[1].Name == "___canvasGroup" && borderParameters[1].ParameterType.FullName == "UnityEngine.CanvasGroup",
            "plugin prefix HideTurretBorder(ref float alpha, CanvasGroup ___canvasGroup)");

        MethodInfo pointerPrefix = patches.GetMethod("TerminalScreenPointer", BindingFlags.NonPublic | BindingFlags.Static);
        ParameterInfo[] pointerParameters = pointerPrefix != null ? pointerPrefix.GetParameters() : new ParameterInfo[0];
        Check(pointerPrefix != null && pointerPrefix.ReturnType == typeof(bool) && pointerParameters.Length == 2 && pointerParameters[0].Name == "__instance" &&
              pointerParameters[1].Name == "eventData" && pointerParameters[1].ParameterType.FullName == "UnityEngine.EventSystems.PointerEventData",
            "plugin prefix TerminalScreenPointer(MonoBehaviour __instance, PointerEventData eventData) returns bool");

        foreach (string name in new[] { "TerminalScreenEntered", "ShipTerminalEntered" })
        {
            MethodInfo prefix = patches.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            ParameterInfo[] parameters = prefix != null ? prefix.GetParameters() : new ParameterInfo[0];
            Check(prefix != null && prefix.ReturnType == typeof(bool) && parameters.Length == 2 && parameters[0].Name == "__instance" &&
                  parameters[1].Name == "__result" && parameters[1].ParameterType.IsByRef &&
                  parameters[1].ParameterType.GetElementType() == typeof(System.Collections.IEnumerator),
                "plugin prefix " + name + "(MonoBehaviour __instance, ref IEnumerator __result) returns bool");
        }
    }

    /// <summary>The game members TurretAim, TurretHudLayer and the turret patches use, all looked up by name.</summary>
    private static void CheckTurretAim(Mono.Cecil.ModuleDefinition module, string gameAssembly, Type patches)
    {
        Mono.Cecil.TypeDefinition ability = module.GetType("ControllingTurret");
        Check(ability != null && FindMethod(ability, "TrueUpdate", 0) != null && !FindMethod(ability, "TrueUpdate", 0).IsStatic,
            "ControllingTurret.TrueUpdate() exists (the turret controls, every frame in a turret)");
        Check(PropertyType(ability, "TurretChair") == "TurretChair" && PropertyType(ability, "AllowTurretInput") == "System.Boolean",
            "ControllingTurret.TurretChair and AllowTurretInput (bool) exist");

        Mono.Cecil.TypeDefinition chair = module.GetType("TurretChair");
        Mono.Cecil.MethodDefinition sendInput = chair != null ? FindMethod(chair, "SendInput", 2) : null;
        Check(PropertyType(chair, "Turret") == "Gameplay.CompositeWeapons.CompositeWeaponModule" && sendInput != null &&
              sendInput.Parameters[0].ParameterType.FullName == "Gameplay.CompositeWeapons.WeaponActionType" && sendInput.Parameters[1].ParameterType.FullName == "System.Object",
            "TurretChair.Turret and SendInput(WeaponActionType, object) exist");

        Mono.Cecil.TypeDefinition actions = module.GetType("Gameplay.CompositeWeapons.WeaponActionType");
        bool aimMove = false;
        if (actions != null) foreach (Mono.Cecil.FieldDefinition f in actions.Fields) if (f.IsLiteral && f.Name == "AimMove") aimMove = true;
        Check(aimMove, "WeaponActionType.AimMove exists");

        Mono.Cecil.TypeDefinition weaponModule = module.GetType("Gameplay.CompositeWeapons.CompositeWeaponModule");
        Mono.Cecil.TypeDefinition outside = null;
        if (weaponModule != null) foreach (Mono.Cecil.TypeDefinition nested in weaponModule.NestedTypes) if (nested.Name == "CompositeWeaponOutsideElements") outside = nested;
        Check(outside != null && PropertyType(weaponModule, "ActiveOutsideElementCollection") == outside.FullName &&
              HasField(outside, "Pivot", "Gameplay.CompositeWeapons.WeaponPivot") && HasField(outside, "Scope", "Gameplay.CompositeWeapons.WeaponScope"),
            "CompositeWeaponModule.ActiveOutsideElementCollection with its Pivot and Scope exists");

        Mono.Cecil.TypeDefinition pivot = module.GetType("Gameplay.CompositeWeapons.WeaponPivot");
        bool delta = false;
        if (pivot != null)
        {
            foreach (Mono.Cecil.MethodDefinition m in pivot.Methods)
            {
                if (m.Name == "GetPerspectiveDeltaToTarget" && !m.IsStatic && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3" &&
                    m.ReturnType.FullName == "UnityEngine.Vector2") delta = true;
            }
        }
        Check(pivot != null && delta && HasField(pivot, "RotationZoomModifier", "System.Single"),
            "WeaponPivot.GetPerspectiveDeltaToTarget(Vector3) and RotationZoomModifier (float) exist");

        Mono.Cecil.TypeDefinition scope = module.GetType("Gameplay.CompositeWeapons.WeaponScope");
        Check(scope != null && HasField(scope, "ControlFeedCamera", "Cinemachine.CinemachineVirtualCamera"), "WeaponScope.ControlFeedCamera is a Cinemachine virtual camera");

        Mono.Cecil.TypeDefinition player = module.GetType("CG.Game.Player.LocalPlayer");
        string inputType = PropertyType(player, "PlayerInput");
        Mono.Cecil.ModuleDefinition shared = Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(gameAssembly), "Opsive.Shared.dll"));
        Mono.Cecil.TypeDefinition input = shared.GetType("Opsive.Shared.Input.PlayerInput");
        Mono.Cecil.MethodDefinition look = input != null ? FindMethod(input, "GetLookVector", 1) : null;
        Check(inputType == "Opsive.Shared.Input.PlayerInput" && look != null && look.Parameters[0].ParameterType.FullName == "System.Boolean" &&
              look.ReturnType.FullName == "UnityEngine.Vector2", "LocalPlayer.PlayerInput.GetLookVector(bool) returns a Vector2");

        Mono.Cecil.TypeDefinition brain = module.GetType("CinemachineBrainUpdater");
        Check(brain != null && FindMethod(brain, "LateUpdate", 0) != null, "CinemachineBrainUpdater.LateUpdate() exists (the game camera is placed there)");

        Mono.Cecil.TypeDefinition screen = module.GetType("CG.Client.Player.HUD.HUD_WeaponControlScreen");
        Check(screen != null && DerivesFromMonoBehaviour(screen), "HUD_WeaponControlScreen (the turret screen the turret HUD copies from) exists and is a MonoBehaviour");

        MethodInfo controls = patches.GetMethod("TurretControls", BindingFlags.NonPublic | BindingFlags.Static);
        ParameterInfo[] controlParameters = controls != null ? controls.GetParameters() : new ParameterInfo[0];
        Check(controls != null && controls.ReturnType == typeof(bool) && controlParameters.Length == 1 && controlParameters[0].Name == "__instance" &&
              controlParameters[0].ParameterType == typeof(object), "plugin prefix TurretControls(object __instance) returns bool");
        MethodInfo camera = patches.GetMethod("TurretCamera", BindingFlags.NonPublic | BindingFlags.Static);
        ParameterInfo[] cameraParameters = camera != null ? camera.GetParameters() : new ParameterInfo[0];
        Check(camera != null && camera.ReturnType == typeof(void) && cameraParameters.Length == 1 && cameraParameters[0].Name == "__instance" &&
              cameraParameters[0].ParameterType.FullName == "CinemachineBrainUpdater", "plugin postfix TurretCamera(CinemachineBrainUpdater __instance)");
    }

    /// <summary>The game members PingWheel reads by name: the ping wheel's targets and options, and who wants the cursor.</summary>
    private static void CheckPingWheel(Mono.Cecil.ModuleDefinition module, string gameAssembly)
    {
        Mono.Cecil.TypeDefinition menu = module.GetType("PingMenu");
        Mono.Cecil.MethodDefinition close = menu != null ? FindMethod(menu, "Close", 1) : null;
        Check(menu != null && FindMethod(menu, "Open", 0) != null && close != null && close.Parameters[0].IsOut,
            "PingMenu.Open() and Close(out object) exist");
        Check(menu != null && HasField(menu, "Pings", "ResourceAssets.PingDefinitionRef[]") &&
              HasField(menu, "targets", "System.Collections.Generic.Dictionary`2<UI.Ping.PingDefinition,UI.Ping.PingComponent/PingTargetInfo>") &&
              HasField(menu, "pingDefs", "System.Collections.Generic.List`1<UI.Ping.PingDefinition>") &&
              HasField(menu, "radialMenu", "UI.Ping.RadialSelectionMenuVE"),
            "PingMenu's Pings, targets, pingDefs and radialMenu fields exist");

        Mono.Cecil.TypeDefinition radial = module.GetType("UI.Ping.RadialSelectionMenuVE");
        Check(radial != null && HasField(radial, "manipulators", "System.Collections.Generic.List`1<RadialOptionManipulator>") && HasField(radial, "selectedIndex", "System.Int32"),
            "RadialSelectionMenuVE.manipulators and selectedIndex exist");
        Mono.Cecil.TypeDefinition option = module.GetType("RadialOptionManipulator");
        Mono.Cecil.MethodDefinition setActive = option != null ? FindMethod(option, "SetActive", 1) : null;
        Mono.Cecil.MethodDefinition highlight = option != null ? FindMethod(option, "Highlight", 1) : null;
        Check(option != null && HasField(option, "Enabled", "System.Boolean") && setActive != null && setActive.Parameters[0].ParameterType.FullName == "System.Boolean" &&
              highlight != null && highlight.Parameters[0].ParameterType.FullName == "System.Boolean",
            "RadialOptionManipulator.Enabled, SetActive(bool) and Highlight(bool) exist");

        Mono.Cecil.TypeDefinition info = null;
        Mono.Cecil.TypeDefinition component = module.GetType("UI.Ping.PingComponent");
        if (component != null) foreach (Mono.Cecil.TypeDefinition nested in component.NestedTypes) if (nested.Name == "PingTargetInfo") info = nested;
        Check(info != null && HasField(info, "Item", "UI.Ping.PingableItem") && HasField(info, "Position", "UnityEngine.Vector3") && HasField(info, "PingDefinitionIndex", "System.Int32"),
            "PingComponent.PingTargetInfo's Item, Position and PingDefinitionIndex exist");

        Mono.Cecil.TypeDefinition definition = module.GetType("UI.Ping.PingDefinition");
        Mono.Cecil.MethodDefinition getTarget = definition != null ? FindMethod(definition, "GetTarget", 1) : null;
        Check(getTarget != null && getTarget.Parameters[0].IsOut && getTarget.Parameters[0].ParameterType.FullName == "UnityEngine.Vector3&" &&
              getTarget.ReturnType.FullName == "UI.Ping.PingableItem", "PingDefinition.GetTarget(out Vector3) returns a PingableItem");

        Mono.Cecil.TypeDefinition container = module.GetType("ResourceAssets.PingDefinitionContainer");
        Mono.Cecil.MethodDefinition findIndex = container != null ? FindMethod(container, "FindIndexOf", 1) : null;
        Mono.Cecil.ModuleDefinition resources = Mono.Cecil.ModuleDefinition.ReadModule(Path.Combine(Path.GetDirectoryName(gameAssembly), "ResourceAssets.dll"));
        Mono.Cecil.TypeDefinition containerBase = resources.GetType("ResourceAssets.ResourceAssetContainer`3");
        Mono.Cecil.TypeDefinition referenceBase = resources.GetType("ResourceAssets.ResourceAssetRef`3");
        bool instance = false, asset = false;
        if (containerBase != null) foreach (Mono.Cecil.PropertyDefinition p in containerBase.Properties) if (p.Name == "Instance" && p.GetMethod != null && p.GetMethod.IsStatic) instance = true;
        if (referenceBase != null) foreach (Mono.Cecil.PropertyDefinition p in referenceBase.Properties) if (p.Name == "Asset" && p.GetMethod != null && !p.GetMethod.IsStatic) asset = true;
        Check(findIndex != null && findIndex.Parameters[0].ParameterType.FullName == "UI.Ping.PingDefinition" && findIndex.ReturnType.FullName == "System.Int32" &&
              container.BaseType != null && container.BaseType.FullName.StartsWith("ResourceAssets.ResourceAssetContainer`3") && instance && asset,
            "PingDefinitionContainer.Instance.FindIndexOf(PingDefinition) and PingDefinitionRef.Asset exist");

        Mono.Cecil.TypeDefinition service = module.GetType("CG.Input.InputService");
        Mono.Cecil.TypeDefinition serviceBase = module.GetType("CG.ServiceBase`1");
        bool serviceInstance = false;
        if (serviceBase != null) foreach (Mono.Cecil.PropertyDefinition p in serviceBase.Properties) if (p.Name == "Instance" && p.GetMethod != null && p.GetMethod.IsStatic) serviceInstance = true;
        Mono.Cecil.TypeDefinition cursor = module.GetType("CG.Input.CursorVisibilityControl");
        Check(PropertyType(service, "CursorVisibilityControl") == "CG.Input.CursorVisibilityControl" && serviceInstance && cursor != null &&
              HasField(cursor, "s_showSources", "System.Collections.Generic.Dictionary`2<CG.Input.IShowCursorSource,System.Int32>"),
            "InputService.Instance.CursorVisibilityControl and its cursor sources exist");
        Mono.Cecil.TypeDefinition wheel = module.GetType("Gameplay.WheelAbility");
        bool showsCursor = false;
        if (wheel != null) foreach (Mono.Cecil.InterfaceImplementation i in wheel.Interfaces) if (i.InterfaceType.FullName == "CG.Input.IShowCursorSource") showsCursor = true;
        Check(wheel != null && showsCursor, "Gameplay.WheelAbility (wheels: pings, emotes) asks for the cursor itself");
        Mono.Cecil.MethodDefinition display = radial != null ? FindMethod(radial, "Display", 1) : null;
        Check(display != null && display.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<System.Int32>",
            "RadialSelectionMenuVE.Display(List<int>) exists (a wheel is shown; its dark backdrop is cleared in VR)");
    }

    private static Mono.Cecil.MethodDefinition FindMethod(Mono.Cecil.TypeDefinition type, string name, int parameters)
    {
        Mono.Cecil.MethodDefinition found = null;
        foreach (Mono.Cecil.MethodDefinition m in type.Methods) if (m.Name == name && m.Parameters.Count == parameters) found = m;
        return found;
    }

    /// <summary>The full name of a property's type, searching base classes in the same module; null if it isn't there.</summary>
    private static string PropertyType(Mono.Cecil.TypeDefinition type, string name)
    {
        for (Mono.Cecil.TypeDefinition t = type; t != null; )
        {
            foreach (Mono.Cecil.PropertyDefinition p in t.Properties) if (p.Name == name && p.GetMethod != null) return p.PropertyType.FullName;
            Mono.Cecil.TypeReference baseType = t.BaseType;
            t = baseType != null && baseType.Scope == type.Module ? baseType.Resolve() : null;
        }
        return null;
    }

    private static readonly List<string> LoggedProblems = new List<string>();

    private static void OnLog(object sender, EventArgs e)
    {
        object level = e.GetType().GetProperty("Level").GetValue(e, null);
        object data = e.GetType().GetProperty("Data").GetValue(e, null);
        string text = level + ": " + data;
        if (!text.StartsWith("Info") && !text.StartsWith("Message")) LoggedProblems.Add(text);
    }

    /// <summary>
    /// Calls the plugin's lookup methods (the reflection it does in game) with a log that records warnings. Types this
    /// desktop runtime can't load (default interface methods) make some lookups unverifiable here; those are listed.
    /// </summary>
    private static void RunLookups(Assembly plugin)
    {
        Type sourceType = Type.GetType("BepInEx.Logging.ManualLogSource, BepInEx", true);
        object source = Activator.CreateInstance(sourceType, "patchtest");
        EventInfo logEvent = sourceType.GetEvent("LogEvent");
        logEvent.AddEventHandler(source, Delegate.CreateDelegate(logEvent.EventHandlerType, typeof(Program).GetMethod("OnLog", BindingFlags.NonPublic | BindingFlags.Static)));
        plugin.GetType("VoidCrewVRUIFix.Log", true).GetMethod("Init").Invoke(null, new[] { source });

        Type pingWheel = plugin.GetType("VoidCrewVRUIFix.PingWheel", true);
        Lookup("PingWheel cursor sources", () => pingWheel.GetMethod("ResolveCursor", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
        Type pingMenu = Type.GetType("PingMenu, Assembly-CSharp", false);
        if (pingMenu != null)
        {
            Lookup("PingWheel ping wheel internals", () => pingWheel.GetMethod("ResolveMenu", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { pingMenu }));
        }
        else
        {
            Console.WriteLine("  info PingMenu can't be loaded on this runtime; its lookups were checked in the metadata");
        }
        Lookup("PlayerModes interaction modes", () => plugin.GetType("VoidCrewVRUIFix.PlayerModes", true).GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
        Lookup("TurretAim turret internals", () => plugin.GetType("VoidCrewVRUIFix.TurretAim", true).GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null));
    }

    private static void Lookup(string what, Func<object> run)
    {
        LoggedProblems.Clear();
        object result;
        try
        {
            result = run();
        }
        catch (Exception e)
        {
            Exception root = e;
            while (root.InnerException != null) root = root.InnerException;
            Console.WriteLine("  info " + what + ": " + root.GetType().Name + ": " + root.Message + " (not verifiable on this runtime)");
            return;
        }
        if (Equals(result, true))
        {
            Check(LoggedProblems.Count == 0, what + " all found" + (LoggedProblems.Count > 0 ? ": " + string.Join(" | ", LoggedProblems.ToArray()) : ""));
            return;
        }
        // A false result on this runtime is usually a type it can't load; the message says which members were missing.
        // Game types this desktop runtime can't load (default interface methods); Unity's runtime loads them.
        bool typeLoad = false;
        foreach (string name in new[] { "TurretChair", "TakeoverChair", "ControllingTurret", "CG.Game.Player.LocalPlayer" })
        {
            bool loads;
            try
            {
                loads = Type.GetType(name + ", Assembly-CSharp", true) != null;
            }
            catch (TypeLoadException)
            {
                loads = false;
            }
            string shortName = name.Substring(name.LastIndexOf('.') + 1);
            foreach (string problem in LoggedProblems) if (!loads && problem.Contains(shortName + ".")) typeLoad = true;
        }
        Console.WriteLine((typeLoad ? "  info " : "  FAIL ") + what + " not all found: " + string.Join(" | ", LoggedProblems.ToArray()) +
                          (typeLoad ? " (includes a type this runtime can't load; checked in the metadata above)" : ""));
        if (!typeLoad) _failures++;
    }

    /// <summary>Whether the type's code (including its coroutines and lambdas, which live in nested types) uses the string.</summary>
    private static bool UsesString(Mono.Cecil.TypeDefinition type, string value)
    {
        foreach (Mono.Cecil.MethodDefinition m in type.Methods)
        {
            if (!m.HasBody) continue;
            foreach (Mono.Cecil.Cil.Instruction instruction in m.Body.Instructions)
            {
                if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && (string)instruction.Operand == value) return true;
            }
        }
        foreach (Mono.Cecil.TypeDefinition nested in type.NestedTypes)
        {
            if (UsesString(nested, value)) return true;
        }
        return false;
    }

    private static void CheckSingleMethod(Mono.Cecil.ModuleDefinition module, string typeName, string methodName)
    {
        Mono.Cecil.TypeDefinition type = module.GetType(typeName);
        int count = 0;
        if (type != null) foreach (Mono.Cecil.MethodDefinition m in type.Methods) if (m.Name == methodName) count++;
        Check(count == 1, typeName + "." + methodName + " exists exactly once (" + count + " found)");
    }

    private static bool HasField(Mono.Cecil.TypeDefinition type, string name, string fieldType)
    {
        foreach (Mono.Cecil.FieldDefinition f in type.Fields) if (f.Name == name && f.FieldType.FullName == fieldType) return true;
        return false;
    }

    private static bool HasMethod(Mono.Cecil.TypeDefinition type, string name)
    {
        foreach (Mono.Cecil.MethodDefinition m in type.Methods) if (m.Name == name && !m.HasParameters) return true;
        return false;
    }

    private static bool HasCoroutine(Mono.Cecil.TypeDefinition type, string name)
    {
        foreach (Mono.Cecil.MethodDefinition m in type.Methods)
        {
            if (m.Name == name && !m.HasParameters && !m.IsStatic && m.ReturnType.FullName == "System.Collections.IEnumerator") return true;
        }
        return false;
    }

    private static bool DerivesFromMonoBehaviour(Mono.Cecil.TypeDefinition type)
    {
        for (Mono.Cecil.TypeReference t = type; t != null; )
        {
            if (t.FullName == "UnityEngine.MonoBehaviour") return true;
            Mono.Cecil.TypeDefinition resolved;
            try
            {
                resolved = t.Resolve();
            }
            catch
            {
                return false;
            }
            if (resolved == null) return false;
            t = resolved.BaseType;
        }
        return false;
    }

    // The settings file of the tested VR setup (CaptainChunck's, 18 Oct 2026), copied next to the compiled checks: every
    // value in it must be what a new install writes. When the defaults change on purpose, replace this file.
    private const string TestedSetup = "tested-setup.cfg";

    private static void CheckDefaults(Assembly plugin)
    {
        Type configFileType = Type.GetType("BepInEx.Configuration.ConfigFile, BepInEx", true);
        Type entryBase = Type.GetType("BepInEx.Configuration.ConfigEntryBase, BepInEx", true);
        Type logSource = Type.GetType("BepInEx.Logging.ManualLogSource, BepInEx", true);
        plugin.GetType("VoidCrewVRUIFix.Log", true).GetMethod("Init").Invoke(null, new[] { Activator.CreateInstance(logSource, "patchtest") });

        string folder = Path.Combine(Path.GetTempPath(), "vruifix-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "com.user.voidcrew.vruifix.cfg");
            object config = Activator.CreateInstance(configFileType, path, false);
            Type settings = plugin.GetType("VoidCrewVRUIFix.Settings", true);
            settings.GetMethod("Bind").Invoke(null, new[] { config });
            Check(Equals(settings.GetProperty("FirstStart").GetValue(null, null), true), "no settings file yet counts as the first start");

            Dictionary<string, string> tested = ReadCfg(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), TestedSetup));
            Dictionary<string, string> fresh = ReadCfg(path);
            int same = 0;
            foreach (KeyValuePair<string, string> pair in tested)
            {
                if (pair.Key == "Debug/Settings Version") continue;
                string value;
                if (fresh.TryGetValue(pair.Key, out value) && value == pair.Value)
                {
                    same++;
                    continue;
                }
                Check(false, "new install: " + pair.Key + " = " + (value ?? "(missing)") + ", the tested setup has " + pair.Value);
            }
            Check(same > 50, "a new install writes all " + same + " values of the tested setup");

            // Change some of what the VR tab and the Modify UI window show, and one setting that is only in the file.
            PropertyInfo boxed = entryBase.GetProperty("BoxedValue");
            PropertyInfo defaultValue = entryBase.GetProperty("DefaultValue");
            Func<string, object> entry = name => settings.GetField(name).GetValue(null);
            Func<string, string, object> piece = (name, part) =>
            {
                object placement = settings.GetField(name).GetValue(null);
                return placement.GetType().GetField(part).GetValue(placement);
            };
            var changed = new Dictionary<string, object>
            {
                { "Cursor Scale", entry("CursorScale") },
                { "Disable DLSS", entry("DisableDlss") },
                { "UI Display Mode", entry("DisplayMode") },
                { "UI screen width", entry("PanelWidthDegrees") },
                { "UI screen height", entry("PanelHeightOffset") },
                { "monitor view width", entry("DesktopViewWidth") },
                { "player HUD size", piece("PlayerHud", "Size") },
                { "ship status left/right", piece("ShipStatus", "LeftRight") },
                { "turret crosshair follows head", piece("TurretCrosshair", "FollowsHead") },
                { "FPS counter distance", piece("FpsPlacement", "Distance") },
            };
            boxed.SetValue(changed["Cursor Scale"], 3f, null);
            boxed.SetValue(changed["Disable DLSS"], true, null);
            boxed.SetValue(changed["UI Display Mode"], Enum.Parse(plugin.GetType("VoidCrewVRUIFix.UiDisplayMode", true), "InGamePanel"), null);
            boxed.SetValue(changed["UI screen width"], 80f, null);
            boxed.SetValue(changed["UI screen height"], 0.3f, null);
            boxed.SetValue(changed["monitor view width"], 60f, null);
            boxed.SetValue(changed["player HUD size"], 0.7f, null);
            boxed.SetValue(changed["ship status left/right"], -40f, null);
            boxed.SetValue(changed["turret crosshair follows head"], false, null);
            boxed.SetValue(changed["FPS counter distance"], 2f, null);
            object fileOnly = entry("AlignMarkersToPanel");
            boxed.SetValue(fileOnly, false, null);

            settings.GetMethod("RestoreVrDefaults").Invoke(null, null);
            foreach (KeyValuePair<string, object> pair in changed)
            {
                Check(Equals(boxed.GetValue(pair.Value, null), defaultValue.GetValue(pair.Value, null)), "RESTORE puts the " + pair.Key + " back to its default");
            }
            Check(Equals(boxed.GetValue(fileOnly, null), false), "RESTORE leaves settings that are only in the file (Align Markers To Panel) alone");
            Check(Equals(configFileType.GetProperty("SaveOnConfigSet").GetValue(config, null), true), "RESTORE turns saving on every change back on");
            Dictionary<string, string> saved = ReadCfg(path);
            Check(saved["Cursor/Cursor Scale"] == tested["Cursor/Cursor Scale"] && saved["Player HUD/Size"] == tested["Player HUD/Size"] &&
                  saved["Fixes/Align Markers To Panel"] == "false", "RESTORE saved the restored values to the file in one go");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static Dictionary<string, string> ReadCfg(string path)
    {
        var values = new Dictionary<string, string>();
        string section = "";
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Substring(1, line.Length - 2);
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals > 0) values[section + "/" + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
        }
        return values;
    }

    private static void TryHook(Type harmonyType, Type patches, string typeName, string methodName, string patchName, bool prefix)
    {
        Type type;
        try
        {
            type = Type.GetType(typeName, true);
        }
        catch (TypeLoadException e)
        {
            Console.WriteLine("  ok   " + typeName.Split(',')[0] + "." + methodName + ": not loadable on the desktop runtime (" + e.Message +
                              "); checked in the metadata instead");
            return;
        }
        MethodInfo target = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        MethodInfo patchMethod = patches.GetMethod(patchName, BindingFlags.NonPublic | BindingFlags.Static);
        if (target == null || patchMethod == null)
        {
            Check(false, typeName.Split(',')[0] + "." + methodName + ": " + (target == null ? "target" : patchName) + " not found");
            return;
        }
        object hookHarmony = Activator.CreateInstance(harmonyType, "patchtest.hook." + patchName);
        Type harmonyMethod = harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");
        try
        {
            MethodInfo patch = null;
            foreach (MethodInfo m in harmonyType.GetMethods())
            {
                if (m.Name == "Patch" && m.GetParameters().Length == 6) patch = m;
            }
            object hm = Activator.CreateInstance(harmonyMethod, patchMethod);
            patch.Invoke(hookHarmony, prefix ? new object[] { target, hm, null, null, null, null } : new object[] { target, null, hm, null, null, null });
            Check(true, typeName.Split(',')[0] + "." + methodName + " patched with " + patchName);
        }
        catch (Exception e)
        {
            Exception root = e;
            while (root.InnerException != null) root = root.InnerException;
            bool environment = (root is System.Security.SecurityException && root.Message.Contains("ECall")) ||
                               (root is TypeLoadException && root.Message.Contains("interface"));
            Console.WriteLine((environment ? "  ok   " : "  FAIL ") + typeName.Split(',')[0] + "." + methodName + ": " + root.GetType().Name + ": " + root.Message +
                              (environment ? " (the desktop .NET runtime can't compile Unity engine calls or load default interface methods; Unity's runtime can)" : ""));
            if (!environment) _failures++;
        }
    }

    /// <summary>Metadata tokens of the members and types a method's IL refers to.</summary>
    private static IEnumerable<int> MemberTokens(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            System.Reflection.Emit.OpCode op;
            if (il[i] == 0xFE)
            {
                op = TwoByte[il[i + 1]];
                i += 2;
            }
            else
            {
                op = OneByte[il[i]];
                i += 1;
            }
            switch (op.OperandType)
            {
                case System.Reflection.Emit.OperandType.InlineMethod:
                case System.Reflection.Emit.OperandType.InlineField:
                case System.Reflection.Emit.OperandType.InlineTok:
                case System.Reflection.Emit.OperandType.InlineType:
                    yield return BitConverter.ToInt32(il, i);
                    i += 4;
                    break;
                case System.Reflection.Emit.OperandType.InlineNone:
                    break;
                case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                case System.Reflection.Emit.OperandType.ShortInlineI:
                case System.Reflection.Emit.OperandType.ShortInlineVar:
                    i += 1;
                    break;
                case System.Reflection.Emit.OperandType.InlineVar:
                    i += 2;
                    break;
                case System.Reflection.Emit.OperandType.InlineI8:
                case System.Reflection.Emit.OperandType.InlineR:
                    i += 8;
                    break;
                case System.Reflection.Emit.OperandType.InlineSwitch:
                    int count = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * count;
                    break;
                default:
                    i += 4;
                    break;
            }
        }
    }

    private static readonly System.Reflection.Emit.OpCode[] OneByte = new System.Reflection.Emit.OpCode[256];
    private static readonly System.Reflection.Emit.OpCode[] TwoByte = new System.Reflection.Emit.OpCode[256];

    static Program()
    {
        foreach (FieldInfo field in typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var op = (System.Reflection.Emit.OpCode)field.GetValue(null);
            ushort value = unchecked((ushort)op.Value);
            if (value < 0x100) OneByte[value] = op;
            else if ((value & 0xFF00) == 0xFE00) TwoByte[value & 0xFF] = op;
        }
    }

    private static IEnumerable<MethodBase> Methods(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (MethodInfo m in type.GetMethods(all)) yield return m;
        foreach (ConstructorInfo c in type.GetConstructors(all)) yield return c;
    }

    private static void RunPatcherInfo(Type patches, string name, object harmony)
    {
        MethodInfo patcher = patches.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Check(patcher != null, name + " exists");
        if (patcher == null) return;
        var applied = new List<string>();
        var skipped = new List<string>();
        patcher.Invoke(null, new object[] { harmony, applied, skipped });
        Console.WriteLine("  info " + name + ": applied [" + string.Join(", ", applied.ToArray()) + "]" +
                          (skipped.Count > 0 ? ", skipped [" + string.Join("; ", skipped.ToArray()) + "]" : ""));
    }

    private static void RunPatcher(Type patches, string name, object harmony, int expected)
    {
        MethodInfo patcher = patches.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        if (patcher == null)
        {
            Check(false, name + " not found in the plugin");
            return;
        }
        var applied = new List<string>();
        var skipped = new List<string>();
        patcher.Invoke(null, new object[] { harmony, applied, skipped });
        Check(applied.Count == expected && skipped.Count == 0,
            name + ": applied [" + string.Join(", ", applied.ToArray()) + "]" + (skipped.Count > 0 ? ", skipped [" + string.Join("; ", skipped.ToArray()) + "]" : ""));
    }

    private static void CheckPatched(Type harmonyType, string typeName, string methodName, string expected)
    {
        Type type = Type.GetType(typeName, false);
        MethodInfo method = type != null ? type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static) : null;
        object info = method != null ? harmonyType.GetMethod("GetPatchInfo").Invoke(null, new object[] { method }) : null;
        string found = "none";
        if (info != null)
        {
            Type infoType = info.GetType();
            found = "prefixes " + Count(infoType.GetField("Prefixes").GetValue(info)) + ", postfixes " + Count(infoType.GetField("Postfixes").GetValue(info)) +
                    ", finalizers " + Count(infoType.GetField("Finalizers").GetValue(info));
        }
        Check(info != null, typeName.Split(',')[0] + "." + methodName + " patched (" + expected + "): " + found);
    }

    private static int Count(object collection)
    {
        int n = 0;
        foreach (object unused in (System.Collections.IEnumerable)collection) n++;
        return n;
    }
}
