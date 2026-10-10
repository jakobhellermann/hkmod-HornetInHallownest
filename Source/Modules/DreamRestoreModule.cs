extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetInHallownest.Modules;

// HK's dreams, such as a dream boss's, Grey Prince Zote's or a Godhome fight, work like Silksong's memories (see its
// GameManager.EnteredNewMapZone): Hornet fights at full health on tool uses set aside, and leaves with the masks, silk
// and tools she entered with.
public sealed class DreamRestoreModule : ModuleBase {
    private const string DreamZone = "DREAM_WORLD";

    // The Radiance's dream is in FINAL_BOSS, with the Black Egg Temple and the Hollow Knight's fight.
    private const string RadianceDream = "Dream_Final";

    // Godhome's fights are in GODS_GLORY like Godhome itself, so only their entry door tells them apart; Godhome is
    // entered through the same door into its atrium.
    private const string DreamEnterGate = "door_dreamEnter";
    private const string GodhomeAtrium = "GG_Atrium";

    private static Silksong::HeroItemsState preDreamState;
    private static bool isWakingUp;

    public override string Id => "dream-restore";

    public override void Initialize() {
        Detour(typeof(GameManager), "OnNextLevelReady", OnNextLevelReady);
        Detour(typeof(GameManager), "FinishedEnteringScene", OnEnteredScene);
        Detour(typeof(Silksong::HeroController), "MaxHealth", OnMaxHealth);
        USceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    protected override void OnDeinitialize() {
        USceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Forget();
    }

    // The new scene's zone is already known before the hero enters it, so she crosses into or out of a dream before she
    // shows up there, and before a dream boss's scene heals her once she's in position (its FSM's "MaxHealth" message).
    // A death in the dream is left by DeathModule's revive, which comes after this.
    private static void OnNextLevelReady(Action<GameManager> orig, GameManager self) {
        isWakingUp = false;
        var isGodhomeFight = self.entryGateName == DreamEnterGate && self.sceneName != GodhomeAtrium;
        var inDream = self.GetCurrentMapZone() == DreamZone || self.sceneName == RadianceDream || isGodhomeFight;
        if (inDream != preDreamState.IsRecorded) {
            if (HornetSpawner.Hornet is not { } hero) Forget();
            else if (inDream) EnterDream(hero, isGodhomeFight);
            else if (!hero.cState.dead) LeaveDream(hero);
        }

        orig(self);
    }

    private static void OnEnteredScene(Action<GameManager> orig, GameManager self) {
        orig(self);
        isWakingUp = false;
    }

    // The scene she wakes up in can heal her the same way, such as Godhome's atrium after a pantheon.
    private static void OnMaxHealth(Action<Silksong::HeroController> orig, Silksong::HeroController self) {
        if (!isWakingUp) orig(self);
    }

    // DeathModule revives her through this after a death in a dream, instead of at full health.
    internal static bool TryLeaveDream(Silksong::HeroController hero) {
        if (!preDreamState.IsRecorded) return false;
        LeaveDream(hero);
        return true;
    }

    // A dream quit to the menu must not carry over into the next save loaded.
    private static void OnActiveSceneChanged(Scene from, Scene to) {
        if (to.name == "Menu_Title") Forget();
    }

    // Cogflies follow Hornet across scenes until cleared. A Godhome fight starts without silk, as it drains the Knight's
    // soul.
    private static void EnterDream(Silksong::HeroController hero, bool drainSilk) {
        hero.SuppressRefillSound(2);
        preDreamState = Silksong::HeroItemsState.Record(hero);
        if (drainSilk) hero.TakeSilk(hero.playerData.silk);
        hero.playerData.CaptureToolAmountsOverride();
        Silksong::EventRegister.SendEvent(Silksong::EventRegisterEvents.ClearEffectsInstant);
        hero.MaxHealthKeepBlue();
    }

    // Silksong's geo mirrors HK's, which the dream doesn't give back. Apply writes silk without redrawing the spool,
    // which only AddSilk and TakeSilk do.
    private static void LeaveDream(Silksong::HeroController hero) {
        hero.SuppressRefillSound(2);
        hero.ClearEffectsInstant();
        var silkDifference = preDreamState.Silk - hero.playerData.silk;
        preDreamState.Silk = hero.playerData.silk;
        preDreamState.Rosaries = hero.playerData.geo;
        preDreamState.Apply(hero);
        if (silkDifference > 0) hero.AddSilk(silkDifference, false);
        else if (silkDifference < 0) hero.TakeSilk(-silkDifference);
        hero.UpdateBlueHealth();
        Silksong::EventRegister.SendEvent(Silksong::EventRegisterEvents.HealthUpdate);
        hero.playerData.ClearToolAmountsOverride();
        isWakingUp = true;
    }

    private static void Forget() {
        preDreamState = default;
        isWakingUp = false;
        Silksong::PlayerData.instance?.ClearToolAmountsOverride();
    }
}
