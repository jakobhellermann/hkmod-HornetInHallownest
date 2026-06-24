extern alias Silksong;
using System;
using System.Collections;
using System.Reflection;
using GlobalEnums;
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
    // Death wait before HK fades out — lets the Silksong death-prefab animation play, mirrors HK's DEATH_WAIT (2.85s).
    private const float DeathWait = 2.5f;
    private static GameObject? go;
    private static FieldInfo? rendererField;
    private static MethodInfo? setStateMethod;
    private static Hook? playerDeadHook;
    private static Hook? takeDamageHook;

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
            StartCoroutine(DeathRoutine());
        }

        wasDead = dead;
    }

    private IEnumerator DeathRoutine() {
        var hkGm = GameManager.UnsafeInstance;
        // try/finally (no catch — yield is legal here) guarantees `handling` is ALWAYS cleared, even if a wait below
        // times out or Revive throws. A stuck DeathRoutine that never cleared `handling` would silently break EVERY
        // later death (the rising-edge gate sees handling==true forever) — exactly the "she just stays dead" regression.
        try {
            if (hkGm == null) {
                Log.Error("[HornetDeath] no HK GameManager — can't respawn");
                yield break;
            }

            // HK's full death sequence inline: FreezeInPlace -> SaveGame -> wait (death anim plays) -> FadeOut ->
            // ReadyForRespawn -> BeginSceneTransition(respawnScene). On return the transition is kicked, RespawningHero set.
            yield return hkGm.PlayerDead(DeathWait);

            // Wait for the respawn to settle — RespawningHero consumed by EnterHero + the Knight placed at the marker
            // (isHeroInPosition). Bounded by a timeout so a bench-respawn quirk can never hang us (which would strand
            // `handling`). After the timeout we revive anyway — better visible-and-controllable than stuck dead.
            var t = 0f;
            while (t < 6f) {
                var k = HeroController.UnsafeInstance;
                if (k != null && !hkGm.RespawningHero && k.isHeroInPosition) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            Revive(HeroController.UnsafeInstance);
        } finally {
            handling = false;
            wasDead = false;
        }
    }

    // Un-die Hornet at the respawn point. Restores the physical death-state Die() set (renderer off, layer 2, kinematic,
    // no_input, HeroBox.Inactive) — always, so she can never be left invisible/dead. CONTROL + animation depend on where
    // she landed: a BENCH respawn (HK's atBench) is owned by HornetBench (it sits her + handles get-up on input), so we
    // must NOT RegainControl/idle here or the two fight (she'd run-in-place pinned to the seat). A ground respawn has no
    // HornetBench involvement, so we put her in a normal controllable idle ourselves. Fetches the LIVE RealHero (a
    // cross-scene respawn could have swapped the instance out from under a captured reference).
    private static void Revive(HeroController? knight) {
        var hero = BundleSpike.RealHero;
        if (hero == null) {
            Log.Error("[HornetDeath] revive: no Hornet to revive");
            return;
        }

        if (knight != null) hero.transform.position = knight.transform.position;
        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) {
            rb.bodyType = RigidbodyType2D.Dynamic;
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

        // Control/animation depend on WHERE she respawned:
        //  - BENCH respawn (atBench) -> she must go through HK's bench rest so its FSM can later cycle back to "Idle" (the
        //    only path that frees the bench for re-use; skipping it leaves the FSM stuck and benching dead until a scene
        //    reload). So leave her to HornetBench (it sits her; the bench-wake unstick advances HK's hung "Startle" to the
        //    get-up-ready "Resting"; the player's get-up then cycles the FSM home). Don't force up / clear atBench here.
        //  - GROUND respawn -> no bench involved, so put her in a normal controllable idle ourselves.
        var atBench = PlayerData.instance != null && PlayerData.instance.atBench;
        if (!atBench) {
            setStateMethod ??= typeof(SHeroController).GetMethod("SetState",
                BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(SActorStates)], null);
            setStateMethod?.Invoke(hero, [SActorStates.idle]);
            hero.StartAnimationControlToIdle();
            hero.RegainControl();
        }

        // Re-apply the active-hero split: HK's Respawn woke the (inert) Knight (renderer/control/physics), so re-inert it
        // and re-assert Hornet active. who==prev so this skips the position handoff, just re-runs SetInert on both.
        HeroSwitch.SetActive(ActiveHero.Hornet);

        Log.Info($"[HornetDeath] revived Hornet at {(Vector2)hero.transform.position} (atBench={atBench})");
    }

    private static IEnumerator Empty() {
        yield break;
    }

    // Debug (POST /getup): force Hornet out of any stuck bench/no_input state — clear atBench, re-enable rendering +
    // control, stand to idle. Also handy to recover a session wedged by an old-build death.
    internal static object ForceGetUp() {
        var hero = BundleSpike.RealHero;
        if (hero == null) return new { error = "no Hornet spawned" };
        if (PlayerData.instance != null) PlayerData.instance.atBench = false;
        var spd = Silksong::PlayerData.instance;
        if (spd != null) spd.isInventoryOpen = false;
        if (Time.timeScale <= 0.0001f) Time.timeScale = 1f;
        hero.cState.dead = false;
        SHeroBox.Inactive = false;
        rendererField ??= typeof(SHeroController).GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic);
        if (rendererField?.GetValue(hero) is MeshRenderer mr) mr.enabled = true;
        hero.gameObject.layer = 9;
        var rb = hero.GetComponent<Rigidbody2D>();
        if (rb != null) rb.bodyType = RigidbodyType2D.Dynamic;
        hero.AffectedByGravity(true);
        setStateMethod ??= typeof(SHeroController).GetMethod("SetState",
            BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(SActorStates)], null);
        setStateMethod?.Invoke(hero, [SActorStates.idle]);
        hero.StartAnimationControlToIdle();
        hero.RegainControl();
        return new { ok = true };
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

    // Debug: trigger a specific hazard on Hornet (POST /hazard?type=N), mapping the raw HK DamageHero.hazardType int the
    // same way ContactDamageBridge does, so we can empirically walk through 2=SPIKES/3=ACID/4=LAVA/5=PIT and observe what
    // Silksong's TakeDamage actually does for each (DieFromHazard path, anim, respawn) without finding each scene hazard.
    internal static object Hazard(string typeStr) {
        var hc = BundleSpike.RealHero;
        if (hc == null) return new { error = "no Hornet spawned" };
        if (!int.TryParse(typeStr, out var hk)) return new { error = $"bad type '{typeStr}'" };
        var ss = hk switch {
            2 => Silksong::GlobalEnums.HazardType.SPIKES,
            3 => Silksong::GlobalEnums.HazardType.ACID,
            4 => Silksong::GlobalEnums.HazardType.LAVA,
            5 => Silksong::GlobalEnums.HazardType.PIT,
            _ => Silksong::GlobalEnums.HazardType.ENEMY
        };
        var pd = Silksong::PlayerData.instance;
        if (pd != null) pd.isInvincible = false;
        hc.TakeDamage(hc.gameObject, Silksong::GlobalEnums.CollisionSide.left, 1, ss,
            Silksong::GlobalEnums.DamagePropertyFlags.NonLethal);
        return new { ok = true, hkType = hk, ssHazard = ss.ToString(), health = pd != null ? pd.health : -1 };
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
        // throws), so hook it to an empty coroutine.
        var pdm = typeof(SGameManager).GetMethod("PlayerDead",
            BindingFlags.Public | BindingFlags.Instance, null, [typeof(float)], null);
        if (pdm != null)
            playerDeadHook = new Hook(pdm,
                (Func<Func<SGameManager, float, IEnumerator>, SGameManager, float, IEnumerator>)
                ((_, _, _) => Empty()));
        else
            Log.Error("[HornetDeath] Silksong GameManager.PlayerDead(float) not found");

        // Hook HK's HeroController.TakeDamage: when Hornet is active, HK's hazard zones still call
        // HeroController.instance.TakeDamage on the inert Knight (hazards target HK's hero, not Hornet).
        // ContactDamageBridge already routes hazard damage to Hornet, so the Knight's TakeDamage is
        // redundant — skip it entirely when Hornet is active.
        var td = typeof(HeroController).GetMethod("TakeDamage",
            BindingFlags.Public | BindingFlags.Instance, null,
            [typeof(GameObject), typeof(CollisionSide), typeof(int), typeof(int)], null);
        if (td != null) {
            takeDamageHook = new Hook(td,
                (Action<Action<HeroController, GameObject, CollisionSide, int, int>,
                    HeroController, GameObject, CollisionSide, int, int>)
                ((orig, self, go, side, dmg, hazard) => {
                    if (HeroSwitch.HornetActive) return;
                    orig(self, go, side, dmg, hazard);
                }));
            Log.Info("[HornetDeath] hooked TakeDamage on HK HeroController");
        }
        else {
            Log.Error("[HornetDeath] HeroController.TakeDamage not found");
        }
    }

    internal static void Cleanup() {
        playerDeadHook?.Dispose();
        playerDeadHook = null;
        takeDamageHook?.Dispose();
        takeDamageHook = null;
        if (go != null) {
            Destroy(go);
            go = null;
        }
    }
}
