extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;
using UnityEngine.Audio;
using SAudio = Silksong::GlobalSettings.Audio;

namespace HornetInHallownest.Modules;

// Map hornets sound to HK's mixer, respecting the volume settings.
public sealed class HeroSfxModule : ModuleBase {
    private readonly HashSet<AudioMixer> ssMixers = [];
    private readonly HashSet<AudioSource> claimed = [];
    private readonly HashSet<GameObject> reroutedPrefabs = [];
    private AudioMixerGroup? hkSfxGroup;
    private bool ready;

    public override string Id => "hero-sfx";

    public override void Initialize() {
        // Unity 6 routes every Play/PlayOneShot through managed static helpers; on Unity 2020 (HK 1.5.78) those helpers
        // are native internal calls, so hook the public managed wrappers there instead.
        if (TryDetour(typeof(AudioSource), "PlayHelper", OnPlay, typeof(AudioSource), typeof(ulong)) == null) {
            TryDetour(typeof(AudioSource), "Play", OnPlayNoDelay, NoParams);
            TryDetour(typeof(AudioSource), "Play", OnPlayInstance, typeof(ulong));
        }
        if (TryDetour(typeof(AudioSource), "PlayOneShotHelper", OnPlayOneShot, typeof(AudioSource), typeof(AudioClip),
                typeof(float)) == null)
            TryDetour(typeof(AudioSource), "PlayOneShot", OnPlayOneShotInstance, typeof(AudioClip), typeof(float));
        // Some pool objects like double damage audio don't go through PlayActive but set playOnAwake.
        Detour(typeof(Silksong::ObjectPool), "Spawn", OnSpawn,
            typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Quaternion), typeof(bool));
        // Hero child effects like the Magnetite Dice shield are instantiated in Awake, not pooled, and set playOnAwake.
        Detour(typeof(Silksong::HeroController), "SpawnChildPrefab", OnSpawnChildPrefab, typeof(GameObject));
    }

    protected override void OnDeinitialize() {
        ssMixers.Clear();
        claimed.Clear();
        reroutedPrefabs.Clear();
        hkSfxGroup = null;
        ready = false;
    }

    // Reroute audio mixer of the prefab template to HK's audio sfx group.
    private GameObject OnSpawn(Func<GameObject, Transform, Vector3, Quaternion, bool, GameObject> orig,
        GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, bool stealActiveSpawned) {
        ReroutePrefabAudio(prefab);
        return orig(prefab, parent, position, rotation, stealActiveSpawned);
    }

    private GameObject OnSpawnChildPrefab(Func<Silksong::HeroController, GameObject, GameObject> orig,
        Silksong::HeroController self, GameObject prefab) {
        ReroutePrefabAudio(prefab);
        return orig(self, prefab);
    }

    private void ReroutePrefabAudio(GameObject? prefab) {
        if (!prefab || !reroutedPrefabs.Add(prefab)) return;
        if (!ready && !EnsureReady()) {
            reroutedPrefabs.Remove(prefab); // not ready yet, retry on a later spawn
            return;
        }

        foreach (var src in prefab.GetComponentsInChildren<AudioSource>(true)) {
            if (!src) continue;
            var group = src.outputAudioMixerGroup;
            if (ReferenceEquals(group, null) || !ssMixers.Contains(group.audioMixer)) continue;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = hkSfxGroup;
        }
    }

    private void OnPlay(Action<AudioSource, ulong> orig, AudioSource source, ulong delay) {
        Normalize(source);
        orig(source, delay);
    }

    private void OnPlayNoDelay(Action<AudioSource> orig, AudioSource source) {
        Normalize(source);
        orig(source);
    }

    private void OnPlayInstance(Action<AudioSource, ulong> orig, AudioSource source, ulong delay) {
        Normalize(source);
        orig(source, delay);
    }

    private void OnPlayOneShotInstance(Action<AudioSource, AudioClip, float> orig, AudioSource source, AudioClip clip,
        float volumeScale) {
        Normalize(source);
        orig(source, clip, volumeScale);
    }

    private void OnPlayOneShot(Action<AudioSource, AudioClip, float> orig, AudioSource source, AudioClip clip,
        float volumeScale) {
        Normalize(source);
        orig(source, clip, volumeScale);
    }

    private void Normalize(AudioSource source) {
        if (!ready && !EnsureReady()) return;
        if (!source) return;
        if (!claimed.Contains(source)) {
            var group = source.outputAudioMixerGroup;
            if (ReferenceEquals(group, null)) return; // unrouted (not a Silksong mixer source)
            if (!ssMixers.Contains(group.audioMixer)) return; // one of HK's mixers -> leave it alone
            claimed.Add(source);
        }

        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = hkSfxGroup;
    }

    // Resolve HK's live SFX group (off the Knight) and the set of Silksong mixers.
    // Both become available only after Hornet spawns.
    private bool EnsureReady() {
        var knight = HeroController.instance;
        if (!knight) return false;
        foreach (var src in knight.GetComponentsInChildren<AudioSource>(true))
            if (src && src.outputAudioMixerGroup) {
                hkSfxGroup = src.outputAudioMixerGroup;
                break;
            }

        if (!hkSfxGroup) return false;

        // On hot reload, reconstruct ss mixers from hornet, since the templates are already on HK mixers
        var hero = HornetSpawner.Hornet;
        if (hero) {
            foreach (var src in hero.GetComponentsInChildren<AudioSource>(true)) {
                var g = src != null ? src.outputAudioMixerGroup : null;
                if (g && g.audioMixer && g != hkSfxGroup) ssMixers.Add(g.audioMixer);
            }
        }

        // on a fresh process the templates cover the mixers the hero does not reference (UI etc.)
        AddMixer(SAudio.DefaultAudioSourcePrefab);
        AddMixer(SAudio.Default2DAudioSourcePrefab);
        AddMixer(SAudio.DefaultUIAudioSourcePrefab);
        if (ssMixers.Count == 0) return false; // Silksong Audio settings not loaded yet

        foreach (var mixer in ssMixers) RouteToHkSfx(mixer);

        ready = true;
        return true;
    }

    // Sources that play without passing Normalize (playOnAwake on activation) still end up in a Silksong mixer, so its root
    // mixer outputs into HK's SFX group instead of straight to the listener.
    private void RouteToHkSfx(AudioMixer mixer) {
        var root = mixer;
        while (root.outputAudioMixerGroup && root.outputAudioMixerGroup != hkSfxGroup)
            root = root.outputAudioMixerGroup.audioMixer;
        root.outputAudioMixerGroup = hkSfxGroup;
    }

    private void AddMixer(AudioSource? prefab) {
        var g = prefab ? prefab.outputAudioMixerGroup : null;
        if (!g || !g.audioMixer) return;
        if (g == hkSfxGroup) return; // already rerouted by a previous hot reload
        ssMixers.Add(g.audioMixer);
    }
}
