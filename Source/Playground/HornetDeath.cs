extern alias Silksong;
using System;
using System.Collections;
using System.Reflection;
using MonoMod.RuntimeDetour;
using UnityEngine;
using SHeroController = Silksong::HeroController;
using SHeroBox = Silksong::HeroBox;
using SGameManager = Silksong::GameManager;
using SActorStates = Silksong::GlobalEnums.ActorStates;

namespace HornetPlayer.Playground;

// What happens when Hornet dies: route her death onto HK's bench-respawn machinery.
//
// Hornet runs Silksong's real death (HeroController.Die), but its tail — StartCoroutine(gm.PlayerDead(...)) — targets our
// INACTIVE bootstrap GameManager (no SetupGameRefs), so Silksong's respawn can't run: "Coroutine couldn't be started
// because 'Silksong_GameManager' is inactive". And Silksong's respawn would drive its own (absent) scenes anyway. This is
// a Hornet-in-HK-world mod, so the WORLD owns respawn: HK knows the last bench (playerData.respawnScene/respawnMarkerName,
// set when the Knight sat on it). We hand the death off to HK's GameManager.PlayerDead — its native fade + save + scene
// transition to the bench, respawning HK's Knight there — then snap Hornet onto the repositioned Knight and revive her.
//
// ContactDamageBridge tags fatal hits NonLethal so Die() takes the nonLethal branch (skips the corpse/cocoon block that
// NullRefs on our null gm.tilemap/gm.gameMap). Real corpse/cocoon = a later feature; this is the "die -> back to bench"
// path. Death detection is on the cState.dead rising edge (robust to which Die overload ran).
internal sealed class HornetDeath : MonoBehaviour {
    private static GameObject? go;
    private static FieldInfo? rendererField;
    private static MethodInfo? setStateMethod;
    private static Hook? playerDeadHook;

    // Death wait before HK fades out — lets the Silksong death-prefab animation play, mirrors HK's DEATH_WAIT (2.85s).
    private const float DeathWait = 2.5f;

    private bool handling;
    private bool wasDead;

    private void Update() {
        var hero = BundleSpike.RealHero;
        if (hero == null) {
            wasDead = false;
            return;
        }

        var dead = hero.cState.dead;
        // Rising edge while Hornet is the active hero and we're not already running the respawn. (If the Knight is active
        // when Hornet "dies" she's an inert prop and ContactDamageBridge wouldn't have hurt her anyway.)
        if (dead && !wasDead && !handling && HeroSwitch.HornetActive) {
            handling = true;
            Log.Info("[HornetDeath] Hornet died -> handing off to HK bench respawn");
            StartCoroutine(DeathRoutine(hero));
        }

        wasDead = dead;
    }

    private IEnumerator DeathRoutine(SHeroController hero) {
        var hkGm = GameManager.UnsafeInstance;
        if (hkGm == null) {
            Log.Error("[HornetDeath] no HK GameManager — can't respawn");
            handling = false;
            yield break;
        }

        // Run HK's full death sequence inline: FreezeInPlace -> SaveGame -> wait (death anim plays) -> FadeOut(HERO_DEATH)
        // -> ReadyForRespawn -> BeginSceneTransition(respawnScene). Driving it from our (DontDestroyOnLoad) coroutine is
        // fine — PlayerDead's body only references gm's own fields. On return the scene transition has been kicked and
        // RespawningHero is set.
        yield return hkGm.PlayerDead(DeathWait);

        // Wait for HK to finish: EnterHero consumes RespawningHero and runs the Knight's Respawn(), which places it at the
        // bench marker (isHeroInPosition flips true at SendHeroInPosition).
        while (hkGm.RespawningHero) yield return null;
        var knight = HeroController.UnsafeInstance;
        while (knight == null || !knight.isHeroInPosition) {
            yield return null;
            knight = HeroController.UnsafeInstance;
        }

        Revive(hero, knight);
        handling = false;
        wasDead = false;
    }

