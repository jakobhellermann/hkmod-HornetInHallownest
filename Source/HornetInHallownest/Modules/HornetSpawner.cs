extern alias Silksong;
using System;
using System.Linq;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.HornetInHallownest.Util;
using HornetPlayer.Playground;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace HornetPlayer.HornetInHallownest.Modules;

// Instantiate Hero_Hornet via Addressables and fix up everything required for Hollow Knight interop.
public sealed class HornetSpawner : IModule {
    private static GameObject? heroPrefab;

    // The live spawned HeroController (in the DontDestroyOnLoad follower).
    internal static Silksong::HeroController? RealHero =>
        HornetRoot ? HornetRoot.GetComponentInChildren<Silksong::HeroController>() : null;

    // Root of the spawned Hornet subtree. PlayMakerFix uses it to tell Hornet's FSMs (resolve actions to Silksong)
    // from HK's FSMs (resolve to HK) — every FSM under here is Silksong-authored.
    internal static GameObject? HornetRoot { get; private set; }

    public string Id => "spawn";

    // Lazy: nothing at mod init. SpawnReal is driven by the /spawn-real route or the AutoSpawn coroutine.
    public void Initialize() {
    }

    public void Deinitialize() {
        DespawnReal();
    }

    // Hero_Hornet, loaded on first access via Addressables. Addressables pulls the full dependency closure and owns
    // every bundle, so there's no double-load conflict with the game's own runtime loads (GameManager.EnsureGlobalPool
    // -> "GlobalPool", etc.); the monoscripts redirect in AddressablesBootstrap binds all m_Script PPtrs to Silksong.*.
    private static GameObject? HeroPrefab {
        get {
            if (heroPrefab) return heroPrefab;
            AddressablesBootstrap.Ensure();
            heroPrefab = Addressables.LoadAssetAsync<GameObject>("Hero_Hornet").WaitForCompletion();
            if (heroPrefab) Log.Info("[HornetSpawner] Hero_Hornet loaded via Addressables");
            return heroPrefab;
        }
    }

    // Instantiate the FULL prefab ACTIVE (no stripping) so every component's Awake/Start runs against our prefixed
    // Silksong.* types. Unity swallows per-component Awake exceptions into Player.log — that log is the "what's
    // missing" list (e.g. GameManager.instance null, input/camera singletons absent).
    internal static object SpawnReal() {
        var prefab = HeroPrefab;
        if (!prefab) return new { error = "Hero_Hornet load via Addressables failed" };
        SilksongBootstrap.Ensure();
        ToolItemManagerBootstrap.Ensure(); // #6: surgical ToolItemManager singleton (tools/crests/nail-art data source)
        CollectableItemManagerBootstrap.Ensure(); // #6: surgical CollectableItemManager singleton (inventory items)
        GlobalSettingsBootstrap.Apply(); // assign GlobalSettings _instance from the loaded SOs (bypass Addressables)
        GameCamerasBootstrap
            .Ensure(); // GameCameras.instance + CameraTarget BEFORE the hero's FSMs Awake (else camera errors)
        PlayMakerUnity2dBootstrap
            .Ensure(); // "PlayMaker Unity 2D" manager so collision/trigger proxies don't disable themselves
        // Tear down the previous spawn SYNCHRONOUSLY. Object.Destroy is deferred to end-of-frame, so the old hero would
        // still be alive when the new one's Awake runs below — its "an instance already exists" singleton branch
        // (HeroController.instance / GameManager.hero_ctrl) then skips ~3 render-relevant components, and the instance
        // ref ping-pongs across the deferred destroys -> the spawn alternates 71-visible / 68-invisible. DestroyImmediate
        // clears the old hero (and its singleton refs via OnDestroy) before we instantiate -> every spawn starts clean.
        if (HornetRoot) {
            Object.DestroyImmediate(HornetRoot);
            HornetRoot = null;
        }

        // Instantiate INACTIVE so we can patch null fields (missing-environment refs) before Awake runs, then activate.
        var staging = new GameObject("hp_real_staging");
        staging.SetActive(false);
        var inst = Object.Instantiate(prefab, staging.transform);
        inst.name = "Hornet_Real";

