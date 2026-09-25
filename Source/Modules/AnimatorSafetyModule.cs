extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine;
using SCreateChild = Silksong::HutongGames.PlayMaker.Actions.CreateChildWithParentedOffset;

namespace HornetInHallownest.Modules;

// Silksong's CreateChildWithParentedOffset force-ticks a fresh instance's Animator (Update(0f)) straight after
// Instantiate. On HK 1.5.78 (Unity 2020.2) that call crashes the player natively when the Animator carries a Unity 6
// controller that has not been bound yet. Re-implement the action without the forced tick; the Animator still starts
// on its own first regular update.
public sealed class AnimatorSafetyModule : ModuleBase {
    public override string Id => "animator-safety";

    public override void Initialize() {
        Detour(typeof(SCreateChild), "OnEnter", OnCreateChildEnter);
    }

    private void OnCreateChildEnter(Action<SCreateChild> orig, SCreateChild self) {
        var prefab = self.Prefab.Value;
        if (!prefab) {
            self.Finish();
            return;
        }

        var parent = self.Fsm.GetOwnerDefaultTarget(self.Parent);
        LogDebugOnce($"cc:{prefab.name}", $"CreateChildWithParentedOffset '{prefab.name}' (fsm '{self.Fsm.Name}' on " +
                                          $"'{(parent ? parent.name : "-")}') without forced Animator tick");

        var offsetParent = new GameObject($"{prefab.name} Offset Parent");
        if (parent) offsetParent.transform.SetParent(parent.transform);
        var child = UnityEngine.Object.Instantiate(prefab);
        var localPosition = child.transform.localPosition;
        child.transform.SetParent(offsetParent.transform);
        child.transform.localPosition = localPosition;
        offsetParent.transform.localPosition = -localPosition + self.Offset.Value;
        self.StoreObject.Value = child;
        self.Finish();
    }
}
