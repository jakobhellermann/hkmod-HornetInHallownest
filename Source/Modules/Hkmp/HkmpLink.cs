#if HK1578
extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Hkmp.Api.Client;
using HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HornetInHallownest.Modules.Hkmp;

// Send hornet animation clips as HKMP animation updates with ids past the HK clip enum, ignored by vanilla HKMP clients.
// Draw remote hornets using HKMP's player object with hornets' library.
internal sealed class HkmpLink : IHkmpLink {
    // id = HornetClipBase + index of the clip in its library, payload = library fingerprint and the animator
    // (0 = Hornet, n = effect entry n - 1).
    private const int HornetClipBase = 0xC000;

    // id = EffectBase + effect entry, payload = EffectState.
    private const int EffectBase = 0xF000;

    // HKMP's "Username" object sits 1.25 above the Knight's pivot; Hornet is taller.
    private const string UsernameObjectName = "Username";
    private const float HornetNameHeight = 2.5f;

    private readonly IClientApi api;
    private readonly HkmpInternals internals;
    private readonly Hook tk2DPlayHook;

    private Silksong::HeroController? trackedHornet;
    private tk2dSpriteAnimator? hornetAnimator;
    private tk2dSpriteAnimation? hornetLibrary;
    private EffectObject[] localEffects = [];
    private EffectState?[] sentEffects = [];
    private readonly Dictionary<tk2dSpriteAnimator, int> animatorTargets = new(ReferenceComparer<tk2dSpriteAnimator>.Instance);

    private readonly Dictionary<tk2dSpriteAnimationClip, (uint library, ushort index)> clipAddresses = new();
    private readonly Dictionary<uint, tk2dSpriteAnimation> librariesByFingerprint = new();
    private readonly HashSet<uint> missingFingerprints = [];

    private readonly Dictionary<ushort, RemoteHornet> remoteHornets = new();

    private Transform? ownName;
    private Vector3 ownNameLocalPosition;

    internal HkmpLink(IClientApi api) {
        this.api = api;
        internals = new HkmpInternals(api, OnAnimationEntry,
            () => HeroSwitch.HornetActive && !HeroSwitch.BothActive, IsRemoteHornet);
        
        tk2DPlayHook = new Hook(
            typeof(tk2dSpriteAnimator).GetMethod(nameof(tk2dSpriteAnimator.Play),
                [typeof(tk2dSpriteAnimationClip), typeof(float), typeof(float)])!,
            OnAnimatorPlay);

        var clientManager = api.ClientManager;
        clientManager.PlayerEnterSceneEvent += OnPlayerEnterScene;
        clientManager.PlayerLeaveSceneEvent += RestoreKnight;
        clientManager.PlayerDisconnectEvent += RestoreKnight;
        clientManager.DisconnectEvent += OnDisconnect;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += ForgetMissingLibraries;
    }

    public void HornetActiveUpdate(Silksong::HeroController hero) {
        TrackLocalHornet(hero);
        if (!internals.Connected || HeroSwitch.BothActive) return;
        internals.PublishLocalHero(hero.transform);
        FollowOwnName(hero.transform);
        SendEffectChanges(hero.transform);
    }

    public void HornetToggled(bool active) {
        // A switch resumes the incoming hero's animator without playing a clip, so nothing would be sent until she moves.
        if (!active) {
            ReleaseOwnName();
            if (internals.Connected) internals.SendKnightClip();
            return;
        }

        TrackLocalHornet(HornetSpawner.Hornet!);
        if (internals.Connected) ResendHornetState();
    }

    private void TrackLocalHornet(Silksong::HeroController hero) {
        if (ReferenceEquals(hero, trackedHornet)) return;
        trackedHornet = hero;
        var animator = hero.GetComponent<tk2dSpriteAnimator>();
        hornetAnimator = animator;
        hornetLibrary = animator.Library;

        localEffects = HornetEffects.Resolve(hero.transform);
        sentEffects = new EffectState?[localEffects.Length];
        animatorTargets.Clear();
        animatorTargets[animator] = 0;
        for (var i = 0; i < localEffects.Length; i++)
            if (localEffects[i].Animator is { } effectAnimator)
                animatorTargets[effectAnimator] = i + 1;
    }

