extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using UnityEngine;

namespace HornetPlayer.HornetInHallownest.Modules;

public sealed class TagModule : ModuleBase {
    private const string RecoilerTag = "Recoiler";

    public override string Id => "tag";

    public override void Initialize() {
        Detour(typeof(GameObject), "CompareTag", OnCompareTag);
    }

    private static bool OnCompareTag(Func<GameObject, string, bool> orig, GameObject self, string tag) {
        // Unknown to hollow knight. Always true seems to work well enough for now.
        if (tag == RecoilerTag) {
            return true;
        }

        return orig(self, tag);
    }
}