        var hc = inst.GetComponent<Silksong::HeroController>();
        if (hc) {
            // wallClingEffect.SetActive(false) at the end of Awake NullRefs when the field is unset.
            EnsureChildField(hc, "wallClingEffect");
            EnsureEmptyConfigs(hc);

            // Pre-set HeroController._instance to the hero BEFORE SetActive. Child components (the slash/downspike
            // objects, e.g. HeroDownAttack) cache `hc = HeroController.instance` in their own Awake. Unity does NOT
            // guarantee the hero root's Awake (which assigns _instance) runs before the children's, and the getter's
            // FindObjectOfType fallback misses anything not yet active -> some children captured a null instance and
            // NullRef'd later (HeroDownAttack.ContinueBounceTrigger -> hc.CanCustomRecoil() -> no pogo on interactive/
            // pogoable objects). Priming _instance here makes every child Awake see the live hero. HeroController.Awake
            // skips its own assignment when _instance is already set; OnDestroy clears it on despawn.
            typeof(Silksong::HeroController).SetFieldValue("_instance", hc);
        }

        // Make the hero its OWN root (no follower wrapper). HeroController.Awake calls DontDestroyOnLoad(gameObject) to
        // persist itself; if the hero is a child (of a wrapper), Unity warns "DontDestroyOnLoad only works for root
        // GameObjects". As a root it persists cleanly + warning-free. Keep it inactive until it's a positioned root,
        // then activate (so Awake/Start run once, in final state).
        var hk = Object.FindFirstObjectByType<HeroController>();
        inst.SetActive(false);
        inst.transform.SetParent(null, false);
        inst.transform.position = hk ? hk.transform.position + new Vector3(3f, 0f, 0f) : Vector3.zero;
        Object.DontDestroyOnLoad(inst);
        Object.DestroyImmediate(staging);
        // Tight SilksongContext window: SetActive(true) synchronously runs HeroController.Awake -> UpdateConfig -> FSM
        // events -> FindGameObject (name/tag lookups must resolve to Silksong objects, not HK's) + Resources.Load
        // (prefer the bundle). See SilksongContext.
        using (SilksongContext.Enter()) {
            inst.SetActive(true);
        }

        HornetRoot = inst;

        // Disable Hornet's standalone screen Vignette (child SpriteRenderer, sprite "vignette_large_v01", sorting
        // layer "Vignette"): a huge black sprite with a transparent hole pinned to the hero. In Silksong the camera
        // rig drives it; here it runs standalone and blacks out everything outside the hole. We keep HK's environment,
        // so just turn it off.
        var vignette = inst.transform.Find("Vignette");
        if (vignette) {
            vignette.gameObject.SetActive(false);
            // Strip HK's "Vignette" tag from Hornet's vignette. HK's SceneManager.orig_Start (runs on every scene load)
            // does FindGameObjectWithTag("Vignette") then an UNGUARDED LocateFSM(go,"Darkness Control").SendEvent("RESET").
            // While Hornet is active, HeroSwitch deactivates the Knight's (real, Darkness-Control-bearing) Vignette, so the
            // tag lookup falls through to Hornet's Silksong vignette — which has a PlayMakerFSM but no "Darkness Control"
            // -> LocateFSM null -> NullRef every transition (a latent HK bug only WE trigger via the tag collision; the
            // SetActive(false) above doesn't stick because Hornet's own FSM re-enables the GO). Hornet references her
            // vignette by field (HeroController.vignette), not tag, and we don't run Silksong's SceneManager, so dropping
            // the tag is safe for her and removes the cross-game collision: the lookup then returns the Knight's vignette
            // (or null while it's inert -> HK's `if (vignetteGO)` guard skips cleanly).
            vignette.gameObject.tag = "Untagged";
            Log.Info(
                "[SpawnReal] disabled standalone Vignette (radial screen darkening) + cleared its HK \"Vignette\" tag (HK SceneManager.Start collision)");
        }

