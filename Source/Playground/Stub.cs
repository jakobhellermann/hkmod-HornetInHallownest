extern alias Silksong;
extern alias SilksongLoc;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace HornetPlayer.Playground;

// Stub out Silksong methods we don't want to run yet (environment managers, FSM actions that NullRef because the full
// game runtime isn't set up). We stub the CALLEE (the leaf method that crashes), not its callers — minimal surface,
// and callers keep whatever they do around the call. Mechanism: MonoMod RuntimeDetour ILHook (no HookGen, no Harmony)
// rewrites the method body to "log once + return default". The code lives in the prefixed Silksong binary, so we can't
// edit it as source; the ILHook is our "guard".
internal static class Stub {
    private static readonly List<ILHook> hooks = new();
    private static readonly HashSet<string> logged = new();

    // The methods that NullRef on spawn because the full game runtime isn't set up (callees, leaf methods).
    // Identified from Player.log on a real spawn — see TODO.md / docs.
    internal static void Install() {
        // Silksong's REMAPPED TeamCherry.Localization.Language is a SEPARATE type from HK's shared one. Its static ctor
        // runs LoadAvailableLanguages() + LoadLanguage() -> DoSwitch, which loads the localization sheets (the data we
        // WANT) and ends with SendMonoMessage("ChangedLanguage", Silksong.LanguageCode): a scene-wide
        // FindObjectsOfType<GameObject>() + BroadcastMessage that hits HK's own SetTextMeshProGameText/FontManager,
        // whose ChangedLanguage(HK.LanguageCode) can't bind a Silksong.LanguageCode arg -> MissingMethodException
        // (present-but-mismatched receiver; DontRequireReceiver doesn't save it). Stubbing SendMonoMessage routes all
        // FUTURE broadcasts (live language switches) into the no-op. The catch: installing this hook makes MonoMod's
        // GetFunctionPointer force Language's type-init, so the cctor runs (loads the sheets — good) and fires its ONE
        // broadcast BEFORE our detour is wired -> exactly ONE MissingMethodException at startup, unavoidably (we proved
        // hooking the .cctor itself also self-triggers it). Accepted as a single, understood startup error.
        //
        // We also use this controlled cctor trigger as the prefer-bundle WINDOW (see ResourcesShim.PreferBundle):
        // Silksong's Languages/*_General sheets exist in BOTH HK's Resources and our bundle, and by default HK wins ->
        // Silksong would read HK's strings. Setting PreferBundle here makes the cctor's sheet loads come from the
        // Silksong bundle; HK's localization already initialized at HK boot (outside this window) and is unaffected.
        var lang = typeof(SilksongLoc::TeamCherry.Localization.Language);
        ResourcesShim.PreferBundle = true;
        try {
            Skip(lang, "SendMonoMessage");                                  // installs stub; its GetFunctionPointer also forces type-init
            RuntimeHelpers.RunClassConstructor(lang.TypeHandle);           // ensure the cctor ran inside the window (no-op if already)
        } finally {
            ResourcesShim.PreferBundle = false;
        }
        Skip(typeof(Silksong::HeroWaterController), "Update");                                  // per-frame
        Skip(typeof(Silksong::PersonalObjectPool), "OnStart");                                  // Start
        Skip(typeof(Silksong::HeroAnimationController), "UpdateToolEquipFlags");                // Start
        // Tool-equipment subsystem isn't initialized -> IsToolEquipped NullRefs; stub the root (no tools equipped),
        // which should cascade-fix ToolItem.IsEquipped / CheckIfToolEquipped / ToolEquipChecker / HeroWispLantern.
        Skip(typeof(Silksong::ToolItemManager), "IsToolEquipped");
        Skip(typeof(Silksong::KeepWorldScalePositive), "OnEnable");
        Skip(typeof(Silksong::HeroNailImbuement), "Awake");
        Skip(typeof(Silksong::FollowTransform), "OnEnable");
        // NOTE: the PlayMaker-ACTION stubs (SetPolygonCollider.OnEnter, ListenForTauntV2/ListenFor* OnUpdate) were
        // removed. They were workarounds for the pre-B era when those actions MIS-RESOLVED to HK's same-named versions
        // (wrong field layout -> NullRef). With Silksong.PlayMaker isolation they resolve to the correct Silksong
        // versions, so they must RUN: a stubbed action never calls Finish(), hanging its FSM state forever (e.g. Sprint
        // stuck in "Cancel All", never returning to Idle to accept DASHED), and stubbed ListenFor* suppress the very
        // input->FSM events the moves need. Input is alive now (InputDriver + buttonQueueTimers).
        // AddSilk -> GameCameras.instance.silkSpool (silk meter UI); GameCameras isn't bootstrapped. UI, not needed
        // for no-input bring-up. TODO: bootstrap GameCameras/silkSpool for the UI/combat phase.
        Skip(typeof(Silksong::HeroController), "AddSilk");
        Skip(typeof(Silksong::HeroController), "SetupDeliveryItems"); // delivery-quest setup entry — quests irrelevant
        // The HUD's DeliveryHudIcon.OnPreUpdateDisplay calls DeliveryQuestItem.GetActiveItems() -> NullRef (delivery-quest
        // system deliberately off, see above). Skip it: currentItem stays null -> RadialHudIcon.GetIsActive() is false ->
        // the icon hides itself. (No deliveries to show anyway.)
        Skip(typeof(Silksong::DeliveryHudIcon), "OnPreUpdateDisplay");
        // StartDashEffect activates dashBurstPrefab/airDashEffect (unresolved external effect prefabs) -> NullRef,
        // aborting HeroDash after cState.dashing is set but before cooldown/FSM. Purely cosmetic -> stub so dash runs.
        Skip(typeof(Silksong::HeroController), "StartDashEffect");
        // gruntAudioTable.SpawnAndPlayOneShot in HeroDash NullRefs in RandomAudioClipTable.CanPlay (audio tables not
        // set up) -> aborts the dash before cState.dashing is set. Stub the spawn extension (no SFX during bring-up).
        Skip(typeof(Silksong::RandomAudioClipTableExtensions), "SpawnAndPlayOneShot");
        // CheckForBump delegates to bumpChecker (null, not set up) -> NullRef EVERY FixedUpdate inside Dash(), before
        // `dash_timer -= dt`, so the timer never decrements, FinishedDashing never fires -> stuck dashing forever. The
        // wrapper discards the out-results (`out var _`), so stubbing is behaviorally free; Unity colliders still
        // handle real collision.
        Skip(typeof(Silksong::HeroController), "CheckForBump");
        // SetParticleScale.OnUpdate (ticked every frame via SetParticleScaleCallbackHooks) derefs a null parentBody
        // (Rigidbody2D.IsAwake) -> per-frame NullRef. Cosmetic particle scaling -> stub.
        Skip(typeof(Silksong::SetParticleScale), "OnUpdate");
        Skip(typeof(Silksong::DeliveryQuestItem), "BreakAllInternal"); // also called directly from Start (BreakTimedNoEffects)
        // Superjump's "Hit Roof Hard" state calls DeliveryQuestItem.TakeHit() via CallStaticMethod (slamming the ceiling
        // damages carried delivery items). TakeHit() -> TakeHit(int) -> GetActiveItems() -> QuestManager/CollectableItemManager
        // (deliveries/quest subsystem off, see DeliveryHudIcon above) -> NullRef. It throws at action index 0, aborting the
        // rest of "Hit Roof Hard" (incl. the Tk2dPlayAnimationWithEvents) so the follow-up "Hit Roof" state's
        // Tk2dWatchAnimationEvents waits forever for an animation event that never fires -> Hornet stuck in soar pose at the
        // ceiling, no fall. Both overloads are void -> clean no-op (Skip stubs all overloads). GetActiveItems itself can't be
        // stubbed here (returns IEnumerable -> default null -> callers foreach over null -> NullRef again).
        Skip(typeof(Silksong::DeliveryQuestItem), "TakeHit");
        // Hornet's EnterScene (HornetSceneEntry) -> EnterHeroSubFadeUp calls gm.FadeSceneIn(): Silksong's own scene fade,
        // which fights HK's fade (HK owns the camera/fade) and needs Silksong's fade FSM/camera context we don't run.
        // No-op it; HK fades the scene.
        Skip(typeof(Silksong::GameManager), "FadeSceneIn");
        // SimpleFadeOut::SetColor throws nullref, because Awake is never ran
        Skip(typeof(Silksong::CameraController), "ScreenFlash");
        // GameCameras.Start does only `gs.LoadOverscanSettings(); SetOverscan(gs.overScanAdjustment)` — gs is
        // gm.gameSettings, assigned only in SetupGameRefs (which we never run) -> null -> NullRef on activating the rig.
        // Overscan is cosmetic and HK owns the camera; skip Start so the rig can come up ACTIVE (HUD FSMs self-init).
        Skip(typeof(Silksong::GameCameras), "Start");
        // HUDCamera.OnEnable does `ih = GameManager.instance.inputHandler; ih.PauseAllowed` -> NullRef (our bootstrap GM's
        // inputHandler field isn't wired) + Invoke("MoveMenuToHudCamera") (menu plumbing we don't need). Skip — none of
        // it matters for the in-game HUD (health/silk render regardless).
        Skip(typeof(Silksong::HUDCamera), "OnEnable");
        // GameCameras.Awake is just `_instance = this; DontDestroyOnLoad(this)` — but our rig lives under an inactive
        // holder (so we can neuter before activating), so `this` is non-root -> "DontDestroyOnLoad only works for root
        // GameObjects" warning. Skip Awake; GameCamerasBootstrap sets _instance itself (before activation, so child HUD
        // FSMs resolve GameCameras.instance) and DDOLs the holder. Without skipping, Awake would also Destroy our rig if
        // _instance was pre-set to a different object — skipping removes that hazard too.
        Skip(typeof(Silksong::GameCameras), "Awake");
        // NOTE: SetConfigGroup's throw is FSMUtility.SendEventToGameObject -> list[i].Fsm.Event() on the hero's
        // PlayMakerFSMs, which aren't fully initialized (linked to the residual ~125 action-resolution failures).
        // NOT stubbed here (FSMUtility is broad / FSM is core) — tracked as the PlayMaker bring-up TODO.
    }

