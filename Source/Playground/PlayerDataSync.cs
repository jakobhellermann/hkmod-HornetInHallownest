extern alias Silksong;
using System;
using System.Collections.Generic;
using Modding;
using SPlayerData = Silksong::PlayerData;

namespace HornetPlayer.Playground;

// HK (Knight) progression -> Hornet's Silksong PlayerData. Seed() at spawn is authoritative; the hooks mirror live
// pickups. Grant-only. SS writes are direct/unhooked, so no feedback loop. See docs/playerdata-sync.md.
internal static class PlayerDataSync {
    // HK field name -> how to apply it to Silksong PD. Single source: Seed() iterates these, the hooks dispatch by name.
    private static readonly Dictionary<string, Action<SPlayerData, bool>> bools = new() {
        ["hasDash"] = (ss, v) => ss.hasDash = v,
        ["hasWalljump"] = (ss, v) => ss.hasWalljump = v,
        ["hasDoubleJump"] = (ss, v) => ss.hasDoubleJump = v,
        ["hasSuperDash"] = (ss, v) => ss.hasHarpoonDash = v, // Harpoon Dash
        ["hasDreamNail"] = (ss, v) => ss.hasNeedolin = v, // Needolin
        ["hasKingsBrand"] = (ss, v) => ss.hasSuperJump = v, // Silk Soar
        // HK's 3 nail arts + Shade Cloak -> Hornet charge attack + silk-heart regen tiers (see RecomputeArts).
        ["hasCyclone"] = (ss, v) => RecomputeArts(ss, cyclone: v),
        ["hasDashSlash"] = (ss, v) => RecomputeArts(ss, dashSlash: v),
        ["hasUpwardSlash"] = (ss, v) => RecomputeArts(ss, greatSlash: v), // Great Slash
        ["hasShadowDash"] = (ss, v) => RecomputeArts(ss, shadeCloak: v) // Shade Cloak
    };

    // Silk skills unlock at level thresholds and are additive (one-way); hasSilkSpecial is the shared gate.
    private static readonly Dictionary<string, Action<SPlayerData, int>> ints = new() {
        ["nailSmithUpgrades"] = (ss, v) => ss.nailUpgrades = v,
        ["fireballLevel"] = (ss, v) => {
            if (v >= 1) { ss.hasNeedleThrow = true; ss.hasSilkSpecial = true; } // Silkspear
            if (v >= 2) ss.hasSilkCharge = true; // Sharpdart
        },
        ["quakeLevel"] = (ss, v) => {
            if (v >= 1) { ss.hasParry = true; ss.hasSilkSpecial = true; } // Cross Stitch
            if (v >= 2) ss.hasSilkBomb = true; // Rune Rage
        },
        ["screamLevel"] = (ss, v) => {
            if (v >= 1) { ss.hasThreadSphere = true; ss.hasSilkSpecial = true; } // Thread Storm
            if (v >= 2) ss.hasSilkBossNeedle = true; // Pale Nails
        },
        ["maxHealthBase"] = (ss, v) => { ss.maxHealthBase = v; ss.maxHealth = v; }, // mask capacity (not current HP)
        ["MPReserveMax"] = (ss, v) => ss.silkMax = 9 + v / 33 * 3 // soul vessels -> silk capacity (base 9, +3/vessel)
    };

    internal static void Install() {
        ModHooks.SetPlayerBoolHook += OnSetBool;
        ModHooks.SetPlayerIntHook += OnSetInt;
    }

    internal static void Cleanup() {
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        ModHooks.SetPlayerIntHook -= OnSetInt;
    }

    internal static void Seed() {
        var hk = PlayerData.instance;
        var ss = SPlayerData.instance;
        if (hk == null || ss == null) return;
        foreach (var kv in bools) kv.Value(ss, hk.GetBool(kv.Key));
        foreach (var kv in ints) kv.Value(ss, hk.GetInt(kv.Key));
        ss.hasBrolly = true; // no HK equivalent
        Log.Info("[PlayerDataSync] seeded HK -> Hornet PlayerData");
    }

    // HK's 3 nail arts + Shade Cloak fill Hornet's leftover unlocks: 1st art -> charge attack; the rest (2nd art, 3rd
    // art, Shade Cloak) each add a silk-heart regen tier (silkRegenMax 0-3). Params override HK PD for the field being set
    // right now (the SetBool hook fires before the write, so HK PD is stale for that one); null => read HK.
    private static void RecomputeArts(SPlayerData ss, bool? cyclone = null, bool? dashSlash = null,
        bool? greatSlash = null, bool? shadeCloak = null) {
        var hk = PlayerData.instance;
        if (hk == null) return;
        var arts = ((cyclone ?? hk.hasCyclone) ? 1 : 0)
                   + ((dashSlash ?? hk.hasDashSlash) ? 1 : 0)
                   + ((greatSlash ?? hk.hasUpwardSlash) ? 1 : 0);
        ss.hasChargeSlash = arts >= 1; // Needle Strike (charge attack)
        ss.silkRegenMax = (arts >= 2 ? 1 : 0) + (arts >= 3 ? 1 : 0) + ((shadeCloak ?? hk.hasShadowDash) ? 1 : 0);
    }

    private static bool OnSetBool(string name, bool orig) {
        var ss = SPlayerData.instance;
        if (ss != null && bools.TryGetValue(name, out var apply)) apply(ss, orig);
        return orig;
    }

    private static int OnSetInt(string name, int orig) {
        var ss = SPlayerData.instance;
        if (ss == null || !ints.TryGetValue(name, out var apply)) return orig;
        apply(ss, orig);
        // A mask gained mid-play bumps maxHealth silently (AddToMaxHealth sends no HUD event); appear the new mask.
        if (name == "maxHealthBase") BundleSpike.RefreshMaxHealthHud();
        return orig;
    }

    // POST /grant-kit — full kit regardless of HK progression (playground testing).
    internal static object GrantFullKit() {
        var ss = SPlayerData.instance;
        if (ss == null) return new { error = "no Silksong PlayerData" };
        ss.hasDash = ss.hasWalljump = ss.hasDoubleJump = ss.hasBrolly = ss.hasSuperJump = ss.hasHarpoonDash = true;
        ss.hasChargeSlash = ss.hasQuill = ss.hasParry = ss.hasNeedolin = ss.hasNeedleThrow = true;
        ss.hasThreadSphere = ss.hasSilkSpecial = ss.hasSilkCharge = ss.hasSilkBomb = ss.hasSilkBossNeedle = true;
        ss.hasNeedolinMemoryPowerup = true;
        return new { ok = true };
    }
}
