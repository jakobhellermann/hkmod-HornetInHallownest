#if HK1578
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HornetInHallownest.Modules.Hkmp;

// Hornet's effect objects shown on remote players. The first path segment is a name unique within Hornet: FSMs reparent
// some entries at runtime (the harpoon needle hangs free in the world while thrown), so fixed paths would break.
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
    ];

    internal static bool IsTopLevel(int entry) => Entries[entry].IndexOf('/') < 0;

    internal static Transform[] Resolve(Transform root) {
        var byName = new Dictionary<string, List<Transform>>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
            if (!byName.TryGetValue(t.name, out var list)) byName[t.name] = list = [];
            list.Add(t);
        }

        var result = new Transform[Entries.Length];
        for (var i = 0; i < Entries.Length; i++) {
            var slash = Entries[i].IndexOf('/');
            var name = slash < 0 ? Entries[i] : Entries[i].Substring(0, slash);
            if (!byName.TryGetValue(name, out var found) || found.Count != 1)
                throw new InvalidOperationException($"effect '{name}' is not a unique name under {root.name}");
            result[i] = slash < 0
                ? found[0]
                : found[0].Find(Entries[i].Substring(slash + 1)) ??
                  throw new InvalidOperationException($"effect '{Entries[i]}' not found under {root.name}");
        }

        return result;
    }
}
#endif