    private void SendEffectChanges(Transform hero) {
        for (var i = 0; i < localEffects.Length; i++) {
            var state = EffectState.Capture(localEffects[i], hero);
            if (sentEffects[i] == state) continue;
            sentEffects[i] = state;
            internals.SendAnimationEntry((ushort)(EffectBase + i), 0, state.Encode());
        }
    }

    private void FollowOwnName(Transform hero) {
        // Moved over Hornet rather than reparented: HKMP looks the name up under the Knight (disconnect, name toggle, reconnect).
        if (!ownName) {
            // Created by HKMP on connect
            ownName = HeroController.UnsafeInstance.transform.Find(UsernameObjectName);
            if (!ownName) return;
            ownNameLocalPosition = ownName.localPosition;
            ownName.GetComponent<MeshRenderer>().enabled = true;
        }

        ownName.position = hero.position + Vector3.up * HornetNameHeight;
    }

    private void ReleaseOwnName() {
        if (!ownName) return;
        ownName.localPosition = ownNameLocalPosition;
        ownName = null;
    }

    private void OnDisconnect() {
        ReleaseOwnName();
        RestoreAllKnights();
    }

    private void OnAnimatorPlay(Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip?, float, float> orig,
        tk2dSpriteAnimator self, tk2dSpriteAnimationClip? clip, float clipStartTime, float overrideFps) {
        if (clip == null || !animatorTargets.TryGetValue(self, out var target)) {
            orig(self, clip, clipStartTime, overrideFps);
            return;
        }

        var restarts = clipStartTime != 0f || !self.IsPlaying(clip);
        orig(self, clip, clipStartTime, overrideFps);
        if (restarts && HeroSwitch.HornetActive && internals.Connected)
            SendClip(clip, (int)(clipStartTime * (overrideFps > 0f ? overrideFps : clip.fps)), target);
    }

    private void OnPlayerEnterScene(IClientPlayer player) {
        // A player who just appeared in our scene (or whose scene we entered) only knows the Knight's clip.
        if (HeroSwitch.HornetActive) ResendHornetState();
    }

    // The current clip now, the effects with the next HornetActiveUpdate.
    private void ResendHornetState() {
        var animator = hornetAnimator;
        if (animator && animator!.CurrentClip is { } clip) SendClip(clip, animator.CurrentFrame, 0);
        Array.Clear(sentEffects, 0, sentEffects.Length);
    }

    private void SendClip(tk2dSpriteAnimationClip clip, int frame, int target) {
        if (!clipAddresses.TryGetValue(clip, out var address)) {
            IndexLoadedLibraries();
            if (!clipAddresses.TryGetValue(clip, out address)) {
                // Not thrown: this runs inside Hornet's own animation code.
                Log.ErrorOnce($"hkmp-clip:{clip.name}", $"[hkmp] clip '{clip.name}' is in no loaded animation library");
                return;
            }
        }

        var payload = new BitWriter(42).Write(address.library, 32).Write((uint)target, 10).ToArray();
        internals.SendAnimationEntry((ushort)(HornetClipBase + address.index), frame, payload);
    }

    private bool OnAnimationEntry(ushort playerId, int id, int frame, bool[]? payload) {
        if (id < HornetClipBase) {
            RestoreKnight(playerId);
            return false;
        }

        var remote = RemoteHornetFor(playerId);
        if (id >= EffectBase) {
            var entry = id - EffectBase;
            if (entry >= HornetEffects.Entries.Length)
                Log.ErrorOnce($"hkmp-effect:{entry}", $"[hkmp] player {playerId} sent unknown effect {entry} (different mod version?)");
            else
                remote.Effects.Apply(entry, EffectState.Decode(payload!));
            return true;
        }

        var reader = new BitReader(payload!);
        var fingerprint = reader.Read(32);
        var target = (int)reader.Read(10);
        if (!librariesByFingerprint.TryGetValue(fingerprint, out var library)) {
            // A peer on another Silksong build misses on every clip: index at most once per scene for it.
            if (missingFingerprints.Contains(fingerprint)) return true;
            IndexLoadedLibraries();
            if (!librariesByFingerprint.TryGetValue(fingerprint, out library)) {
                missingFingerprints.Add(fingerprint);
                Log.ErrorOnce($"hkmp-library:{fingerprint}", $"[hkmp] player {playerId} plays Hornet animation " +
                                                            $"library {fingerprint:X8}, which isn't loaded here (different Silksong build?)");
                return true;
            }
        }

        var targetAnimator = target == 0 ? remote.Animator : remote.Effects.Animator(target - 1)!;
        targetAnimator.PlayFromFrame(library.clips[id - HornetClipBase], frame);
        return true;
    }

