extern alias Silksong;
extern alias SilksongPM;
using System;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HornetInHallownest.Modules;

public sealed class MapMarkerModule : ModuleBase {
    private const string CompassIconName = "Compass Icon";
    private const string CocoonMarkerName = "Shade Pos";

    private static readonly Type[] silksongMapDrivers = [
        typeof(SilksongPM::PlayMakerFSM), typeof(Silksong::EventRegister), typeof(Silksong::DeactivatePlayerDataTest),
        typeof(Silksong::ShadeMarkerArrow)
    ];

    private static GameObject? hornetCompass;
    private static GameObject? hornetWideMapCompass;
    private static GameObject? cocoonMarker;
    private static string? compassScene;

    public override string Id => "map-markers";

    public override void Initialize() {
        Detour(typeof(GameMap), nameof(GameMap.PositionCompass), OnPositionCompass);
        Detour(typeof(GameMap), "Update", OnMapUpdate);
    }

    // HK picks the compass's room only as the map opens, since the Knight can't walk with it open.
    private static void OnMapUpdate(Action<GameMap> orig, GameMap self) {
        if (HeroSwitch.HornetActive && compassScene != GameManager.instance.sceneName &&
            self.GetFieldValue<bool>("displayingCompass")) {
            if (HornetSpawner.Hornet is not { isHeroInPosition: true }) return;
            self.PositionCompass(false);
        }

        orig(self);
    }

    protected override void OnDeinitialize() {
        if (hornetCompass) Object.Destroy(hornetCompass);
        if (hornetWideMapCompass) Object.Destroy(hornetWideMapCompass);
        if (cocoonMarker) Object.Destroy(cocoonMarker);
    }

    private static void OnPositionCompass(Action<GameMap, bool> orig, GameMap self, bool posShade) {
        orig(self, posShade);
        if (posShade) return;
        compassScene = GameManager.instance.sceneName;
        try {
            UpdateCompass(self);
            UpdateCocoon(self);
        } catch (Exception e) {
            Log.Error($"[MapMarker] {e}");
        }
    }

    private static void UpdateCompass(GameMap map) {
        // The map places the compass at the hero it cached, the Knight, who stays at his previous room's spot for a moment
        // after a door until HK places him.
        if (HeroSwitch.ActiveHeroGameObject is { } hero) map.SetFieldValue("hero", hero);
        var knightHead = map.compassIcon.GetComponent<Renderer>();
        if (!hornetCompass) hornetCompass = CloneMarker(CompassIconName, map.compassIcon.transform, knightHead);
        if (!hornetCompass) return;
        knightHead.enabled = !HeroSwitch.HornetActive;
        hornetCompass.SetActive(HeroSwitch.HornetActive);
    }

    // The area overview of HK's inventory map marks the hero's area with the Knight's head.
    internal static void SetWideMapCompass(Transform compassIcon, bool hornet) {
        var knightHead = compassIcon.GetComponent<Renderer>();
        if (!hornetWideMapCompass) {
            hornetWideMapCompass = CloneMarker(CompassIconName, compassIcon, knightHead);
            if (!hornetWideMapCompass) return;
            hornetWideMapCompass.AddComponent<AlphaFollower>().Source = compassIcon.GetComponent<tk2dSprite>();
        }

        knightHead.enabled = !hornet;
        hornetWideMapCompass.SetActive(hornet);
    }

    // The cocoon sits in the room's area so a quick map of another area hides it along with that area.
    private static void UpdateCocoon(GameMap map) {
        var corpseScene = Silksong::PlayerData.instance.HeroCorpseScene;
        var room = HeroSwitch.HornetActive && !string.IsNullOrEmpty(corpseScene) ? FindRoom(map, corpseScene) : null;
        if (!room) {
            if (cocoonMarker) cocoonMarker.SetActive(false);
            return;
        }

        if (!cocoonMarker)
            cocoonMarker = CloneMarker(CocoonMarkerName, room.parent,
                map.shadeMarker.GetComponentInChildren<Renderer>(true));
        if (!cocoonMarker) return;
        cocoonMarker.transform.SetParent(room.parent, false);
        cocoonMarker.transform.localPosition = new Vector3(room.localPosition.x, room.localPosition.y, -1f);
        cocoonMarker.SetActive(true);
    }

    private static Transform? FindRoom(GameMap map, string scene) {
        foreach (Transform area in map.transform) {
            var room = area.Find(scene);
            if (room) return room;
        }

        return null;
    }

    private static GameObject? CloneMarker(string name, Transform parent, Renderer? hkMarker) {
        var source = SilksongMapPrefab()?.transform.Find(name);
        if (!source) {
            Log.Error($"[MapMarker] Silksong map has no '{name}'");
            return null;
        }

        var holder = new GameObject("Map Marker Holder");
        holder.SetActive(false);
        var clone = Object.Instantiate(source.gameObject, holder.transform);
        clone.name = $"Hornet {name}";
        foreach (var type in silksongMapDrivers)
        foreach (var component in clone.GetComponentsInChildren(type, true))
            Object.DestroyImmediate(component);

        // Silksong's sorting layer ids don't exist in HK.
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) {
            renderer.gameObject.layer = parent.gameObject.layer;
            if (!hkMarker) continue;
            renderer.sortingLayerID = hkMarker.sortingLayerID;
            renderer.sortingOrder = hkMarker.sortingOrder;
        }

        clone.transform.SetParent(parent, false);
        clone.transform.localPosition = new Vector3(0f, 0f, clone.transform.localPosition.z);
        clone.SetActive(false);
        Object.Destroy(holder);
        return clone;
    }

    private static Silksong::GameMap? SilksongMapPrefab() {
        var manager = Object.FindAnyObjectByType<Silksong::InventoryMapManager>(FindObjectsInactive.Include);
        return manager ? manager.GetFieldValue<Silksong::GameMap>("gameMapPrefab") : null;
    }
}

// HK fades its map icons through the sprite's own color, which a marker cloned into one doesn't share.
internal sealed class AlphaFollower : MonoBehaviour {
    internal tk2dSprite? Source;
    private tk2dBaseSprite[] sprites = [];

    private void Awake() => sprites = GetComponentsInChildren<tk2dBaseSprite>(true);

    private void LateUpdate() {
        if (!Source) return;
        foreach (var sprite in sprites) {
            var color = sprite.color;
            color.a = Source!.color.a;
            sprite.color = color;
        }
    }
}
