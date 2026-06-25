extern alias Silksong;
using System.Linq;
using System.Reflection;

namespace HornetPlayer.Playground;

// SURGICAL bring-up of Silksong's `ToolItemManager` (open item #6) — the singleton the tools/crests/nail-art systems
// deref. It carries two serialized assets: `toolItems` (ToolItemList) + `crestList` (ToolCrestList). The mechanics
// (load the _GameManager prefab ASSET, copy serialized fields onto a fresh single-manager GO, run only its Awake) live
// in ManagerSingletonBootstrap — see there for why we don't instantiate the prefab. `cursedCrest` (the 3rd
// SerializeField) is NOT copied: ToolItemManager.Awake derives it from crestList by name.
internal static class ToolItemManagerBootstrap {
    private const string GoName = "Silksong_ToolItemManager";

    internal static object Ensure() {
        // equipChangedToolSingleReminder / equipChangedToolModifierReminder are NOT [SerializeField] — they're
        // private runtime fields initialized only in SceneInit() (see below). On the prefab asset they're null, so
        // copying them is a no-op; omit them from the field list.
        var mgr = ManagerSingletonBootstrap.BringUp(typeof(Silksong::ToolItemManager), GoName, "toolItems",
            "crestList");
        if (mgr == null) return new { error = "ToolItemManager bring-up failed (see log)" };

        // ToolItemManager.Awake subscribes SceneInit to GameManager.SceneInit — our bootstrap GM never fires that
        // event, so SceneInit never runs. It's the ONLY place that initializes equipChangedToolSingleReminder /
        // equipChangedToolModifierReminder (new ControlReminder.{Single,Double}Config). Without it, those fields stay
        // null and ReportBoundAttackToolUsed NullRefs on .Disappear() when a Red tool was equipped+thrown.
        // Call SceneInit directly to reuse the real initialization. AddReminder inside it calls ControlReminder.Instance
        // (stubbed to return null in Stub.cs — no ControlReminder MonoBehaviour in our scene), so SubscribeEvents(null)
        // is a graceful no-op. Disappear is also stubbed since Owner is null.
        var sceneInit = mgr.GetType().GetMethod("SceneInit", BindingFlags.NonPublic | BindingFlags.Instance);
        if (sceneInit != null) sceneInit.Invoke(mgr, null);

        return Diag();
    }

    // Diagnostic: confirm the singleton resolves and the serialized lists are populated, and probe GetCrestByName
    // (which relies on the SO's OnEnable-built name dictionary — verifies that fired on bundle load).
    internal static object Diag() {
        var mgr = Silksong::ToolItemManager.SilentInstance;
        if (mgr == null) return new { error = "ToolItemManager singleton null (Ensure not run / spawn first)" };
        var crests = Silksong::ToolItemManager.GetAllCrests();
        var tools = Silksong::ToolItemManager.GetUnlockedTools().ToList();
        var crestId = Silksong::PlayerData.instance?.CurrentCrestID;
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

    internal static void Cleanup() {
        ManagerSingletonBootstrap.Destroy(GoName);
    }
}
