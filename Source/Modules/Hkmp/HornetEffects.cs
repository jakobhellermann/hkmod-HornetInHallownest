#if HK1578
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HornetInHallownest.Modules.Hkmp;

// Hornet's effect objects shown on remote players, matched by the end of their path: FSMs reparent some at runtime (the
// harpoon needle hangs free in the world while thrown).
internal static class HornetEffects {
    internal static readonly string[] Entries = [
        // Harpoon
        "Harpoon Needle",
        "Harpoon Needle/Thread",
        "Hornet_harpoon_throw_effect",
        "Hornet_harpoon_dash",
        "Hornet_harpoon_grab_effect",
        // Silk Soar
        "Super Jump Antic Effect L",
        "Super Jump Antic Effect R",
        "Super Jump Charged",
        "Super Jump Charging Fader",
        "Super Jump Thread",
        "Super Jump Thread Loop",
        "Super Jump Catch Effect",
        "Super Jump Extra Ground Effect",
        "Super Jump Extra Throw Effect",
        "Super Jump Needle Throw",
        "Super Jump Needle Throw Fall",
        "Super Jump Needle Stick",
        // Movement and skill effects
        "Effects/Roar Dust", "Effects/Roar Dust Lil", "Effects/Dash Ash", "Effects/Dash Burst", "Effects/Dust Plume",
        "Effects/Burst Rocks", "Effects/Dust Wall Impact", "Effects/Wallslide Dust", "Effects/Wall Puff",
        "Effects/Backflip Puff", "Effects/Frost Antic Pt", "Effects/Damage Effect", "Effects/Fall Trail",
        "Effects/clamber_impact_effects", "Effects/wall_launch_puff", "Effects/Tool_wall_cling_effect",
        "Effects/air_sprint_effect", "Effects/umbrella_inflate_effect", "Effects/Downspike Burst",
        "Effects/Taunt Thread", "Effects/umbrella_updraftJump_fx", "Effects/Wall Bump", "Effects/Swimp Bonk Effect",
        "Effects/Can Bind Effect", "Effects/Cursed Bind Hornet", "Effects/Silk Charge Particles",
        "Effects/Silk Charge Particles Zap", "Effects/Needolin Thread", "Effects/Water Dash Burst",
        "Effects/Hornet Void Soak Particle Effect", "Effects/silk acid loop pts", "Effects/silk acid loop strand pts",
        "Effects/Harpoon Thread Ring", "Effects/Needolin Snap Effect", "Effects/Walldash Kickoff",
        "Effects/Hornet_finger_blade_cast_silk", "Effects/WallScramble Dust", "Effects/Pt RunningWater",
        "Effects/Hunter SprintAttackBurst", "Effects/Wanderer DashCombo Burst",
        "Effects/Hornet_Wall_Scramble_Burst_Effect", "Effects/Taunt Rings Flash",
        // Silk skills
        "Special Attacks/Sphere Flash", "Special Attacks/Sphere Ball", "Special Attacks/Needle Throw",
        "Special Attacks/Parry Stance Flash", "Special Attacks/Parry Clash Effect", "Special Attacks/Parry Thread",
        "Special Attacks/Parry Thread Zap", "Special Attacks/Parry DashBurst", "Special Attacks/Silk Charge Damager",
        "Special Attacks/Silk Charge DashBurst", "Special Attacks/Silk Charge WallBonk",
        "Special Attacks/attack_bomb_cast_antic_thread",
        // Binding
        "Bind Effects/Dust L", "Bind Effects/Dust R", "Bind Effects/Heal Anim", "Bind Effects/Pt Charge",
        "Bind Effects/Pt Heal", "Bind Effects/Bind Silk", "Bind Effects/Pt Heal Small", "Bind Effects/Witch Bind",
        "Bind Effects/Witch Bind Maggot Cleanse", "Bind Effects/Witch Pt Heal", "Bind Effects/shaman_focus_halo",
        "Bind Effects/hornet_quick_bind_effect", "Bind Effects/Shaman_Bind_antic_silk",
        "Bind Effects/Warrior_Bind_antic_silk", "Bind Effects/Whip_Bind_silk_antic",
        // Tools
        "Tool Effects/Tool Hornet", "Tool Effects/Tool Hornet Poison Screw Attack", "Tool Effects/Scuttle Burst",
        "Tool Effects/Syringe Glow", "Tool Effects/Syringe Glow poison", "Tool Effects/Syringe Flash",
        "Tool Effects/Syringe Flash poison", "Tool Effects/Syringe Spatter", "Tool Effects/Syringe Spatter poison",
        "Tool Effects/Craft Silk", "Tool Effects/Craft Flash", "Tool Effects/Screw Attack Impact",
        "Tool Effects/Screw Attack Burst", "Tool Effects/Screw Activation Flash", "Tool Effects/Screw Burst",
        "Tool Effects/Rosary Cannon Point", "Tool Effects/WebShot Effects", "Tool Effects/Pt Extract M",
        "Tool Effects/Pt Extract B", "Tool Effects/Extractor ChargedEffect", "Tool Effects/Pt QuickCraft",
        "Tool Effects/QuickCraft Silk", "Tool Effects/Pt Extract Swamp", "Tool Effects/Amplify_Effect",
        "Tool Effects/Pt ScrewAttack Poison Trail", "Tool Effects/Pt ScrewAttack Poison Impact",
        "Tool Effects/Pt Poison Thunk", "Tool Effects/RosaryCannon TurnShootEffect",
        // Nail slashes, per crest
        "Attacks/Default",
        "Attacks/Default/Slash", "Attacks/Default/AltSlash", "Attacks/Default/DownSlash", "Attacks/Default/UpSlash",
        "Attacks/Default/WallSlash", "Attacks/Default/Dash Stab",
        "Attacks/Cloakless",
        "Attacks/Cloakless/Kick", "Attacks/Cloakless/AltKick", "Attacks/Cloakless/DownKick",
        "Attacks/Cloakless/UpKick", "Attacks/Cloakless/WallKick", "Attacks/Cloakless/Dash Kick",
        "Attacks/Scythe",
        "Attacks/Scythe/Slash", "Attacks/Scythe/AltSlash", "Attacks/Scythe/DownSlash", "Attacks/Scythe/UpSlash",
        "Attacks/Scythe/WallSlash", "Attacks/Scythe/Dash Stab", "Attacks/Scythe/DashUpper Slash",
        "Attacks/Scythe/DownSlash New",
        "Attacks/Warrior",
        "Attacks/Warrior/Slash", "Attacks/Warrior/AltSlash", "Attacks/Warrior/DownSlash", "Attacks/Warrior/UpSlash",
        "Attacks/Warrior/WallSlash", "Attacks/Warrior/SpinSlash", "Attacks/Warrior/SpinSlashRage",
        "Attacks/Warrior/Slash Rage", "Attacks/Warrior/AltSlash Rage", "Attacks/Warrior/UpSlash Rage",
        "Attacks/Warrior/WallSlash Rage", "Attacks/Warrior/DashSlash", "Attacks/Warrior/DashSlash Rage",
        "Attacks/Wanderer",
        "Attacks/Wanderer/Slash", "Attacks/Wanderer/AltSlash", "Attacks/Wanderer/DownSlash",
        "Attacks/Wanderer/DownSlashAlt", "Attacks/Wanderer/UpSlash", "Attacks/Wanderer/WallSlash",
        "Attacks/Wanderer/DashStab", "Attacks/Wanderer/DashStabAlt", "Attacks/Wanderer/RecoilStab",
        "Attacks/Toolmaster",
        "Attacks/Toolmaster/Slash", "Attacks/Toolmaster/AltSlash", "Attacks/Toolmaster/DownSlash",
        "Attacks/Toolmaster/UpSlash", "Attacks/Toolmaster/WallSlash", "Attacks/Toolmaster/Dash Stab",
        "Attacks/Toolmaster/DownSlash New", "Attacks/Toolmaster/DownSlash New Charged",
        "Attacks/Toolmaster/Dash Stab Charged",
        "Attacks/Witch",
        "Attacks/Witch/Slash", "Attacks/Witch/AltSlash", "Attacks/Witch/DownSlash", "Attacks/Witch/UpSlash",
        "Attacks/Witch/WallSlash", "Attacks/Witch/Dash Stab Parent", "Attacks/Witch/DownSlash New",
        "Attacks/Witch/RevengeSlash", "Attacks/Witch/Followup Slash", "Attacks/Witch/Slash Prev",
        "Attacks/Shaman",
        "Attacks/Shaman/Shaman_blade_cast_effect Slash", "Attacks/Shaman/Slash", "Attacks/Shaman/AltSlash",
        "Attacks/Shaman/Shaman_blade_cast_effect DownSlash", "Attacks/Shaman/DownSlash",
        "Attacks/Shaman/Shaman_blade_cast_effect UpSlash", "Attacks/Shaman/UpSlash",
        "Attacks/Shaman/Shaman_blade_cast_effect WallSlash", "Attacks/Shaman/WallSlash",
        "Attacks/Shaman/Shaman_blade_cast_effect DashSlash", "Attacks/Shaman/DashSlash",
        // Nail arts
        "NA Charge",
        "NA Charged",
        "Attacks/Charge Slash Basic",
        "Attacks/Charge Slash Scythe",
        "Attacks/Charge Slash Upgraded",
        "Attacks/Charge Slash Wanderer",
        "Attacks/Charge Slash Toolmaster",
        "Attacks/Charge Slash Witch",
        "Attacks/Charge Slash Shaman",
        "Attacks/Charge Slash Warrior",
        "Attacks/Charge Slash Warrior/Charge Slash Warrior Right",
        "Attacks/Charge Slash Warrior/Charge Slash Warrior Left",
        "Attacks/Charge Slash Warrior Rage",
        "Attacks/Charge Slash Warrior Rage/Charge Slash Warrior Right",
        "Attacks/Charge Slash Warrior Rage/Charge Slash Warrior Left",
    ];

