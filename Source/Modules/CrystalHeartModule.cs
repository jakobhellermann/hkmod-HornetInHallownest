extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Object = UnityEngine.Object;
using SHeroController = Silksong::HeroController;
using SInputHandler = Silksong::InputHandler;

namespace HornetInHallownest.Modules;

public sealed class CrystalHeartModule : ModuleBase {
    private const float Speed = 30f;
    private const float CancelDelay = 0.2f;
    private const float KickUpHeight = 0.25f;
    private const float KickUpSpeed = 7f;
    private const float Deceleration = 0.85f;
    private const float FlightEffectsRise = 0.5f;
    private const string ChargeClip = "Silk Charge Antic Zap";
    private const string WallChargeClip = "Wall Cling";
    private const string FlightClip = "Silk Charge Zap";
    private const string BrakeClip = "Silk Charge Recover Zap";
    private const string EffectsName = "Crystal Heart Effects";

    private static readonly string[] effectPaths = [
        "Effects/SD Charge", "Effects/SD Charge Wall", "Effects/SD Charge Glow", "Effects/SD Bling",
        "Effects/SD Sharp Flash", "Effects/SD Energy", "Effects/SD Burst", "Effects/SD Burst Glow", "Effects/SD Trail",
        "Effects/SD Pulse Pt", "Effects/SD Crystal Gen G1", "Effects/SD Crystal Gen G2", "Effects/SD Crystal Gen W1",
        "Effects/SD Crystal Gen W2", "Effects/SD Crystal Burst GL", "Effects/SD Crystal Burst GR",
        "Effects/SD Crystal Burst W", "SuperDash Damage"
    ];

    private static readonly string[] chargeEffects = ["SD Charge", "SD Charge Wall"];
    private static readonly string[] flightEffects = ["SD Burst", "SD Burst Glow", "SD Trail", "SuperDash Damage"];

    private static readonly Dictionary<tk2dSpriteAnimationClip, tk2dSpriteAnimationClip> loopedClips = [];
    private bool superdashing;
    private bool airSuperdashUsed;
    private int controlVersion;
    private GameObject? effects;

    public override string Id => "crystal-heart";

    public override void Initialize() {
        Detour(typeof(SHeroController), "LookForQueueInput", OnLookForQueueInput);
        Detour(typeof(SHeroController), nameof(SHeroController.CanHarpoonDash), OnCanHarpoonDash);
    }

    protected override void OnDeinitialize() {
        if (effects) Object.Destroy(effects);
    }

    private void OnLookForQueueInput(Action<SHeroController> orig, SHeroController self) {
        if (self.cState.onGround || OnWall(self)) airSuperdashUsed = false;
        if (!superdashing && CanStart(self)) StartCoroutine(Superdash(self, Input(self)));
        orig(self);
    }

    private bool OnCanHarpoonDash(Func<SHeroController, bool> orig, SHeroController self) =>
        !superdashing && orig(self);

    // One superdash per jump: chained in the air, they would carry her across any gap.
    private bool CanStart(SHeroController hero) {
        if (!HeroSwitch.HornetActive || hero != HornetSpawner.Hornet || !PlayerData.instance.hasSuperDash) return false;
        var actions = Input(hero).inputActions;
        return actions.SuperDash.WasPressed && actions.Up.IsPressed && !actions.Down.IsPressed &&
               !airSuperdashUsed && hero.CanHarpoonDash();
    }

    private static bool OnWall(SHeroController hero) => hero.cState.wallSliding || hero.cState.wallClinging;

    private static SInputHandler Input(SHeroController hero) => hero.GetFieldValue<SInputHandler>("inputHandler")!;

