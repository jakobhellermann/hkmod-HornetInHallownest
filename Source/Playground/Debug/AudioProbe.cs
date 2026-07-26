extern alias Silksong;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HornetInHallownest.Modules;
using HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetInHallownest.Playground;

// DIAGNOSTIC (log-only, armed via POST /audio-probe?on=true): which silent gate eats Hornet's one-shot SFX?
//
// RandomAudioClipTableExtensions.SpawnAndPlayOneShot (every overload funnels into the 6-arg) returns null with zero
// logs when any gate rejects: table null / prefab null / SelectClip null (cooldown, player-voice setting, probability)
// or AudioEventManager.TryPlayAudioClip false (frequency limit, missing GameCameras, or the distance cull: blend >
// 0.95 and farther from GameCameras.mainCamera than prefab.maxDistance). In our rig that camera belongs to the parked
// Silksong rig, and the spawned source additionally carries LowPassDistance (muffling by the same camera distance),
// so both the cull and the muffling measure against wherever that rig camera sits, not against HK's listener. This
// probe logs each one-shot call + outcome while armed; GET /audio-test runs the full chain live at the hero, including
// an audible forced spawn and a dump of the spawned source's filter state.
internal static class AudioProbe {
    private delegate AudioSource? SpawnDel(
        Func<Silksong::RandomAudioClipTable, AudioSource, Vector3, bool, float, Action, AudioSource> orig,
        Silksong::RandomAudioClipTable table, AudioSource prefab, Vector3 position, bool forcePlay, float volume,
        Action onRecycled);

    // tk2d attack clips deliver SFX as int-indexed anim events -> AudioEventAnimationEvents.PlayAudioEvent (swoosh etc.)
    private delegate void PlayAudioEventDel(Action<Silksong::AudioEventAnimationEvents, int> orig,
        Silksong::AudioEventAnimationEvents self, int index);

    // shared gate of BOTH one-shot paths (table extension + AudioEventRandom); returns false = silently dropped
    private delegate bool TryPlayDel(Func<AudioClip, AudioSource, Vector3, bool> orig, AudioClip clip, AudioSource prefab,
        Vector3 position);

    // the needle swoosh is NOT a one-shot: NailSlash.StartSlash plays the AudioSource sitting on the slash GO directly
    private delegate void StartSlashDel(Action<Silksong::NailSlash> orig, Silksong::NailSlash self);

    // EVERY snapshot transition funnels through UnityEngine.Audio.AudioMixerSnapshot.TransitionTo; catches the muting
    // call regardless of caller (SS GameManager, CustomSceneManager, SetSceneAudio, PlayMaker actions)
    private delegate void TransitionToDel(Action<UnityEngine.Audio.AudioMixerSnapshot, float> orig,
        UnityEngine.Audio.AudioMixerSnapshot snapshot, float timeToReach);

    private delegate void BeginTransitionDel(Action<Silksong::GameManager, Silksong::GameManager.SceneLoadInfo> orig,
        Silksong::GameManager self, Silksong::GameManager.SceneLoadInfo info);

    private delegate void GmVoidDel(Action<Silksong::GameManager> orig, Silksong::GameManager self);
    private delegate void StaticVoidDel(Action orig);

    private delegate AssetBundle? LoadFileDel(Func<string, AssetBundle?> orig, string path);
    private delegate AssetBundle? LoadFileDel3(Func<string, uint, ulong, AssetBundle?> orig, string path, uint crc,
        ulong offset);
    private delegate AssetBundle? LoadStreamDel(Func<System.IO.Stream, AssetBundle?> orig, System.IO.Stream stream);
    private delegate UnityEngine.AssetBundleCreateRequest? LoadFileAsyncDel(Func<string, UnityEngine.AssetBundleCreateRequest?> orig,
        string path);
    private delegate UnityEngine.AssetBundleCreateRequest? LoadFileAsyncDel3(
        Func<string, uint, ulong, UnityEngine.AssetBundleCreateRequest?> orig, string path, uint crc, ulong offset);
    private delegate void UnloadDel(Action<AssetBundle, bool> orig, AssetBundle self, bool unloadAllObjects);

