extern alias Silksong;
using System;
using System.Collections.Generic;
using HornetInHallownest.Bootstrap;
using HornetInHallownest.Core;
using Modding;
using SPlayerData = Silksong::PlayerData;

namespace HornetInHallownest.Modules;

// HK (Knight) progression -> Hornet's Silksong PlayerData. SyncHkToSs() at spawn is the full authoritative sync; the
// ModHooks mirror live pickups. Grant-only, and the Silksong writes are direct/unhooked, so there's no feedback loop.
// See docs/playerdata-sync.md.
public sealed class PlayerDataSyncModule : ModuleBase {
    // HK field name -> how to apply it to Silksong PD. Single source: SyncHkToSs() iterates these, hooks dispatch by name.
    private static readonly Dictionary<string, Action<SPlayerData, bool>> bools = new() {
        ["hasDash"] = (ss, v) => ss.hasDash = v,
        ["hasWalljump"] = (ss, v) => ss.hasWalljump = v,
        ["hasJournal"] = (ss, v) => ss.hasJournal = v,
        ["hasDoubleJump"] = (ss, v) => ss.hasDoubleJump = v,
        ["hasSuperDash"] = (ss, v) => ss.hasHarpoonDash = v, // Harpoon Dash
        ["hasDreamNail"] = (ss, v) => ss.hasNeedolinMemoryPowerup = v, // Elegy of the Deep (Needolin + down)
        ["hasDreamGate"] = (ss, v) => ss.UnlockedFastTravelTeleport = v, // Beastling Call (Needolin + up)
        ["hasKingsBrand"] = (ss, v) => ss.hasSuperJump = v, // Silk Soar
        // HK's 3 nail arts + Shade Cloak -> Hornet charge attack + silk-heart regen tiers (see RecomputeArts).
        ["hasCyclone"] = (ss, v) => RecomputeArts(ss, cyclone: v),
        ["hasDashSlash"] = (ss, v) => RecomputeArts(ss, dashSlash: v),
        ["hasUpwardSlash"] = (ss, v) => RecomputeArts(ss, greatSlash: v), // Great Slash
        ["hasShadowDash"] = (ss, v) => RecomputeArts(ss, shadeCloak: v) // Shade Cloak
    };

    private static readonly Dictionary<string, Action<SPlayerData, int>> ints = new() {
        // Nail upgrades set tool pouches + crafting kits as well
        ["nailSmithUpgrades"] = (ss, v) => {
            ss.nailUpgrades = v;
            ss.ToolPouchUpgrades = v;
            ss.ToolKitUpgrades = v;
        },
        // Any silk skill sets hasSilkSpecial
        ["fireballLevel"] = (ss, v) => {
            // Silkspear
            if (v >= 1) {
                ss.hasNeedleThrow = true;
                ss.hasSilkSpecial = true;
            }

            if (v >= 2) ss.hasSilkCharge = true; // Sharpdart
        },
        ["quakeLevel"] = (ss, v) => {
            // Cross Stitch
            if (v >= 1) {
                ss.hasParry = true;
                ss.hasSilkSpecial = true;
            }

            if (v >= 2) ss.hasSilkBomb = true; // Rune Rage
        },
        ["screamLevel"] = (ss, v) => {
            // Thread Storm
            if (v >= 1) {
                ss.hasThreadSphere = true;
                ss.hasSilkSpecial = true;
            }

            if (v >= 2) ss.hasSilkBossNeedle = true; // Pale Nails
        },
        // mask capacity (not current HP)
        ["maxHealthBase"] = (ss, v) => {
            ss.maxHealthBase = v;
            ss.maxHealth = v;
        },
        // Soul Vessel progression -> silk spools (9 -> 18)
        ["MPReserveMax"] = (ss, v) => RecomputeSilk(ss, mpReserveMax: v),
        ["vesselFragments"] = (ss, v) => RecomputeSilk(ss, vesselFragments: v)
    };

    public override string Id => "playerdata-sync";

