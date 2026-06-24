extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HornetPlayer.HornetInHallownest.Modules;
using UnityEngine;

namespace HornetPlayer.Playground;

// Feasibility spike: can HK's Unity (6000.0.61) load a Silksong-authored AssetBundle (6000.0.50)?
// Everything loaded here is tracked and torn down in Cleanup(), called from HornetPlayerMod.Unload — so a
// `dotnet build` hot-reload (Unload → Initialize) doesn't leak a still-loaded bundle (which would make the next
// LoadFromFile fail with "another AssetBundle with the same files is already loaded").
internal static class BundleSpike {
    // Minimal binding test: load a hand-built bundle with one GameObject+MonoBehaviour per test script (m_Script ->
    // CAB-283454ff monoscripts, base fields only). Isolates pure script binding from scene/closure complexity. Needs
    // only the remapped monoscripts bundle resident.
    private const string MinimalBundlePath =
        "/home/jakob/dev/hk/mods/HornetPlayer/Source/lib/minimal-binding-test.silksong.bundle";

    // Spawn moved to HornetInHallownest/Core/HornetSpawner (the first migrated module). These forwarders keep the
    // existing Playground callers (HornetDeath/HornetBench/HeroSwitch/…) and the diagnostics below reading the live
    // spawn state without churn; they get repointed to HornetSpawner when each of those systems migrates in turn.
    internal static Silksong::HeroController? RealHero => HornetSpawner.RealHero;
    internal static GameObject? HornetRoot => HornetSpawner.HornetRoot;

    // Scan ALL loaded GameObjects (incl. inactive + DontDestroyOnLoad) for components that are null — i.e. a
    // MonoBehaviour whose m_Script didn't resolve ("The referenced script ... is missing!"). Reports each GO's path,
    // the count of missing slots, and the names of its surviving sibling components (which usually reveal WHAT is
    // missing). Filters to real scene objects (asset prefabs loaded from bundles legitimately have unresolved scripts).
    internal static object ScanMissing() {
        var sceneHits = new List<object>();
        var assetHits = new List<object>();
        var byScene = new Dictionary<string, int>();
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) {
            if (t == null) continue;
            var go = t.gameObject;
            var comps = go.GetComponents<Component>();
            var nulls = 0;
            foreach (var c in comps)
                if (c == null)
                    nulls++;
            if (nulls == 0) continue;
            var inScene = go.scene.IsValid();
            var scene = inScene ? go.scene.name ?? "<none>" : "<asset>";
            byScene.TryGetValue(scene, out var sc);
            byScene[scene] = sc + nulls;
            var list = inScene ? sceneHits : assetHits;
            if (list.Count < 50) {
                var path = go.name;
                for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var siblings = comps.Where(c => c != null).Select(c => c.GetType().Name).ToArray();
                list.Add(new { path, missing = nulls, siblings });
            }
        }

