extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections;
using System.Linq;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using HutongGames.PlayMaker.Actions;
using UnityEngine;
using Object = UnityEngine.Object;
using SHeroController = Silksong::HeroController;
using SInputHandler = Silksong::InputHandler;

namespace HornetInHallownest.Modules;

public sealed class CrystalHeartModule : ModuleBase {
    private const float ChargeTime = 0.8f;
    private const float Speed = 30f;
    private const float CancelDelay = 0.2f;
    private const float WallBonkSpeed = 11.75f;
    private const float AirBrakeTime = 0.54f;
    private const float AirBrakeDeceleration = 0.9f;
    private const string GroundChargeClip = "Abyss Kneel";
    private const string AirBrakeClip = "Harpoon Catch";
    private const string WallChargeClip = "Wall Cling";
    private const string FlightClip = "Harpoon Dash";
    private const string EffectsName = "Crystal Heart Effects";

    private static readonly string[] effectPaths = [
        "Effects/SD Charge", "Effects/SD Charge Wall", "Effects/SD Charge Glow", "Effects/SD Bling",
        "Effects/SD Sharp Flash", "Effects/SD Energy", "Effects/SD Burst", "Effects/SD Burst Glow", "Effects/SD Trail",
        "Effects/SD Pulse Pt", "Effects/SD Crystal Gen G1", "Effects/SD Crystal Gen G2", "Effects/SD Crystal Gen W1",
        "Effects/SD Crystal Gen W2", "Effects/SD Crystal Burst GL", "Effects/SD Crystal Burst GR",
        "Effects/SD Crystal Burst W", "SuperDash Damage"
    ];

    private static readonly string[] chargeEffects = ["SD Charge", "SD Charge Wall"];

