extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine;
using SCheckIsTeleportBlocked = Silksong::HutongGames.PlayMaker.Actions.CheckIsTeleportBlocked;
using SGetFastTravelScene = Silksong::HutongGames.PlayMaker.Actions.GetFastTravelScene;
using SStartPreloadingScene = Silksong::HutongGames.PlayMaker.Actions.StartPreloadingScene;
using SGameManager = Silksong::GameManager;
using SFindFastTravelTeleportPoint = Silksong::HutongGames.PlayMaker.Actions.FindFastTravelTeleportPoint;

namespace HornetInHallownest.Modules;

// Silksong's Beastling Call (Needolin played holding up) calls the Bell Beast to the bellway it waits at. In Hallownest it
// takes Hornet to the stag station the Stag waits at.
public sealed class BeastlingCallModule : ModuleBase {
    private const string FastTravelExitGate = "door_fastTravelExit";
    private const string StagExitGate = "door_stagExit";

    private const float FootOffset = 0.3f;
    private const float FootRayLift = 0.1f;
    private const float FootRayLength = 0.6f;
    private const float NoRoofHeight = 10f;
    private static readonly int terrainMask = LayerMask.GetMask("Terrain");

    private static readonly string[] stationScenes = [
        "Room_Town_Stag_Station", "Crossroads_47", "Fungus1_16_alt", "Fungus2_02", "Fungus3_40", "Ruins1_29",
        "Ruins2_08", "RestingGrounds_09", "Deepnest_09", "Cliffs_03", "Abyss_22"
    ];

    public override string Id => "beastling-call";

    public override void Initialize() {
        Detour(typeof(SCheckIsTeleportBlocked), "get_IsTrue", OnIsTeleportBlocked);
        Detour(typeof(SFindFastTravelTeleportPoint), nameof(SFindFastTravelTeleportPoint.OnEnter),
            OnFindFastTravelTeleportPoint);
        Detour(typeof(SGetFastTravelScene), nameof(SGetFastTravelScene.OnEnter), OnGetFastTravelScene);
        Detour(typeof(SStartPreloadingScene), nameof(SStartPreloadingScene.OnEnter), OnStartPreloadingScene);
        Detour(typeof(SGameManager), nameof(SGameManager.BeginSceneTransition), OnBeginSceneTransition,
            typeof(SGameManager.SceneLoadInfo));
    }

    private static string? StationScene() {
        var position = PlayerData.instance.GetInt("stagPosition");
        return position >= 0 && position < stationScenes.Length ? stationScenes[position] : null;
    }

    // Silksong's own limits are all about Pharloom's scenes. A blocked call plays on with no reaction, so Hornet's limits
    // fail it where Silksong finds no room for the Bell Beast, which plays Silksong's whole failed call.
    private static bool OnIsTeleportBlocked(Func<SCheckIsTeleportBlocked, bool> orig, SCheckIsTeleportBlocked self) =>
        !HeroSwitch.HornetActive && orig(self);

    private static bool CanCallStag() {
        var station = StationScene();
        return station != null && station != GameManager.instance.sceneName && IsDreamGateAllowed();
    }

    // Replaces Silksong's search for room to land the Bell Beast's children with HK's Dream Gate check: floor right under
    // both feet.
    private static void OnFindFastTravelTeleportPoint(Action<SFindFastTravelTeleportPoint> orig,
        SFindFastTravelTeleportPoint self) {
        var hero = HeroSwitch.HornetActive ? HornetSpawner.Hornet : null;
        if (!hero || !hero.TryGetComponent<Collider2D>(out var body)) {
            orig(self);
            return;
        }

        var feet = new Vector2(body.bounds.center.x, body.bounds.min.y + FootRayLift);
        var floor = Physics2D.Raycast(feet, Vector2.down, FootRayLength, terrainMask);
        var clear = CanCallStag() && floor.collider &&
                    Physics2D.Raycast(feet + Vector2.left * FootOffset, Vector2.down, FootRayLength, terrainMask) &&
                    Physics2D.Raycast(feet + Vector2.right * FootOffset, Vector2.down, FootRayLength, terrainMask);
        self.StoreGroundPos.Value = clear ? floor.point : Vector2.zero;
        self.StoreRoofHeight.Value = NoRoofHeight;
        self.StoreLeftClear.Value = clear;
        self.StoreRightClear.Value = clear;
        self.Fsm.Event(clear ? self.IsClearEvent : self.NotClearEvent);
        self.Finish();
    }

    // The Knight's "Dream Nail" FSM "Can Set?" state.
    private static bool IsDreamGateAllowed() {
        var knight = HeroController.instance;
        var dreamNail = knight ? FSMUtility.LocateFSM(knight.gameObject, "Dream Nail") : null;
        if (!dreamNail) return true;
        var variables = dreamNail.FsmVariables;
        if (!variables.GetFsmBool("Dream Gate Allowed").Value) return false;

        var zone = GameManager.instance.GetCurrentMapZone();
        if (zone is "DREAM_WORLD" or "GODS_GLORY") return false;

        var map = GameManager.instance.gameMap ? GameManager.instance.gameMap.GetComponent<GameMap>() : null;
        return !map || !map.GetInRoom() || variables.GetFsmBool("Room Override").Value;
    }

    private static void OnGetFastTravelScene(Action<SGetFastTravelScene> orig, SGetFastTravelScene self) {
        if (!HeroSwitch.HornetActive) {
            orig(self);
            return;
        }

        var station = StationScene();
        self.StoreSceneName.Value = station ?? "";
        if (station == null) self.Fsm.Event(self.InvalidEvent);
        self.Finish();
    }

    // The bellway preload would look for the HK station among Silksong's scenes.
    private static void OnStartPreloadingScene(Action<SStartPreloadingScene> orig, SStartPreloadingScene self) {
        if (HeroSwitch.HornetActive && Array.IndexOf(stationScenes, self.SceneName.Value) >= 0) {
            self.Finish();
            return;
        }

        orig(self);
    }

    private static void OnBeginSceneTransition(Action<SGameManager, SGameManager.SceneLoadInfo> orig, SGameManager self,
        SGameManager.SceneLoadInfo info) {
        if (!HeroSwitch.HornetActive || info.EntryGateName != FastTravelExitGate) {
            orig(self, info);
            return;
        }

        GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo {
            SceneName = info.SceneName,
            EntryGateName = StagExitGate,
            Visualization = GameManager.SceneLoadVisualizations.Default
        });
    }
}
