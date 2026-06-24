extern alias Silksong;
using System.Collections;
using HornetPlayer.HornetInHallownest.Modules;

namespace HornetPlayer.HornetInHallownest.Validation.Scenarios;

// Drives a fresh despawn -> respawn so the whole spawn (HUD/TMP/Resources.Load + per-frame settle) runs INSIDE the
// watched window — the runner fails the scenario on any engine error (Application.logMessageReceived) or mod error
// (Log.Error), so spawn-time issues like a Resources.Load outside a SilksongContext surface here. Template for the
// rest: richer scenarios (drive movement/dash/attack, hazard death -> bench) build on this shape.
public sealed class SpawnSanityScenario : IScenario {
    public string Name => "spawn-sanity";

    public IEnumerator Run(ScenarioContext ctx) {
        HornetSpawner.Despawn();
        yield return ctx.WaitFrames(1);
        HornetSpawner.Spawn();

        var hero = HornetSpawner.RealHero;
        ctx.Assert(hero, "RealHero is null after spawn");
        if (!hero) yield break;

        ctx.Assert(!hero.cState.dead, "Hornet spawned dead");

        // Let the per-frame loop settle so latent errors surface, then confirm she's still there.
        yield return ctx.WaitSeconds(1f);
        ctx.Assert(HornetSpawner.RealHero, "Hornet vanished after spawn");
    }
}