    private IEnumerator Superdash(SHeroController hero, SInputHandler input) {
        superdashing = true;
        var body = hero.GetComponent<Rigidbody2D>();
        var onWall = OnWall(hero);
        if (!onWall && !hero.cState.onGround) airSuperdashUsed = true;
        // Like her Clawline, it cuts short her sprint and its recovery, which would otherwise let go of her mid-superdash.
        Silksong::EventRegister.SendEvent(Silksong::EventRegisterEvents.FsmCancel);
        hero.RelinquishControl();
        controlVersion = SHeroController.ControlVersion;
        hero.StopAnimationControl();
        hero.AffectedByGravity(false);
        // Drawn after her silk charge, Silksong's Sharpdart: a hop off the ground and slowing down through its wind-up.
        if (onWall) {
            body.linearVelocity = Vector2.zero;
            hero.AnimCtrl.PlayClipForced(WallChargeClip);
        }
        else {
            if (hero.cState.onGround) {
                // The hop only pushes her up further when the lift alone leaves her on the ground, such as on a slope.
                hero.transform.Translate(0f, KickUpHeight, 0f, Space.World);
                if (hero.CheckTouchingGround())
                    body.linearVelocity = new Vector2(body.linearVelocity.x, KickUpSpeed);
            }

            hero.AnimCtrl.PlayClipForced(ChargeClip);
        }

        var fx = EnsureEffects(hero);
        var chargeEffect = onWall ? "SD Charge Wall" : "SD Charge";
        PlayMakerFSM.BroadcastEvent(onWall ? "SUPERDASH CHARGING W" : "SUPERDASH CHARGING G");
        SendTo(fx, chargeEffect, "UP");
        SendTo(fx, "SD Charge Glow", "UP");
        Play(fx, chargeEffect, "SD Fx Charge");
        SetKnightLoop("SuperDash Charge", playing: true);
        Rumble(focus: true, charged: false);
        Shake("FocusRumble");

        // She launches once the wind-up has played, with no holding it and no letting go to cancel.
        var chargeTime = Duration(hero, ChargeClip);
        for (var time = 0f; time < chargeTime && !IsInterrupted(hero); time += Time.fixedDeltaTime) {
            yield return new WaitForFixedUpdate();
            if (IsInterrupted(hero)) break;
            if (onWall) body.linearVelocity = Vector2.zero;
            else body.linearVelocity *= Deceleration;
        }

        SetKnightLoop("SuperDash Charge", playing: false);
        SendTo(fx, chargeEffect, "DOWN");
        SendTo(fx, "SD Charge Glow", "DOWN");
        // Their faders leave them drawn after fading them out, and their looping clips' frames bring them back faintly.
        foreach (var name in chargeEffects) Draw(fx, name, false);
        Rumble(focus: false, charged: false);
        if (IsInterrupted(hero)) {
            PlayMakerFSM.BroadcastEvent("SUPERDASH CANCEL");
            Finish(hero);
            yield break;
        }

        PlayMakerFSM.BroadcastEvent("SUPERDASH READY");
        KnightClip(onWall ? "Wall Charged" : "Ground Charged");
        Shake("AverageShake");
        Activate(fx, "SD Bling", true);
        Play(fx, "SD Bling", "SD Fx Bling");
        if (!onWall) Activate(fx, "SD Sharp Flash", true);

        // Hornet faces a wall she clings to.
        var faceRight = onWall
            ? !hero.cState.facingRight
            : input.inputActions.Right.IsPressed || (hero.cState.facingRight && !input.inputActions.Left.IsPressed);
        if (faceRight) hero.FaceRight();
        else hero.FaceLeft();
        var direction = faceRight ? 1f : -1f;
        PlayFlight(hero);
        PlayMakerFSM.BroadcastEvent("SUPERDASH BLAST");
        KnightClip("Dash Start");
        KnightVibration("Dash Start");
        KnightRandomClip("Dash Start");
        Shake("SuperDashShake");
        Activate(fx, "SD Burst", true);
        Activate(fx, "SD Burst Glow", true);
        Activate(fx, "SD Trail", true);
        Play(fx, "SD Trail", "SD Trail");
        Activate(fx, "SuperDash Damage", true);
        SetKnightLoop("SuperDashing", playing: true);
        body.linearVelocity = new Vector2(direction * Speed, 0f);

        // Per frame, not per physics step: a jump or harpoon press lasts one frame.
        var hitWall = false;
        for (var time = 0f;; time += Time.deltaTime) {
            yield return null;
            if (IsInterrupted(hero)) break;
            if (hero.cState.transitioning) {
                while (!IsInterrupted(hero) && hero.cState.transitioning) yield return null;
                if (IsInterrupted(hero)) break;
                hero.RelinquishControl();
                controlVersion = SHeroController.ControlVersion;
                hero.StopAnimationControl();
                hero.AffectedByGravity(false);
                PlayFlight(hero);
                body.linearVelocity = new Vector2(direction * Speed, 0f);
                continue;
            }

            if (Mathf.Abs(body.linearVelocity.x) < Speed / 2) {
                hitWall = true;
                break;
            }

            if (time >= CancelDelay &&
                (input.inputActions.Jump.WasPressed || input.inputActions.SuperDash.WasPressed))
                break;
            body.linearVelocity = new Vector2(direction * Speed, 0f);
        }

        PlayMakerFSM.BroadcastEvent("SUPERDASH END");
        Play(fx, "SD Trail", "SD Trail End");
        Activate(fx, "SuperDash Damage", false);
        SetKnightLoop("SuperDashing", playing: false);
        if (!hitWall && !IsInterrupted(hero)) {
            KnightClip("Air Cancel");
            KnightVibration("Air Cancel");
            Activate(fx, "SD Break", true);
            Shake("EnemyKillShake");
            // The jump that stops her would be taken the moment she's let go, as a jump or, held, her float.
            hero.ResetInputQueues();
            hero.ClearJumpInputState();
            input.GetWasButtonPressedQueued(Silksong::GlobalEnums.HeroActionButton.JUMP, consume: true);
            hero.AnimCtrl.PlayClipForced(BrakeClip);
            // Sharpdart slows from 44 to about 27 through this clip; flying at 30, she keeps her speed instead.
            var brakeTime = Duration(hero, BrakeClip);
            for (var time = 0f; time < brakeTime; time += Time.fixedDeltaTime) {
                yield return new WaitForFixedUpdate();
                if (IsInterrupted(hero)) break;
            }

            // Like Sharpdart's end, she's let go at once with the speed she has left, falling through Silk Charge End.
            Finish(hero);
            if (hero && !IsInterrupted(hero)) hero.SilkChargeEnd();
            yield break;
        }

        // A scene load can replace Hornet mid-flight, destroying the body.
        if (body) body.linearVelocity = Vector2.zero;
        // Into a wall she clings to it, as from her silk charge; it gives her control back itself.
        if (hitWall && !IsInterrupted(hero)) {
            superdashing = false;
            hero.AffectedByGravity(true);
            if (hero.TryFsmCancelToWallSlide()) yield break;
        }

        Finish(hero);
    }

