extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using SHeroController = Silksong::HeroController;
using SPlayerData = Silksong::PlayerData;
using SInventoryPane = Silksong::InventoryPane;
using SInventoryPaneBase = Silksong::InventoryPaneBase;
using SInventoryPaneList = Silksong::InventoryPaneList;

namespace HornetInHallownest.Modules;

// Hornet's own "Map Control" FSM runs the quick map (slow map walk, animations, double tap for the inventory map), but it
// opens Silksong's map UI through EventRegister events. Gate it on HK's map and route the quick map's events to HK's
// quick map. Her inventory's map pane runs HK's inventory map instead of Pharloom's.
public sealed class QuickMapModule : ModuleBase {
    private const string DoNotClose = "Do Not Close";
    private const string InSubMenu = "In Sub Menu";
    private const string AllowPaneSwapping = "Allow Pane Swapping";
    private const string MarkerMenuState = "Marker Select Menu";

    // Silksong EventRegister event -> HK's quick map FSM event
    private static readonly Dictionary<int, string> quickMapEvents = new() {
        { "OPEN QUICK MAP".GetHashCode(), "OPEN QUICK MAP" },
        { "CLOSE QUICK MAP".GetHashCode(), "CLOSE QUICK MAP" },
        { "CLOSE QUICK MAP INSTANT".GetHashCode(), "CLOSE QUICK MAP" }
    };

    // HK's map FSM state that moves past the map's edge -> Silksong's inventory event for that pane move
    private static readonly Dictionary<string, string> paneMoves = new() {
        ["Move Pane L"] = "MOVE PANE L",
        ["Move Pane R"] = "MOVE PANE R"
    };

    private static GameMap? shownMap;
    private static Vector3 hiddenMapPosition;
    private static Vector3 hiddenMapScale;
    private static GameObject? hkMapPane;
    private static PlayMakerFSM? hkMapControl;
    private static PlayMakerFSM? hkInventoryControl;
    private static SilksongPM::PlayMakerFSM? inventoryControl;
    private static GameObject? pharloomMap;
    private static GameObject? wideMapCompass;
    private static bool wasWideMapCompassActive;
    private static bool isActivating;
    private static bool isZoomShortcut;
    private static bool shownAllowPaneSwapping;

    public override string Id => "quick-map";

    public override bool RunWhilePaused => true;

    public override void Initialize() {
        Detour(typeof(SHeroController), "CanQuickMap", OnCanQuickMap, typeof(bool));
        Detour(typeof(Silksong::EventRegister), "SendEvent", OnSendEvent, typeof(int), typeof(GameObject));
        Detour(typeof(SInventoryPane), "get_IsAvailable", OnPaneIsAvailable);
        Detour(typeof(SInventoryPane), nameof(SInventoryPane.PaneStart), OnPaneStart);
        Detour(typeof(SInventoryPaneBase), nameof(SInventoryPaneBase.PaneEnd), OnPaneEnd);
        Detour(typeof(SInventoryPaneList), nameof(SInventoryPaneList.OnClosingInventory), OnClosingInventory);
        Detour(typeof(Silksong::InventoryPaneInput), "Update", OnPaneInputUpdate);
        Detour(typeof(Silksong::InventoryMapManager), nameof(Silksong::InventoryMapManager.AutoZoomIn), OnAutoZoomIn);
        Detour(typeof(Silksong::InventoryMapManager), nameof(Silksong::InventoryMapManager.ZoomOut), OnZoomOut);
    }

    protected override void OnDeinitialize() => HideHkMap();

