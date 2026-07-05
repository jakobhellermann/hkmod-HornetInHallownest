extern alias Silksong;
extern alias SilksongPM;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetPlayer.Playground;

// Make HK boss roars lock Hornet, reusing her own lock behavior — no per-boss logic.
//
// HK bosses roar-lock the hero two ways (see the "Roar Lock" FSM on the Knight): (a) the roar wave carries a
// "Roar"-tagged trigger that the hero's "Roar Lock" FSM self-detects (Trigger2dEvent), and (b) SetFsmGameObject(Hero,
// fsmName="Roar Lock", "Roar Object"=self) hands it the push source. Hornet has no "Roar Lock" FSM (so (b) just warns,
// harmless); her equivalent is the "Roar and Wound States" FSM — but it locks on a *pushed* event "ROAR ENTER" (+ a
// "Roar Wave Emitter" var for push direction) rather than self-detecting the collider. So HK roars never reach her and
// she can act through them.
//
// Bridge: a receiver on Hornet_Real (its body BoxCollider2D is layer 9/Player — exactly what HK's Knight uses and what
// the roar wave collides with; her hurtbox is a separate layer-20 child, so we add NO collider and don't perturb
// enemy-attack detection) that relays Silksong's own events to her FSM: ROAR ENTER (+ set Roar Wave Emitter) when a
// "Roar" trigger enters, ROAR EXIT when it leaves -> her FSM's Regain Control. While the Knight is active Hornet's
// Rigidbody2D is unsimulated, so the receiver is naturally silent.
internal sealed class RoarLockReceiver : MonoBehaviour {
    private const string RoarTag = "Roar";
    private const string FsmName = "Roar and Wound States";
    private SilksongPM::PlayMakerFSM? fsm;

    private SilksongPM::PlayMakerFSM? Fsm {
        get {
            if (fsm == null)
                foreach (var f in GetComponents<SilksongPM::PlayMakerFSM>())
                    if (f.FsmName == FsmName) {
                        fsm = f;
                        break;
                    }

            return fsm;
        }
    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (!other.CompareTag(RoarTag)) return;
        var f = Fsm;
        if (f == null) return;
        // Push-direction source (Silksong's Roar Lock Start reads it via CheckTargetDirection; falls back to its own
        // "Roar Wave Emitter Main" if unset, so this only refines the flip/shove).
        var emitter = f.FsmVariables.GetFsmGameObject("Roar Wave Emitter");
        if (emitter != null) emitter.Value = other.gameObject;
        f.SendEvent("ROAR ENTER");
    }

    private void OnTriggerExit2D(Collider2D other) {
        if (!other.CompareTag(RoarTag)) return;
        Fsm?.SendEvent("ROAR EXIT");
    }
}

internal static class RoarLockBridge {
    // Attach once when Hornet is spawned (fresh GO each spawn). The receiver is passive (OnTriggerEnter/Exit2D) — no
    // per-frame work — and stays silent while the Knight is active (Hornet's Rigidbody2D is then unsimulated).
    internal static void Attach(Silksong::HeroController hero) {
        hero.gameObject.AddComponent<RoarLockReceiver>();
    }

    internal static void Cleanup() {
        // Our component type identity changes on a hot-reload; strip stale receivers so the next Tick re-adds a fresh one.
        foreach (var r in Resources.FindObjectsOfTypeAll<RoarLockReceiver>())
            Object.Destroy(r);
    }
}