    private static Hook? hook;
    private static Hook? animEventHook;
    private static Hook? tryPlayHook;
    private static Hook? startSlashHook;
    private static Hook? transitionHook;
    private static Hook? safeTransitionHook;
    private static Hook? beginTransitionHook;
    private static Hook? levelReadyHook;
    private static Hook? pauseActorHook;
    private static Hook? snapshotReadyHook;
    private static Hook? runCallbackHook;
    private static Hook? loadFileHook1;
    private static Hook? loadFileHook3;
    private static Hook? loadStreamHook;
    private static Hook? loadFileAsyncHook1;
    private static Hook? loadFileAsyncHook3;
    private static Hook? unloadHook;
    internal static bool Enabled = false;

    private static void HookStatic(Type type, string method, ref Hook? slot, BindingFlags? extra = null) {
        var flags = BindingFlags.Public | BindingFlags.Static;
        if (extra != null) flags |= extra.Value;
        var mi = type.GetMethod(method, flags);
        if (mi == null) {
            Log.Error($"[AudioProbe] {type.Name}.{method} not found");
            return;
        }
        slot = new Hook(mi, (StaticVoidDel)Handler);

        return;

        void Handler(Action orig) {
            orig();
            if (Enabled) Log.Debug($"[AudioProbe] {type.Name}.{method}() <- {ShortStack()}");
        }
    }

    private static string ShortStack() {
        var st = new System.Diagnostics.StackTrace(2, false);
        var frames = st.GetFrames();
        if (frames == null) return "?";
        var sb = new System.Text.StringBuilder();
        var n = 0;
        foreach (var f in frames) {
            var m = f.GetMethod();
            if (m == null) continue;
            sb.Append(m.DeclaringType?.Name).Append('.').Append(m.Name).Append(" <- ");
            if (++n >= 6) break;
        }
        return sb.ToString(0, System.Math.Max(0, sb.Length - 4));
    }

    internal static void Install() {
        if (hook != null) return;
        var mi = typeof(Silksong::RandomAudioClipTableExtensions).GetMethod("SpawnAndPlayOneShot",
            BindingFlags.Public | BindingFlags.Static, null,
            new[] {
                typeof(Silksong::RandomAudioClipTable), typeof(AudioSource), typeof(Vector3),
                typeof(bool), typeof(float), typeof(Action)
            }, null);
        if (mi == null) {
            Log.Error("[AudioProbe] SpawnAndPlayOneShot(6-arg) not found");
            return;
        }
        hook = new Hook(mi, (SpawnDel)OnSpawn);

        var playEv = typeof(Silksong::AudioEventAnimationEvents).GetMethod("PlayAudioEvent",
            BindingFlags.Public | BindingFlags.Instance);
        if (playEv != null) animEventHook = new Hook(playEv, (PlayAudioEventDel)OnPlayAudioEvent);

        var tryPlay = typeof(Silksong::AudioEventManager).GetMethod("TryPlayAudioClip",
            BindingFlags.Public | BindingFlags.Static);
        if (tryPlay != null) tryPlayHook = new Hook(tryPlay, (TryPlayDel)OnTryPlay);

        var startSlash = typeof(Silksong::NailSlash).GetMethod("StartSlash",
            BindingFlags.Public | BindingFlags.Instance);
        if (startSlash != null) startSlashHook = new Hook(startSlash, (StartSlashDel)OnStartSlash);

        // snapshot-transition chokepoints (the suspected mute of the hero-side Actors mixer instance)
        var transTo = typeof(UnityEngine.Audio.AudioMixerSnapshot).GetMethod("TransitionTo",
            BindingFlags.Public | BindingFlags.Instance);
        if (transTo != null) transitionHook = new Hook(transTo, (TransitionToDel)OnTransitionTo);

        var safeT = typeof(Silksong::AudioMixerExtensions).GetMethod("TransitionToSafe",
            BindingFlags.Public | BindingFlags.Static);
        if (safeT != null) safeTransitionHook = new Hook(safeT, (TransitionToDel)OnTransitionToSafe);

        var beginT = typeof(Silksong::GameManager).GetMethod("BeginSceneTransition",
            BindingFlags.Public | BindingFlags.Instance);
        if (beginT != null) beginTransitionHook = new Hook(beginT, (BeginTransitionDel)OnBeginSceneTransition);

        var readyT = typeof(Silksong::GameManager).GetMethod("OnNextLevelReady",
            BindingFlags.Public | BindingFlags.Instance);
        if (readyT != null) levelReadyHook = new Hook(readyT, (GmVoidDel)OnNextLevelReady);

        HookStatic(typeof(Silksong::AudioManager), "PauseActorSnapshot", ref pauseActorHook);
        HookStatic(typeof(Silksong::AudioManager), "CustomSceneManagerSnapshotReady", ref snapshotReadyHook);
        HookStatic(typeof(Silksong::AudioManager), "RunActorSnapshotCallback", ref runCallbackHook,
            BindingFlags.NonPublic | BindingFlags.Static);

        // who mounts what: log every AssetBundle load/unload while armed. Two mixer generations require the same
        // file mounted twice (objects leak alive across Unload), so load/unload timing is the formation mechanism.
        var lf1 = typeof(AssetBundle).GetMethod("LoadFromFile", new[] { typeof(string) });
        if (lf1 != null) loadFileHook1 = new Hook(lf1, (LoadFileDel)OnLoadFromFile);
        var lf3 = typeof(AssetBundle).GetMethod("LoadFromFile", new[] { typeof(string), typeof(uint), typeof(ulong) });
        if (lf3 != null) loadFileHook3 = new Hook(lf3, (LoadFileDel3)OnLoadFromFile3);
        var ls1 = typeof(AssetBundle).GetMethod("LoadFromStream", new[] { typeof(System.IO.Stream) });
        if (ls1 != null) loadStreamHook = new Hook(ls1, (LoadStreamDel)OnLoadFromStream);
        // the addressables runtime mounts via the ASYNC variants; the sync hooks above see nothing from it
        var lfa1 = typeof(AssetBundle).GetMethod("LoadFromFileAsync", new[] { typeof(string) });
        if (lfa1 != null) loadFileAsyncHook1 = new Hook(lfa1, (LoadFileAsyncDel)OnLoadFromFileAsync);
        var lfa3 = typeof(AssetBundle).GetMethod("LoadFromFileAsync",
            new[] { typeof(string), typeof(uint), typeof(ulong) });
        if (lfa3 != null) loadFileAsyncHook3 = new Hook(lfa3, (LoadFileAsyncDel3)OnLoadFromFileAsync3);
        var un1 = typeof(AssetBundle).GetMethod("Unload", new[] { typeof(bool) });
        if (un1 != null) unloadHook = new Hook(un1, (UnloadDel)OnUnload);

        Log.Debug("[AudioProbe] installed (arm via POST /audio-probe?on=true)");
    }

