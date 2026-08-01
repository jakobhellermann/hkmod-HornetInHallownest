extern alias Silksong;
using System.Collections;
using GlobalEnums;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using SHeroController = Silksong::HeroController;
using SHeroBox = Silksong::HeroBox;
using SGameManager = Silksong::GameManager;
using SActorStates = Silksong::GlobalEnums.ActorStates;

namespace HornetInHallownest.Modules;

// Silksong's HeroController.Die is invoked, but the gm.PlayerDead is skipped, HK owns the respawn.
// TODO: currently all deaths are nonlethal, fix lethal pathway and cocoon spawns
public sealed class DeathModule : ModuleBase {
    private const float DeathWait = 2.5f; // lets the Silksong death anim play before HK fades (mirrors HK's DEATH_WAIT)
    private bool handling;
    private bool wasDead;

    public override string Id => "death";

    public override void Initialize() {
        Detour(typeof(SGameManager), "PlayerDead", NoPlayerDead, typeof(float));

        // HK hazard zones call HeroController.instance.TakeDamage on the inert Knight; ContactDamageBridge already routes
        // hazard damage to Hornet, so skip the Knight's while she's active.
        Detour(typeof(HeroController), "TakeDamage", OnKnightTakeDamage,
            typeof(GameObject), typeof(CollisionSide), typeof(int), typeof(int));

        Detour(typeof(SHeroController), "SetBenchRespawn", OnSetBenchRespawn,
            typeof(string), typeof(string), typeof(int), typeof(bool));
        Detour(typeof(SHeroController), "SetHazardRespawn", OnSetHazardRespawn, typeof(Vector3), typeof(bool));
    }

    public override void HornetActiveUpdate(SHeroController hero) {
        var dead = hero.cState.dead;
        if (dead && !wasDead && !handling) {
            handling = true;
            StartCoroutine(DeathRoutine());
        }

        wasDead = dead;
    }

    public override void HornetToggled(bool active) {
        if (!active) wasDead = false; // don't carry a stale edge across a switch/despawn
    }