    private static readonly bool[] topLevel = FindTopLevel();

    internal static bool IsTopLevel(int entry) => topLevel[entry];

    internal static EffectObject[] Resolve(Transform root) {
        var byName = new Dictionary<string, List<Transform>>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
            if (!byName.TryGetValue(t.name, out var list)) byName[t.name] = list = [];
            list.Add(t);
        }

        var result = new EffectObject[Entries.Length];
        for (var i = 0; i < Entries.Length; i++) {
            var segments = Entries[i].Split('/');
            Transform? match = null;
            if (byName.TryGetValue(segments[segments.Length - 1], out var candidates))
                foreach (var candidate in candidates) {
                    if (!PathEndsWith(candidate, root, segments)) continue;
                    if (match) throw new InvalidOperationException($"effect '{Entries[i]}' is ambiguous under {root.name}");
                    match = candidate;
                }

            if (!match) throw new InvalidOperationException($"effect '{Entries[i]}' not found under {root.name}");
            result[i] = new EffectObject(match!, match!.GetComponent<Renderer>(), match.GetComponent<tk2dSpriteAnimator>());
        }

        return result;
    }

    private static bool PathEndsWith(Transform t, Transform root, string[] segments) {
        for (var k = segments.Length - 1; k >= 0; k--) {
            if (ReferenceEquals(t, root) || t.name != segments[k]) return false;
            t = t.parent;
        }

        return true;
    }

    private static bool[] FindTopLevel() {
        var result = new bool[Entries.Length];
        for (var i = 0; i < Entries.Length; i++) {
            result[i] = true;
            foreach (var other in Entries)
                if (Entries[i].StartsWith(other + "/", StringComparison.Ordinal))
                    result[i] = false;
        }

        return result;
    }
}

internal readonly record struct EffectObject(Transform Transform, Renderer? Renderer, tk2dSpriteAnimator? Animator);
#endif