    private static float Duration(SHeroController hero, string clipName) =>
        hero.AnimCtrl.animator.GetClipByName(clipName) is { } clip ? clip.frames.Length / clip.fps : 0f;

    // Sharpdart's flight lasts one play of its clip; a superdash flies on until stopped, so the clip loops.
    private static void PlayFlight(SHeroController hero) {
        var animator = hero.AnimCtrl.animator;
        var clip = animator.GetClipByName(FlightClip);
        if (clip == null) return;
        if (!loopedClips.TryGetValue(clip, out var looped))
            loopedClips[clip] = looped = new tk2dSpriteAnimationClip {
                name = clip.name, fps = clip.fps, frames = clip.frames, loopStart = clip.loopStart,
                wrapMode = tk2dSpriteAnimationClip.WrapMode.LoopSection
            };
        animator.Play(looped);
    }

    // Silksong counts every RelinquishControl, also one that finds her relinquished already, such as an arena's that
    // stops her for its boss's entrance.
    private bool IsInterrupted(SHeroController hero) =>
        !hero || !HeroSwitch.HornetActive || hero.cState.dead || hero.cState.hazardDeath ||
        hero.cState.hazardRespawning || hero.cState.recoiling || SHeroController.ControlVersion != controlVersion;

    // Damage, death, transitions and arenas take Hornet over themselves.
    private void Finish(SHeroController hero) {
        superdashing = false;
        if (!hero) return;
        hero.AffectedByGravity(true);
        if (IsInterrupted(hero)) return;
        // Taking control back finds out whether she's on the ground, which picks the animation to go on with.
        hero.RegainControl();
        hero.StartAnimationControlToIdle();
    }

    // The Knight's effects stay hidden and their FSMs off while Hornet plays (KnightPresence).
    private GameObject? EnsureEffects(SHeroController hero) {
        if (effects) return effects;
        var knight = HeroController.UnsafeInstance;
        if (!knight) return null;
        effects = new GameObject(EffectsName);
        effects.transform.SetParent(hero.transform, false);
        // Placed from the Knight's feet: the crystals' floor check must reach the floor under Hornet's.
        effects.transform.localPosition = new Vector3(0f, -HeroTargetModule.FeetDelta(), 0f);
        foreach (var path in effectPaths) {
            var source = knight.transform.Find(path);
            if (!source) continue;
            var copy = Object.Instantiate(source.gameObject, effects.transform, false);
            copy.name = source.name;
            copy.transform.localPosition = knight.transform.InverseTransformPoint(source.position);
            // They're drawn around the Knight's flying body, which sits lower over his feet than her zap's.
            if (Array.IndexOf(flightEffects, copy.name) >= 0)
                copy.transform.localPosition += Vector3.up * FlightEffectsRise;
            foreach (var fsm in copy.GetComponentsInChildren<PlayMakerFSM>(true)) fsm.enabled = true;
            // The charge effects' faders draw them from UP on. Drawn earlier, the one a superdash doesn't charge with
            // loops its default clip, whose frames undo the fader's fade.
            Draw(effects, copy.name, Array.IndexOf(chargeEffects, copy.name) < 0);
        }

        return effects;
    }