    internal static void Cleanup() {
        hook?.Dispose();
        animEventHook?.Dispose();
        tryPlayHook?.Dispose();
        startSlashHook?.Dispose();
        transitionHook?.Dispose();
        safeTransitionHook?.Dispose();
        beginTransitionHook?.Dispose();
        levelReadyHook?.Dispose();
        pauseActorHook?.Dispose();
        snapshotReadyHook?.Dispose();
        runCallbackHook?.Dispose();
        hook = null;
        animEventHook = null;
        tryPlayHook = null;
        startSlashHook = null;
        transitionHook = null;
        safeTransitionHook = null;
        beginTransitionHook = null;
        levelReadyHook = null;
        pauseActorHook = null;
        snapshotReadyHook = null;
        runCallbackHook = null;
        Enabled = false;
    }

    internal static object SetArmed(string? on) {
        Enabled = on == null || on.ToLowerInvariant() != "false";
        return new { armed = Enabled, hint = "attack/land/move: each one-shot logs to ModLog; then GET /audio-test" };
    }

    private static AudioSource? OnSpawn(
        Func<Silksong::RandomAudioClipTable, AudioSource, Vector3, bool, float, Action, AudioSource> orig,
        Silksong::RandomAudioClipTable table, AudioSource prefab, Vector3 position, bool forcePlay, float volume,
        Action onRecycled) {
        var result = orig(table, prefab, position, forcePlay, volume, onRecycled);
        if (!Enabled) return result;

        if (result != null) {
            Log.Debug($"[AudioProbe] {table?.name} pos=({position.x:F1},{position.y:F1}) -> spawned {result.name}");
        } else {
            // No side-effect-free way to name the exact gate from outside; report the cull inputs so the log
            // distinguishes "distance cull" (cam distance > maxDistance) from the clip/table gates.
            var cam = CamPos();
            Log.Debug($"[AudioProbe] {table?.name} pos=({position.x:F1},{position.y:F1}) -> NULL"
                + $" (camDist={((cam - position).magnitude):F1} blend={prefab?.spatialBlend.ToString() ?? "?"}"
                + $" maxD={prefab?.maxDistance.ToString() ?? "?"} forcePlay={forcePlay})");
        }
        return result;
    }

