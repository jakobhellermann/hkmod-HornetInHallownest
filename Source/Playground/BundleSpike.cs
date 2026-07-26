extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;
using UnityEngine;

namespace HornetInHallownest.Playground;

// Hero state, clip and FSM inspection for the debug routes.
internal static class BundleSpike {
    internal static Silksong::HeroController? Hornet => HornetSpawner.Hornet;
    internal static GameObject? HornetRoot => HornetSpawner.HornetRoot;

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

        var pd = Silksong::PlayerData.instance;
        return new {
            scene = GameManager.instance.sceneName, activeHero = HeroSwitch.Active.ToString(),
            health = pd.health, maxHealth = pd.maxHealth, blueHealth = pd.healthBlue, dead = hc.cState.dead,
            silk = pd.silk, silkMax = pd.CurrentSilkMax, crest = pd.CurrentCrestID, reaperMode = hc.ReaperState.IsInReaperMode,
            inWalkZone = hc.cState.inWalkZone, corpseScene = pd.HeroCorpseScene, corpsePos = pd.HeroDeathScenePos.ToString(),
            hkHealth = PlayerData.instance.health, hkMaxHealth = PlayerData.instance.maxHealth,
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
            pos = new { pos.x, pos.y }, vel = rb != null ? new { rb.GetVelocity().x, rb.GetVelocity().y } : null,
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

    // List Hornet's tk2d animation clips — to pick a Hornet clip that maps well onto a missing HK clip (e.g. the
    // "Collect SD 1 Back" item-collect pose). `filter` (optional substring, case-insensitive) narrows the list.
    internal static object ListHeroClips(string? filter = null) {
        var hero = Hornet;
        var anim = hero != null ? hero.GetComponent<tk2dSpriteAnimator>() : null;
        if (anim == null || anim.Library == null) return new { error = "no animator/library" };
        var names = new List<string>();
        foreach (var c in anim.Library.clips)
            if (c != null && (string.IsNullOrEmpty(filter) ||
                              c.name.ToLowerInvariant().Contains(filter.ToLowerInvariant())))
                names.Add($"{c.name} [{c.wrapMode}, {(c.frames != null ? c.frames.Length : 0)}f]");
        names.Sort();
        return new { count = names.Count, clips = names };
    }

    // Play a Hornet clip by name (StopAnimationControl first so HeroController's animator doesn't override it next
    // frame) — to eyeball a candidate mapping live. /hero-anim-resume restores normal animation control.
    internal static object PlayHeroClip(string? name) {
        var hero = Hornet;
        if (hero == null) return new { error = "no hero" };
        if (string.IsNullOrEmpty(name)) return new { error = "name required" };
        var anim = hero.GetComponent<tk2dSpriteAnimator>();
        if (anim == null) return new { error = "no animator" };
        if (anim.GetClipByName(name) == null) return new { error = $"clip '{name}' not found on Hornet" };
        hero.StopAnimationControl();
        anim.Play(name);
        return new { played = name };
    }

    internal static object ResumeHeroAnim() {
        var hero = Hornet;
        if (hero == null) return new { error = "no hero" };
        hero.StartAnimationControlToIdle();
        return new { resumed = true };
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

    // HK-side counterpart to FsmVars: dump an HK (global PlayMaker) FSM's variables by name, with an optional GO filter
    // (many scene objects share an FSM name like "Fade", so pass go=... to disambiguate — e.g. go=RestBench Spider).
    // FsmVars only scans the isolated Silksong PlayMakerFSM type, so HK scene-object FSMs (benches, traps, levers) are
    // invisible to it; this scans HK's PlayMakerFSM type instead.
    internal static object FsmVarsHk(string fsmName, string? goFilter = null) {
        foreach (var f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>()) {
            if (f == null || !string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!f.gameObject.activeInHierarchy) continue;
            if (!string.IsNullOrEmpty(goFilter) &&
                f.gameObject.name.IndexOf(goFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var vars = f.FsmVariables;
            var d = new Dictionary<string, string>();
            foreach (var b in vars.BoolVariables) d[b.Name] = "bool:" + b.Value;
            foreach (var n in vars.IntVariables) d[n.Name] = "int:" + n.Value;
            foreach (var ff in vars.FloatVariables) d[ff.Name] = "float:" + ff.Value;
            foreach (var v3 in vars.Vector3Variables) d[v3.Name] = "v3:" + v3.Value;
            foreach (var g in vars.GameObjectVariables) d[g.Name] = "go:" + (g.Value != null ? g.Value.name : "<null>");
            return new { fsm = f.FsmName, go = f.gameObject.name, activeState = f.Fsm.ActiveStateName, vars = d };
        }

        return new { error = "hk fsm not found", hint = "pass go=<substring> if the name is shared" };
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

}
