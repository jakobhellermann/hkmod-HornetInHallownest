extern alias Silksong;
using UnityEngine;
using SHeroController = Silksong::HeroController;

namespace HornetPlayer.Playground;

// Make Hornet sit when the player rests on an HK bench.
//
// HK's bench machinery already runs (its "Bench Control" FSM moves the inert Knight onto the bench, sets the respawn
// point via RespawnTrigger, heals HK's PlayerData, saves) — but it's Knight-tied, so Hornet just stands beside the
// resting Knight and her Silksong HP isn't healed. Same seam as death: HK drives, Hornet mirrors. The clean signal is
// HK's `PlayerData.atBench` (set true by HK's Bench Control FSM while resting); we mirror its edges onto Hornet:
//   enter -> snap to the bench (Knight's spot), heal her Silksong HP, relinquish control + gravity off + stop HAC, play
//            her real sit clips ("Sit" -> "Sit Idle").
//   exit  -> StartAnimationControlToIdle + gravity back + RegainControl (she stands up).
// Respawn-point/heal/save on the HK side already happen; this only adds Hornet's sit visual + her own heal.
internal sealed class HornetBench : MonoBehaviour {
    private static GameObject? go;
    private bool sitting;

    private void Update() {
        var hero = BundleSpike.RealHero;
        if (hero == null) {
            sitting = false;
            return;
        }

        var knight = HeroController.UnsafeInstance;
        var pd = PlayerData.instance; // HK's — atBench is HK's flag
        var resting = pd != null && pd.atBench && HeroSwitch.HornetActive;

        if (resting && !sitting) EnterSit(hero);
        else if (!resting && sitting) ExitSit(hero);

        if (knight == null || !HeroSwitch.HornetActive) return;

        if (sitting) {
            // Track the Knight as HK's bench FSM slides it the last bit onto the seat (over several frames after atBench
            // flips), so Hornet ends up exactly where it settles.
            hero.transform.position = knight.transform.position;
        }
        else if (knight.cState != null && knight.cState.nearBench) {
            // Hornet is standing on a bench trigger — HK's RestBench set the KNIGHT's nearBench (it calls
            // HeroController.instance.NearBench on any layer-9 collider). Glue the far, inert Knight onto her NOW, BEFORE
            // rest is chosen: otherwise HK's bench FSM slides it onto the seat from across the room and the camera flashes
            // to that far slide. Pre-positioned here, the slide is a short local hop with nothing to flash to.
            knight.transform.position = hero.transform.position;
        }
    }

    private void EnterSit(SHeroController hero) {
        sitting = true;

        // The Knight is already glued to Hornet here (the nearBench pre-position in Update ran while she walked onto the
        // bench), so no enter-time teleport is needed. Just kill carried velocity so she doesn't drift while seated.
        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        hero.RelinquishControl(); // no movement/abilities while seated
        hero.AffectedByGravity(false);
        hero.StopAnimationControl(); // stop HAC so it doesn't override the sit clips with Idle/locomotion

        // Bench heals — HK's heal only touched HK's PlayerData, so heal her Silksong HP too.
        hero.MaxHealth();

        // Her real sit anim: "Sit" (sit-down) then loop "Sit Idle". tk2d AnimationCompleted chains the loop.
        var anim = hero.AnimCtrl?.animator;
        if (anim != null) {
            anim.Play("Sit");
            anim.AnimationCompleted = (a, _) => a.Play("Sit Idle");
        }

        Log.Info($"[HornetBench] Hornet sits (atBench) at {(Vector2)hero.transform.position}, healed");
    }

    private void ExitSit(SHeroController hero) {
        sitting = false;
        var anim = hero.AnimCtrl?.animator;
        if (anim != null) anim.AnimationCompleted = null; // drop the Sit-Idle chaining
        hero.StartAnimationControlToIdle(); // resume HAC -> stand in Idle
        hero.AffectedByGravity(true);
        hero.RegainControl();
        Log.Info("[HornetBench] Hornet stands up (left bench)");
    }

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.HornetBench");
        go.AddComponent<HornetBench>();
        DontDestroyOnLoad(go);
    }

    internal static void Cleanup() {
        if (go != null) {
            Destroy(go);
            go = null;
        }
    }
}
