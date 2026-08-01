extern alias Silksong;
extern alias SilksongPM;
using System.Collections;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using HornetInHallownest.Util;
using Modding;
using UnityEngine;
using SCurrencyCounter = Silksong::CurrencyCounter;
using SCurrencyCounterIcon = Silksong::CurrencyCounterIcon;
using SCurrencyType = Silksong::CurrencyType;
using SPlayerData = Silksong::PlayerData;

namespace HornetInHallownest.Modules;

// HK's geo shown as Hornet's Rosary counter (Silksong's Money currency reads Silksong.PlayerData.geo). The counter is a
// transient popup that's invisible until shown; ForceCurrencyCountersAppear pins it up like HK's always-on counter.
public sealed class CurrencyModule : ModuleBase {
    public override string Id => "currency";

    public override void Initialize() {
        Silksong::CheatManager.ForceCurrencyCountersAppear = true;
        ModHooks.SetPlayerIntHook += OnSetInt;
        GameCamerasBootstrap.HornetHudShown += OnHudShown;

        // HK's geo is the single source of truth.
        Detour(typeof(Silksong::HeroController), "TakeGeo",
            (System.Action<System.Action<Silksong::HeroController, int>, Silksong::HeroController, int>)
            ((_, _, amount) => PlayerData.instance.TakeGeo(amount)), typeof(int));
        Detour(typeof(Silksong::HeroController), "AddGeo",
            (System.Action<System.Action<Silksong::HeroController, int>, Silksong::HeroController, int>)
            ((_, _, amount) => PlayerData.instance.AddGeo(amount)), typeof(int));
    }

    protected override void OnDeinitialize() {
        GameCamerasBootstrap.HornetHudShown -= OnHudShown;
        ModHooks.SetPlayerIntHook -= OnSetInt;
        Silksong::CheatManager.ForceCurrencyCountersAppear = false;
    }

    private void OnHudShown() {
        StartCoroutine(ShowDeferred());
    }

    // Deferred one frame: the HUD GO reactivates during the switch, and showing while its OnEnable/LateUpdate churn is
    // mid-flight leaves the counter's fade-group at alpha 0. A frame later it's settled.
    private static IEnumerator ShowDeferred() {
        yield return null;
        Show();
    }

    private static void Show() {
        var ss = SPlayerData.instance;
        var hk = PlayerData.instance;
        ss.geo = hk.geo;

        // setStackVisible reparents the stack under Hud Canvas + enables its positioner; ForceCurrencyCountersAppear only
        // pins render visibility, so without it the counter can stay in its out-parent (HudCamera) over the health HUD.
        SCurrencyCounter.Show(SCurrencyType.Money, setStackVisible: true);

        foreach (var c in Object.FindObjectsByType<SCurrencyCounter>(FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None)) {
            if (c.GetFieldValue<SCurrencyType>("currencyType") != SCurrencyType.Money) continue;

            // The icon is a separate FSM (RestartOnEnable) that hides in its Init state until APPEAR, so it vanishes on
            // every HUD/scene reactivation. Setting "No Disappear" var makes Init go visible.
            var icon = c.GetComponentInChildren<SCurrencyCounterIcon>(true);
            var iconFsm = icon.GetFieldValue<SilksongPM::PlayMakerFSM>("fsm");
            var noDisappear = iconFsm ? iconFsm.FsmVariables.FindFsmBool("No Disappear") : null;
            noDisappear?.Value = true;
            icon.Appear();
        }
    }

    private static int OnSetInt(string name, int value) {
        if (name != "geo") return value;
        
        var ss = SPlayerData.instance;
        var delta = value - ss.geo;
        ss.geo = value;
        if (!HeroSwitch.HornetActive || delta == 0) return value;

        OnGeoModified(delta);

        return value;
    }

    private static void OnGeoModified(int delta) {
        SCurrencyCounter.RefreshStartCount(SCurrencyType.Money);
        if (delta > 0) SCurrencyCounter.Add(delta, SCurrencyType.Money);
        else SCurrencyCounter.Take(-delta, SCurrencyType.Money);
    }
}
