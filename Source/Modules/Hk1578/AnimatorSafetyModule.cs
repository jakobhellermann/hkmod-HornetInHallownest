#if HK1578
extern alias Silksong;
using System;
using HornetInHallownest.Core;
using UnityEngine;
using SCreateChild = Silksong::HutongGames.PlayMaker.Actions.CreateChildWithParentedOffset;

namespace HornetInHallownest.Modules.Hk1578;

// Silksong's CreateChildWithParentedOffset force-ticks the new instance's Animator, which crashes Unity 2020 natively
// on a not yet bound Unity 6 controller. Same action without that Update(0f); the Animator starts on its own.
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

        var parent = Silksong::FSMUtility.GetSafe(self.Parent, self);
        LogDebugOnce($"cc:{prefab.name}", $"CreateChildWithParentedOffset '{prefab.name}' (fsm '{self.Fsm.Name}' on " +
                                          $"'{(parent ? parent.name : "-")}') without forced Animator tick");

        var offsetParent = new GameObject($"{prefab.name} Offset Parent");
        if (parent) offsetParent.transform.SetParent(parent.transform);
        var child = UnityEngine.Object.Instantiate(prefab, offsetParent.transform, true);
        var localPosition = child.transform.localPosition;
        child.transform.localPosition = localPosition;
        offsetParent.transform.localPosition = -localPosition + self.Offset.Value;
        self.StoreObject.Value = child;
        self.Finish();
    }
}
#endif
