extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetInHallownest.Modules;

// HK's dreams, such as a dream boss's or Grey Prince Zote's, work like Silksong's memories (see its
// GameManager.EnteredNewMapZone): Hornet fights at full health on tool uses set aside, and leaves with the masks, silk
// and tools she entered with.
public sealed class DreamRestoreModule : ModuleBase {
    private const string DreamZone = "DREAM_WORLD";

    private static Silksong::HeroItemsState preDreamState;

    public override string Id => "dream-restore";

    public override void Initialize() {
        Detour(typeof(GameManager), "FinishedEnteringScene", OnEnteredScene);
        USceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    protected override void OnDeinitialize() {
        USceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Forget();
    }

    private static void OnEnteredScene(Action<GameManager> orig, GameManager self) {
        orig(self);
        var inDream = self.GetCurrentMapZone() == DreamZone;
        if (inDream == preDreamState.IsRecorded) return;
        if (HornetSpawner.Hornet is not { } hero) Forget();
        else if (inDream) EnterDream(hero);
        else LeaveDream(hero);
    }

    // A dream quit to the menu must not carry over into the next save loaded.
    private static void OnActiveSceneChanged(Scene from, Scene to) {
        if (to.name == "Menu_Title") Forget();
    }

    // Cogflies follow Hornet across scenes until cleared.
    private static void EnterDream(Silksong::HeroController hero) {
        hero.SuppressRefillSound(2);
        preDreamState = Silksong::HeroItemsState.Record(hero);
        hero.playerData.CaptureToolAmountsOverride();
        Silksong::EventRegister.SendEvent(Silksong::EventRegisterEvents.ClearEffectsInstant);
        hero.MaxHealthKeepBlue();
    }

    // Silksong's geo mirrors HK's, which the dream doesn't give back.
    private static void LeaveDream(Silksong::HeroController hero) {
        hero.SuppressRefillSound(2);
        hero.ClearEffectsInstant();
        preDreamState.Rosaries = hero.playerData.geo;
        preDreamState.Apply(hero);
        hero.UpdateBlueHealth();
        Silksong::EventRegister.SendEvent(Silksong::EventRegisterEvents.HealthUpdate);
        hero.playerData.ClearToolAmountsOverride();
    }

    private static void Forget() {
        preDreamState = default;
        Silksong::PlayerData.instance?.ClearToolAmountsOverride();
    }
}
