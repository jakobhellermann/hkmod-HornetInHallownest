extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetPlayer.Playground;

// Feasibility spike: can HK's Unity (6000.0.61) load a Silksong-authored AssetBundle (6000.0.50)?
// Everything loaded here is tracked and torn down in Cleanup(), called from HornetPlayerMod.Unload — so a
// `dotnet build` hot-reload (Unload → Initialize) doesn't leak a still-loaded bundle (which would make the next
// LoadFromFile fail with "another AssetBundle with the same files is already loaded").
internal static class BundleSpike {
    private const string Aa =
        "/home/jakob/.local/share/Steam/steamapps/common/Hollow Knight Silksong/Hollow Knight Silksong_Data/StreamingAssets/aa/StandaloneLinux64/";

    // Dependency bundles must be resident BEFORE the prefab is loaded so its cross-bundle PPtrs resolve. The
    // MonoScripts bundle is the key one: the tk2d MonoBehaviours' m_Script points into it, and those MonoScripts
    // carry (assembly="TeamCherry.TK2D", class="tk2dSprite") which binds to HK's identical assembly.
    // b2 test: the REMAPPED monoscripts bundle (MonoScripts repointed to Silksong.* — examples/remap_monoscripts.rs),
    // same CAB name as the original so the prefab's m_Script PPtrs resolve to it.
    private const string RemappedMonoScripts =
        "/home/jakob/dev/hk/mods/HornetPlayer/Source/lib/monoscripts.silksong.bundle";
    // Silksong's _GameCameras rig (camera + HUD) repacked from Menu_Title (unity-scene-repacker addressable mode).
    // Its MonoScripts live in CAB 283454ff -> covered by RemappedMonoScripts; deps overlap the hero closure.
    private const string GameCamerasBundlePath =
        "/home/jakob/dev/hk/mods/HornetPlayer/Source/lib/gamecameras.silksong.bundle";

    private static readonly string[] DepBundles = {
        RemappedMonoScripts,                          // remapped MonoScripts -> Silksong.*
        Aa + "herocollections_assets_shared.bundle",  // tk2dSpriteCollectionData + materials
        Aa + "herodynamic_assets_all.bundle",         // tk2dSpriteAnimation (the clip library)
        Aa + "herostatic_assets_all.bundle",          // hero static assets / effect prefabs
        Aa + "herosfxstatic_assets_all.bundle",       // hero sfx / effect prefabs
        Aa + "herocollections_assets_tools.bundle",   // tool sprite collections
        Aa + "herocollections_assets_crestarchitect.bundle",
        Aa + "herocollections_assets_crestbeast.bundle",
        Aa + "herocollections_assets_crestcloakless.bundle",
        Aa + "herocollections_assets_crestreaper.bundle",
        Aa + "herocollections_assets_crestshaman.bundle",
        Aa + "herocollections_assets_crestwanderer.bundle",
        Aa + "herocollections_assets_crestwitch.bundle",
    };
    private const string PrefabBundlePath = Aa + "heroloading_assets_all.bundle";

    private static readonly List<AssetBundle> bundles = new();
    private static AssetBundle? bundle; // the prefab bundle
    private static GameObject? heroPrefab;
    private static GameObject? real;
    private static GameObject? gameCamerasGo;


    // Instantiate the FULL prefab ACTIVE (no stripping) so every component's Awake/Start runs against our prefixed
    // Silksong.* types. Unity swallows per-component Awake exceptions into Player.log — that log is the "what's
    // missing" list (e.g. GameManager.instance null, input/camera singletons absent).
    internal static object SpawnReal() {
        if (heroPrefab == null) return new { error = "hero prefab not loaded" };
        SilksongBootstrap.Ensure();
        GlobalSettingsBootstrap.Apply(); // assign GlobalSettings _instance from the loaded SOs (bypass Addressables)
        if (real != null) { Object.Destroy(real); real = null; }

        // Instantiate INACTIVE so we can patch null fields (missing-environment refs) before Awake runs, then activate.
        var staging = new GameObject("hp_real_staging");
        staging.SetActive(false);
        var inst = Object.Instantiate(heroPrefab, staging.transform);
        inst.name = "Hornet_Real";

