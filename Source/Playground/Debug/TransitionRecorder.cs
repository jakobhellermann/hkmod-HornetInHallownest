extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using GlobalEnums;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetInHallownest.Playground;

// Always-on flight recorder for scene transitions: a ring of per-frame Knight/Hornet/GameManager state plus hooked
// transition events, dumped to a TSV when transitions ping-pong (3 within 6s), on F10, or via POST /transition-dump.
// Every hook logs inside try/catch and always calls orig.
internal static class TransitionRecorder {
    private const int FrameCapacity = 1800;
    private const int EventCapacity = 600;
    private const int PingPongCount = 3;
    private const float PingPongWindow = 6f;
    private const float DumpDelay = 3f;
    private const float StallThreshold = 0.1f;

    private static readonly List<Hook> hooks = new();
    private static readonly Frame[] frames = new Frame[FrameCapacity];
    private static readonly string[] events = new string[EventCapacity];
    private static readonly Queue<float> recentTransitions = new();
    private static int frameCount, eventCount;
    private static float lastAutoDump = float.NegativeInfinity;
    private static bool dumpScheduled;
    private static bool insideTryDoTransition;
    private static bool transitionBegan;
    private static readonly Dictionary<int, string> lastGateLog = new();
    private static GameObject? go;

    private struct Frame {
        public float T, Dt, TimeScale;
        public int FrameNo;
        public string Scene;
        public GameState GmState;
        public string KnightTrans;
        public bool KnightInPos, KnightSim, KnightCol;
        public int KnightLayer;
        public float KnightScale;
        public Vector2 KnightPos;
        public string HornetTrans, HornetState;
        public bool HornetGround, HornetCReq, HornetAccept, HornetSim, HornetCol;
        public int HornetLayer;
        public float HornetScale;
        public Vector2 HornetPos, HornetVel;
    }

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetInHallownest.TransitionRecorder");
        go.AddComponent<TransitionRecorderBehaviour>();
        Object.DontDestroyOnLoad(go);

