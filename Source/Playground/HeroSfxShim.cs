extern alias Silksong;
using System;
using System.Collections.Generic;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SRandomAudioTable = Silksong::RandomAudioClipTable;
using SAudioExt = Silksong::RandomAudioClipTableExtensions;
using SAudioEventManager = Silksong::AudioEventManager;
using SNailSlash = Silksong::NailSlash;

namespace HornetPlayer.Playground;

// Make Hornet's code-driven SFX audible in HK. Silksong plays one-shots through Audio.DefaultAudioSourcePrefab gated by
// AudioEventManager.TryPlayAudioClip, which distance-CULLS 3D one-shots against Silksong's neutered rig camera; on top,
// effect prefabs (the nail slash) Play() their OWN 3D AudioSource. HK's camera/listener sits farther from the hero than
// Silksong's, so all that 3D audio attenuates to ~nothing. (FSM audio like Bind uses a different pooled-actor path, so
// it played.) HK owns the AudioListener and we can't cheaply re-tune Silksong's 3D, so we make Hornet's SFX 2D:
//   - RandomAudioClipTable.SpawnAndPlayOneShot (dash/attack/grunt/footsteps): REPLACED — play the selected clip on our
//     own persistent 2D source. (2-arg/4-arg overloads funnel into the hooked 6-arg one.)
//   - AudioEvent one-shots: hook TryPlayAudioClip to un-cull (return true) AND flip the prefab it's about to Spawn to 2D.
//   - NailSlash (the swing): its own AudioSource is authored 3D; flip it to 2D in Awake so the swing is heard.
// Losses vs Silksong: AudioMixer routing + spatial panning (negligible for the player's own hero SFX). Occasional
// silence on rapid hits is Silksong's own SelectClip probability roll (by design), not a bug.
internal static class HeroSfxShim {
    private static readonly List<Hook> hooks = new();
    private static GameObject? go;
    private static AudioSource? sfx; // one persistent 2D source; PlayOneShot overlaps clips

    private static void PlayClip(AudioClip? clip, float volume, float pitch) {
        if (clip == null) return;
        if (sfx == null) {
            go = new GameObject("HornetPlayer.HeroSfx");
            UnityEngine.Object.DontDestroyOnLoad(go);
            sfx = go.AddComponent<AudioSource>();
            sfx.spatialBlend = 0f;
            sfx.volume = 1f;
            sfx.outputAudioMixerGroup = null;
            sfx.playOnAwake = false;
        }

        sfx.pitch = pitch;
        sfx.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    // Flip an AudioSource to raw 2D so HK's farther camera doesn't distance-attenuate it to silence.
    private static void MakeRaw2D(AudioSource? src) {
        if (src == null) return;
        src.spatialBlend = 0f;
        src.outputAudioMixerGroup = null;
    }

    internal static void Install() {
        HookMethod(typeof(SAudioExt), "SpawnAndPlayOneShot", BindingFlags.Public | BindingFlags.Static,
            [typeof(SRandomAudioTable), typeof(AudioSource), typeof(Vector3), typeof(bool), typeof(float), typeof(Action)],
            (Func<Func<SRandomAudioTable, AudioSource, Vector3, bool, float, Action, AudioSource>,
                SRandomAudioTable, AudioSource, Vector3, bool, float, Action, AudioSource>)((_, table, _, _, force, vol, _) => {
                if (table != null) PlayClip(table.SelectClip(force), table.SelectVolume() * vol, table.SelectPitch());
                return null!;
            }));

        // AudioEvent one-shots: un-cull + route through a 2D prefab so the real prefab.Spawn play is audible.
        HookMethod(typeof(SAudioEventManager), "TryPlayAudioClip", BindingFlags.Public | BindingFlags.Static,
            [typeof(AudioClip), typeof(AudioSource), typeof(Vector3)],
            (Func<Func<AudioClip, AudioSource, Vector3, bool>, AudioClip, AudioSource, Vector3, bool>)((_, _, prefab, _) => {
                MakeRaw2D(prefab);
                return true;
            }));

        // NailSlash's own 3D AudioSource (the swing) — flip it to 2D once, in Awake.
        HookMethod(typeof(SNailSlash), "Awake", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes,
            (Action<Action<SNailSlash>, SNailSlash>)((orig, self) => {
                orig(self);
                MakeRaw2D(self.GetComponent<AudioSource>());
            }));

        Log.Info($"[HeroSfx] installed {hooks.Count} hooks -> 2D hero SFX");
    }

    private static void HookMethod(Type type, string name, BindingFlags flags, Type[] sig, Delegate replacement) {
        var mi = type.GetMethod(name, flags, null, sig, null);
        if (mi == null) {
            Log.Error($"[HeroSfx] {type.Name}.{name}({sig.Length} args) not found");
            return;
        }

        try {
            hooks.Add(new Hook(mi, replacement));
        } catch (Exception e) {
            Log.Error($"[HeroSfx] hook {type.Name}.{name}: {e.Message}");
        }
    }

    internal static void Cleanup() {
        foreach (var h in hooks) h.Dispose();
        hooks.Clear();
        if (go != null) UnityEngine.Object.Destroy(go);
        go = null;
        sfx = null;
    }
}
