extern alias Silksong;
extern alias SilksongPM;
using System;
using System.Runtime.Serialization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Bootstrap;

// The Silksong singletons the hero reads, on an inactive GameManager GO so their Awakes never run.
internal static class SilksongBootstrap {
    private static Silksong::GameManager? bootstrapGm;
    private static GameObject? gmGo;
    private static GameObject? poolGo;
    private static bool done;

    // Not cached: InputHandler.OnAwake and Setup reassign inputActions.
    internal static Silksong::HeroActions? InputActions => Handler?.inputActions;

    internal static Silksong::InputHandler? Handler { get; private set; }

    internal static void Ensure() {
        if (done) return;
        done = true;
        var pd = Silksong::PlayerData.instance;
        SeedPlayerData(pd);

        gmGo = new GameObject("Silksong_GameManager");
        gmGo.SetActive(false);
        var gm = gmGo.AddComponent<Silksong::GameManager>();
        var ih = gmGo.AddComponent<Silksong::InputHandler>();
        ih.inputActions = new Silksong::HeroActions();
        Handler = ih;

        var habLen = 0;
        foreach (int v in Enum.GetValues(typeof(Silksong::GlobalEnums.HeroActionButton))) habLen = Math.Max(habLen, v + 1);
        ih.buttonQueueTimers = new float[habLen];

        // Its FindAnyObjectByType fallback skips inactive objects.
        Silksong::ManagerSingleton<Silksong::InputHandler>.UnsafeInstance = ih;
        gm.inputHandler = ih;
        gm.cameraCtrl = gmGo.AddComponent<Silksong::CameraController>();
        gm.sm = gmGo.AddComponent<Silksong::CustomSceneManager>();

        // Silk-cost FSM actions read it; a NullRef there never finishes the action and hangs the hero's FSM.
        Silksong::SilkSpool.Instance = gmGo.AddComponent<Silksong::SilkSpool>();
        gmGo.AddComponent<Silksong::UpdateBatcher>();

        // TMP_Settings.instance's untyped Resources.Load gets HK's asset; loading it logs 6 accepted missing-script
        // warnings from TMP's default assets.
        using (SilksongContext.Enter()) {
            Silksong::TMProOld.TMP_Settings.LoadDefaultSettings();
        }

        // Without constructor, so no Steam init.
        Silksong::Platform.current =
            (Silksong::Platform)FormatterServices.GetUninitializedObject(typeof(Silksong::DesktopPlatform));
        Silksong::CheatManager.SceneEntryWait = 0f;

        Object.DontDestroyOnLoad(gmGo);

        // ObjectPool.instance finds it with FindObjectOfType, which needs an active GO.
        poolGo = new GameObject("Silksong_ObjectPool");
        poolGo.AddComponent<Silksong::ObjectPool>();
        Object.DontDestroyOnLoad(poolGo);

        Silksong::GameManager._instance = gm;
        bootstrapGm = gm;
        gm.isPaused = false;
        gm.playerData = pd;
        gm.gameSettings ??= new Silksong::GameSettings();
        SilksongPM::PlayMakerPrefs.LogPerformanceWarnings = false;

        // Set by GameManager.Awake; FSM actions targeting the GameManager resolve it through this global.
        SilksongPM::PlayMakerGlobals.Instance.Variables.FindFsmGameObject("GameManager").Value = gmGo;
    }

    // Abilities/nailUpgrades are seeded from the Knight's progression at spawn (PlayerDataSync); Hornet-only bits here.
    internal static void SeedPlayerData(Silksong::PlayerData pd) {
        pd.silkMax = 9;
        pd.silk = 9;
        pd.IsSilkSpoolBroken = false;
        pd.silkSpecialLevel = 1;
        pd.maxHealth = 5;
        pd.health = 5;
        // Set by the intro we skip; the health masks only appear once it is.
        pd.bindCutscenePlayed = true;
        // A fresh PlayerData skips [DefaultValue("Hunter")].
        if (string.IsNullOrEmpty(pd.CurrentCrestID)) pd.CurrentCrestID = "Hunter";
    }

    internal static void SetHeroCtrl(Silksong::HeroController hero) {
        bootstrapGm!.hero_ctrl = hero;
        bootstrapGm.cameraCtrl = Silksong::GameCameras._instance.cameraController;

        var gameplayChild = GameCamerasBootstrap.HudCameraGo!.GetComponent<Silksong::HUDCamera>().GameplayChild;
        bootstrapGm.screenFader_fsm = Silksong::FSMUtility.LocateFSM(gameplayChild, "Screen Fader");
        bootstrapGm.inventoryFSM = gameplayChild.transform.Find("Inventory").GetComponent<SilksongPM::PlayMakerFSM>();
    }

    internal static void Cleanup() {
        // Immediate, or a hot-reload's rebuild in the same frame binds to the old instances.
        if (gmGo != null) {
            Object.DestroyImmediate(gmGo);
            gmGo = null;
        }

        if (poolGo != null) {
            Object.DestroyImmediate(poolGo);
            poolGo = null;
        }

        Silksong::GameManager._instance = null;
        Handler = null;
        done = false;
    }
}