        USceneManager.activeSceneChanged += OnActiveSceneChanged;
        USceneManager.sceneLoaded += OnSceneLoaded;
        USceneManager.sceneUnloaded += OnSceneUnloaded;

        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Add(typeof(TransitionPoint).GetMethod("TryDoTransition", all),
            (Action<Action<TransitionPoint, Collider2D>, TransitionPoint, Collider2D>)OnGateTrigger);
        Add(typeof(GameManager).GetMethod("BeginSceneTransition", all),
            (Action<Action<GameManager, GameManager.SceneLoadInfo>, GameManager, GameManager.SceneLoadInfo>)
            OnBeginSceneTransition);
        Add(typeof(GameManager).GetMethod("SetState", all),
            (Action<Action<GameManager, GameState>, GameManager, GameState>)OnSetState);
        Add(typeof(GameManager).GetMethod("FinishedEnteringScene", all),
            (Action<Action<GameManager>, GameManager>)((orig, self) => {
                Event($"HK gm.FinishedEnteringScene (gameState={self.gameState}) caller: {Callers(1)}");
                orig(self);
            }));
        Add(typeof(GameManager).GetMethod("LevelActivated", all),
            (Action<Action<GameManager, Scene, Scene>, GameManager, Scene, Scene>)((orig, self, from, to) => {
                Event($"HK gm.LevelActivated {from.name} -> {to.name} (knight before={KnightPos()})");
                orig(self, from, to);
                Event($"HK gm.LevelActivated done (knight after={KnightPos()})");
            }));
        Add(typeof(HeroController).GetMethod("EnterScene", all),
            (Func<Func<HeroController, TransitionPoint, float, IEnumerator>, HeroController, TransitionPoint, float,
                IEnumerator>)((orig, self, gate, delay) => {
                Event($"Knight EnterScene gate={GateDesc(gate)} delay={delay:F2}");
                return orig(self, gate, delay);
            }));
        Add(typeof(HeroController).GetMethod("FinishedEnteringScene", all),
            (Action<Action<HeroController, bool, bool>, HeroController, bool, bool>)((orig, self, hazard, runBob) => {
                Event($"Knight FinishedEnteringScene (gameState={GmState()}) caller: {Callers(1)}");
                orig(self, hazard, runBob);
            }));
        Add(typeof(HeroController).GetMethod("LeaveScene", all),
            (Action<Action<HeroController, GatePosition?>, HeroController, GatePosition?>)((orig, self, gate) => {
                Event($"Knight LeaveScene gate={gate?.ToString() ?? "null"}");
                orig(self, gate);
            }));
        Add(typeof(HeroController).GetMethod("SendHeroInPosition", all),
            (Action<Action<HeroController, bool>, HeroController, bool>)((orig, self, forceDirect) => {
                Event($"Knight SendHeroInPosition forceDirect={forceDirect} pos={KnightPos()}");
                orig(self, forceDirect);
            }));
        Add(typeof(Silksong::HeroController).GetMethod("EnterScene", all),
            (Func<Func<Silksong::HeroController, Silksong::TransitionPoint, float, bool, Action, bool, IEnumerator>,
                Silksong::HeroController, Silksong::TransitionPoint, float, bool, Action, bool, IEnumerator>)(
                (orig, self, gate, delay, customFade, onEnd, skip) => {
                    Event($"Hornet EnterScene gate={(gate ? gate.name : "null")} delay={delay:F2} " +
                          $"scaleX={self.transform.localScale.x} pos={(Vector2)self.transform.position}");
                    return orig(self, gate!, delay, customFade, onEnd!, skip);
                }));
        Add(typeof(Silksong::HeroController).GetMethod("FinishedEnteringScene", all),
            (Action<Action<Silksong::HeroController, bool, bool>, Silksong::HeroController, bool, bool>)(
                (orig, self, hazard, runBob) => {
                    Event($"Hornet FinishedEnteringScene (gameState={GmState()}) caller: {Callers(1)}");
                    orig(self, hazard, runBob);
                }));
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        USceneManager.activeSceneChanged -= OnActiveSceneChanged;
        USceneManager.sceneLoaded -= OnSceneLoaded;
        USceneManager.sceneUnloaded -= OnSceneUnloaded;
        if (go != null) Object.Destroy(go);
        go = null;
        frameCount = eventCount = 0;
        recentTransitions.Clear();
        lastGateLog.Clear();
        dumpScheduled = false;
    }

    private static void Add(MethodInfo? mi, Delegate detour) {
        if (mi == null) {
            Log.Error($"[TransitionRec] method not found for {detour.GetType()}");
            return;
        }

        try {
            hooks.Add(new Hook(mi, detour));
        } catch (Exception e) {
            Log.Error($"[TransitionRec] hook {mi.DeclaringType?.Assembly.GetName().Name}:{mi.DeclaringType?.Name}.{mi} " +
                      $"with {detour.GetType()} failed: {e.Message}");
        }
    }

    // OnTriggerStay2D re-runs this every physics step while overlapping, so only log changes per gate.
    private static void OnGateTrigger(Action<TransitionPoint, Collider2D> orig, TransitionPoint self,
        Collider2D col) {
        string? desc = null;
        try {
            if (col && col.gameObject.layer == 9)
                desc = $"gate={GateDesc(self)} collider={col.transform.root.name}/{col.name} " +
                       $"scaleX={col.transform.localScale.x} gameState={GmState()} " +
                       $"knightRecoil={HeroController.UnsafeInstance?.cState.recoiling}";
        } catch (Exception e) {
            Log.Error($"[TransitionRec] TryDoTransition log: {e.Message}");
        }

        insideTryDoTransition = true;
        transitionBegan = false;
        try {
            orig(self, col!);
        } finally {
            insideTryDoTransition = false;
        }

        if (desc == null) return;
        var line = $"{desc} -> {(transitionBegan ? "TRANSITION" : "no transition")}";
        var id = self.GetInstanceID();
        if (transitionBegan || !lastGateLog.TryGetValue(id, out var prev) || prev != line) {
            lastGateLog[id] = line;
            Event($"HK TryDoTransition {line}");
        }
    }

    private static void OnBeginSceneTransition(Action<GameManager, GameManager.SceneLoadInfo> orig, GameManager self,
        GameManager.SceneLoadInfo info) {
        try {
            if (insideTryDoTransition) transitionBegan = true;
            Event($"HK BeginSceneTransition -> {info.SceneName} @ {info.EntryGateName} " +
                  $"(gameState={self.gameState}, fromGate={insideTryDoTransition}) caller: {Callers(2)}");
            NoteTransition();
        } catch (Exception e) {
            Log.Error($"[TransitionRec] BeginSceneTransition log: {e.Message}");
        }

        orig(self, info);
    }

    private static void OnSetState(Action<GameManager, GameState> orig, GameManager self, GameState state) {
        var before = self.gameState;
        orig(self, state);
        try {
            if (before != state) Event($"HK gameState {before} -> {state} caller: {Callers(2)}");
        } catch (Exception e) {
            Log.Error($"[TransitionRec] SetState log: {e.Message}");
        }
    }

    private static void OnActiveSceneChanged(Scene from, Scene to) =>
        Event($"activeSceneChanged {from.name} -> {to.name}");

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Event($"sceneLoaded {scene.name} ({mode})");

    private static void OnSceneUnloaded(Scene scene) => Event($"sceneUnloaded {scene.name}");

    private static void NoteTransition() {
        var now = Time.realtimeSinceStartup;
        recentTransitions.Enqueue(now);
        while (recentTransitions.Count > 0 && now - recentTransitions.Peek() > PingPongWindow)
            recentTransitions.Dequeue();
        if (recentTransitions.Count < PingPongCount || dumpScheduled || now - lastAutoDump < 30f) return;
        dumpScheduled = true;
        lastAutoDump = now;
        Event($"PING-PONG detected ({recentTransitions.Count} transitions in {PingPongWindow}s), dumping in {DumpDelay}s");
        go!.GetComponent<TransitionRecorderBehaviour>().StartCoroutine(DelayedDump());
    }

    private static IEnumerator DelayedDump() {
        yield return new WaitForSecondsRealtime(DumpDelay);
        dumpScheduled = false;
        var path = Dump("pingpong");
        Log.Info($"[TransitionRec] transition ping-pong recorded -> {path}");
    }

    internal static object DumpRoute() => new { path = Dump("manual") };

    internal static string Dump(string reason) {
        var path = Path.Combine(Path.GetTempPath(),
            $"hornet_transition_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.tsv");
        var sb = new StringBuilder();
        sb.Append("# events\n");
        var n = Math.Min(eventCount, EventCapacity);
        for (var i = eventCount - n; i < eventCount; i++) sb.Append(events[i % EventCapacity]).Append('\n');
        sb.Append("\n# frames\n");
        sb.Append("t\tframe\tdt\ttimeScale\tscene\tgameState\t" +
                  "kTrans\tkInPos\tkLayer\tkScaleX\tkSim\tkCol\tkX\tkY\t" +
                  "hTrans\thState\thGround\thCReq\thAccept\thLayer\thScaleX\thSim\thCol\thVx\thVy\thX\thY\n");
        var m = Math.Min(frameCount, FrameCapacity);
        for (var i = frameCount - m; i < frameCount; i++) {
            var f = frames[i % FrameCapacity];
            sb.Append(string.Format(CultureInfo.InvariantCulture,
                "{0:F3}\t{1}\t{2:F3}\t{3:F2}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}\t{12:F2}\t{13:F2}\t" +
                "{14}\t{15}\t{16}\t{17}\t{18}\t{19}\t{20}\t{21}\t{22}\t{23:F2}\t{24:F2}\t{25:F2}\t{26:F2}\n",
                f.T, f.FrameNo, f.Dt, f.TimeScale, f.Scene, f.GmState,
                f.KnightTrans, f.KnightInPos, f.KnightLayer, f.KnightScale, f.KnightSim, f.KnightCol,
                f.KnightPos.x, f.KnightPos.y,
                f.HornetTrans, f.HornetState, f.HornetGround, f.HornetCReq, f.HornetAccept, f.HornetLayer,
                f.HornetScale, f.HornetSim, f.HornetCol, f.HornetVel.x, f.HornetVel.y, f.HornetPos.x, f.HornetPos.y));
        }

        File.WriteAllText(path, sb.ToString());
        Log.Debug($"[TransitionRec] dumped {n} events + {m} frames -> {path}");
        return path;
    }

    private static void Event(string msg) {
        var line = string.Format(CultureInfo.InvariantCulture, "{0:F3}\t{1}\t{2}", Time.realtimeSinceStartup,
            Time.frameCount, msg);
        events[eventCount++ % EventCapacity] = line;
        Log.Debug($"[TransitionRec] {msg}");
    }

    // MonoMod detour/trampoline frames hide the real caller, so skip them.
    private static string Callers(int skip) {
        var st = new System.Diagnostics.StackTrace(skip, false);
        var sb = new StringBuilder();
        var shown = 0;
        for (var i = 0; i < st.FrameCount && shown < 8; i++) {
            var m = st.GetFrame(i)?.GetMethod();
            if (m == null) continue;
            var type = m.DeclaringType?.Name ?? "";
            if (type.StartsWith("DynamicMethodDefinition", StringComparison.Ordinal) ||
                m.Name.StartsWith("DMD<", StringComparison.Ordinal) ||
                m.Name.StartsWith("Trampoline<", StringComparison.Ordinal) ||
                type == nameof(TransitionRecorder) || type.StartsWith("<>c", StringComparison.Ordinal)) continue;
            if (sb.Length > 0) sb.Append(" < ");
            sb.Append(type).Append('.').Append(m.Name);
            shown++;
        }

        return sb.ToString();
    }

    private static string GateDesc(TransitionPoint? tp) =>
        tp ? $"{tp!.gameObject.scene.name}/{tp.name}->{tp.targetScene}@{tp.entryPoint}" : "null";

    private static string GmState() {
        var gm = GameManager.UnsafeInstance;
        return gm ? gm.gameState.ToString() : "no gm";
    }

    private static string KnightPos() {
        var k = HeroController.UnsafeInstance;
        return k ? ((Vector2)k.transform.position).ToString() : "no knight";
    }

    internal sealed class TransitionRecorderBehaviour : MonoBehaviour {
        private HeroController? knight;
        private Rigidbody2D? knightRb;
        private Collider2D? knightCol;
        private Silksong::HeroController? hornet;
        private Rigidbody2D? hornetRb;
        private Collider2D? hornetCol;

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F10)) {
                var path = Dump("f10");
                Log.Info($"[TransitionRec] dumped -> {path}");
            }
        }

        private void LateUpdate() {
            try {
                Sample();
            } catch (Exception e) {
                Log.ErrorOnce("transition-recorder-sample", $"[TransitionRec] sample failed: {e}");
            }
        }

        private void Sample() {
            var dt = Time.unscaledDeltaTime;
            if (dt > StallThreshold && Application.isFocused) Event($"frame stall {dt * 1000f:F0} ms");

            var k = HeroController.UnsafeInstance;
            if (!ReferenceEquals(k, knight)) {
                knight = k;
                knightRb = k ? k!.GetComponent<Rigidbody2D>() : null;
                knightCol = k ? k!.GetComponent<Collider2D>() : null;
            }

            var h = HornetSpawner.Hornet;
            if (!ReferenceEquals(h, hornet)) {
                hornet = h;
                hornetRb = h ? h!.GetComponent<Rigidbody2D>() : null;
                hornetCol = h ? h!.GetComponent<Collider2D>() : null;
            }

            var gm = GameManager.UnsafeInstance;
            var f = new Frame {
                T = Time.realtimeSinceStartup,
                FrameNo = Time.frameCount,
                Dt = dt,
                TimeScale = Time.timeScale,
                Scene = USceneManager.GetActiveScene().name,
                GmState = gm ? gm!.gameState : default,
                KnightTrans = "-",
                HornetTrans = "-",
                HornetState = "-",
            };
            if (knight) {
                var kt = knight!.transform;
                f.KnightTrans = knight.transitionState.ToString();
                f.KnightInPos = knight.isHeroInPosition;
                f.KnightLayer = knight.gameObject.layer;
                f.KnightScale = kt.localScale.x;
                f.KnightSim = knightRb && knightRb!.simulated;
                f.KnightCol = knightCol && knightCol!.enabled;
                f.KnightPos = kt.position;
            }

            if (hornet) {
                var ht = hornet!.transform;
                f.HornetTrans = hornet.transitionState.ToString();
                f.HornetState = hornet.hero_state.ToString();
                f.HornetGround = hornet.cState.onGround;
                f.HornetCReq = hornet.controlReqlinquished;
                f.HornetAccept = hornet.acceptingInput;
                f.HornetLayer = hornet.gameObject.layer;
                f.HornetScale = ht.localScale.x;
                f.HornetSim = hornetRb && hornetRb!.simulated;
                f.HornetCol = hornetCol && hornetCol!.enabled;
                f.HornetPos = ht.position;
                f.HornetVel = hornetRb ? hornetRb!.linearVelocity : Vector2.zero;
            }

            frames[frameCount++ % FrameCapacity] = f;
        }
    }
}
