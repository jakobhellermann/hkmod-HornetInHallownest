#if HK1578
using System;
using GlobalEnums;
using HornetInHallownest.Core;
using UnityEngine;

namespace HornetInHallownest.Modules.Hk1578;

// 1.5.12620 transition gates only fire when facing into them, 1.5.78 on every overlap, sending Hornet back and forth when sprinting.
public sealed class GateFacingModule : ModuleBase {
    public override string Id => "gate-facing";

    public override void Initialize() {
        Detour(typeof(TransitionPoint), "OnTriggerEnter2D", OnTriggerEnter2D, typeof(Collider2D));
    }

    private static void OnTriggerEnter2D(Action<TransitionPoint, Collider2D> orig, TransitionPoint self, Collider2D movingObj) {
        var facingRight = movingObj.transform.localScale.x < 0f;
        switch (self.GetGatePosition()) {
            case GatePosition.right when !facingRight:
            case GatePosition.left when facingRight:
                return;
            case GatePosition.top:
            case GatePosition.bottom:
            case GatePosition.door:
            case GatePosition.unknown:
            default:
                orig(self, movingObj);
                break;
        }
    }
}
#endif