    // HK's map listens for its input only while HK isn't paused, and Hornet's inventory pauses HK, so its input events
    // come from here.
    public override void HornetActiveUpdate(SHeroController hero) {
        if (!shownMap) return;
        if (isActivating) {
            // The map's FSM ignores ACTIVATE until it has started, a frame after its pane first turns on.
            if (hkMapControl!.ActiveStateName is "" or "Init") return;
            if (hkMapControl.ActiveStateName == "Inactive") hkMapControl.SendEvent("ACTIVATE");
            isActivating = false;
        }

        if (isZoomShortcut) {
            // Activating selects the current area, which MAP ZOOM zooms into.
            isZoomShortcut = false;
            hkMapControl!.SendEvent("MAP ZOOM");
        }

        // While zoomed in, HK's map holds its inventory open (Do Not Close) and takes Cancel. Hornet's inventory keeps
        // only Cancel from closing it then (In Sub Menu), and stops pane moves only for the marker menu, which cycles
        // markers on the pane buttons. Pharloom's map FSM still sets Do Not Close as it zooms on the quick map shortcut.
        var variables = inventoryControl!.FsmVariables;
        variables.GetFsmBool(DoNotClose).Value = false;
        variables.GetFsmBool(InSubMenu).Value = hkInventoryControl!.FsmVariables.GetFsmBool(DoNotClose).Value;
        variables.GetFsmBool(AllowPaneSwapping).Value =
            shownAllowPaneSwapping && hkMapControl!.ActiveStateName != MarkerMenuState;

        var input = InputHandler.Instance.inputActions;
        if (input.dreamNail.WasPressed) MapKey()?.SendEvent("ACTION");
        var uiEvent = Platform.Current.GetMenuAction(input, true) switch {
            Platform.MenuActions.Submit => "UI CONFIRM",
            Platform.MenuActions.Cancel => "UI CANCEL",
            _ when input.left.WasPressed => "UI LEFT",
            _ when input.right.WasPressed => "UI RIGHT",
            _ when input.up.WasPressed => "UI UP",
            _ when input.down.WasPressed => "UI DOWN",
            _ => null
        };
        if (uiEvent != null) hkMapControl!.SendEvent(uiEvent);

        if (!paneMoves.TryGetValue(hkMapControl!.ActiveStateName, out var paneMove)) return;
        hkMapControl.SendEvent("CANCEL");
        inventoryControl.SendEvent(paneMove);
    }

    // Pharloom's map would act on the same presses as HK's map.
    private static void OnPaneInputUpdate(Action<Silksong::InventoryPaneInput> orig,
        Silksong::InventoryPaneInput self) {
        if (!shownMap || !IsMapPane(self)) orig(self);
    }

    // Pharloom's map stays at its wide view; the quick map shortcut that would zoom it in zooms HK's map instead.
    private static void OnAutoZoomIn(Action<Silksong::InventoryMapManager> orig, Silksong::InventoryMapManager self) {
        if (HeroSwitch.HornetActive) isZoomShortcut = true;
        else orig(self);
    }

    private static void OnZoomOut(Action<Silksong::InventoryMapManager> orig, Silksong::InventoryMapManager self) {
        if (!HeroSwitch.HornetActive) orig(self);
    }

    private static void OnClosingInventory(Action<SInventoryPaneList> orig, SInventoryPaneList self) {
        orig(self);
        HideHkMap();
    }

    // Silksong's map pane only shows Pharloom; it's listed while HK's map is owned.
    private static bool OnPaneIsAvailable(Func<SInventoryPane, bool> orig, SInventoryPane self) {
        if (HeroSwitch.HornetActive && IsMapPane(self)) return PlayerData.instance.hasMap;
        return orig(self);
    }

    private static void OnPaneStart(Action<SInventoryPane> orig, SInventoryPane self) {
        orig(self);
        if (HeroSwitch.HornetActive && IsMapPane(self)) ShowHkMap(self);
    }

    private static void OnPaneEnd(Action<SInventoryPaneBase> orig, SInventoryPaneBase self) {
        orig(self);
        if (IsMapPane(self)) HideHkMap();
    }

    private static bool IsMapPane(Component pane) => pane.TryGetComponent<Silksong::InventoryMapManager>(out _);

    private static void ShowHkMap(SInventoryPane pane) {
        var map = GameManager.instance.gameMap ? GameManager.instance.gameMap.GetComponent<GameMap>() : null;
        var mapPane = GameCameras.instance.hudCamera.transform.Find("Inventory/Map");
        var paneList = pane.GetComponentInParent<SInventoryPaneList>();
        if (!map || !mapPane || !paneList) return;

        HideHkMap();
        shownMap = map;
        hiddenMapPosition = map.transform.localPosition;
        hiddenMapScale = map.transform.localScale;
        hkMapPane = mapPane.gameObject;
        hkInventoryControl = FSMUtility.LocateFSM(mapPane.parent.gameObject, "Inventory Control");
        inventoryControl = Silksong::FSMUtility.LocateFSM(paneList.gameObject, "Inventory Control");
        shownAllowPaneSwapping = inventoryControl.FsmVariables.GetFsmBool(AllowPaneSwapping).Value;

        // HK's inventory opens its map pane this way.
        mapPane.localPosition = Vector3.zero;
        hkMapPane.SetActive(true);
        FSMUtility.SendEventToGameObject(hkMapPane, "UP");
        var worldMap = mapPane.Find("World Map");
        wideMapCompass = worldMap.Find("Wide Map/Compass Icon").gameObject;
        wasWideMapCompassActive = wideMapCompass.activeSelf;
        // HK shows it while Wayward Compass is on; the bool reads Hornet's Compass tool for her.
        wideMapCompass.SetActive(PlayerData.instance.GetBool("equippedCharm_2"));
        MapMarkerModule.SetWideMapCompass(wideMapCompass.transform, true);
        // The zoom fades the area overview out, and only HK's zoom out brings it back.
        FSMUtility.SendEventToGameObject(worldMap.Find("Wide Map").gameObject, "UP", true);
        var arrows = worldMap.Find("Pan Arrows");
        map.SetPanArrows(arrows.Find("Arrow U").gameObject, arrows.Find("Arrow D").gameObject,
            arrows.Find("Arrow L").gameObject, arrows.Find("Arrow R").gameObject);
        hkMapControl = FSMUtility.LocateFSM(worldMap.gameObject, "UI Control");
        isActivating = true;

        var pharloom = pane.transform.Find("World Map");
        if (!pharloom) return;
        pharloomMap = pharloom.gameObject;
        pharloomMap.SetActive(false);
    }

