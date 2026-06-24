using System.Collections;

namespace HornetPlayer.HornetInHallownest.Validation;

// A runtime validation scenario: setup -> act -> assert, expressed as a coroutine so it can span frames (wait for a
// spawn, drive input over time, let physics settle). The runner captures Unity Exceptions/Errors for the whole
// duration of Run, so a scenario fails on ANY engine-level error in its window even without asserting it explicitly
// (the zero-error policy, enforced in-process — no Player.log parsing, no restart).
public interface IScenario {
    string Name { get; }
    IEnumerator Run(ScenarioContext ctx);
}
