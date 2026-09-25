extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using Modding;
using SReplenishMethod = Silksong::ToolItemManager.ReplenishMethod;
using SReplenishResources = Silksong::ToolItem.ReplenishResources;
using SReplenishUsages = Silksong::ToolItem.ReplenishUsages;

namespace HornetInHallownest.Modules;

// Sync hornets onBench state, refill tools on bench.
public sealed class BenchModule : ModuleBase {
    public override string Id => "bench";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
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
        ResetAtBench();
    }

    private static void ResetAtBench() {
        if (!HeroSwitch.HornetActive) return;
        var pd = PlayerData.instance;
        if (!pd.atBench) return;
        foreach (var fsm in UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>()) {
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
        if (value) RefillTools();
        return value;
    }

    // Setting a tool's AmountLeft doesn't notify the HUD; icons redraw only on these events.
    public static void RefreshToolHud() {
        Silksong::ToolItemManager.ReportAllBoundAttackToolsUpdated();
        Silksong::ToolItemManager.SendEquippedChangedEvent(true);
    }

    // TODO: implement shell shards?
    private void RefillTools() {
        try {
            var tools = Silksong::PlayerData.instance.Tools;
            if (tools == null) return;
            foreach (var tool in Silksong::ToolItemManager.GetUnlockedTools()) {
                if (!tool) continue;
                var data = tools.GetData(tool.name);
                data.AmountLeft = Silksong::ToolItemManager.GetToolStorageAmount(tool);
                tools.SetData(tool.name, data);
            }

            RefreshToolHud();
        } catch (Exception e) {
            LogError($"tool refill failed: {e.Message}");
        }
    }
}
