extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Modules;

// Silksong's sorting layers have HK's names, but two of them have different ids.
public sealed class SortingLayerModule : ModuleBase {
    private static readonly Dictionary<int, string> renamedIds = new() {
        [59515797] = "Inventory",
        [1017658613] = "Scene Border"
    };

    public override string Id => "sorting-layer";

    public override void Initialize() {
        Detour(typeof(Silksong::MeshSortingOrder), "Awake", OnMeshSortingOrderAwake);
    }

    // Awake copies the serialized layer id onto the renderer.
    private void OnMeshSortingOrderAwake(Action<Silksong::MeshSortingOrder> orig, Silksong::MeshSortingOrder self) {
        if (!SortingLayer.IsValid(self.layerID)) {
            if (renamedIds.TryGetValue(self.layerID, out var name)) self.layerID = SortingLayer.NameToID(name);
            else LogErrorOnce($"layer-{self.layerID}", $"unknown Silksong sorting layer id {self.layerID} on {self.name}");
        }

        orig(self);
    }
}
