extern alias Silksong;
using System;
using System.Collections;
using System.Collections.Generic;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using Modding;
using UnityEngine;
using SReplenishMethod = Silksong::ToolItemManager.ReplenishMethod;
using SReplenishResources = Silksong::ToolItem.ReplenishResources;
using SReplenishUsages = Silksong::ToolItem.ReplenishUsages;

namespace HornetInHallownest.Modules;

// Sync hornets onBench state, refill tools on bench.
public sealed class BenchModule : ModuleBase {
    private const float SylphsongStartDelay = 1f;
    private const float SylphsongSilkInterval = 0.26f;

    public override string Id => "bench";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
        // A tool icon can't redraw while the HUD is hidden, and it doesn't redraw on its own when shown again.
        GameCamerasBootstrap.HornetHudShown += RefreshToolHud;
        Silksong::ToolItemManager.OnEquippedStateChanged += OnEquipsChanged;
        Detour(typeof(Silksong::ToolItemManager), "TryReplenishTools", OnTryReplenishTools, typeof(bool),
            typeof(SReplenishMethod));
    }

    // The Architect Crest's quick craft pays in shell shards, which HK doesn't have; refill the tools it would refill
    // for free instead, like the bench does.
    private static bool OnTryReplenishTools(Func<bool, SReplenishMethod, bool> orig, bool doReplenish,
        SReplenishMethod method) {
        if (method != SReplenishMethod.QuickCraft) return orig(doReplenish, method);
        var tools = typeof(Silksong::ToolItemManager).InvokeMethod<List<Silksong::ToolItem>>("GetCurrentEquippedTools");
        if (tools == null) return false;

        var pd = Silksong::PlayerData.instance;
        var canRefill = false;
        foreach (var tool in tools) {
            if (!tool || !tool.IsAutoReplenished() || tool.ReplenishResource == SReplenishResources.None ||
                tool.ReplenishUsage == SReplenishUsages.OneForOne) continue;
            var data = pd.GetToolData(tool.name);
            var storage = Silksong::ToolItemManager.GetToolStorageAmount(tool);
            if (data.AmountLeft >= storage) continue;
            canRefill = true;
            if (!doReplenish) continue;
            data.AmountLeft = storage;
            pd.SetToolData(tool.name, data);
        }

        if (canRefill && doReplenish) Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        return canRefill;
    }

    protected override void OnDeinitialize() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        GameCamerasBootstrap.HornetHudShown -= RefreshToolHud;
        Silksong::ToolItemManager.OnEquippedStateChanged -= OnEquipsChanged;
        ResetAtBench();
    }

    private static void ResetAtBench() {
        if (!HeroSwitch.HornetActive) return;
        var pd = PlayerData.instance;
        if (!pd.atBench) return;
        foreach (var fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None)) {
            if (fsm.FsmName != "Bench Control" || fsm.ActiveStateName is "Idle" or "Inert" or "") continue;
            fsm.enabled = false; // RestartOnEnable -> Init -> Check Start State -> Idle
            fsm.enabled = true;
        }

        pd.SetBool("atBench", false);
    }

    private bool OnSetBool(string name, bool value) {
        if (name != "atBench" || !HeroSwitch.HornetActive) return value;
        var spd = Silksong::PlayerData.instance;
        if (spd.atBench == value) return value;

        spd.atBench = value;
        if (!value) return value;
        RefillTools();
        if (spd.HasBoundCrestUpgrader) StartCoroutine(RestoreSilk(spd));
        return value;
    }

    // Sylphsong restores silk at a bench through the "Eva Heal" FSM on Silksong's benches, which HK's benches don't have.
    private static IEnumerator RestoreSilk(Silksong::PlayerData pd) {
        yield return new WaitForSeconds(SylphsongStartDelay);
        while (pd.atBench && pd.silk < pd.CurrentSilkMax && HornetSpawner.Hornet is { } hero) {
            hero.AddSilk(1, false);
            yield return new WaitForSeconds(SylphsongSilkInterval);
        }
    }

    // Setting a tool's AmountLeft doesn't notify the HUD; icons redraw only on these events.
    public static void RefreshToolHud() {
        Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    // Shell Satchel raises tool storage only while equipped, so equipping it on the bench leaves the extra empty.
    // RefreshToolHud would raise this event again, but the icons only need the bound tools' update.
    private static void OnEquipsChanged() {
        if (!HeroSwitch.HornetActive || !Silksong::PlayerData.instance.atBench) return;
        if (FillTools()) Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
    }

    // TODO: implement shell shards?
    private static void RefillTools() {
        if (FillTools()) RefreshToolHud();
    }

    private static bool FillTools() {
        try {
            var tools = Silksong::PlayerData.instance.Tools;
            if (tools == null) return false;
            foreach (var tool in Silksong::ToolItemManager.GetUnlockedTools()) {
                if (!tool) continue;
                var data = tools.GetData(tool.name);
                data.AmountLeft = Silksong::ToolItemManager.GetToolStorageAmount(tool);
                tools.SetData(tool.name, data);
            }

            return true;
        } catch (Exception e) {
            Log.Error($"[Bench] tool refill failed: {e.Message}");
            return false;
        }
    }
}