    private bool superdashing;
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
        if (!superdashing && CanStart(self)) StartCoroutine(Superdash(self, Input(self)));
        orig(self);
    }

    private bool OnCanHarpoonDash(Func<SHeroController, bool> orig, SHeroController self) =>
        !superdashing && orig(self);

    private static bool CanStart(SHeroController hero) {
        if (!HeroSwitch.HornetActive || hero != HornetSpawner.Hornet || !PlayerData.instance.hasSuperDash) return false;
        var actions = Input(hero).inputActions;
        return actions.SuperDash.WasPressed && actions.Up.IsPressed && !actions.Down.IsPressed &&
               (hero.cState.onGround || hero.cState.wallSliding || hero.cState.wallClinging) && hero.CanHarpoonDash();
    }

    private static SInputHandler Input(SHeroController hero) => hero.GetFieldValue<SInputHandler>("inputHandler")!;

    private IEnumerator Superdash(SHeroController hero, SInputHandler input) {
        superdashing = true;
        var body = hero.GetComponent<Rigidbody2D>();
        var onWall = !hero.cState.onGround;
        hero.RelinquishControl();
        hero.StopAnimationControl();
        hero.AffectedByGravity(false);
        body.linearVelocity = Vector2.zero;
        hero.AnimCtrl.PlayClipForced(onWall ? WallChargeClip : GroundChargeClip);
        var fx = EnsureEffects(hero);
        var chargeEffect = onWall ? "SD Charge Wall" : "SD Charge";
        PlayMakerFSM.BroadcastEvent(onWall ? "SUPERDASH CHARGING W" : "SUPERDASH CHARGING G");
        SendTo(fx, chargeEffect, "UP");
        SendTo(fx, "SD Charge Glow", "UP");
        Play(fx, chargeEffect, "SD Fx Charge");
        KnightSound("SuperDash Charge", play: true);
        Rumble(focus: true, charged: false);
        Shake("FocusRumble");

        var charged = false;
        for (var time = 0f; input.inputActions.SuperDash.IsPressed && !IsInterrupted(hero); time += Time.deltaTime) {
            if (!charged && time >= ChargeTime) {
                charged = true;
                PlayMakerFSM.BroadcastEvent("SUPERDASH READY");
                KnightClip(onWall ? "Wall Charged" : "Ground Charged");
                Rumble(focus: false, charged: true);
                Shake("AverageShake");
                Activate(fx, "SD Bling", true);
                Play(fx, "SD Bling", "SD Fx Bling");
                if (!onWall) Activate(fx, "SD Sharp Flash", true);
            }

            body.linearVelocity = Vector2.zero;
            yield return null;
        }

        KnightSound("SuperDash Charge", play: false);
        SendTo(fx, chargeEffect, "DOWN");
        SendTo(fx, "SD Charge Glow", "DOWN");
        Rumble(focus: false, charged: false);
        if (!charged || IsInterrupted(hero)) {
            PlayMakerFSM.BroadcastEvent("SUPERDASH CANCEL");
            Finish(hero);
            yield break;
        }

        // Hornet faces a wall she clings to.
        var faceRight = onWall
            ? !hero.cState.facingRight
            : input.inputActions.Right.IsPressed || (hero.cState.facingRight && !input.inputActions.Left.IsPressed);
        if (faceRight) hero.FaceRight();
        else hero.FaceLeft();
        var direction = faceRight ? 1f : -1f;
        hero.AnimCtrl.PlayClipForced(FlightClip);
        PlayMakerFSM.BroadcastEvent("SUPERDASH BLAST");
        KnightClip("Dash Start");
        KnightRandomClip("Dash Start");
        Shake("SuperDashShake");
        Activate(fx, "SD Burst", true);
        Activate(fx, "SD Burst Glow", true);
        Activate(fx, "SD Trail", true);
        Play(fx, "SD Trail", "SD Trail");
        Activate(fx, "SuperDash Damage", true);
        KnightSound("SuperDashing", play: true);
        body.linearVelocity = new Vector2(direction * Speed, 0f);

        // Per frame, not per physics step: a jump or harpoon press lasts one frame.
        var hitWall = false;
        for (var time = 0f; !IsInterrupted(hero); time += Time.deltaTime) {
            yield return null;
            if (hero.cState.transitioning) {
                while (!IsInterrupted(hero) && hero.cState.transitioning) yield return null;
                if (IsInterrupted(hero)) break;
                hero.RelinquishControl();
                hero.StopAnimationControl();
                hero.AffectedByGravity(false);
                hero.AnimCtrl.PlayClipForced(FlightClip);
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
        KnightSound("SuperDashing", play: false);
        if (!hitWall && !IsInterrupted(hero)) {
            KnightClip("Air Cancel");
            Activate(fx, "SD Break", true);
            Shake("EnemyKillShake");
            hero.AnimCtrl.PlayClipForced(AirBrakeClip);
            for (var time = 0f; time < AirBrakeTime && !IsInterrupted(hero); time += Time.fixedDeltaTime) {
                yield return new WaitForFixedUpdate();
                body.linearVelocity *= AirBrakeDeceleration;
            }
        }

        body.linearVelocity = Vector2.zero;
        if (hitWall && !IsInterrupted(hero)) {
            superdashing = false;
            hero.AffectedByGravity(true);
            Bonk(hero, direction);
            yield break;
        }

        Finish(hero);
    }

    // Her sprint's own wall bonk gives her control back after it.
    private static void Bonk(SHeroController hero, float direction) {
        var sprint = hero.GetComponents<SilksongPM::PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Sprint");
        if (!sprint) {
            hero.StartAnimationControl();
            hero.RegainControl();
            return;
        }

        sprint.FsmVariables.GetFsmFloat("Sprint Speed Crt").Value = direction * WallBonkSpeed;
        sprint.SetState("Run To WallBonk");
    }

    private static bool IsInterrupted(SHeroController hero) =>
        !hero || !HeroSwitch.HornetActive || hero.cState.dead || hero.cState.hazardDeath ||
        hero.cState.hazardRespawning || hero.cState.recoiling;

    // Damage, death and transitions take Hornet over themselves.
    private void Finish(SHeroController hero) {
        superdashing = false;
        if (!hero) return;
        hero.AffectedByGravity(true);
        if (IsInterrupted(hero)) return;
        hero.StartAnimationControl();
        hero.RegainControl();
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
            foreach (var fsm in copy.GetComponentsInChildren<PlayMakerFSM>(true)) fsm.enabled = true;
            // The charge effects' faders draw them from UP on. Drawn earlier, the one a superdash doesn't charge with
            // loops its default clip, whose frames undo the fader's fade.
            var drawn = Array.IndexOf(chargeEffects, copy.name) < 0;
            foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = drawn;
        }

        return effects;
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
        var knight = HeroController.UnsafeInstance;
        var fsm = knight ? knight.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Superdash") : null;
        var clip = fsm
            ? fsm.Fsm.GetState(state)?.Actions.OfType<AudioPlay>()
                .Select(action => action.oneShotClip.Value as AudioClip).FirstOrDefault(c => c)
            : null;
        var source = knight ? knight.transform.Find("Sounds/SuperDash Charge")?.GetComponent<AudioSource>() : null;
        if (clip && source) source.PlayOneShot(clip);
    }

    private static void KnightRandomClip(string state) {
        var knight = HeroController.UnsafeInstance;
        var fsm = knight ? knight.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Superdash") : null;
        var clips = fsm
            ? fsm.Fsm.GetState(state)?.Actions.OfType<AudioPlayRandom>().FirstOrDefault()?.audioClips
            : null;
        var source = knight ? knight.transform.Find("Sounds/SuperDash Charge")?.GetComponent<AudioSource>() : null;
        if (clips is not { Length: > 0 } || !source) return;
        var clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (clip) source.PlayOneShot(clip);
    }

    private static void KnightSound(string child, bool play) {
        var knight = HeroController.UnsafeInstance;
        var source = knight ? knight.transform.Find("Sounds/" + child)?.GetComponent<AudioSource>() : null;
        if (!source) return;
        if (play) source.Play();
        else source.Stop();
    }
}