    // Stub `method` on every Silksong type in `ns` whose name starts with `prefix` (category stub).
    internal static void SkipAllInNamespace(string ns, string prefix, string method) {
        Type?[] types;
        try { types = typeof(Silksong::HeroController).Assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types; }
        var n = 0;
        foreach (var t in types) {
            if (t?.Namespace != ns || !t.Name.StartsWith(prefix)) continue;
            if (t.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null) continue;
            Skip(t, method);
            n++;
        }
        Log.Info($"[Stub] category {ns}.{prefix}*::{method} -> {n} types");
    }

    // Stub every method named `method` on `type` (all overloads/visibilities) to log-once + return default.
    internal static void Skip(Type type, string method) {
        var found = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var any = false;
        foreach (var mi in found) {
            if (mi.Name != method || mi.IsAbstract || mi.GetMethodBody() == null) continue;
            var label = $"{type.Name}.{mi.Name}";
            try {
                hooks.Add(new ILHook(mi, il => Rewrite(il, label)));
                any = true;
            } catch (Exception e) {
                Log.Error($"[Stub] hook failed {label}: {e.Message}");
            }
        }
        if (!any) Log.Error($"[Stub] no method '{method}' on {type.FullName}");
        else Log.Info($"[Stub] installed: {type.Name}.{method}");
    }