        // Re-arm the global hero-box gate. HeroBox.Inactive is a STATIC bool that Die() sets true (no damage during the
        // death sequence) and HornetDeath.Revive clears. A death that didn't complete the revive (e.g. one before this
        // code existed, or a mid-death hot-reload) leaves it stuck true across reloads — the Silksong assembly's statics
        // aren't reset by the mod hot-reload — so CheckForDamage skips forever and Hornet takes no damage. Reset on spawn.
        Silksong::HeroBox.Inactive = false;

        // Apply the current active-hero state to the freshly spawned Hornet (default Knight => Hornet spawns inert but
        // visible). Switch control with Tab or POST /switch.
        // NOTE: do NOT auto-activate Hornet here — the spawn coincides with HK's scene entry, and inerting the Knight
        // mid-entry breaks HK's entry handshake (it never finishes -> Hornet ends in nirvana). A "reload stays on Hornet"
        // feature must DEFER the switch until the Knight's entry has completed (isHeroInPosition + grounded).
        HeroSwitch.SetActive(HeroSwitch.Active);

        // Bring up Hornet's HUD now that the rig + hero are up (masks self-appear via bindCutscenePlayed). The per-frame
        // HeroSwitch driver then toggles its visibility with the active hero. Non-fatal if it hiccups.
        try {
            GameCamerasBootstrap.BringUpHud(true);
        } catch (Exception e) {
            Log.Error($"[SpawnReal] BringUpHud: {e}");
        }

        // Wire gm.hero_ctrl + bare CameraController.camTarget so Silksong's hazard respawn flow
        // (PlayerDeadFromHazard → HazardRespawn) runs without NullRefs.
        SilksongBootstrap.SetHeroCtrl(hc!);

        DamageEnemyProxy.Install();

        var comps = inst.GetComponents<Component>();
        var alive = comps.Count(c => c);
        var instanceSet = (bool)Silksong::HeroController.instance;
        Log.Info(
            $"[SpawnReal] instantiated — {alive}/{comps.Length} root components non-null; HeroController.instance set: {instanceSet}");
        return new { ok = true, components = comps.Length, alive };
    }

    internal static object DespawnReal() {
        if (!HornetRoot) return new { ok = true, note = "nothing to despawn" };
        // DestroyImmediate so a follow-up /spawn-real (or a hot-reload) never races the deferred end-of-frame Destroy —
        // a lingering old hero re-grabs singletons and orphans the input binding (ia_same=false). Matches SpawnReal.
        Object.DestroyImmediate(HornetRoot);
        HornetRoot = null;
        return new { ok = true, despawned = true };
    }

    // Unity drops nested custom-serializable arrays (configs/specialConfigs : ConfigGroup[]) when the prefab loads
    // cross-build (MonoScript bound to the renamed Silksong.* assembly), leaving them null — Awake's `array.Length`
    // loop then NullRefs. Init them to empty so Awake completes. NOTE: this loses combat/crest config setup; real
    // values must be repopulated later (the data exists in the bundle, recoverable via rabex).
    private static void EnsureEmptyConfigs(Component owner) {
        foreach (var name in new[] { "configs", "specialConfigs" }) {
            var fi = owner.GetType().GetFieldInfo(name, logFailure: false);
            if (fi == null || fi.GetValue(owner) != null) continue;
            fi.SetValue(owner, Array.CreateInstance(fi.FieldType.GetElementType()!, 0));
            Log.Info($"[SpawnReal] initialized null array field '{name}' to empty");
        }
    }

    // If a (private, serialized) GameObject field is null, give it a throwaway child so Awake's
    // `field.SetActive(...)`-style derefs don't NullRef. Used to patch missing-environment refs before activation.
    private static void EnsureChildField(Component owner, string field) {
        var fi = owner.GetType().GetFieldInfo(field, logFailure: false);
        if (fi == null || fi.FieldType != typeof(GameObject)) return;
        if (fi.GetValue(owner) != null) return;
        var dummy = new GameObject(field);
        dummy.transform.SetParent(owner.transform, false);
        dummy.SetActive(false);
        fi.SetValue(owner, dummy);
        Log.Info($"[SpawnReal] patched null field '{field}' with dummy child");
    }
}
