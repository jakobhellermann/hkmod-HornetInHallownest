using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace HornetInHallownest.Core;

// Rename a set of assemblies to "<prefix>.<name>", fixing cross-references within the set.
internal static class AssemblyPrefixer {
    internal static string PrefixedName(string prefix, string original) => $"{prefix}.{original}";

    // Namespace prefixing breaks PlayMaker's FsmStateAction lookup, so only the assembly identity is renamed.
    internal static void Prefix(string prefix, IReadOnlyCollection<AssemblyDefinition> asms) {
        var rename = asms.ToDictionary(a => a.Name.Name, a => PrefixedName(prefix, a.Name.Name));
        foreach (var asm in asms) {
            var module = asm.MainModule;
            foreach (var r in module.AssemblyReferences)
                if (rename.TryGetValue(r.Name, out var newRefName)) r.Name = newRefName;
            var newName = rename[asm.Name.Name];
            asm.Name.Name = newName;
            module.Name = newName + ".dll";
        }
    }
}