    private static void OnTransitionTo(Action<UnityEngine.Audio.AudioMixerSnapshot, float> orig,
        UnityEngine.Audio.AudioMixerSnapshot snapshot, float timeToReach) {
        if (Enabled) LogSnapshot("TransitionTo", snapshot, timeToReach);
        orig(snapshot, timeToReach);
    }

    private static void OnTransitionToSafe(Action<UnityEngine.Audio.AudioMixerSnapshot, float> orig,
        UnityEngine.Audio.AudioMixerSnapshot snapshot, float timeToReach) {
        if (Enabled) LogSnapshot("TransitionToSafe", snapshot, timeToReach);
        orig(snapshot, timeToReach);
    }

    private static void LogSnapshot(string what, UnityEngine.Audio.AudioMixerSnapshot? snapshot, float t) {
        var mixer = snapshot != null ? snapshot.audioMixer : null;
        Log.Debug($"[AudioProbe] snapshot {what} '{snapshot?.name}' on mixer "
            + $"'{mixer?.name}#{(mixer != null ? mixer.GetInstanceID() : 0)}' t={t} <- {ShortStack()}");
    }

    private static void OnBeginSceneTransition(Action<Silksong::GameManager, Silksong::GameManager.SceneLoadInfo> orig,
        Silksong::GameManager self, Silksong::GameManager.SceneLoadInfo info) {
        if (Enabled) {
            Log.Debug($"[AudioProbe] GM.BeginSceneTransition '{info.SceneName}' on '{self.gameObject.name}'"
                + $" <- {ShortStack()}");
        }
        orig(self, info);
    }

    private static void OnNextLevelReady(Action<Silksong::GameManager> orig, Silksong::GameManager self) {
        if (Enabled) Log.Debug($"[AudioProbe] GM.OnNextLevelReady on '{self.gameObject.name}' <- {ShortStack()}");
        orig(self);
    }

    private static AssetBundle? OnLoadFromFile(Func<string, AssetBundle?> orig, string path) {
        var b = orig(path);
        if (Enabled) Log.Debug($"[AudioProbe] LoadFromFile('{System.IO.Path.GetFileName(path)}') -> {b?.name}");
        return b;
    }

    private static AssetBundle? OnLoadFromFile3(Func<string, uint, ulong, AssetBundle?> orig, string path, uint crc, ulong offset) {
        var b = orig(path, crc, offset);
        if (Enabled) Log.Debug($"[AudioProbe] LoadFromFile3('{System.IO.Path.GetFileName(path)}', crc, off={offset}) -> {b?.name}");
        return b;
    }

    private static AssetBundle? OnLoadFromStream(Func<System.IO.Stream, AssetBundle?> orig, System.IO.Stream stream) {
        var b = orig(stream);
        if (Enabled) Log.Debug($"[AudioProbe] LoadFromStream -> {b?.name}");
        return b;
    }

    private static UnityEngine.AssetBundleCreateRequest? OnLoadFromFileAsync(
        Func<string, UnityEngine.AssetBundleCreateRequest?> orig, string path) {
        var r = orig(path);
        if (Enabled) Log.Debug($"[AudioProbe] LoadFromFileAsync('{System.IO.Path.GetFileName(path)}')");
        return r;
    }

    private static UnityEngine.AssetBundleCreateRequest? OnLoadFromFileAsync3(
        Func<string, uint, ulong, UnityEngine.AssetBundleCreateRequest?> orig, string path, uint crc, ulong offset) {
        var r = orig(path, crc, offset);
        if (Enabled)
            Log.Debug($"[AudioProbe] LoadFromFileAsync('{System.IO.Path.GetFileName(path)}', crc, off={offset})");
        return r;
    }

    private static void OnUnload(Action<AssetBundle, bool> orig, AssetBundle self, bool unloadAllObjects) {
        if (Enabled) Log.Debug($"[AudioProbe] Unload('{self?.name}', all={unloadAllObjects})");
        orig(self!, unloadAllObjects);
    }