    // Bring Hornet back to life at the Knight's bench position. Mirrors the essential state-restores of Silksong's
    // HeroController.Respawn (cState.dead clear, renderer/physics/gravity on, MaxHealth) without its scene-entry handshake
    // (SendHeroInPosition / SilkSpool / bench FSM) — HK already owns the scene + camera here.
    private static void Revive(SHeroController hero, HeroController knight) {
        // Position: land where HK placed the Knight (the bench). CameraSwitchDriver also snaps on scene change, but a
        // same-scene respawn doesn't trip its scene-name check, so do it here unconditionally.
        hero.transform.position = knight.transform.position;
        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) {
            rb.isKinematic = false;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
        }

        var cs = hero.cState;
        cs.dead = false;
        cs.hazardDeath = false;
        cs.recoiling = false;
        cs.falling = false;

        SHeroBox.Inactive = false; // Die set the global hero-box gate off; re-arm it
        if (hero.heroBox != null) hero.heroBox.HeroBoxNormal();

        rendererField ??= typeof(SHeroController)
            .GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic);
        if (rendererField?.GetValue(hero) is MeshRenderer mr) mr.enabled = true;

        hero.gameObject.layer = 9; // Die moved her to layer 2 (no-collision)
        hero.AffectedByGravity(true);
        hero.MaxHealth();

        // Out of no_input (Die set it) back to a normal, input-accepting state.
        setStateMethod ??= typeof(SHeroController).GetMethod("SetState",
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(SActorStates)], null);
        setStateMethod?.Invoke(hero, [SActorStates.idle]);
        hero.RegainControl();

        // Re-apply the active-hero split: HK's Respawn woke the (inert) Knight (renderer/control/physics), so re-inert it
        // and re-assert Hornet active. who==prev so this skips the position handoff, just re-runs SetInert on both.
        HeroSwitch.SetActive(ActiveHero.Hornet);

        Log.Info($"[HornetDeath] revived Hornet at {(Vector2)hero.transform.position} (bench respawn complete)");
    }

    private static IEnumerator Empty() {
        yield break;
    }

    // Debug: kill Hornet on demand (POST /kill) via the real damage path (NonLethal, like ContactDamageBridge), so the
    // death -> HK bench respawn sequence can be reproduced without dying to an enemy.
    internal static object Kill() {
        var hc = BundleSpike.RealHero;
        if (hc == null) return new { error = "no Hornet spawned" };
        var pd = Silksong::PlayerData.instance;
        if (pd != null) {
            pd.isInvincible = false;
            pd.health = 1;
        }

        hc.TakeDamage(hc.gameObject, Silksong::GlobalEnums.CollisionSide.left, 1,
            Silksong::GlobalEnums.HazardType.ENEMY, Silksong::GlobalEnums.DamagePropertyFlags.NonLethal);
        return new { ok = true, health = pd != null ? pd.health : -1 };
    }

    internal static void Install() {
        if (go != null) return;
        go = new GameObject("HornetPlayer.HornetDeath");
        go.AddComponent<HornetDeath>();
        DontDestroyOnLoad(go);

        // Neutralize Silksong's own respawn handoff. Die()'s tail does StartCoroutine(gm.PlayerDead(deathWait)) — started
        // on the ACTIVE Hornet, so it actually runs Silksong's PlayerDead against our inactive bootstrap GM and NullRefs
        // in CameraController.FreezeInPlace (bare controller, no rig), plus drips "Coroutine couldn't be started" from its
        // internal gm.StartCoroutine calls. We replaced respawn with HK's bench path (DeathRoutine), so Silksong's
        // PlayerDead is pure noise. Skip can't no-op it (it returns IEnumerator -> default null -> StartCoroutine(null)
        // throws), so hook it to an empty coroutine. PlayerDeadFromHazard isn't hooked: fatal hits always route through
        // Die (the health==0 check precedes the hazard switch), so PlayerDead is the only handoff in play.
        var pdm = typeof(SGameManager).GetMethod("PlayerDead",
            BindingFlags.Public | BindingFlags.Instance, null, [typeof(float)], null);
        if (pdm != null)
            playerDeadHook = new Hook(pdm,
                (Func<Func<SGameManager, float, IEnumerator>, SGameManager, float, IEnumerator>)
                ((_, _, _) => Empty()));
        else
            Log.Error("[HornetDeath] Silksong GameManager.PlayerDead(float) not found");
    }

    internal static void Cleanup() {
        playerDeadHook?.Dispose();
        playerDeadHook = null;
        if (go != null) {
            Destroy(go);
            go = null;
        }
    }
}
