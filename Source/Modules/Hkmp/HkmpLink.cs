#if HK1578
extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using Hkmp.Api.Client;
using HornetInHallownest.Util;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HornetInHallownest.Modules.Hkmp;

// Send hornet animation clips as HKMP animation updates with ids past the HK clip enum, ignored by vanilla HKMP clients.
// Draw remote hornets using HKMP's player object with hornets' library.
internal sealed class HkmpLink : IHkmpLink {
    // id = HornetClipBase + index of the clip in its library, payload = that library's fingerprint.
    private const int HornetClipBase = 0xC000;

    // HKMP's "Username" object sits 1.25 above the Knight's pivot; Hornet is taller.
    private const string UsernameObjectName = "Username";
    private const float HornetNameHeight = 2.5f;

    private readonly IClientApi api;
    private readonly HkmpInternals internals;
    private readonly Hook tk2DPlayHook;

    private Silksong::HeroController? trackedHornet;
    private tk2dSpriteAnimator? hornetAnimator;
    private tk2dSpriteAnimation? hornetLibrary;

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
    }

    public void HornetToggled(bool active) {
        if (!active) ReleaseOwnName();
        if (!internals.Connected) return;
        // A switch resumes the incoming hero's animator without playing a clip, so nothing would be sent until she moves.
        if (!active) {
            internals.SendKnightClip();
            return;
        }

        TrackLocalHornet(HornetSpawner.Hornet!);
        SendCurrentClip();
    }

    private void TrackLocalHornet(Silksong::HeroController hero) {
        if (ReferenceEquals(hero, trackedHornet)) return;
        trackedHornet = hero;
        var animator = hero.GetComponent<tk2dSpriteAnimator>();
        hornetAnimator = animator;
        hornetLibrary = animator.Library;
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
        if (!ReferenceEquals(self, hornetAnimator) || clip == null) {
            orig(self, clip, clipStartTime, overrideFps);
            return;
        }

        var restarts = clipStartTime != 0f || !self.IsPlaying(clip);
        orig(self, clip, clipStartTime, overrideFps);
        if (restarts && HeroSwitch.HornetActive && internals.Connected)
            SendClip(clip, (int)(clipStartTime * (overrideFps > 0f ? overrideFps : clip.fps)));
    }

    private void OnPlayerEnterScene(IClientPlayer player) {
        // A player who just appeared in our scene (or whose scene we entered) only knows the Knight's clip.
        if (HeroSwitch.HornetActive) SendCurrentClip();
    }

    private void SendCurrentClip() {
        var animator = hornetAnimator;
        if (animator && animator!.CurrentClip is { } clip) SendClip(clip, animator.CurrentFrame);
    }

    private void SendClip(tk2dSpriteAnimationClip clip, int frame) {
        if (!clipAddresses.TryGetValue(clip, out var address)) {
            IndexLoadedLibraries();
            if (!clipAddresses.TryGetValue(clip, out address)) {
                // Not thrown: this runs inside Hornet's own animation code.
                Log.ErrorOnce($"hkmp-clip:{clip.name}", $"[hkmp] clip '{clip.name}' is in no loaded animation library");
                return;
            }
        }

        internals.SendAnimationEntry((ushort)(HornetClipBase + address.index), frame, ToBits(address.library));
    }

    private bool OnAnimationEntry(ushort playerId, int id, int frame, bool[]? payload) {
        if (id < HornetClipBase) {
            RestoreKnight(playerId);
            return false;
        }

        var fingerprint = FromBits(payload!);
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

        if (!remoteHornets.TryGetValue(playerId, out var remote)) {
            var player = api.ClientManager.GetPlayer(playerId);
            var animator = player.PlayerObject.GetComponent<tk2dSpriteAnimator>();
            var name = player.PlayerContainer.transform.Find(UsernameObjectName);
            remote = new RemoteHornet(player.PlayerObject, animator, animator.Library, name, name.localPosition);
            remoteHornets[playerId] = remote;
            animator.Library = HornetLibrary();
            name.localPosition = new Vector3(name.localPosition.x, HornetNameHeight, name.localPosition.z);
        }

        remote.Animator.PlayFromFrame(library.clips[id - HornetClipBase], frame);
        return true;
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
    }

    private void RestoreAllKnights() {
        foreach (var id in new List<ushort>(remoteHornets.Keys)) RestoreKnight(id);
    }

    private static bool[] ToBits(uint value) {
        var bits = new bool[32];
        new BitArray([(int)value]).CopyTo(bits, 0);
        return bits;
    }

    private static uint FromBits(bool[] bits) {
        var value = new int[1];
        new BitArray(bits).CopyTo(value, 0);
        return (uint)value[0];
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
        Vector3 NameLocalPosition);
}
#endif