    private static void HideHkMap() {
        if (!shownMap) return;
        // Hornet's inventory also closes mid-zoom (on damage), which HK's map never expects: undo what its zoom holds.
        var worldMap = hkMapPane!.transform.Find("World Map");
        if (hkMapControl!.ActiveStateName == MarkerMenuState)
            worldMap.Find("Map Markers").GetComponent<MapMarkerMenu>().Close();
        // Reset is where HK's zoom out ends: the zoom-in prompt and the inventory free to close.
        if (hkInventoryControl!.FsmVariables.GetFsmBool(DoNotClose).Value) hkMapControl.SetState("Reset");
        hkMapControl.SendEvent("PANE RESET");
        FSMUtility.SendEventToGameObject(hkMapPane, "DOWN");
        // Its fade group holds the HK inventory border's pane arrows, which stay on screen outside the inventory.
        var borderFade = worldMap.GetComponent<FadeGroup>();
        borderFade.InvokeMethod("DisableRenderers");
        foreach (var arrow in borderFade.animators) arrow.GetComponent<MeshRenderer>().enabled = false;
        MapMarkerModule.SetWideMapCompass(wideMapCompass!.transform, false);
        wideMapCompass.SetActive(wasWideMapCompassActive);
        var variables = inventoryControl!.FsmVariables;
        variables.GetFsmBool(DoNotClose).Value = false;
        variables.GetFsmBool(InSubMenu).Value = false;
        variables.GetFsmBool(AllowPaneSwapping).Value = shownAllowPaneSwapping;
        isZoomShortcut = false;
        MapKey()?.SendEvent("MAP KEY DOWN");

        iTween.Stop(shownMap.gameObject);
        shownMap.StopPan();
        shownMap.CloseQuickMap();
        shownMap.transform.localPosition = hiddenMapPosition;
        shownMap.transform.localScale = hiddenMapScale;
        shownMap = null;
        hkMapPane.SetActive(false);
        if (pharloomMap) pharloomMap.SetActive(true);
        pharloomMap = null;
    }

    // HK's map legend, which its inventory shows beside the zoomed-in map. It sits outside the inventory's map pane.
    private static PlayMakerFSM? MapKey() {
        var mapKey = GameCameras.instance ? GameCameras.instance.hudCamera.transform.Find("Inventory/Map Key") : null;
        return mapKey ? FSMUtility.LocateFSM(mapKey.gameObject, "Control") : null;
    }

    // Silksong requires a Pharloom map (HasAnyMap); mapAllRooms short-circuits that check to HK's hasMap.
    private static bool OnCanQuickMap(Func<SHeroController, bool, bool> orig, SHeroController self, bool onBench) {
        var pd = SPlayerData.instance;
        var mapAllRooms = pd.mapAllRooms;
        pd.mapAllRooms = PlayerData.instance.hasMap;
        try {
            return orig(self, onBench);
        } finally {
            pd.mapAllRooms = mapAllRooms;
        }
    }

    private static void OnSendEvent(Action<int, GameObject> orig, int eventNameHash, GameObject excludeGameObject) {
        if (!HeroSwitch.HornetActive || !quickMapEvents.TryGetValue(eventNameHash, out var hkEvent)) {
            orig(eventNameHash, excludeGameObject);
            return;
        }

        var quickMap = GameCameras.instance ? GameCameras.instance.hudCamera.transform.Find("Quick Map") : null;
        if (quickMap)
            foreach (var fsm in quickMap.GetComponents<PlayMakerFSM>())
                fsm.SendEvent(hkEvent);
    }
}