    private IEnumerator DeathRoutine() {
        var hkGm = GameManager.UnsafeInstance;
        // try/finally guarantees `handling` always clears - a stuck DeathRoutine would break every later death.
        try {
            // HK does a "dream return" (wake at the dream entry, no bench) instead of a bench respawn for deaths in these
            // two zones (HeroController.Die). Route there so a dream-boss death doesn't wrongly sit her on the last bench.
            var mapZone = hkGm.GetCurrentMapZone();
            if (mapZone is "DREAM_WORLD" or "GODS_GLORY") {
                yield return DreamReturnRoutine(hkGm);
            } else {
                yield return hkGm.PlayerDead(DeathWait); // HK's full death sequence: freeze -> save -> fade -> transition

                // Wait for the respawn to settle (Knight placed at the marker), timeout-bounded so a quirk can't strand us.
                var t = 0f;
                while (t < 6f) {
                    var k = HeroController.UnsafeInstance;
                    if (k != null && !hkGm.RespawningHero && k.isHeroInPosition) break;
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            Revive(HeroController.UnsafeInstance);

            // Revive toggled the Hornet HUD off; the tool-icon refresh is lost across that toggle (ToolHudIcon doesn't
            // re-read on re-enable), so re-fire it once the HUD is active again (two frames covers the re-activation).
            if (PlayerData.instance.atBench) {
                yield return null;
                yield return null;
                BenchModule.RefreshToolHud();
            }
        } finally {
            handling = false;
            wasDead = false;
        }
    }

    // Dream/godhome death -> HK's dream return: MaxHealth + EnterWithoutInput, then transition to dreamReturnScene@
    // door_dreamReturn (no bench). The Knight is placed at the entry gate; Revive snaps Hornet onto it (atBench stays
    // false -> ground-idle path).
    private IEnumerator DreamReturnRoutine(GameManager hkGm) {
        var returnScene = PlayerData.instance.dreamReturnScene;
        if (string.IsNullOrEmpty(returnScene)) {
            LogError("dream death but dreamReturnScene empty - falling back to bench respawn");
            yield return hkGm.PlayerDead(DeathWait);
            yield break;
        }

        yield return new WaitForSeconds(DeathWait);

        var knight = HeroController.UnsafeInstance;
        if (!knight) yield break;
        knight.MaxHealth();
        knight.EnterWithoutInput(true);

        var fromScene = hkGm.GetSceneNameString();
        hkGm.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = returnScene,
            EntryGateName = "door_dreamReturn",
            Visualization = GameManager.SceneLoadVisualizations.Dream,
            PreventCameraFadeOut = true,
            WaitForSceneTransitionCameraFade = false,
            AlwaysUnloadUnusedAssets = true
        });

        // A dream return is a plain transition (no RespawningHero), so gate on the scene actually changing first, then
        // on the Knight reaching the entry gate. Timeout-bounded.
        var t = 0f;
        while (t < 8f) {
            if (hkGm.GetSceneNameString() != fromScene && knight.isHeroInPosition) break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // Un-die Hornet at the respawn point. Always restores the physical death-state Die set (renderer/layer/physics/
    // HeroBox) so she can't be left invisible-and-dead. Control+anim depend on where she landed: a bench respawn is
    // owned by BenchModule (it sits her + handles get-up), so don't RegainControl there or the two fight; a ground
    // respawn we idle ourselves.
    private void Revive(HeroController? knight) {
        var hero = HornetSpawner.Hornet;
        if (!hero) {
            LogError("revive: no Hornet to revive");
            return;
        }

        if (knight) hero.transform.position = knight.transform.position;
        if (hero.TryGetComponent<Rigidbody2D>(out var rb)) {
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.simulated = true;
            rb.linearVelocity = Vector2.zero;
        }

        var cs = hero.cState;
        cs.dead = false;
        cs.hazardDeath = false;
        cs.recoiling = false;
        cs.falling = false;

        SHeroBox.Inactive = false; // Die turned the global hero-box gate off
        if (hero.heroBox) hero.heroBox.HeroBoxNormal();

        if (hero.GetFieldValue<MeshRenderer>("renderer") is { } mr) mr.enabled = true;
        hero.gameObject.layer = 9; // Die moved her to layer 2 (no-collision)
        hero.AffectedByGravity(true);
        hero.MaxHealth();

        var atBench = PlayerData.instance.atBench;
        if (!atBench) {
            hero.InvokeMethod("SetState", SActorStates.idle);
            hero.StartAnimationControlToIdle();
            hero.RegainControl();
        }

        // HK's respawn woke the inert Knight, so re-assert the active-hero split (who==prev, just re-inerts both).
        HeroSwitch.SetActive(ActiveHero.Hornet);

        // HK's death disabled the HUD mask renderers; they only re-appear on the "In-game" GO's OnEnable, and SetActive
        // above already showed the HUD. Toggle it off->on to force that OnEnable to re-fire.
        GameCamerasBootstrap.SetHornetHudVisible(false);
        GameCamerasBootstrap.SetHornetHudVisible(true);
    }

    private static IEnumerator Empty() {
        yield break;
    }

    private IEnumerator NoPlayerDead(System.Func<SGameManager, float, IEnumerator> orig, SGameManager self, float wait) {
        return Empty();
    }

    private void OnKnightTakeDamage(System.Action<HeroController, GameObject, CollisionSide, int, int> orig,
        HeroController self, GameObject source, CollisionSide side, int damage, int hazard) {
        if (HeroSwitch.HornetActive) return;
        orig(self, source, side, damage, hazard);
    }

    #region Respawn point sync

    // HK owns respawn (reads HK's PlayerData), but cross-game scene FSMs set the respawn via Silksong's HeroController
    // (SetBenchRespawn/SetHazardRespawn), which the death path never reads. Mirror both onto HK's HeroController so its
    // PlayerData stays the source of truth (first case: the Vengeful-Spirit hard save in Crossroads_ShamanTemple).
    private void OnSetBenchRespawn(System.Action<SHeroController, string, string, int, bool> orig,
        SHeroController self, string marker, string scene, int type, bool facingRight) {
        orig(self, marker, scene, type, facingRight);
        if (HeroController.UnsafeInstance is { } hk) hk.SetBenchRespawn(marker, scene, type, facingRight);
    }

    private void OnSetHazardRespawn(System.Action<SHeroController, Vector3, bool> orig,
        SHeroController self, Vector3 pos, bool facingRight) {
        orig(self, pos, facingRight);
        if (HeroController.UnsafeInstance is { } hk) hk.SetHazardRespawn(pos, facingRight);
    }

    #endregion

    #region Debug routes

    // POST /getup - force Hornet out of any stuck bench/no_input/dead state.
    internal static object ForceGetUp() {
        var hero = HornetSpawner.Hornet;
        if (!hero) return new { error = "no Hornet spawned" };
        PlayerData.instance.atBench = false;
        Silksong::PlayerData.instance.isInventoryOpen = false;
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        hero.cState.dead = false;
        SHeroBox.Inactive = false;
        if (hero.GetFieldValue<MeshRenderer>("renderer") is { } mr) mr.enabled = true;
        hero.gameObject.layer = 9;
        if (hero.TryGetComponent<Rigidbody2D>(out var rb)) rb.bodyType = RigidbodyType2D.Dynamic;
        hero.AffectedByGravity(true);
        hero.InvokeMethod("SetState", SActorStates.idle);
        hero.StartAnimationControlToIdle();
        hero.RegainControl();
        return new { ok = true };
    }

    // POST /kill - kill Hornet via the real damage path (NonLethal, like ContactDamageBridge).
    internal static object Kill() {
        var hc = HornetSpawner.Hornet;
        if (!hc) return new { error = "no Hornet spawned" };
        var pd = Silksong::PlayerData.instance;
        pd.isInvincible = false;
        pd.health = 1;

        hc.TakeDamage(hc.gameObject, Silksong::GlobalEnums.CollisionSide.left, 1,
            Silksong::GlobalEnums.HazardType.ENEMY, Silksong::GlobalEnums.DamagePropertyFlags.NonLethal);
        return new { ok = true, health = pd.health };
    }

    // POST /hazard?type=N - trigger a specific hazard (2=spikes,3=acid,4=lava,5=pit), same mapping as ContactDamageBridge.
    internal static object Hazard(string typeStr) {
        var hc = HornetSpawner.Hornet;
        if (!hc) return new { error = "no Hornet spawned" };
        if (!int.TryParse(typeStr, out var hk)) return new { error = $"bad type '{typeStr}'" };
        var ss = hk switch {
            2 => Silksong::GlobalEnums.HazardType.SPIKES,
            3 => Silksong::GlobalEnums.HazardType.ACID,
            4 => Silksong::GlobalEnums.HazardType.LAVA,
            5 => Silksong::GlobalEnums.HazardType.PIT,
            _ => Silksong::GlobalEnums.HazardType.ENEMY
        };
        var pd = Silksong::PlayerData.instance;
        pd.isInvincible = false;
        hc.TakeDamage(hc.gameObject, Silksong::GlobalEnums.CollisionSide.left, 1, ss,
            Silksong::GlobalEnums.DamagePropertyFlags.NonLethal);
        return new { ok = true, hkType = hk, ssHazard = ss.ToString(), health = pd.health };
    }

    #endregion
}
