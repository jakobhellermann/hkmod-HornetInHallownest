// Builds the Silksong assemblies exactly like the in-game setup does; with --shim (HK 1.5.78) also downgraded.
// usage: SilksongAssemblyBuilder --hk <hkManagedDir> --silksong <silksongManagedDir> [--shim <UnityCompat.dll>] --out <dir>
using HornetInHallownest.Core.SilksongSetup;

const string usage =
    "usage: SilksongAssemblyBuilder --hk <hkManagedDir> --silksong <silksongManagedDir> [--shim <UnityCompat.dll>] --out <dir>";
string? hkManaged = null, silksongManaged = null, shimPath = null, outDir = null;
for (var i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--hk": hkManaged = args[++i]; break;
        case "--silksong": silksongManaged = args[++i]; break;
        case "--shim": shimPath = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}\n{usage}"); return 1;
    }
}
if (hkManaged == null || silksongManaged == null || outDir == null) {
    Console.Error.WriteLine(usage);
    return 1;
}

string[] deps = shimPath == null
    ? SilksongAssemblies.Deps
    : [
        ..SilksongAssemblies.Deps, ..SilksongAssemblies.Hk1578Deps,
        ..SilksongAssemblies.PresentIn(silksongManaged, SilksongAssemblies.Hk1578PlatformDeps),
    ];
SilksongAssemblyBuilder.Build(silksongManaged, hkManaged, deps, shimPath, outDir, Console.WriteLine);
return 0;