        var hc = inst.GetComponent<Silksong::HeroController>();
        if (hc != null) {
            // wallClingEffect.SetActive(false) at the end of Awake NullRefs when the field is unset.
            EnsureChildField(hc, "wallClingEffect");
            EnsureEmptyConfigs(hc);
        }

        var follower = new GameObject("HornetReal"); // active parent → activating the instance runs Awake/Start
        Object.DontDestroyOnLoad(follower);
        var hk = Object.FindFirstObjectByType<HeroController>();
        follower.transform.position = hk != null ? hk.transform.position + new Vector3(3f, 0f, 0f) : Vector3.zero;
        inst.transform.SetParent(follower.transform, false);
        inst.transform.localPosition = Vector3.zero;
        Object.DestroyImmediate(staging);
        real = follower;

        var comps = inst.GetComponents<Component>();
        var alive = comps.Count(c => c != null);
        Log.Info($"[SpawnReal] instantiated — {alive}/{comps.Length} root components non-null; HeroController.instance set: {(Silksong::HeroController.instance != null)}");
        return new { ok = true, components = comps.Length, alive };
    }

    // Unity drops nested custom-serializable arrays (configs/specialConfigs : ConfigGroup[]) when the prefab loads
    // cross-build (MonoScript bound to the renamed Silksong.* assembly), leaving them null — Awake's `array.Length`
    // loop then NullRefs. Init them to empty so Awake completes. NOTE: this loses combat/crest config setup; real
    // values must be repopulated later (the data exists in the bundle, recoverable via rabex).
    private static void EnsureEmptyConfigs(Component owner) {
        foreach (var name in new[] { "configs", "specialConfigs" }) {
            var fi = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null || fi.GetValue(owner) != null) continue;
            fi.SetValue(owner, System.Array.CreateInstance(fi.FieldType.GetElementType()!, 0));
            Log.Info($"[SpawnReal] initialized null array field '{name}' to empty");
        }
    }

    // If a (private, serialized) GameObject field is null, give it a throwaway child so Awake's
    // `field.SetActive(...)`-style derefs don't NullRef. Used to patch missing-environment refs before activation.
    private static void EnsureChildField(Component owner, string field) {
        var fi = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fi == null || fi.FieldType != typeof(GameObject)) return;
        if (fi.GetValue(owner) != null) return;
        var dummy = new GameObject(field);
        dummy.transform.SetParent(owner.transform, false);
        dummy.SetActive(false);
        fi.SetValue(owner, dummy);
        Log.Info($"[SpawnReal] patched null field '{field}' with dummy child");
    }

    // Pinpoint the Awake NullRef: instantiate inactive, invoke the private Awake via reflection inside try/catch so we
    // get the full inner stack trace (Unity's log only shows the top frame for inlined throws).
    internal static object DiagnoseAwake() {
        if (heroPrefab == null) return new { error = "no prefab" };
        SilksongBootstrap.Ensure();
        var staging = new GameObject("hp_diag");
        staging.SetActive(false);
        var inst = Object.Instantiate(heroPrefab, staging.transform);
        var hc = inst.GetComponent<Silksong::HeroController>();

        // Dump null reference-type fields (candidates Awake may deref). Only those whose type is a UnityEngine.Object
        // or array/collection — value types/strings/primitives are noise.
        var nulls = new List<string>();
        foreach (var fi in hc.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            if (fi.FieldType.IsValueType || fi.FieldType == typeof(string)) continue;
            if (fi.GetValue(hc) == null) nulls.Add($"{fi.FieldType.Name} {fi.Name}");
        }
        Log.Info($"[DiagnoseAwake] {nulls.Count} null ref-fields: {string.Join(", ", nulls.Take(60))}");

        // The first array deref in Awake is `configs`/`specialConfigs` loops (array[i].Setup()). Report their shape.
        foreach (var fn in new[] { "configs", "specialConfigs" }) {
            var arr = (System.Array?)hc.GetType().GetField(fn, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(hc);
            if (arr == null) { Log.Info($"[DiagnoseAwake] {fn} = NULL"); continue; }
            var nullElems = 0;
            for (var i = 0; i < arr.Length; i++) if (arr.GetValue(i) == null) nullElems++;
            Log.Info($"[DiagnoseAwake] {fn}.Length={arr.Length}, nullElements={nullElems}");
        }

        // Decisive binding test: are serialized fields applied at all? wallClingEffect/vignette/heroBox are internal
        // child PPtrs that MUST resolve within the instantiated prefab. If null while the child object exists, the
        // MonoBehaviour serialized data was not applied (MonoScript/type-tree binding gap).
        foreach (var fn in new[] { "wallClingEffect", "vignette", "heroBox" }) {
            var fi = hc.GetType().GetField(fn, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var val = fi?.GetValue(hc) as UnityEngine.Object;
            Log.Info($"[DiagnoseAwake] field {fn}: bound={(val != null)}");
        }
        var childExists = inst.transform.Find("Effects/Tool_wall_cling_effect") != null;
        Log.Info($"[DiagnoseAwake] child 'Effects/Tool_wall_cling_effect' exists in prefab: {childExists}");

        // Probe: can the nested ConfigGroup type and its Config field type (HeroControllerConfig) actually load?
        // If Unity can't construct ConfigGroup at deserialize time, it drops the whole `configs` array to null.
        try {
            var asm = hc.GetType().Assembly;
            var cgType = asm.GetType("HeroController+ConfigGroup");
            var cfgType = asm.GetType("HeroControllerConfig");
            Log.Info($"[DiagnoseAwake] typeof ConfigGroup={cgType != null}, HeroControllerConfig={cfgType != null}");
            if (cgType != null) {
                var instCg = System.Activator.CreateInstance(cgType);
                Log.Info($"[DiagnoseAwake] new ConfigGroup() OK = {instCg != null}");
            }
        } catch (Exception e) {
            Log.Info($"[DiagnoseAwake] ConfigGroup type/ctor FAILED: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}");
        }

        // Probe Unity's managed serializer on our runtime-loaded ConfigGroup type. If JsonUtility round-trips the
        // public fields, Unity CAN reflect/serialize the type (so bundle-transfer skip is something else, and
        // repopulation via JsonUtility.FromJson is viable); if it returns "{}", Unity ignores the type entirely.
        try {
            var cgType = hc.GetType().Assembly.GetType("HeroController+ConfigGroup");
            var cg = System.Activator.CreateInstance(cgType!);
            cgType!.GetField("ActiveRoot")?.SetValue(cg, inst); // a non-null UnityEngine.Object public field
            var json = UnityEngine.JsonUtility.ToJson(cg);
            Log.Info($"[DiagnoseAwake] JsonUtility.ToJson(ConfigGroup) = {json}");
        } catch (Exception e) {
            Log.Info($"[DiagnoseAwake] JsonUtility probe failed: {(e.InnerException ?? e).Message}");
        }

        EnsureChildField(hc, "wallClingEffect");
        EnsureEmptyConfigs(hc);
        var awake = hc.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        string result;
        try {
            awake!.Invoke(hc, null);
            result = "Awake completed (no throw)";
        } catch (Exception e) {
            var inner = e.InnerException ?? e;
            result = inner.ToString();
        }
        Object.DestroyImmediate(staging);
        Log.Info($"[DiagnoseAwake] {result}");
        return new { result };
    }

    internal static object DespawnReal() {
        if (real == null) return new { ok = true, note = "nothing to despawn" };
        Object.Destroy(real);
        real = null;
        return new { ok = true, despawned = true };
    }

    // The live spawned HeroController (in the DontDestroyOnLoad follower).
    internal static Silksong::HeroController? RealHero =>
        real != null ? real.GetComponentInChildren<Silksong::HeroController>() : null;

    // Root of the spawned Hornet subtree. PlayMakerFix uses it to tell Hornet's FSMs (resolve actions to Silksong)
    // from HK's FSMs (resolve to HK) — every FSM under here is Silksong-authored.
    internal static GameObject? HornetRoot => real;

    // Dump the live spawned Hornet's movement state (reachable directly via `real`; /inspect can't, it's DontDestroyOnLoad).
    internal static object HeroState() {
        if (real == null) return new { error = "not spawned" };
        var hc = real.GetComponentInChildren<Silksong::HeroController>();
        if (hc == null) return new { error = "no HeroController" };
        var t = hc.GetType();
        object F(string n) => t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(hc);
        var cs = F("cState");
        object CS(string n) => cs?.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public)?.GetValue(cs);
        var pos = ((Component)hc).transform.position;
        var rb = F("rb2d") as Rigidbody2D;
        var ia = SilksongBootstrap.InputActions;
        var mv = ia?.MoveVector.Vector ?? default;
        return new {
            move_input = F("move_input"), hero_state = F("hero_state")?.ToString(),
            transitionState = F("transitionState")?.ToString(), isGameplayScene = F("isGameplayScene"),
            gameState = (F("gm") is { } g ? g.GetType().GetProperty("GameState")?.GetValue(g)?.ToString() : "gm-null"),
            inputBlocked = (bool?)t.GetMethod("IsInputBlocked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(hc, null),
            onGround = CS("onGround"), jumping = CS("jumping"), falling = CS("falling"),
            facingRight = CS("facingRight"), dashing = CS("dashing"), attacking = CS("attacking"),
            controlReqlinquished = F("controlReqlinquished"), acceptingInput = F("acceptingInput"),
            pos = new { pos.x, pos.y }, vel = rb != null ? new { rb.linearVelocity.x, rb.linearVelocity.y } : null,
            // input chain diagnostics: are our InControl commits landing?
            ia_null = ia == null,
            ia_same = ia != null && ReferenceEquals(ia, (F("inputHandler") as Silksong::InputHandler)?.inputActions),
            right = ia?.Right.IsPressed, left = ia?.Left.IsPressed, jumpWasPressed = ia?.Jump.WasPressed,
            moveVec = new { mv.x, mv.y },
        };
    }

    // READ-ONLY input/dash diagnostics: enabled-state + CanDash() (a pure query) + its gating fields. Does NOT invoke
    // Update/LookForInput/HeroDashPressed — those mutate hero state (and Update's FailSafeChecks can destroy the hero).
    internal static object DiagInput() {
        if (real == null) return new { error = "not spawned" };
        var hc = real.GetComponentInChildren<Silksong::HeroController>();
        if (hc == null) return new { error = "no HeroController" };
        var t = hc.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var comp = (Behaviour)(object)hc;
        object Field(string n) => t.GetField(n, BF)?.GetValue(hc)!;
        var cs2 = Field("cState");
        object CsB(string n) => cs2?.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public)?.GetValue(cs2)!;
        var pd = Field("playerData") as Silksong::PlayerData;
        var canDash = (bool?)t.GetMethod("CanDash", BF, null, Type.EmptyTypes, null)?.Invoke(hc, null);
        // Verify the buttonQueueTimers fix actually reached the hero's live InputHandler.
        var ih = Field("inputHandler");
        var bqtField = ih?.GetType().GetField("buttonQueueTimers", BF);
        var bqt = bqtField?.GetValue(ih) as Array;
        return new {
            enabled = comp.enabled, activeAndEnabled = comp.isActiveAndEnabled,
            inputQueue = new { ihNull = ih == null, fieldFound = bqtField != null, bqtNull = bqt == null, len = bqt?.Length ?? -1 },
            move_input = Field("move_input"), hero_state = Field("hero_state")?.ToString(),
            dash = new {
                canDash, hasDash = pd?.hasDash, dashCooldownTimer = Field("dashCooldownTimer"),
                preventDash = CsB("preventDash"), dashing = CsB("dashing"), airDashed = Field("airDashed"),
                onGround = CsB("onGround"),
            },
        };
    }

    // Ground truth on Hornet's FSMs: how many PlayMakerFSM components, how many enabled / active-in-hierarchy /
    // actually running (have an active state) / carrying MissingActions (unresolved). Answers "are the FSMs enabled
    // and running, free of resolution failures?".
    internal static object FsmState() {
        if (real == null) return new { error = "not spawned" };
        var hc = real.GetComponentInChildren<Silksong::HeroController>();
        var hcB = hc as Behaviour;
        var fsms = real.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true);
        int enabled = 0, activeInHier = 0, withState = 0, withMissing = 0;
        var sample = new List<object>();
        foreach (var f in fsms) {
            var b = (Behaviour)(object)f;
            var en = b.enabled;
            var act = b.gameObject.activeInHierarchy;
            if (en) enabled++;
            if (act) activeInHier++;
            string? state = null;
            try { state = f.Fsm?.ActiveStateName; } catch { }
            if (!string.IsNullOrEmpty(state)) withState++;
            var missing = false;
            try {
                foreach (var st in f.FsmStates) {
                    foreach (var a in st.Actions)
                        if (a.GetType().Name == "MissingAction") { missing = true; break; }
                    if (missing) break;
                }
            } catch { }
            if (missing) withMissing++;
            if (sample.Count < 60) sample.Add(new { name = f.FsmName, en, act, state, missing });
        }
        return new {
            hero = new { enabled = hcB?.enabled, activeAndEnabled = hcB?.isActiveAndEnabled, activeInHierarchy = hcB?.gameObject.activeInHierarchy },
            fsms = new { total = fsms.Length, enabled, activeInHier, running = withState, withMissingActions = withMissing },
            sample,
        };
    }

    // Dump a named FSM's full state machine (active state, every state's transitions + action types, global
    // transitions) so we can see why a move-FSM (e.g. Sprint) sits where it does and what event should advance it.
    internal static object FsmDump(string name) {
        if (real == null) return new { error = "not spawned" };
        var fsms = real.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true);
        SilksongPM::PlayMakerFSM? target = null;
        foreach (var f in fsms)
            if (string.Equals(f.FsmName, name, StringComparison.OrdinalIgnoreCase)) { target = f; break; }
        if (target == null)
            return new { error = "fsm not found", available = fsms.Select(f => f.FsmName).Distinct().ToArray() };
        var fsm = target.Fsm;
        var states = new List<object>();
        foreach (var st in fsm.States) {
            var trans = st.Transitions.Select(tr => $"{tr.EventName} -> {tr.ToState}").ToArray();
            var actions = st.Actions.Select(a => a.GetType().Name).ToArray();
            states.Add(new { name = st.Name, active = st.Name == fsm.ActiveStateName, trans, actions });
        }
        return new {
            fsmName = target.FsmName,
            enabled = ((Behaviour)(object)target).enabled,
            activeState = fsm.ActiveStateName,
            globalTransitions = fsm.GlobalTransitions.Select(tr => $"{tr.EventName} -> {tr.ToState}").ToArray(),
            states,
        };
    }

    // Load Silksong's _GameCameras rig (scene-mode bundle, repacked with --disable so the root loads INACTIVE → no
    // child FSM Awake/Update runs, which is what froze the frame before). LoadScene additive, wait for it (the scene
    // activates end-of-frame, not synchronously), then MOVE the live _GameCameras root out via DontDestroyOnLoad —
    // this reparents it into the DDOL scene, so UnloadSceneAsync drops the rest while the root survives with ZERO
    // copy. (Earlier we Object.Instantiate'd it; cloning the full 6216-object rig is what hung the frame — the
    // user's "normally not that expensive" was the tell.) The root stays inactive; activating + camera handover is a
    // deliberate later step. Load reload-all-deps with the Menu_Title closure first so externals resolve.
    // Inspect the moved _GameCameras root: which component types are present (+ owning assembly) and how many slots are
    // null (missing scripts = MonoScript didn't bind to a loaded type). Tells us if GameCameras resolved to Silksong.*.
    internal static object GcDump() {
        if (gameCamerasGo == null) return new { error = "not loaded" };
        var comps = gameCamerasGo.GetComponentsInChildren<Component>(true);
        var missing = comps.Count(c => c == null);
        var byType = comps.Where(c => c != null)
            .GroupBy(c => c.GetType().FullName + "  @" + c.GetType().Assembly.GetName().Name)
            .OrderByDescending(g => g.Count())
            .Select(g => new { type = g.Key, count = g.Count() })
            .ToArray();
        var gcLike = comps.Where(c => c != null && c.GetType().Name == "GameCameras")
            .Select(c => c.GetType().AssemblyQualifiedName).ToArray();
        return new {
            root = gameCamerasGo.name,
            totalComponents = comps.Length,
            missingScripts = missing,
            gameCamerasTypes = gcLike,
            children = gameCamerasGo.transform.Cast<Transform>().Select(t => t.name).ToArray(),
            types = byType,
        };
    }

    internal static IEnumerator LoadGameCamerasCo(System.Action<object?> respond) {
        if (gameCamerasGo != null) { respond(new { error = "already loaded" }); yield break; }
        var b = AssetBundle.LoadFromFile(GameCamerasBundlePath);
        if (b == null) { respond(new { error = "LoadFromFile failed (just repack-gamecameras)" }); yield break; }
        bundles.Add(b);
        var scenePaths = b.GetAllScenePaths();
        if (scenePaths.Length == 0) { respond(new { error = "no scenes in bundle (repack with --mode scene)" }); yield break; }
        var sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePaths[0]);
        USceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
        var scene = USceneManager.GetSceneByName(sceneName);
        var guard = 0;
        while (!scene.isLoaded && guard++ < 300) yield return null;
        if (!scene.isLoaded) { respond(new { error = "scene never loaded", sceneName }); yield break; }
        var roots = scene.GetRootGameObjects();
        var src = roots.FirstOrDefault(r => r.name.ToLowerInvariant().Contains("gamecameras"));
        if (src == null) {
            USceneManager.UnloadSceneAsync(scene);
            respond(new { error = "no _GameCameras root", roots = roots.Select(r => r.name).ToArray() });
            yield break;
        }
        // Report whether --disable held (root should be inactive). MOVE, don't clone.
        var wasActive = src.activeSelf;
        src.SetActive(false); // belt-and-suspenders: never let its FSMs tick before we're ready
        src.name = "Silksong_GameCameras";
        Object.DontDestroyOnLoad(src); // reparents into the DontDestroyOnLoad scene; survives the unload below
        gameCamerasGo = src;
        var gcComp = src.GetComponentInChildren<Silksong::GameCameras>(true);
        if (gcComp != null)
            typeof(Silksong::GameCameras).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, gcComp);
        USceneManager.UnloadSceneAsync(scene);
        respond(new {
            ok = true, sceneName,
            rootWasActiveOnLoad = wasActive, // expect false if --disable worked
            gcComponentFound = gcComp != null,
            instanceResolved = Silksong::GameCameras.SilentInstance != null,
            components = src.GetComponentsInChildren<Component>(true).Length,
        });
    }

    internal static void Run() {
        if (bundles.Count > 0) {
            Log.Info("[BundleSpike] already loaded, skipping");
            return;
        }

        foreach (var dep in DepBundles) {
            var b = AssetBundle.LoadFromFile(dep);
            Log.Info($"[BundleSpike] dep {(b == null ? "FAILED" : "ok")}: {dep}");
            if (b != null) bundles.Add(b);
        }

        Log.Info($"[BundleSpike] LoadFromFile: {PrefabBundlePath}");
        bundle = AssetBundle.LoadFromFile(PrefabBundlePath);
        if (bundle == null) {
            Log.Error("[BundleSpike] LoadFromFile returned null — bundle format likely incompatible");
            return;
        }
        bundles.Add(bundle);

        var names = bundle.GetAllAssetNames();
        Log.Info($"[BundleSpike] loaded OK — {names.Length} assets. Sample:");
        foreach (var n in names.Take(20)) Log.Info($"[BundleSpike]   {n}");

        // Try to load (not instantiate) the Hero_Hornet prefab asset. Missing dependencies (505 of them, in other
        // bundles) will resolve to null PPtrs, but the load itself tells us whether cross-game deserialization works.
        var heroName = names.FirstOrDefault(n => n.ToLowerInvariant().Contains("hero_hornet"));
        if (heroName == null) {
            Log.Info("[BundleSpike] no asset name contains 'hero_hornet'");
            return;
        }

        Log.Info($"[BundleSpike] LoadAsset<GameObject>({heroName})");
        var prefab = bundle.LoadAsset<GameObject>(heroName);
        if (prefab == null) {
            Log.Error("[BundleSpike] LoadAsset returned null");
            return;
        }
        heroPrefab = prefab;

        // b2 validation: before stripping, log how the prefab's root components bind. With the remapped monoscripts
        // bundle, Silksong scripts should resolve to Silksong.* (assembly Silksong.AssemblyCSharp) — not HK's
        // Assembly-CSharp (false-bind) or <null> (missing).
        Log.Info("[SilksongRemap] Hero_Hornet root component bindings:");
        foreach (var c in prefab.GetComponents<Component>())
            Log.Info(c == null
                ? "[SilksongRemap]   <null/missing-script>"
                : $"[SilksongRemap]   {c.GetType().FullName} [{c.GetType().Assembly.GetName().Name}]");

    }


    // One-shot test for the user's hypothesis: is `configs` null because the prefab's external PPtrs aren't resolved?
    // Full reload with the ENTIRE addressables dependency closure resident BEFORE LoadAsset (so every external PPtr
    // resolves), then report configs. If configs flips null->populated, unresolved externals WERE the cause.
    internal static object ReloadWithAllDeps(string listPath) {
        Cleanup();
        if (!System.IO.File.Exists(listPath)) return new { error = $"no list at {listPath}" };

        var monoB = AssetBundle.LoadFromFile(RemappedMonoScripts);
        if (monoB != null) bundles.Add(monoB);

        int ok = 0, fail = 0, skipped = 0;
        foreach (var raw in System.IO.File.ReadAllLines(listPath)) {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            // Skip the ORIGINAL monoscripts (would conflict with our remapped CAB) and heroloading (loaded below).
            if (name.Contains("monoscripts") || name.Contains("heroloading")) { skipped++; continue; }
            var b = AssetBundle.LoadFromFile(Aa + name);
            if (b != null) { bundles.Add(b); ok++; } else fail++;
        }
        Log.Info($"[ReloadAllDeps] deps loaded: {ok} ok, {fail} failed, {skipped} skipped");

        bundle = AssetBundle.LoadFromFile(PrefabBundlePath);
        if (bundle == null) return new { error = "heroloading load failed" };
        bundles.Add(bundle);
        var heroName = bundle.GetAllAssetNames().FirstOrDefault(n => n.ToLowerInvariant().Contains("hero_hornet"));
        heroPrefab = heroName != null ? bundle.LoadAsset<GameObject>(heroName) : null;
        if (heroPrefab == null) return new { error = "Hero_Hornet LoadAsset failed" };

        var staging = new GameObject("hp_depcheck");
        staging.SetActive(false);
        var inst = Object.Instantiate(heroPrefab, staging.transform);
        var hc = inst.GetComponent<Silksong::HeroController>();
        var fi = hc.GetType().GetField("configs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var arr = (System.Array?)fi?.GetValue(hc);
        // Also probe whether an external PPtr now resolves (jumpEffectPrefab) vs before.
        var jf = hc.GetType().GetField("jumpEffectPrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        var jumpResolved = (jf?.GetValue(hc) as UnityEngine.Object) != null;
        var configsLen = arr?.Length ?? -1;
        Log.Info($"[ReloadAllDeps] configs: null={arr == null}, length={configsLen}; jumpEffectPrefab resolved={jumpResolved}");
        Object.DestroyImmediate(staging);
        return new { depsOk = ok, depsFailed = fail, configsNull = arr == null, configsLength = configsLen, jumpEffectResolved = jumpResolved };
    }

    // Systemic-scope probe: across EVERY component in the instantiated prefab, find Unity-serialized fields whose
    // (element) type is a custom [Serializable] class from our Silksong assemblies (i.e. NOT a UnityEngine.Object
    // PPtr, NOT a primitive/string/enum, NOT a UnityEngine value type like Vector2). Report how many are null/empty
    // vs populated. If the custom-serializable fields are overwhelmingly null while PPtr fields bind, the transfer
    // gap is systemic — not specific to HeroController.configs.
    internal static object ScanSerializable() {
        if (heroPrefab == null) return new { error = "no prefab" };
        var staging = new GameObject("hp_scan");
        staging.SetActive(false);
        var inst = Object.Instantiate(heroPrefab, staging.transform);

        // Group reference-type custom-serializable fields by the OWNING component's assembly. The control is
        // TeamCherry.TK2D (e.g. tk2dSpriteCollectionData.spriteDefinitions : tk2dSpriteDefinition[]): HK-native, NOT
        // renamed, NOT runtime-injected, but loaded from the SAME bundle (same version skew). If tk2d's nested data
        // populates while Silksong.AssemblyCSharp's stays null, the gap is specific to our renamed/injected assembly.
        var perAsm = new Dictionary<string, (int withData, int empty)>();
        var examples = new List<string>();
        foreach (var comp in inst.GetComponentsInChildren<Component>(true)) {
            if (comp == null) continue;
            var ct = comp.GetType();
            foreach (var fi in ct.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!IsUnitySerialized(fi)) continue;
                var et = ElementType(fi.FieldType);
                if (!IsCustomSerializable(et) || et.IsValueType) continue; // reference types only
                var asm = et.Assembly.GetName().Name; // group by the FIELD TYPE's assembly (main vs firstpass vs ...)
                var val = fi.GetValue(comp);
                var hasData = val != null
                    && !(val is System.Array a && a.Length == 0)
                    && !(val is System.Collections.IList l && l.Count == 0);
                perAsm.TryGetValue(asm, out var cur);
                perAsm[asm] = hasData ? (cur.withData + 1, cur.empty) : (cur.withData, cur.empty + 1);
                if (!hasData && examples.Count < 30) examples.Add($"NULL {asm}: {ct.Name}.{fi.Name} = {et.FullName}");
            }
        }
        Object.DestroyImmediate(staging);
        Log.Info("[ScanSerializable] reference-type custom-serializable, per owning assembly (withData / empty-or-null):");
        foreach (var kv in perAsm.OrderBy(k => k.Key))
            Log.Info($"[ScanSerializable]   {kv.Key}: {kv.Value.withData} withData, {kv.Value.empty} empty/null");
        foreach (var e in examples) Log.Info($"[ScanSerializable]   POPULATED {e}");
        return new {
            perAssembly = perAsm.OrderBy(k => k.Key)
                .Select(k => new { asm = k.Key, withData = k.Value.Item1, empty = k.Value.Item2 }).ToList(),
            examples,
        };
    }

    private static bool IsUnitySerialized(System.Reflection.FieldInfo fi) {
        if (fi.IsStatic) return false;
        if (fi.IsPublic) return !fi.IsNotSerialized;
        return fi.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
    }

    private static System.Type ElementType(System.Type t) =>
        t.IsArray ? t.GetElementType()! :
        (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) ? t.GetGenericArguments()[0] : t;

    // A game-content custom [Serializable] class (not a UnityEngine.Object PPtr, not a primitive/string/enum, not a
    // BCL/UnityEngine type). Scans every assembly so we can compare native (TeamCherry.*) vs renamed (Silksong.*).
    private static bool IsCustomSerializable(System.Type t) {
        if (t == null || t.IsPrimitive || t.IsEnum || t == typeof(string)) return false;
        if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false; // PPtr — handled natively
        if (!t.IsSerializable) return false;
        var asm = t.Assembly.GetName().Name;
        if (asm.StartsWith("System") || asm.StartsWith("Unity") || asm == "mscorlib" || asm == "netstandard") return false;
        return true;
    }

    internal static void Cleanup() {
        if (real != null) { Object.Destroy(real); real = null; }
        if (gameCamerasGo != null) { Object.Destroy(gameCamerasGo); gameCamerasGo = null; }

        foreach (var b in bundles)
            if (b != null) b.Unload(true);
        var n = bundles.Count;
        bundles.Clear();
        bundle = null;
        if (n > 0) Log.Info($"[BundleSpike] {n} bundles unloaded");
    }
}
