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
