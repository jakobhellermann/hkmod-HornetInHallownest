// Builds the HK 1.5.78 Silksong assemblies exactly like the in-game setup does (prefixed + downgraded).
// usage: UnityDowngrader --hk <hkManagedDir> --silksong <silksongManagedDir> --shim <UnityCompat.dll> --out <dir>
using HornetInHallownest.Core.SilksongSetup;

string? hkManaged = null, silksongManaged = null, shimPath = null, outDir = null;
for (var i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--hk": hkManaged = args[++i]; break;
        case "--silksong": silksongManaged = args[++i]; break;
        case "--shim": shimPath = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        default: Console.Error.WriteLine($"unknown argument {args[i]}"); return 1;
    }
}
if (hkManaged == null || silksongManaged == null || shimPath == null || outDir == null) {
    Console.Error.WriteLine("usage: UnityDowngrader --hk <hkManagedDir> --silksong <silksongManagedDir> --shim <UnityCompat.dll> --out <dir>");
    return 1;
}

string[] deps = [
    ..SilksongAssemblies.Deps, ..SilksongAssemblies.Hk1578Deps,
    ..SilksongAssemblies.PresentIn(silksongManaged, SilksongAssemblies.Hk1578PlatformDeps),
];
SilksongAssemblyBuilder.Build(silksongManaged, hkManaged, deps, shimPath, outDir, Console.WriteLine);
return 0;
