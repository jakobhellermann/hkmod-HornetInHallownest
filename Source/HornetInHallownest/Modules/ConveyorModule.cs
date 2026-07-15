extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules;

// HK conveyor belts gate on GetComponent<HK.HeroController>, which Hornet lacks.
// Manually set her state on conveyor enter/exit.
public sealed class ConveyorModule : ModuleBase {
    public override string Id => "conveyor";

    public override void Initialize() {
        Detour(typeof(ConveyorBelt), "OnCollisionEnter2D", OnEnter);
        Detour(typeof(ConveyorBelt), "OnCollisionExit2D", OnExit);
    }

    private static void OnEnter(Action<ConveyorBelt, Collision2D> orig, ConveyorBelt self, Collision2D collision) {
        orig(self, collision);
        if (!HeroSwitch.HornetActive) return;
        if (!collision.gameObject.TryGetComponent<Silksong::HeroController>(out var hero)) return;
        
        if (self.vertical) {
            collision.gameObject.GetComponent<Silksong::ConveyorMovementHero>().StartConveyorMove(0f, self.speed);
            hero.cState.onConveyorV = true;
        } else {
            hero.conveyorSpeed = self.speed;
            hero.cState.onConveyor = true;
        }
    }

    private static void OnExit(Action<ConveyorBelt, Collision2D> orig, ConveyorBelt self, Collision2D collision) {
        orig(self, collision);
        if (!collision.gameObject.TryGetComponent<Silksong::HeroController>(out var hero)) return;
        
        collision.gameObject.GetComponent<Silksong::ConveyorMovementHero>().StopConveyorMove();
        hero.conveyorSpeed = 0f;
        hero.cState.onConveyor = false;
        hero.cState.onConveyorV = false;
    }
}
