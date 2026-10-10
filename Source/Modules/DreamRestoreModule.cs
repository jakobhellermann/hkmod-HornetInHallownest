extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine.SceneManagement;
using USceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HornetInHallownest.Modules;

// Hornet leaves a dream boss's or Grey Prince Zote's dream with the masks, silk and tools she entered it with.
public sealed class DreamRestoreModule : ModuleBase {
    private const string DreamZone = "DREAM_WORLD";

    private static int? silk;
    private static int health;
    private static readonly Dictionary<string, int> toolAmounts = [];

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
        if (inDream && silk == null) Remember();
        else if (!inDream && silk != null) Restore();
    }

    // A dream quit to the menu must not carry over into the next save loaded.
    private static void OnActiveSceneChanged(Scene from, Scene to) {
        if (to.name == "Menu_Title") Forget();
    }

    private static void Remember() {
        var pd = Silksong::PlayerData.instance;
        silk = pd.silk;
        health = pd.health;
        foreach (var tool in Silksong::ToolItemManager.GetUnlockedTools())
            if (tool)
                toolAmounts[tool.name] = pd.Tools.GetData(tool.name).AmountLeft;
    }

    private static void Restore() {
        var pd = Silksong::PlayerData.instance;
        foreach (var (name, amount) in toolAmounts) {
            var data = pd.Tools.GetData(name);
            data.AmountLeft = amount;
            pd.Tools.SetData(name, data);
        }

        var silkDifference = silk!.Value - pd.silk;
        if (HornetSpawner.Hornet is not { } hero) pd.silk = silk.Value;
        else if (silkDifference > 0) hero.AddSilk(silkDifference, false);
        else if (silkDifference < 0) hero.TakeSilk(-silkDifference);

        // Dying in a dream revives her at full health, as it does the Knight, so only lost masks come back.
        var healthDifference = health - pd.health;
        if (healthDifference > 0) {
            if (HornetSpawner.Hornet is { } healed) healed.AddHealth(healthDifference);
            else pd.health = health;
        }

        BenchModule.RefreshToolHud();
        Forget();
    }

    private static void Forget() {
        silk = null;
        toolAmounts.Clear();
    }
}
