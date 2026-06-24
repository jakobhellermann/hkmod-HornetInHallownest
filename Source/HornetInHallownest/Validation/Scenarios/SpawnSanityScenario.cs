extern alias Silksong;
using System.Collections;
using HornetPlayer.HornetInHallownest.Modules;

namespace HornetPlayer.HornetInHallownest.Validation.Scenarios;

// First scenario + template for the rest: assert Hornet is spawned and in a sane state, and that no engine error
// fires during a short observation window (the runner's Application.logMessageReceived watcher provides the
// zero-error half). Deliberately minimal — proves the validation harness end-to-end (route -> coroutine -> assert ->
// verdict). Richer scenarios (drive movement/dash/attack, hazard death -> bench) build on this shape.
public sealed class SpawnSanityScenario : IScenario {
    public string Name => "spawn-sanity";

    public IEnumerator Run(ScenarioContext ctx) {
        var hero = HornetSpawner.RealHero;
        ctx.Assert(hero, "RealHero is null (Hornet not spawned)");
        if (!hero) yield break;

        ctx.Assert(!hero.cState.dead, "Hornet starts dead");

        // Observe a window for engine errors / latent NullRefs surfacing from the per-frame loop.
        yield return ctx.WaitSeconds(1.5f);

        // Still alive and present after the window (a crash/destroy would null this out).
        ctx.Assert(HornetSpawner.RealHero, "Hornet vanished during observation window");
    }
}
