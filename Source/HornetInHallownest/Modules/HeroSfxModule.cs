extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.HornetInHallownest.Core;
using UnityEngine;
using UnityEngine.Audio;
using SAudio = Silksong::GlobalSettings.Audio;

namespace HornetInHallownest.HornetInHallownest.Modules;

// Map hornets sound to HK's mixer, respecting the volume settings.
public sealed class HeroSfxModule : ModuleBase {
    private readonly HashSet<AudioMixer> ssMixers = [];
    private readonly HashSet<AudioSource> claimed = [];
    private AudioMixerGroup? hkSfxGroup;
    private bool ready;

    public override string Id => "hero-sfx";

    public override void Initialize() {
        Detour(typeof(AudioSource), "PlayHelper", OnPlay, typeof(AudioSource), typeof(ulong));
        Detour(typeof(AudioSource), "PlayOneShotHelper", OnPlayOneShot, typeof(AudioSource), typeof(AudioClip), typeof(float));
    }

    protected override void OnDeinitialize() {
        ssMixers.Clear();
        claimed.Clear();
        hkSfxGroup = null;
        ready = false;
    }

    private void OnPlay(Action<AudioSource, ulong> orig, AudioSource source, ulong delay) {
        Normalize(source);
        orig(source, delay);
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
            claimed.Add(source); }

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

        AddMixer(SAudio.DefaultAudioSourcePrefab);
        AddMixer(SAudio.Default2DAudioSourcePrefab);
        AddMixer(SAudio.DefaultUIAudioSourcePrefab);
        if (ssMixers.Count == 0) return false; // Silksong Audio settings not loaded yet

        ready = true;
        return true;
    }

    private void AddMixer(AudioSource? prefab) {
        var g = prefab ? prefab.outputAudioMixerGroup : null;
        if (g && g.audioMixer) ssMixers.Add(g.audioMixer);
    }
}