        var total = byScene.Values.Sum();
        Log.Info(
            $"[ScanMissing] {total} missing-script slots; scene GOs={sceneHits.Count} asset GOs={assetHits.Count}");
        return new {
            totalMissingSlots = total, perScene = byScene, sceneObjects = sceneHits, assetObjects = assetHits
        };
    }

    // Dump the live spawned Hornet's movement state (reachable directly via `real`; /inspect can't, it's DontDestroyOnLoad).
    internal static object HeroState() {
        if (HornetRoot == null) return new { error = "not spawned" };
        var hc = HornetRoot.GetComponentInChildren<Silksong::HeroController>();
        if (hc == null) return new { error = "no HeroController" };
        var t = hc.GetType();

        object? F(string n) {
            return t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(hc);
        }

        var cs = F("cState");

        object? CS(string n) {
            return cs?.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public)?.GetValue(cs);
        }

        var pos = hc.transform.position;
        var rb = F("rb2d") as Rigidbody2D;
        var ia = SilksongBootstrap.InputActions;
        var mv = ia?.MoveVector.Vector ?? default;

        // Pure gate queries (no mutation) — these are exactly what Update checks before HeroJump()/DoAttack().
        bool? Q(string n) {
            return (bool?)t.GetMethod(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                Type.EmptyTypes, null)?.Invoke(hc, null);
        }

        return new {
            move_input = F("move_input"), hero_state = F("hero_state")?.ToString(),
            transitionState = F("transitionState")?.ToString(), isGameplayScene = F("isGameplayScene"),
            gameState = F("gm") is { } g ? g.GetType().GetProperty("GameState")?.GetValue(g)?.ToString() : "gm-null",
            inputBlocked = (bool?)t
                .GetMethod("IsInputBlocked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(hc, null),
            // The decisive trio for "can't jump/attack while sprinting": dashing & isSprinting gate the predicates,
            // canJump/canAttack are the actual answers. canInput rules out the transition/pause gate.
            canJump = Q("CanJump"), canAttack = Q("CanAttack"), canDash = Q("CanDash"), canInput = Q("CanInput"),
            isSprinting = CS("isSprinting"), sprintBufferSteps = F("sprintBufferSteps"),
            onGround = CS("onGround"), jumping = CS("jumping"), falling = CS("falling"),
            facingRight = CS("facingRight"), dashing = CS("dashing"), attacking = CS("attacking"),
            controlReqlinquished = F("controlReqlinquished"), acceptingInput = F("acceptingInput"),
            pos = new { pos.x, pos.y }, vel = rb != null ? new { rb.linearVelocity.x, rb.linearVelocity.y } : null,
            // input chain diagnostics: are our InControl commits landing?
            ia_null = ia == null,
            ia_same = ia != null && ReferenceEquals(ia, (F("inputHandler") as Silksong::InputHandler)?.inputActions),
            right = ia?.Right.IsPressed, left = ia?.Left.IsPressed, jumpWasPressed = ia?.Jump.WasPressed,
            moveVec = new { mv.x, mv.y },
            // Pinpoint an ia_same=false: is the hero bound to OUR bootstrap Handler/gm at all? If handlerIsHeros=false
            // the hero found a different InputHandler (different gm) — then the fix is the binding, not inputActions.
            handlerIsHeros = ReferenceEquals(SilksongBootstrap.Handler, F("inputHandler")),
            gmIsBootstrap = ReferenceEquals(Silksong::GameManager._instance, F("gm")),
            heroHandlerNull = F("inputHandler") == null
        };
    }

    // Audio diagnostics: which gate in RandomAudioClipTableExtensions.SpawnAndPlayOneShot silently returns null (no SFX).
    internal static object AudioDiag() {
        var prefab = Silksong::GlobalSettings.Audio.DefaultAudioSourcePrefab;
        var gc = Silksong::GameCameras.SilentInstance;
        return new {
            defaultPrefabNull = prefab == null,
            prefabSpatialBlend = prefab != null ? prefab.spatialBlend : -1f,
            silentInstanceNull = gc == null,
            mainCameraNull = gc != null && gc.mainCamera == null
        };
    }

    // Bench diagnostics: HK's atBench signal (set by HK's Bench Control FSM when resting), Knight/Hornet positions, and
    // Hornet's live animation clips (filtered to rest/sit/bench) — to confirm the mirror signal + the real sit-clip names
    // for HornetBench. Capture during a held rest.
    internal static object BenchState() {
        var hc = RealHero;
        var knight = HeroController.UnsafeInstance;
        var pdHk = PlayerData.instance; // HK PlayerData — atBench is HK's
        var anim = hc != null ? hc.AnimCtrl?.animator : null;
        var restClips = Array.Empty<string>();
        if (anim?.Library?.clips != null)
            restClips = anim.Library.clips
                .Where(c => c != null && c.name != null &&
                            (c.name.IndexOf("rest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             c.name.IndexOf("sit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             c.name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(c => c.name).ToArray();

        return new {
            atBench = pdHk != null && pdHk.atBench,
            knightPos = knight != null ? (Vector2)knight.transform.position : (Vector2?)null,
            hornetPos = hc != null ? (Vector2)hc.transform.position : (Vector2?)null,
            hornetClip = anim?.CurrentClip?.name,
            hornetControlReq = hc?.controlReqlinquished,
            totalClips = anim?.Library?.clips?.Length ?? 0,
            restClips
        };
    }

    // READ-ONLY input/dash diagnostics: enabled-state + CanDash() (a pure query) + its gating fields. Does NOT invoke
    // Update/LookForInput/HeroDashPressed — those mutate hero state (and Update's FailSafeChecks can destroy the hero).
    internal static object DiagInput() {
        if (HornetRoot == null) return new { error = "not spawned" };
        var hc = HornetRoot.GetComponentInChildren<Silksong::HeroController>();
        if (hc == null) return new { error = "no HeroController" };
        var t = hc.GetType();
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var comp = (Behaviour)hc;

        object Field(string n) {
            return t.GetField(n, BF)?.GetValue(hc)!;
        }

        var cs2 = Field("cState");

        object CsB(string n) {
            return cs2?.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public)?.GetValue(cs2)!;
        }

        var pd = Field("playerData") as Silksong::PlayerData;
        var canDash = (bool?)t.GetMethod("CanDash", BF, null, Type.EmptyTypes, null)?.Invoke(hc, null);
        // Verify the buttonQueueTimers fix actually reached the hero's live InputHandler.
        var ih = Field("inputHandler");
        var bqtField = ih?.GetType().GetField("buttonQueueTimers", BF);
        var bqt = bqtField?.GetValue(ih) as Array;
        return new {
            comp.enabled, activeAndEnabled = comp.isActiveAndEnabled,
            inputQueue = new
                { ihNull = ih == null, fieldFound = bqtField != null, bqtNull = bqt == null, len = bqt?.Length ?? -1 },
            move_input = Field("move_input"), hero_state = Field("hero_state")?.ToString(),
            dash = new {
                canDash,
                pd?.hasDash, dashCooldownTimer = Field("dashCooldownTimer"),
                preventDash = CsB("preventDash"), dashing = CsB("dashing"), airDashed = Field("airDashed"),
                onGround = CsB("onGround")
            }
        };
    }

    // Ground truth on Hornet's FSMs: how many PlayMakerFSM components, how many enabled / active-in-hierarchy /
    // actually running (have an active state) / carrying MissingActions (unresolved). Answers "are the FSMs enabled
    // and running, free of resolution failures?".
    internal static object FsmState() {
        if (HornetRoot == null) return new { error = "not spawned" };
        var hc = HornetRoot.GetComponentInChildren<Silksong::HeroController>();
        var hcB = hc as Behaviour;
        var fsms = HornetRoot.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true);
        int enabled = 0, activeInHier = 0, withState = 0, withMissing = 0;
        var sample = new List<object>();
        foreach (var f in fsms) {
            var b = (Behaviour)f;
            var en = b.enabled;
            var act = b.gameObject.activeInHierarchy;
            if (en) enabled++;
            if (act) activeInHier++;
            string? state = null;
            try {
                state = f.Fsm?.ActiveStateName;
            } catch {
            }

            if (!string.IsNullOrEmpty(state)) withState++;
            var missing = false;
            try {
                foreach (var st in f.FsmStates) {
                    if (!st.ActionsLoaded)
                        continue; // accessing .Actions on an uninitialized FSM (fsm==null) logs "Fsm not initialized" + a broken LoadActions (NullRef) — skip
                    foreach (var a in st.Actions)
                        if (a.GetType().Name == "MissingAction") {
                            missing = true;
                            break;
                        }

                    if (missing) break;
                }
            } catch {
            }

            if (missing) withMissing++;
            if (sample.Count < 60) sample.Add(new { name = f.FsmName, en, act, state, missing });
        }

        return new {
            hero = new { hcB?.enabled, activeAndEnabled = hcB?.isActiveAndEnabled, hcB?.gameObject.activeInHierarchy },
            fsms = new {
                total = fsms.Length, enabled, activeInHier, running = withState, withMissingActions = withMissing
            },
            sample
        };
    }

    // Probe what the Sprint FSM's CallMethodProper("CameraTarget","SetSprint") actually resolves to on the live hero:
    // does the hero GO have a CameraTarget component (runtime-added?), what TYPE/assembly is it, and does that type
    // expose SetSprint? Answers whether "Method Name is invalid" is a wrong-type CameraTarget vs an overload/param miss.
    internal static object ProbeCameraTarget() {
        var hc = RealHero;
        if (hc == null) return new { error = "not spawned" };
        var go = hc.gameObject;
        var ct = go.GetComponent(typeof(Silksong::CameraTarget).Name);
        var roots = go.GetComponents<Component>().Where(c => c != null)
            .Select(c => c.GetType().Name + " [" + c.GetType().Assembly.GetName().Name + "]").ToArray();
        object? ctInfo = ct == null
            ? null
            : new {
                type = ct.GetType().FullName,
                asm = ct.GetType().Assembly.GetName().Name,
                getMethodSetSprint = ct.GetType().GetMethod("SetSprint") != null,
                sprintMethods = ct.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.Name.IndexOf("Sprint", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m =>
                        m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                    .ToArray()
            };
        return new { heroGoHasCameraTarget = ct != null, cameraTarget = ctInfo, heroRootComponents = roots };
    }

    // Resolve, at runtime, exactly what the Sprint FSM's CallMethodProper("CameraTarget","SetSprint") targets: its
    // FsmOwnerDefault option, the GameObject GetOwnerDefaultTarget() returns, and the type/assembly of the CameraTarget
    // component found there (+ whether it exposes SetSprint). This nails why we get "Method Name is invalid" despite the
    // hero having no CameraTarget — i.e. which OTHER object (likely HK's) the lookup lands on.
    internal static object ProbeSprintTarget() {
        if (HornetRoot == null) return new { error = "not spawned" };
        var results = new List<object>();
        foreach (var f in HornetRoot.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true)) {
            foreach (var st in f.FsmStates) {
                if (!st.ActionsLoaded) continue; // don't trigger LoadActions on uninitialized FSMs (NullRef spam)
                foreach (var a in st.Actions) {
                    var t = a.GetType();
                    if (t.Name != "CallMethodProper") continue;
                    var mn = t.GetField("methodName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(a);
                    var mnVal = mn?.GetType().GetProperty("Value")?.GetValue(mn) as string;
                    if (mnVal != "SetSprint") continue;
                    var beh = t.GetField("behaviour", BindingFlags.Instance | BindingFlags.Public)?.GetValue(a);
                    var behVal = beh?.GetType().GetProperty("Value")?.GetValue(beh) as string ?? "";
                    var owner = t.GetField("gameObject", BindingFlags.Instance | BindingFlags.Public)?.GetValue(a)
                        as SilksongPM::HutongGames.PlayMaker.FsmOwnerDefault;
                    GameObject? resolved = null;
                    string? ownerOption = null, specified = null, ownerVarName = null;
                    var ownerUseVariable = false;
                    if (owner != null) {
                        ownerOption = owner.OwnerOption.ToString();
                        var fgo = owner.GameObject; // FsmGameObject (NamedVariable)
                        ownerUseVariable = fgo != null && fgo.UseVariable;
                        ownerVarName = fgo?.Name; // the variable/name slot
                        specified = fgo?.Value != null ? fgo.Value.name : null;
                        try {
                            resolved = f.Fsm.GetOwnerDefaultTarget(owner);
                        } catch (Exception e) {
                            ownerOption += " (resolve threw: " + e.Message + ")";
                        }
                    }

                    object comp = "n/a";
                    if (resolved != null) {
                        var ct = resolved.GetComponent(behVal);
                        comp = ct == null
                            ? "MISSING on resolved GO"
                            : new {
                                type = ct.GetType().FullName, asm = ct.GetType().Assembly.GetName().Name,
                                hasSetSprint = ct.GetType().GetMethod("SetSprint") != null
                            };
                    }

                    results.Add(new {
                        fsm = f.FsmName, state = st.Name, behaviour = behVal, ownerOption, ownerUseVariable,
                        ownerVarName, specified, resolvedGo = resolved?.name, found = comp
                    });
                }
            }

            // Also dump the Sprint FSM's GameObject variables (who might hold the resolved "Camera Target") + action
            // types per state (to spot a Find/SetGameObject that populates it by name).
            if (f.FsmName == "Sprint") {
                var vars = f.Fsm.Variables.GameObjectVariables
                    .Select(v => new {
                        v.Name, value = v.Value != null ? v.Value.name : null,
                        asm = v.Value != null ? (object?)null : null
                    }).ToArray();
                var actionTypes = f.FsmStates.SelectMany(s => s.Actions.Select(x => x.GetType().Name)).Distinct()
                    .OrderBy(x => x).ToArray();
                results.Add(new { sprintFsmVars = vars, sprintActionTypes = actionTypes });
            }
        }

        return new { count = results.Count, results };
    }

    // Find which FSM/state/action references a string `needle` (e.g. a method name like "SetSprint"). Scans every
    // action's FsmString/string fields across the spawned hero's FSMs and dumps all string fields of the matching
    // action (so a CallMethodProper match shows both its methodName AND its `behaviour` target). For root-causing
    // "Method Name is invalid" / "missing behaviour" style PlayMaker errors.
    internal static object FindFsmAction(string needle) {
        if (HornetRoot == null) return new { error = "not spawned" };
        var hits = new List<object>();
        foreach (var f in HornetRoot.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true))
        foreach (var st in f.FsmStates) {
            if (!st.ActionsLoaded) continue; // don't trigger LoadActions on uninitialized FSMs (NullRef spam)
            foreach (var a in st.Actions) {
                var fields = new Dictionary<string, string>();
                var match = false;
                foreach (var fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)) {
                    var v = fi.GetValue(a);
                    if (v == null) continue;
                    var s = v.GetType().Name == "FsmString"
                        ? v.GetType().GetProperty("Value")?.GetValue(v) as string
                        : v as string;
                    if (string.IsNullOrEmpty(s)) continue;
                    fields[fi.Name] = s;
                    if (s!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                }

                if (match)
                    hits.Add(new {
                        fsm = f.FsmName, go = f.gameObject.name, state = st.Name, action = a.GetType().Name, fields
                    });
            }
        }

        return new { needle, count = hits.Count, hits };
    }

    // Dump a Silksong FSM's bool/int/float variables (name=value) — to inspect gate vars like "Bind Locked".
    internal static object FsmVars(string fsmName, string? goFilter = null) {
        foreach (var f in Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>()) {
            if (f == null || !string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!f.gameObject.activeInHierarchy) continue;
            if (!string.IsNullOrEmpty(goFilter) &&
                f.gameObject.name.IndexOf(goFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var vars = f.FsmVariables;
            var d = new Dictionary<string, string>();
            foreach (var b in vars.BoolVariables) d[b.Name] = "bool:" + b.Value;
            foreach (var n in vars.IntVariables) d[n.Name] = "int:" + n.Value;
            foreach (var ff in vars.FloatVariables) d[ff.Name] = "float:" + ff.Value;
            foreach (var g in vars.GameObjectVariables) d[g.Name] = "go:" + (g.Value != null ? g.Value.name : "<null>");
            // also surface the isolated Silksong PlayMaker global "Hero" (set by the Globalise_Hero_Hornet FSM)
            var globalHero = "<no global var>";
            var gh = SilksongPM::PlayMakerGlobals.Instance?.Variables?.FindFsmGameObject("Hero");
            if (gh != null) globalHero = gh.Value != null ? gh.Value.name : "<null>";
            return new { fsm = fsmName, go = f.gameObject.name, vars = d, globalHero };
        }

        return new { error = "fsm not found" };
    }

    // Readable repr of a PlayMaker action field: FsmEvent -> "evt:NAME", named Fsm vars -> "name=value", else ToString.
    private static string? FsmFieldRepr(object v) {
        var vt = v.GetType();
        var nameP = vt.GetProperty("Name");
        if (nameP != null) {
            var nm = nameP.GetValue(v) as string;
            if (vt.Name == "FsmEvent") return string.IsNullOrEmpty(nm) ? null : "evt:" + nm;
            var valP = vt.GetProperty("Value");
            if (valP != null) {
                object? val = null;
                try {
                    val = valP.GetValue(v);
                } catch {
                }

                return (string.IsNullOrEmpty(nm) ? "" : nm + "=") + (val?.ToString() ?? "?");
            }
        }

        var s = v.ToString();
        return !string.IsNullOrEmpty(s) && s != vt.FullName ? s : null;
    }

    internal static object DumpStateActions(string fsmName, string stateName, string? goFilter = null) {
        foreach (var f in Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>()) {
            if (f == null || !string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!f.gameObject.activeInHierarchy) continue;
            if (!string.IsNullOrEmpty(goFilter) &&
                f.gameObject.name.IndexOf(goFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            SilksongPM::HutongGames.PlayMaker.Fsm fsm;
            try {
                fsm = f.Fsm;
            } catch {
                continue;
            }

            foreach (var st in fsm.States) {
                if (!string.Equals(st.Name, stateName, StringComparison.OrdinalIgnoreCase)) continue;
                var acts = new List<object>();
                SilksongPM::HutongGames.PlayMaker.FsmStateAction[] actions;
                try {
                    actions = st.Actions;
                } catch {
                    return new { error = "actions not loaded" };
                }

                foreach (var a in actions ?? new SilksongPM::HutongGames.PlayMaker.FsmStateAction[0]) {
                    if (a == null) continue;
                    var fields = new Dictionary<string, string>();
                    foreach (var fi in a.GetType()
                                 .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                        object? v;
                        try {
                            v = fi.GetValue(a);
                        } catch {
                            continue;
                        }

                        if (v == null) continue;
                        var repr = FsmFieldRepr(v);
                        if (!string.IsNullOrEmpty(repr))
                            fields[fi.Name] = repr!.Length > 60 ? repr.Substring(0, 60) : repr;
                    }

                    acts.Add(new { action = a.GetType().Name, fields });
                }

                return new { fsm = fsmName, state = stateName, go = f.gameObject.name, actions = acts };
            }
        }

        return new { error = "fsm/state not found" };
    }

    // Find which FSMs SEND a given PlayMaker event (reflect each loaded action's FsmEvent-typed fields). Reveals the
    // natural orchestrator that fires HUD events like "SHOW HP" — so we trigger IT instead of re-sending by hand.
    internal static object FindEventSenders(string evt) {
        var hits = new List<object>();
        foreach (var f in Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>()) {
            if (f == null) continue;
            SilksongPM::HutongGames.PlayMaker.Fsm fsm;
            try {
                fsm = f.Fsm;
            } catch {
                continue;
            }

            if (fsm?.States == null) continue;
            foreach (var st in fsm.States) {
                if (!st.ActionsLoaded)
                    try {
                        _ = st.Actions;
                    } catch {
                        continue;
                    }

                SilksongPM::HutongGames.PlayMaker.FsmStateAction[] actions;
                try {
                    actions = st.Actions;
                } catch {
                    continue;
                }

                if (actions == null) continue;
                foreach (var a in actions) {
                    if (a == null) continue;
                    foreach (var fi in a.GetType()
                                 .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                        if (!typeof(SilksongPM::HutongGames.PlayMaker.FsmEvent).IsAssignableFrom(fi.FieldType))
                            continue;
                        var ev = fi.GetValue(a) as SilksongPM::HutongGames.PlayMaker.FsmEvent;
                        if (ev != null && string.Equals(ev.Name, evt, StringComparison.OrdinalIgnoreCase)) {
                            var go = f.gameObject;
                            var path = go.name;
                            for (var p = go.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                            hits.Add(new { fsm = f.FsmName, go = path, state = st.Name, action = a.GetType().Name });
                        }
                    }
                }
            }
        }

        return new { evt, senderCount = hits.Count, senders = hits };
    }

    // List all PlayMakerFSMs whose GameObject path contains `pathContains` (name, go path, active state, enabled,
    // global transitions). Used to find the HUD's orchestrator FSMs.
    internal static object FsmList(string pathContains) {
        var all = Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>();
        var seen = new HashSet<string>();
        var list = new List<object>();
        foreach (var f in all) {
            if (f == null) continue;
            var go = f.gameObject;
            if (!go.activeInHierarchy) continue;
            var path = go.name;
            for (var p = go.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
            if (!path.Contains(pathContains)) continue;
            var key = path + "::" + f.FsmName;
            if (!seen.Add(key)) continue; // dedupe identical clones
            string state;
            bool en;
            try {
                state = f.Fsm?.ActiveStateName ?? "?";
            } catch {
                state = "<err>";
            }

            try {
                en = f.enabled;
            } catch {
                en = false;
            }

            string[] gts;
            try {
                gts = f.Fsm?.GlobalTransitions.Select(t => $"{t.EventName}->{t.ToState}").ToArray() ??
                      Array.Empty<string>();
            } catch {
                gts = new string[0];
            }

            list.Add(new { fsm = f.FsmName, go = path, state, enabled = en, globals = gts });
        }

        return new { count = list.Count, fsms = list };
    }

    // Send a PlayMaker event to every live FSM with the given name (HUD FSMs respond to global transitions like
    // "HUD APPEAR RESET"). Also broadcasts via EventRegister (TeamCherry's global event layer) for listeners.
    internal static object SendFsmEvent(string name, string evt) {
        var all = Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>();
        var sent = 0;
        var enabledCount = 0;
        foreach (var f in all)
            if (string.Equals(f.FsmName, name, StringComparison.OrdinalIgnoreCase) && f.gameObject.activeInHierarchy)
                try {
                    var b = (Behaviour)f;
                    if (!b.enabled) {
                        b.enabled = true;
                        enabledCount++;
                    } // a disabled FSM ignores events; enable to drive it

                    f.SendEvent(evt);
                    sent++;
                } catch (Exception e) {
                    Log.Error($"[SendFsmEvent] {e.Message}");
                }

        return new { fsm = name, evt, sentToFsms = sent, enabledFirst = enabledCount };
    }

    private static void SendToHealthDisplays(string evt) {
        foreach (var f in Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>())
            if (f != null && string.Equals(f.FsmName, "health_display", StringComparison.OrdinalIgnoreCase) &&
                f.gameObject.activeInHierarchy)
                try {
                    var b = (Behaviour)f;
                    if (!b.enabled) b.enabled = true;
                    f.SendEvent(evt);
                } catch {
                }
    }

    // Drive the health-HUD appear sequence over frames (the per-mask `health_display` FSMs need Update cycles between
    // events, so this is a coroutine, not a one-shot). The chain, reverse-engineered from the FSM graph:
    //   HUD APPEAR RESET -> Init (rebuilds the mask mesh)  ->  MAX HP UP xN (Inactive->Appear path)  ->  SHOW HP
    //   (First Pause -> Appear Pause -> ... -> Idle = visible). This is a STAND-IN for the natural trigger (HeroController
    //   scene-entry -> proxyFSM -> HUD), to make the HUD reproducible until that real wiring is in place.
    internal static IEnumerator DriveHealthHud(Action<object?> respond) {
        SendToHealthDisplays("HUD APPEAR RESET");
        yield return new WaitForSeconds(0.25f);
        for (var i = 0; i < 8; i++) {
            SendToHealthDisplays("MAX HP UP");
            yield return new WaitForSeconds(0.08f);
        }

        yield return new WaitForSeconds(0.1f);
        SendToHealthDisplays("SHOW HP");
        yield return new WaitForSeconds(0.2f);
        respond(new { ok = true, driven = "HUD APPEAR RESET -> MAX HP UP x8 -> SHOW HP" });
    }

    // Like FsmDump but searches ALL loaded PlayMakerFSMs (not just the hero) — for HUD/rig FSMs. Prefers an
    // active-in-hierarchy instance (there are often many same-named copies, e.g. one health_display per mask).
    internal static object FsmDumpAny(string name) {
        var all = Resources.FindObjectsOfTypeAll<SilksongPM::PlayMakerFSM>();
        SilksongPM::PlayMakerFSM? target = null;
        foreach (var f in all)
            if (string.Equals(f.FsmName, name, StringComparison.OrdinalIgnoreCase)) {
                target = f;
                if (f.gameObject.activeInHierarchy) break; // prefer a live one
            }

        if (target == null)
            return new { error = "fsm not found", count = all.Length };
        return DumpFsm(target);
    }

    // Dump an HK (global PlayMaker) FSM by name — FsmDumpAny only scans Silksong PlayMakerFSM, so HK scene objects
    // (levers, bells, geo rocks, …) need this. Same shape as DumpFsm; HK's PlayMaker API is identical.
    internal static object FsmDumpHk(string name) {
        var all = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
        PlayMakerFSM? target = null;
        foreach (var f in all)
            if (string.Equals(f.FsmName, name, StringComparison.OrdinalIgnoreCase)) {
                target = f;
                if (f.gameObject.activeInHierarchy) break;
            }

        if (target == null) return new { error = "hk fsm not found", count = all.Length };
        var fsm = target.Fsm;
        var states = new List<object>();
        foreach (var st in fsm.States) {
            var trans = st.Transitions.Select(tr => $"{tr.EventName} -> {tr.ToState}").ToArray();
            var actions = st.ActionsLoaded
                ? st.Actions.Select(a => {
                    // public FsmVar fields (collideTag/sendEvent/fsmName/...) carry the action's config — dump them.
                    var flds = a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Select(f => {
                            object? v = null;
                            try {
                                v = f.GetValue(a);
                            } catch {
                            }

                            return $"{f.Name}={v}";
                        });
                    return $"{a.GetType().Name}({string.Join(", ", flds)})";
                }).ToArray()
                : ["<not loaded>"];
            states.Add(new { name = st.Name, active = st.Name == fsm.ActiveStateName, trans, actions });
        }

        return new {
            fsmName = target.FsmName, gameObject = target.gameObject.name,
            activeState = fsm.ActiveStateName,
            globalTransitions = fsm.GlobalTransitions.Select(tr => $"{tr.EventName} -> {tr.ToState}").ToArray(),
            states
        };
    }

    internal static object FsmDump(string name) {
        if (HornetRoot == null) return new { error = "not spawned" };
        var fsms = HornetRoot.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true);
        SilksongPM::PlayMakerFSM? target = null;
        foreach (var f in fsms)
            if (string.Equals(f.FsmName, name, StringComparison.OrdinalIgnoreCase)) {
                target = f;
                break;
            }

        if (target == null)
            return new { error = "fsm not found", available = fsms.Select(f => f.FsmName).Distinct().ToArray() };
        return DumpFsm(target);
    }

    private static object DumpFsm(SilksongPM::PlayMakerFSM target) {
        var fsm = target.Fsm;
        var states = new List<object>();
        foreach (var st in fsm.States) {
            var trans = st.Transitions.Select(tr => $"{tr.EventName} -> {tr.ToState}").ToArray();
            // accessing .Actions on an uninitialized FSM logs "Fsm not initialized" + a broken LoadActions (NullRef).
            var actions = st.ActionsLoaded
                ? st.Actions.Select(a => a.GetType().Name).ToArray()
                : ["<not loaded>"];
            states.Add(new { name = st.Name, active = st.Name == fsm.ActiveStateName, trans, actions });
        }

        var go = target.gameObject;
        return new {
            fsmName = target.FsmName,
            gameObject = go.name,
            go.activeInHierarchy,
            target.enabled,
            activeState = fsm.ActiveStateName,
            globalTransitions = fsm.GlobalTransitions.Select(tr => $"{tr.EventName} -> {tr.ToState}").ToArray(),
            states
        };
    }

    // Activate the instantiated GameCameras rig: register GameCameras._instance, unparent from the inactive holder,
    // DontDestroyOnLoad, SetActive(true). FSMs/CameraController will run (and likely complain about GameManager) —
    // first visible-fruit step. Reports the cameras (name/depth/enabled) so we can do the handover next.
    // Enumerate every loaded assembly that defines PlayMaker actions (FsmStateAction subclasses) + which PlayMaker the
    // base resolves to. Assemblies whose base is the ORIGINAL "PlayMaker" (not Silksong.PlayMaker) are what Hornet's
    // isolated FSMs can't use -> the set we'd need to prefix to cover all of Hornet's actions.
    // Why is HeroController.sprintFSM null on the spawned Hornet? It's a SERIALIZED field (prefab-wired, not located at
    // runtime). Report the serialized FSM fields' null-ness + enumerate the actual PlayMakerFSMs in Hornet's hierarchy
    // (by Fsm.Name) so we can tell: FSM present-but-unwired, FSM missing, or FSM component didn't bind.
    internal static object ProbeHeroFsms() {
        var hc = RealHero;
        if (hc == null) return new { error = "not spawned" };

        string? FsmName(SilksongPM::PlayMakerFSM? f) {
            return f == null ? null : f.FsmName;
        }

        var fsms = HornetRoot!.GetComponentsInChildren<SilksongPM::PlayMakerFSM>(true)
            .Select(f => new { go = f.gameObject.name, fsm = f.FsmName })
            .ToArray();
        return new {
            serializedFields = new {
                sprintFSM = FsmName(hc.sprintFSM),
                toolsFSM = FsmName(hc.toolsFSM),
                dashBurst = FsmName(hc.dashBurst),
                spellControl = FsmName(hc.spellControl)
            },
            fsmCount = fsms.Length,
            sprintLike = fsms.Where(f =>
                f.fsm != null && (f.fsm.ToLowerInvariant().Contains("sprint") ||
                                  f.go.ToLowerInvariant().Contains("sprint"))).ToArray(),
            allFsms = fsms.Select(f => $"{f.go}::{f.fsm}").OrderBy(s => s).ToArray()
        };
    }

    internal static object ProbeActions() {
        var byAsm = new Dictionary<string, List<string>>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
            Type[] types;
            try {
                types = asm.GetTypes();
            } catch {
                continue;
            }

            foreach (var t in types) {
                var b = t.BaseType;
                while (b != null && b.Name != "FsmStateAction") b = b.BaseType;
                if (b == null) continue;
                var key = $"{asm.GetName().Name}  (base@{b.Assembly.GetName().Name})";
                if (!byAsm.TryGetValue(key, out var l)) byAsm[key] = l = new List<string>();
                l.Add(t.Name);
            }
        }

        return byAsm.Select(kv => new { asm = kv.Key, count = kv.Value.Count, sample = kv.Value.Take(6).ToArray() })
            .OrderByDescending(x => x.count).ToArray();
    }
}
