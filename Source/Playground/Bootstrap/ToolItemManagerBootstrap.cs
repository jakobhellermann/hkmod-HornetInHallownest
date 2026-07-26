extern alias Silksong;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HornetInHallownest.Playground;

// Bring up Silksong's `ToolItemManager` (tools/crests/nail-arts deref it). Copies its `toolItems` + `crestList`
// serialized assets onto a single-manager GO (see ManagerSingletonBootstrap); `cursedCrest` is derived by its Awake.
internal static class ToolItemManagerBootstrap {
    private const string GoName = "Silksong_ToolItemManager";

    internal static object Ensure() {
        var mgr = ManagerSingletonBootstrap.BringUp(typeof(Silksong::ToolItemManager), GoName, "toolItems",
            "crestList");
        if (mgr == null) return new { error = "ToolItemManager bring-up failed (see log)" };

        // Our bootstrap GM never fires GameManager.SceneInit, so ToolItemManager.SceneInit (the only place that inits
        // the equipChangedTool*Reminder fields) never runs -> ReportBoundAttackToolUsed NullRefs. Call it directly.
        var sceneInit = mgr.GetType().GetMethod("SceneInit", BindingFlags.NonPublic | BindingFlags.Instance);
        if (sceneInit != null) sceneInit.Invoke(mgr, null);

        return new { ok = true };
    }

    // Diagnostic: confirm the singleton + serialized lists resolve, and that GetCrestByName works.
    internal static object Diag() {
        var mgr = Silksong::ToolItemManager.SilentInstance;
        if (mgr == null) return new { error = "ToolItemManager singleton null (Ensure not run / spawn first)" };
        var crests = Silksong::ToolItemManager.GetAllCrests();
        var tools = Silksong::ToolItemManager.GetUnlockedTools().ToList();
        var crestId = Silksong::PlayerData.instance.CurrentCrestID;
        var byName = string.IsNullOrEmpty(crestId) ? null : Silksong::ToolItemManager.GetCrestByName(crestId);
        return new {
            instanceSet = true,
            crestCount = crests.Count,
            crestNames = crests.Select(c => c.name).ToArray(),
            unlockedToolCount = tools.Count,
            currentCrestID = crestId,
            getCrestByNameResolves = byName != null
        };
    }

    // Like ToolItemManager.UnlockAllTools() but PopupFlags.None instead of Default, so it doesn't spawn a "tool get"
    // popup per tool.
    internal static void UnlockAllToolsSilently() {
        var pd = Silksong::PlayerData.instance;
        pd.SeenToolGetPrompt = true;
        pd.SeenToolWeaponGetPrompt = true;

        foreach (var tool in Silksong::ToolItemManager.GetAllTools()) {
            if (!tool) continue;
            tool.SetUnlockedTestsComplete();
            tool.Unlock(null, Silksong::ToolItem.PopupFlags.None);
        }
    }

    // Unlock every crest + all its tool slots: rebuild each crest's slot list from its config with IsUnlocked=true,
    // preserving equipped tools. Requires ToolItemManager + PlayerData up (post-spawn).
    internal static object UnlockAllCrestSlots() {
        var pd = Silksong::PlayerData.instance;
        var crests = Silksong::ToolItemManager.GetAllCrests();
        if (crests == null || crests.Count == 0) return new { error = "no crests (ToolItemManager not up?)" };
        var equips = pd.ToolEquips;
        int crestCount = 0, slotCount = 0;
        foreach (var crest in crests) {
            if (crest == null) continue;
            var data = equips.GetData(crest.name);
            var config = crest.Slots; // SlotInfo[] from the crest config — the authoritative slot count/layout
            var count = config?.Length ?? data.Slots?.Count ?? 0;
            var slots = new List<Silksong::ToolCrestsData.SlotData>(count);
            for (var i = 0; i < count; i++) {
                var equipped = data.Slots != null && i < data.Slots.Count ? data.Slots[i].EquippedTool : null;
                slots.Add(new Silksong::ToolCrestsData.SlotData { EquippedTool = equipped, IsUnlocked = true });
            }

            data.Slots = slots;
            data.IsUnlocked = true;
            equips.SetData(crest.name, data);
            crestCount++;
            slotCount += count;
        }

        Log.Debug($"[ToolItemManager] unlocked all crest slots: {crestCount} crests, {slotCount} slots");
        return new { crests = crestCount, slots = slotCount };
    }

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