    private RemoteHornet RemoteHornetFor(ushort playerId) {
        if (remoteHornets.TryGetValue(playerId, out var remote)) return remote;

        var player = api.ClientManager.GetPlayer(playerId);
        var animator = player.PlayerObject.GetComponent<tk2dSpriteAnimator>();
        var name = player.PlayerContainer.transform.Find(UsernameObjectName);
        var effects = new RemoteEffects(player.PlayerObject.transform, HornetSpawner.HeroPrefab.transform);
        remote = new RemoteHornet(player.PlayerObject, animator, animator.Library, name, name.localPosition, effects);
        remoteHornets[playerId] = remote;
        animator.Library = HornetLibrary();
        name.localPosition = new Vector3(name.localPosition.x, HornetNameHeight, name.localPosition.z);
        return remote;
    }

    private void ForgetMissingLibraries(Scene from, Scene to) => missingFingerprints.Clear();

    private void IndexLoadedLibraries() {
        foreach (var library in Resources.FindObjectsOfTypeAll<tk2dSpriteAnimation>()) {
            var fingerprint = Fingerprint(library);
            librariesByFingerprint[fingerprint] = library;
            for (var i = 0; i < library.clips.Length; i++) clipAddresses[library.clips[i]] = (fingerprint, (ushort)i);
        }
    }

    // FNV-1a over the clip names in order: equal across clients only for the same Silksong build.
    private static uint Fingerprint(tk2dSpriteAnimation library) {
        var hash = 2166136261u;
        foreach (var clip in library.clips) {
            foreach (var c in clip.name) hash = (hash ^ c) * 16777619u;
            hash *= 16777619u; // name separator
        }

        return hash;
    }

    private tk2dSpriteAnimation HornetLibrary() {
        // Remote Hornets can show up before the local player ever switched to Hornet.
        if (!hornetLibrary) TrackLocalHornet(HornetSpawner.Hornet!);
        
        return hornetLibrary!;
    }

    private bool IsRemoteHornet(GameObject playerObject) {
        foreach (var remote in remoteHornets.Values)
            if (ReferenceEquals(remote.PlayerObject, playerObject))
                return true;
        return false;
    }

    private void RestoreKnight(IClientPlayer player) => RestoreKnight(player.Id);

    private void RestoreKnight(ushort playerId) {
        if (!remoteHornets.TryGetValue(playerId, out var remote)) return;
        remoteHornets.Remove(playerId);
        
        if (remote.Animator) remote.Animator.Library = remote.KnightLibrary;
        if (remote.Name) remote.Name.localPosition = remote.NameLocalPosition;
        remote.Effects.Destroy();
    }

    private void RestoreAllKnights() {
        foreach (var id in new List<ushort>(remoteHornets.Keys)) RestoreKnight(id);
    }

    public void Dispose() {
        var clientManager = api.ClientManager;
        clientManager.PlayerEnterSceneEvent -= OnPlayerEnterScene;
        clientManager.PlayerLeaveSceneEvent -= RestoreKnight;
        clientManager.PlayerDisconnectEvent -= RestoreKnight;
        clientManager.DisconnectEvent -= OnDisconnect;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= ForgetMissingLibraries;
        OnDisconnect();
        tk2DPlayHook.Dispose();
        internals.Dispose();
    }

    private sealed record RemoteHornet(
        GameObject PlayerObject,
        tk2dSpriteAnimator Animator,
        tk2dSpriteAnimation KnightLibrary,
        Transform Name,
        Vector3 NameLocalPosition,
        RemoteEffects Effects);

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class {
        internal static readonly ReferenceComparer<T> Instance = new();
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
#endif