    private static void Draw(GameObject? fx, string child, bool drawn) {
        var effect = fx ? fx.transform.Find(child) : null;
        if (!effect) return;
        foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true)) renderer.enabled = drawn;
    }

    private static void Shake(string shakeEvent) {
        var shake = GameCameras.instance ? GameCameras.instance.cameraShakeFSM : null;
        if (shake) shake.SendEvent(shakeEvent);
    }

    private static void Rumble(bool focus, bool charged) {
        var shake = GameCameras.instance ? GameCameras.instance.cameraShakeFSM : null;
        if (!shake) return;
        shake.FsmVariables.GetFsmBool("RumblingFocus").Value = focus;
        shake.FsmVariables.GetFsmBool("RumblingFocus2").Value = charged;
    }

    private static void SendTo(GameObject? fx, string child, string fsmEvent) {
        var effect = fx ? fx.transform.Find(child) : null;
        if (!effect) return;
        foreach (var fsm in effect.GetComponents<PlayMakerFSM>()) fsm.SendEvent(fsmEvent);
    }

    private static void Play(GameObject? fx, string child, string clip) {
        var effect = fx ? fx.transform.Find(child) : null;
        var animator = effect ? effect.GetComponent<tk2dSpriteAnimator>() : null;
        if (animator) animator.Play(clip);
    }

    // An animator plays its clip on its first start only.
    private static void Activate(GameObject? fx, string child, bool active) {
        var effect = fx ? fx.transform.Find(child) : null;
        if (!effect) return;
        effect.gameObject.SetActive(active);
        var animator = effect.GetComponent<tk2dSpriteAnimator>();
        if (active && animator) animator.Play();
    }

    // The one-shot sounds of the Knight's superdash live only in his Superdash FSM's states. The Knight's own audio
    // source stays silent while Hornet plays, so they go through his superdash sound's.
    private static void KnightClip(string state) {
        var clip = KnightState(state)?.Actions.OfType<AudioPlay>()
            .Select(action => action.oneShotClip.Value as AudioClip).FirstOrDefault(c => c);
        var source = KnightSounds("SuperDash Charge")?.GetComponent<AudioSource>();
        if (clip && source) source.PlayOneShot(clip);
    }

    private static void KnightRandomClip(string state) {
        var clips = KnightState(state)?.Actions.OfType<AudioPlayRandom>().FirstOrDefault()?.audioClips;
        var source = KnightSounds("SuperDash Charge")?.GetComponent<AudioSource>();
        if (clips is not { Length: > 0 } || !source) return;
        var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (clip) source.PlayOneShot(clip);
    }

    // The superdash's one-shot rumbles live in the same states.
    private static void KnightVibration(string state) {
        if (KnightState(state)?.Actions.OfType<PlayVibration>().FirstOrDefault() is not { } vibration) return;
        VibrationManager.PlayVibrationClipOneShot(VibrationData.Create(
            (LowFidelityVibrations)vibration.lowFidelityVibration.Value,
            vibration.highFidelityVibration.Value as TextAsset, vibration.gamepadVibration.Value as GamepadVibration));
    }

    private static FsmState? KnightState(string state) {
        var knight = HeroController.UnsafeInstance;
        var fsm = knight ? knight.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Superdash") : null;
        return fsm ? fsm.Fsm.GetState(state) : null;
    }

    // His looping charge and flight sounds each carry the rumble that goes with them.
    private static void SetKnightLoop(string child, bool playing) {
        var sounds = KnightSounds(child);
        if (!sounds) return;
        var source = sounds.GetComponent<AudioSource>();
        var vibration = sounds.GetComponent<VibrationPlayer>();
        if (playing) {
            if (source) source.Play();
            if (vibration) vibration.Play();
        }
        else {
            if (source) source.Stop();
            if (vibration) vibration.Stop();
        }
    }

    private static Transform? KnightSounds(string child) {
        var knight = HeroController.UnsafeInstance;
        return knight ? knight.transform.Find("Sounds/" + child) : null;
    }
}
