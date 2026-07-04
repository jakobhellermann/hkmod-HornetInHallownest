extern alias Silksong;
using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace HornetPlayer.Playground;

// HK conveyor belts (ConveyorBelt.OnCollisionEnter2D) move the hero by calling HeroController.SetConveyorSpeed /
// ConveyorMovementHero.StartConveyorMove — but they gate on GetComponent<HK.HeroController>(), which Hornet's GO lacks
// (she carries Silksong's HeroController, a different type), so the belt's hero branch skips her entirely. Hook the belt
// and, for Hornet, drive Silksong's own conveyor machinery: she has the matching `conveyorSpeed` field + cState.onConveyor
// (her HeroController FixedUpdate adds conveyorSpeed to velocity while onConveyor+onGround) and a ConveyorMovementHero
// for the vertical case. orig() is a no-op for her (its GetComponent<HK.HeroController> is null), so no double-apply.
internal static class ConveyorBridge {
    private static Hook? enterHook;
    private static Hook? exitHook;

    internal static void Install() {
        var enter = typeof(ConveyorBelt).GetMethod("OnCollisionEnter2D", BindingFlags.Instance | BindingFlags.NonPublic);
        var exit = typeof(ConveyorBelt).GetMethod("OnCollisionExit2D", BindingFlags.Instance | BindingFlags.NonPublic);
        if (enter == null || exit == null) {
            Log.Error("[Conveyor] ConveyorBelt.OnCollision{Enter,Exit}2D not found");
            return;
        }

        enterHook = new Hook(enter, (Action<Action<ConveyorBelt, Collision2D>, ConveyorBelt, Collision2D>)OnEnter);
        exitHook = new Hook(exit, (Action<Action<ConveyorBelt, Collision2D>, ConveyorBelt, Collision2D>)OnExit);
        Log.Debug("[Conveyor] installed: ConveyorBelt -> Hornet");
    }

    private static void OnEnter(Action<ConveyorBelt, Collision2D> orig, ConveyorBelt self, Collision2D collision) {
        orig(self, collision);
        if (!HeroSwitch.HornetActive) return;
        var hero = collision.gameObject.GetComponent<Silksong::HeroController>();
        if (hero == null) return;
        if (self.vertical) {
            collision.gameObject.GetComponent<Silksong::ConveyorMovementHero>()?.StartConveyorMove(0f, self.speed);
            hero.cState.onConveyorV = true;
        }
        else {
            hero.conveyorSpeed = self.speed;
            hero.cState.onConveyor = true;
        }
    }

    private static void OnExit(Action<ConveyorBelt, Collision2D> orig, ConveyorBelt self, Collision2D collision) {
        orig(self, collision);
        var hero = collision.gameObject.GetComponent<Silksong::HeroController>();
        if (hero == null) return;
        collision.gameObject.GetComponent<Silksong::ConveyorMovementHero>()?.StopConveyorMove();
        hero.conveyorSpeed = 0f;
        hero.cState.onConveyor = false;
        hero.cState.onConveyorV = false;
    }

    internal static void Cleanup() {
        enterHook?.Dispose();
        enterHook = null;
        exitHook?.Dispose();
        exitHook = null;
    }
}