    private static void OnStartSlash(Action<Silksong::NailSlash> orig, Silksong::NailSlash self) {
        orig(self);
        if (!Enabled) return;
        var src = self.GetComponent<AudioSource>();
        var grp = src != null ? src.outputAudioMixerGroup : null;
        var mix = grp != null ? grp.audioMixer : null;
        Log.Debug($"[AudioProbe] StartSlash '{self.name}': src={self.GetFieldValue<AudioSource>("audio")}"
            + $" clip={src?.clip?.name} playing={src != null && src.isPlaying}"
            + $" goActive={(self.gameObject.activeInHierarchy)}"
            + $" group={grp?.name}#{grp?.GetInstanceID()} mixer={mix?.name}#{mix?.GetInstanceID()}");
    }

    private static void OnPlayAudioEvent(Action<Silksong::AudioEventAnimationEvents, int> orig,
        Silksong::AudioEventAnimationEvents self, int index) {
        if (Enabled) {
            var n = self.GetFieldValue<Silksong::AudioEventRandom[]>("audioEvents")?.Length ?? -1;
            Log.Debug($"[AudioProbe] anim-audio-event '{self.name}' idx={index} events={n}");
        }
        orig(self, index);
    }

    private static bool OnTryPlay(Func<AudioClip, AudioSource, Vector3, bool> orig, AudioClip clip, AudioSource prefab,
        Vector3 position) {
        var result = orig(clip, prefab, position);
        if (Enabled) {
            var camPos = CamPos();
            Log.Debug($"[AudioProbe] TryPlay '{clip?.name}' pos=({position.x:F1},{position.y:F1}) -> {result}"
                + $" (blend={prefab?.spatialBlend.ToString() ?? "?"} camDist={(camPos - position).magnitude:F1}"
                + $" maxD={prefab?.maxDistance.ToString() ?? "?"})");
        }
        return result;
    }

    private static Vector3 CamPos() {
        var gc = Silksong::GameCameras.SilentInstance;
        var cam = gc != null ? gc.mainCamera : null;
        return cam != null ? cam.transform.position : Vector3.zero;
    }

