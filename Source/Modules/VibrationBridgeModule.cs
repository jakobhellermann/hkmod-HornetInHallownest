extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using SGamepadVibration = Silksong::GamepadVibration;
using SVibrationData = Silksong::VibrationData;
using SVibrationEmission = Silksong::VibrationEmission;
using SVibrationMixer = Silksong::VibrationMixer;
using SVibrationTarget = Silksong::VibrationTarget;

namespace HornetInHallownest.Modules;

// Silksong finds its vibration mixer on its platform or its input device, and has neither here: the platform is a stand-in
// and its input manager never runs. Its rumbles play on HK's mixer instead, under HK's vibration setting.
public sealed class VibrationBridgeModule : ModuleBase {
    private static readonly HkMixer mixer = new();

    public override string Id => "vibration-bridge";

    public override void Initialize() {
        Detour(typeof(Silksong::VibrationManager), nameof(Silksong::VibrationManager.GetMixer), OnGetMixer);
    }

    protected override void OnDeinitialize() {
        mixer.StopAllEmissions();
    }

    private static SVibrationMixer OnGetMixer(Func<SVibrationMixer?> orig) => orig() ?? mixer;

    private sealed class HkMixer : SVibrationMixer {
        private readonly List<Emission> playing = [];
        private readonly Dictionary<SGamepadVibration, GamepadVibration> vibrations = [];

        public override bool IsPaused {
            get => VibrationManager.GetMixer() is { IsPaused: true };
            set {
                if (VibrationManager.GetMixer() is { } hk) hk.IsPaused = value;
            }
        }

        public override int PlayingEmissionCount => Prune().Count;

        public override SVibrationEmission? PlayEmission(SVibrationData data, SVibrationTarget target, bool isLooping,
            string tag, bool isRealtime) {
            var hk = VibrationManager.GetMixer();
            var vibration = Convert(data.GamepadVibration);
            if (hk == null || !vibration) return null;
            var hkData = VibrationData.Create((LowFidelityVibrations)(int)data.LowFidelityVibration,
                data.HighFidelityVibration, vibration);
            var hkTarget = new VibrationTarget((VibrationMotors)(int)target.Motors);
            var inner = hk.PlayEmission(hkData, hkTarget, isLooping, tag, isRealtime);
            if (inner == null) return null;
            var emission = new Emission(inner);
            Prune().Add(emission);
            return emission;
        }

        public override SVibrationEmission? PlayEmission(SVibrationEmission emission) {
            if (emission is not Emission ours || VibrationManager.GetMixer() is not { } hk) return null;
            hk.PlayEmission(ours.Inner);
            if (!playing.Contains(ours)) Prune().Add(ours);
            return ours;
        }

        public override SVibrationEmission GetPlayingEmission(int playingEmissionIndex) =>
            Prune()[playingEmissionIndex];

        public override void StopAllEmissions() {
            foreach (var emission in Prune()) emission.Stop();
        }

        public override void StopAllEmissionsWithTag(string tag) {
            foreach (var emission in Prune())
                if (emission.Tag == tag)
                    emission.Stop();
        }

        private List<Emission> Prune() {
            playing.RemoveAll(emission => !emission.IsPlaying);
            return playing;
        }

        // HK's mixer reads the curves of its own GamepadVibration, a separate type with the same fields.
        private GamepadVibration? Convert(SGamepadVibration? source) {
            if (!source) return null;
            if (vibrations.TryGetValue(source, out var converted) && converted) return converted;
            converted = ScriptableObject.CreateInstance<GamepadVibration>();
            converted.name = source.name;
            converted.SetFieldValue("smallMotor", source.SmallMotor);
            converted.SetFieldValue("largeMotor", source.LargeMotor);
            converted.SetFieldValue("playbackRate", source.PlaybackRate);
            vibrations[source] = converted;
            return converted;
        }
    }

    private sealed class Emission(VibrationEmission inner) : SVibrationEmission {
        internal VibrationEmission Inner => inner;

        public override SVibrationTarget Target {
            get => new((Silksong::VibrationMotors)(int)inner.Target.Motors);
            set => inner.Target = new VibrationTarget((VibrationMotors)(int)value.Motors);
        }

        public override bool IsLooping {
            get => inner.IsLooping;
            set => inner.IsLooping = value;
        }

        public override string Tag {
            get => inner.Tag;
            set => inner.Tag = value;
        }

        public override bool IsRealtime => inner.IsRealtime;

        public override float BaseStrength {
            get => inner.BaseStrength;
            set => inner.BaseStrength = value;
        }

        public override float Strength {
            get => inner.Strength;
            set => inner.Strength = value;
        }

        public override float Speed {
            get => inner.Speed;
            set => inner.Speed = value;
        }

        public override float Time {
            get => inner.Time;
            set => inner.Time = value;
        }

        public override bool IsPlaying => inner.IsPlaying;

        public override void Play() => inner.Play();

        public override void Stop() => inner.Stop();
    }
}
