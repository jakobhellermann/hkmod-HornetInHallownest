#if HK1512620
using System;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;

namespace HornetInHallownest.Modules;

// HK's HUD Appearance option (Large/Small/None) also applies to Hornet's HUD.
public sealed class HudAppearanceModule : ModuleBase {
    public override string Id => "hud-appearance";

    public override void Initialize() {
        Detour(typeof(HudGlobalHide), "set_" + nameof(HudGlobalHide.IsHidden), OnSetHudAppearance);
        Detour(typeof(HudGlobalHide), "set_" + nameof(HudGlobalHide.IsReduced), OnSetHudAppearance);
    }

    private static void OnSetHudAppearance(Action<bool> orig, bool value) {
        orig(value);
        GameCamerasBootstrap.SetHudAppearance(HudGlobalHide.IsHidden, HudGlobalHide.IsReduced);
    }
}
#endif