    public override void Initialize() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
        ModHooks.SetPlayerIntHook += OnSetInt;
    }

    protected override void OnDeinitialize() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        ModHooks.SetPlayerIntHook -= OnSetInt;
    }

    internal static void SyncHkToSs() {
        var hk = PlayerData.instance;
        var ss = SPlayerData.instance;
        foreach (var kv in bools) kv.Value(ss, hk.GetBool(kv.Key));
        foreach (var kv in ints) kv.Value(ss, hk.GetInt(kv.Key));
        ss.hasBrolly = true; // no HK equivalent
        // Plain Needolin does nothing in HK, so it's always there.
        ss.hasNeedolin = true;
    }

    // HK's 3 nail arts + Shade Cloak fill Hornet's leftover unlocks: 1st art -> charge attack; the rest (2nd art, 3rd
    // art, Shade Cloak) each add a silk-heart regen tier (silkRegenMax 0-3). Params override HK PD for the field being set
    // right now (the SetBool hook fires before the write, so HK PD is stale for that one); null => read HK.
    private static void RecomputeArts(SPlayerData ss, bool? cyclone = null, bool? dashSlash = null,
        bool? greatSlash = null, bool? shadeCloak = null) {
        var hk = PlayerData.instance;
        var arts = ((cyclone ?? hk.hasCyclone) ? 1 : 0)
                   + ((dashSlash ?? hk.hasDashSlash) ? 1 : 0)
                   + ((greatSlash ?? hk.hasUpwardSlash) ? 1 : 0);
        ss.hasChargeSlash = arts >= 1; // Needle Strike (charge attack)
        ss.silkRegenMax = (arts >= 2 ? 1 : 0) + (arts >= 3 ? 1 : 0) + ((shadeCloak ?? hk.hasShadowDash) ? 1 : 0);
    }

    // Soul vessel -> silk spools. Both full vessels (MPReserveMax, +33 each) and loose fragments
    // (vesselFragments 0-2) count: 9 HK fragments map to Silksong's 18 spool parts (2 parts = +1 silk), so each HK
    // fragment is +1 silkMax (base 9 -> 18 at 9 fragments). Params override HK PD for the field being set right now (the
    // SetInt hook fires before the write); null => read HK.
    private static void RecomputeSilk(SPlayerData ss, int? mpReserveMax = null, int? vesselFragments = null) {
        var hk = PlayerData.instance;
        int fragments = (mpReserveMax ?? hk.MPReserveMax) / 33 * 3 + (vesselFragments ?? hk.vesselFragments);
        ss.silkMax = 9 + fragments;
    }

    private static bool OnSetBool(string name, bool orig) {
        var ss = SPlayerData.instance;
        if (bools.TryGetValue(name, out var apply)) apply(ss, orig);
        return orig;
    }

    private static int OnSetInt(string name, int orig) {
        var ss = SPlayerData.instance;
        if (!ints.TryGetValue(name, out var apply)) return orig;
        apply(ss, orig);
        // Refresh hud if sync made changes
        if (name == "maxHealthBase") HudRefresh.RefreshMaxHealthHud();
        if (name is "MPReserveMax" or "vesselFragments") HudRefresh.RefreshMaxSilkHud();
        return orig;
    }

    // POST /grant-kit: full kit regardless of HK progression (playground testing). The route lives on the debug branch.
    // ReSharper disable once UnusedMember.Global
    internal static object GrantFullKit() {
        var ss = SPlayerData.instance;
        ss.hasDash = ss.hasWalljump = ss.hasDoubleJump = ss.hasBrolly = ss.hasSuperJump = ss.hasHarpoonDash = true;
        ss.hasChargeSlash = ss.hasQuill = ss.hasParry = ss.hasNeedolin = ss.hasNeedleThrow = true;
        ss.hasThreadSphere = ss.hasSilkSpecial = ss.hasSilkCharge = ss.hasSilkBomb = ss.hasSilkBossNeedle = true;
        ss.hasNeedolinMemoryPowerup = true;
        return new { ok = true };
    }
}
