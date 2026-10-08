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
    private static GameObject? cocoonMarker;

    public override string Id => "map-markers";

    public override void Initialize() {
        Detour(typeof(GameMap), nameof(GameMap.PositionCompass), OnPositionCompass);
    }

    protected override void OnDeinitialize() {
        if (hornetCompass) Object.Destroy(hornetCompass);
        if (cocoonMarker) Object.Destroy(cocoonMarker);
    }

    private static void OnPositionCompass(Action<GameMap, bool> orig, GameMap self, bool posShade) {
        orig(self, posShade);
        if (posShade) return;
        try {
            UpdateCompass(self);
            UpdateCocoon(self);
        } catch (Exception e) {
            Log.Error($"[MapMarker] {e}");
        }
    }

    private static void UpdateCompass(GameMap map) {
        var knightHead = map.compassIcon.GetComponent<Renderer>();
        if (!hornetCompass) hornetCompass = CloneMarker(CompassIconName, map.compassIcon.transform, knightHead);
        if (!hornetCompass) return;
        knightHead.enabled = !HeroSwitch.HornetActive;
        hornetCompass.SetActive(HeroSwitch.HornetActive);
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