    // Called from stubbed methods (emitted by Rewrite). Logs each distinct stub once to avoid per-frame spam.
    public static void Logged(string label) {
        if (logged.Add(label)) Log.Info($"[Stub] >> {label} (stubbed, no-op)");
    }

    private static void Rewrite(ILContext il, string label) {
        il.Body.Instructions.Clear();
        il.Body.ExceptionHandlers.Clear();
        il.Body.Variables.Clear();
        var c = new ILCursor(il);
        c.Emit(OpCodes.Ldstr, label);
        c.Emit(OpCodes.Call, typeof(Stub).GetMethod(nameof(Logged))!);
        EmitDefaultReturn(c, il);
    }

    private static void EmitDefaultReturn(ILCursor c, ILContext il) {
        var rt = il.Method.ReturnType;
        if (rt.MetadataType == MetadataType.Void) {
            c.Emit(OpCodes.Ret);
        } else if (!rt.IsValueType) {
            c.Emit(OpCodes.Ldnull);
            c.Emit(OpCodes.Ret);
        } else {
            var v = new VariableDefinition(rt);
            il.Body.Variables.Add(v);
            c.Emit(OpCodes.Ldloca, v);
            c.Emit(OpCodes.Initobj, rt);
            c.Emit(OpCodes.Ldloc, v);
            c.Emit(OpCodes.Ret);
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        logged.Clear();
    }
}