    private static Silksong::HeroController? LiveHero() {
        if (HornetSpawner.Hornet is { } h && h.gameObject.activeInHierarchy) return h;
        foreach (var c in UnityEngine.Object.FindObjectsByType<Silksong::HeroController>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None)) {
            if (c.gameObject.activeInHierarchy) return c;
        }
        return null;
    }

    // GET /mixer-census: every loaded AudioMixer object, grouped by name — the generation census. Two objects with
    // the same name = two generations (the split-brain), no hero or gameplay needed. Works at the menu.
    internal static object Census() {
        var mixers = Resources.FindObjectsOfTypeAll<UnityEngine.Audio.AudioMixer>()
            .Select(m => new { name = m.name, id = m.GetInstanceID() })
            .OrderBy(m => m.id).ToArray();
        var dups = mixers.GroupBy(m => m.name).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()} ({string.Join(",", g.Select(m => m.id))})").ToArray();
        var n = 0;
        var names = new List<string>();
        foreach (var b in AssetBundle.GetAllLoadedAssetBundles()) { n++; names.Add(b.name); }
        var dupB = string.Join(", ", names.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => $"{g.Key} x{g.Count()}"));
        return new {
            mixerGenerations = mixers,
            duplicatedMixers = dups,
            loadedBundles = n,
            duplicateBundles = string.IsNullOrEmpty(dupB) ? "none" : dupB,
        };
    }

    // Full chain analysis at the live hero. Side effects: fires one audible forced spawn at the hero position, and
    // with ?repoint=1 re-points the hero's Attack AudioSources at the pooled-actor mixer group (the audible path).
    internal static object Test(string? repoint = null, string? restore = null) {
        var hero = LiveHero();
        if (hero == null) return new { error = "no live Silksong hero" };

        var heroPos = hero.transform.position;
        var table = hero.attackAudioTable;
        if (table == null) return new { error = "attackAudioTable is null on the hero", heroPos };

        var clip = table.SelectClip(true); // forcePlay: skips cooldown/probability, NOT the voice gate
        var prefab = Silksong::GlobalSettings.Audio.DefaultAudioSourcePrefab;
        var camPos = CamPos();
        var hkCam = GameCameras.instance != null ? GameCameras.instance.mainCamera : null;
        var ssCam = Silksong::GameCameras.SilentInstance != null
            ? Silksong::GameCameras.SilentInstance.mainCamera
            : null;
        var listener = UnityEngine.Object.FindObjectsByType<AudioListener>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        string listenerInfo = "";
        foreach (var l in listener) listenerInfo += $"{l.name}@({l.transform.position.x:F1},{l.transform.position.y:F1}) ";

        // the audible test
        var spawned = Silksong::RandomAudioClipTableExtensions.SpawnAndPlayOneShot(table, heroPos, true);
        object? spawnedInfo = null;
        if (spawned != null) {
            var lp = spawned.GetComponent<AudioLowPassFilter>();
            var lpDistance = spawned.GetComponent<Silksong::LowPassDistance>();
            object? mapTo = null;
            object? lpCam = null;
            if (lpDistance != null) {
                mapTo = lpDistance.GetFieldValue<object>("mapToRange");
                lpCam = lpDistance.GetFieldValue<object>("camera");
            }
            spawnedInfo = new {
                name = spawned.name,
                clip = spawned.clip != null ? spawned.clip.name : "(one-shot: clip not on source)",
                blend = spawned.spatialBlend, volume = spawned.volume, maxDistance = spawned.maxDistance,
                playing = spawned.isPlaying,
                pos = spawned.transform.position.ToString("F1"),
                cutoffNow = lp != null ? lp.cutoffFrequency : -1f,
                lowpass_cam = lpCam is Transform t ? t.position.ToString("F1") : "null",
                mapToRange = mapTo == null ? null : $"{mapTo.GetFieldValue<float>("Start")}..{mapTo.GetFieldValue<float>("End")}",
            };
        }

        var lastPlayed = typeof(Silksong::RandomAudioClipTable)
            .GetFieldValue<System.Collections.IDictionary>("_lastPlayedInfos");

        // double-mount forensics: the mixer asset exists ONCE on disk and every serialized ref points at the same
        // pathID, so two runtime mixer instances require the audioobjects bundle loaded twice (Unity dedups bundle
        // files per load path/API, not by content)
        var bundleNames = new List<string>();
        foreach (var b in AssetBundle.GetAllLoadedAssetBundles()) bundleNames.Add(b.name);
        var dupBundles = string.Join(", ", bundleNames
            .GroupBy(n => n).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} x{g.Count()}"));
        string lastPlayedInfo = "";
        if (lastPlayed != null) {
            foreach (System.Collections.DictionaryEntry e in lastPlayed) {
                var v = e.Value;
                lastPlayedInfo += $"{e.Key}(prio={v?.GetFieldValue<int>("Priority")},end={v?.GetFieldValue<double>("EndTime")}@now={Time.unscaledTimeAsDouble:F1}) ";
            }
        }

        // --- swoosh diagnosis: the needle swing sound is NOT a one-shot; NailSlash.StartSlash plays the AudioSource
        // on the slash GO directly. It reports playing=True while being inaudible, so compare its clip data and its
        // mixer-group OBJECT against the pooled actor path that IS audible, and offer an audible bypass spawn of the
        // same clip through the working actor pool.
        var slashT = hero.transform.Find("Attacks/Default/Slash");
        object? swoosh = null;
        AudioSource? slashSrc = null;
        if (slashT != null) {
            var sSrc = slashT.GetComponent<AudioSource>();
            slashSrc = sSrc;
            var sClip = sSrc != null ? sSrc.clip : null;
            var sGroup = sSrc != null ? sSrc.outputAudioMixerGroup : null;

            // a pooled actor from the active GlobalPool (the path voicelines play through, audible)
            AudioSource? aSrc = null;
            var gp = GameObject.Find("GlobalPool(Clone)");
            if (gp != null) {
                for (var i = 0; i < gp.transform.childCount; i++) {
                    var ch = gp.transform.GetChild(i);
                    if (ch.name.StartsWith("Audio Player Actor")) { aSrc = ch.GetComponent<AudioSource>(); break; }
                }
            }
            var aGroup = aSrc != null ? aSrc.outputAudioMixerGroup : null;

            // audible bypass: same clip through the WORKING actor-pool path
            string? bypass = null;
            if (sClip != null) {
                var ev = new Silksong::AudioEventRandom { Clips = new[] { sClip }, Volume = 1f, PitchMin = 1f, PitchMax = 1f };
                var b = ev.SpawnAndPlayOneShot(null, heroPos);
                bypass = b != null ? "spawned (listen!)" : "NULL (gated)";
            }

            swoosh = new {
                clip = sClip == null ? (object)"null" : new {
                    name = sClip.name, length = sClip.length, samples = sClip.samples,
                    frequency = sClip.frequency, loadState = sClip.loadState.ToString()
                },
                slash_group = sGroup == null ? (object)"null" : new { id = sGroup.GetInstanceID(), name = sGroup.name,
                    mixer = sGroup.audioMixer == null ? "null" : sGroup.audioMixer.name + "#" + sGroup.audioMixer.GetInstanceID() },
                actor_group = aGroup == null ? (object)"null" : new { id = aGroup.GetInstanceID(), name = aGroup.name,
                    mixer = aGroup.audioMixer == null ? "null" : aGroup.audioMixer.name + "#" + aGroup.audioMixer.GetInstanceID() },
                same_group_object = sGroup != null && aGroup != null && sGroup.GetInstanceID() == aGroup.GetInstanceID(),
                audible_bypass_via_actor_pool = bypass,
            };

            // Live fix experiment: route every Attack AudioSource through the group the audible pooled actors use.
            if (repoint == "1" || repoint == "true") {
                var n = 0;
                var attacks = hero.transform.Find("Attacks");
                if (attacks != null && aGroup != null) {
                    foreach (var a in attacks.GetComponentsInChildren<AudioSource>(true)) {
                        a.outputAudioMixerGroup = aGroup;
                        n++;
                    }
                }
                swoosh = new Dictionary<string, object?> {
                    ["repointed"] = n,
                    ["to_group"] = aGroup == null ? "null" : aGroup.name + "#" + aGroup.GetInstanceID(),
                    ["diagnosis"] = swoosh,
                };
            }
        }

        // restore=1: transition the HERO'S OWN mixer generation to the 'On' snapshot (what SceneManager.Start does
        // for the early generation but never for the one the hero can end up on). If the mute hypothesis holds, this
        // alone must revive the swoosh WITHOUT re-pointing anything — falsification test and fix prototype in one.
        object? restoreInfo = null;
        if (restore == "1" || restore == "true") {
            var heroMixer = slashSrc?.outputAudioMixerGroup?.audioMixer;
            var snap = heroMixer?.FindSnapshot("On");
            if (heroMixer != null && snap != null) {
                snap.TransitionTo(0.5f);
                restoreInfo = new { mixer = $"{heroMixer.name}#{heroMixer.GetInstanceID()}", snapshot = "On", t = 0.5f };
            } else {
                restoreInfo = new { error = heroMixer == null ? "hero mixer null" : "snapshot 'On' not found" };
            }
        }

        return new {
            hero = hero.name,
            heroPos = heroPos.ToString("F1"),
            table = new {
                name = table.name,
                type = table.GetFieldValue<object>("type"),
                clipCount = table.GetFieldValue<Array>("clips")?.Length ?? -1,
                selectClip_forced = clip != null ? clip.name : "NULL",
                nextPlayTime = table.GetFieldValue<double>("nextPlayTime"),
                now = Time.unscaledTimeAsDouble,
                priority = table.GetFieldValue<int>("priority"),
                lastPlayedInfos = lastPlayedInfo,
            },
            gates = new {
                eventFrequencyLimit = Silksong::GlobalSettings.Audio.AudioEventFrequencyLimit,
                prefabNull = prefab == null,
                tryPlayGate = clip != null && prefab != null
                    ? Silksong::AudioEventManager.TryPlayAudioClip(clip, prefab, heroPos)
                    : (bool?)null,
            },
            cameras = new {
                hkMainCam = hkCam != null ? hkCam.transform.position.ToString("F1") : "null",
                ssRigCam = ssCam != null ? ssCam.transform.position.ToString("F1") : "null",
                ssCamToHero = ssCam != null ? (ssCam.transform.position - heroPos).magnitude : -1f,
                listeners = listenerInfo,
            },
            audible_spawn = spawnedInfo ?? "NULL (gated silent)",
            swoosh = swoosh ?? "(no Attacks/Default/Slash child found)",
            loadedBundles = bundleNames.Count,
            duplicateBundles = string.IsNullOrEmpty(dupBundles) ? "none" : dupBundles,
            restore = restoreInfo,
        };
    }
}
