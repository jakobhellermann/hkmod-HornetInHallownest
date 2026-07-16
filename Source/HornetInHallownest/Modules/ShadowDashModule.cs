extern alias Silksong;
using System;
using HornetPlayer.HornetInHallownest.Core;
using HornetPlayer.Playground;
using UnityEngine;
using SHeroController = Silksong::HeroController;

namespace HornetPlayer.HornetInHallownest.Modules;

// Apply shade cloak to hornet dash (TODO harpoon as well)
// - shadow gates read HeroController.instance
// - void tendril disable collider through SHADOW DASH START/END
public sealed class ShadowDashModule : ModuleBase {
    private const float ShadowDashGracePeriod = 0.1f;

    private bool shadowDashWindowActive;
    private float endTimer;

    public override string Id => "shadow-dash";

    public override void Initialize() {
        Detour(typeof(ShadowGateColliderControl), "FixedUpdate", OnGateFixedUpdate);
    }

    private static bool HasShadowDash => PlayerData.instance is { hasShadowDash: true };

    public override void HornetActiveUpdate(SHeroController hero) {
        var dashing = hero.cState is { dashing: true };
        if (dashing && HasShadowDash) {
            endTimer = ShadowDashGracePeriod;
            if (!shadowDashWindowActive) {
                shadowDashWindowActive = true;
                ShadowDashStart();
            }
            return;
        }

        if (shadowDashWindowActive) {
            endTimer -= Time.deltaTime;
            if (endTimer <= 0f) {
                shadowDashWindowActive = false;
                ShadowDashEnd();
            }
        }
    }

    public override void HornetToggled(bool active) {
        if (!active && shadowDashWindowActive) {
            shadowDashWindowActive = false;
            ShadowDashEnd();
        }
    }

    private static void ShadowDashStart() {
        PlayMakerFSM.BroadcastEvent("SHADOW DASH START");
    }

    private static void ShadowDashEnd() {
        PlayMakerFSM.BroadcastEvent("SHADOW DASH END");
    }

    private static void OnGateFixedUpdate(Action<ShadowGateColliderControl> orig, ShadowGateColliderControl self) {
        if (!HeroSwitch.HornetActive) {
            orig(self);
            return;
        }
        if (!HasShadowDash) return;
        var col = self.disableCollider;
        if (!col) return;
        var hc = SHeroController.SilentInstance;
        var dashing = hc != null && hc.cState is { dashing: true };
        if (dashing && col.enabled) col.enabled = false;
        else if (!dashing && !col.enabled) col.enabled = true;
    }
}
