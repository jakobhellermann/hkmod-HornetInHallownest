using System.IO;
using System.Linq;

namespace HornetInHallownest.Core.SilksongSetup;

// The assemblies the setup installs into HK's Managed folder.
internal static class SilksongAssemblies {
    internal const string Prefix = "Silksong";

    // Unity 6 API surface Unity 2020.2 lacks; only installed on HK 1.5.78.
    internal const string Shim = "HornetInHallownest.UnityCompat";

    // The shim and its dependencies, shipped side by side.
    internal static readonly string[] ShimFiles = [Shim];

    // Silksong assemblies HK lacks, installed under their own name.
    internal static readonly string[] Deps = [
        "Unity.Addressables",
        "Unity.ResourceManager",
        "Unity.Profiling.Core",
        "Unity.Mathematics",
        "Unity.Burst",
        "Unity.Burst.Unsafe",
        "Newtonsoft.Json.UnityConverters",
        "TeamCherry.Splines",
        "Coffee.SoftMaskForUGUI",
    ];

    // Additionally lacking on HK 1.5.78 and referenced by the installed assemblies. HK 1.5.12620 ships its own.
    internal static readonly string[] Hk1578Deps = [
        "TeamCherry.SharedUtils", // TeamCherry.Splines references the unprefixed one
        "TeamCherry.Cinematics",
        "TeamCherry.BuildBot",
        "Unity.InputSystem",
        "com.rlabrecque.steamworks.net",
    ];

    // Only in some platforms' Silksong builds (Windows), whose assemblies then reference them; installed on HK 1.5.78
    // when present.
    internal static readonly string[] Hk1578PlatformDeps = ["SharpDX", "SharpDX.DirectInput", "XGamingRuntime"];

    internal static string[] PresentIn(string managed, string[] names) =>
        [..names.Where(n => File.Exists($"{managed}/{n}.dll"))];

    // Silksong assemblies whose types conflict with HK's, installed as Silksong.*.
    internal static readonly string[] PrefixInputs = [
        "Assembly-CSharp",
        "Assembly-CSharp-firstpass",
        "PlayMaker",
        "ConditionalExpression",
        "TeamCherry.NestedFadeGroup",
        "TeamCherry.Localization",
        "TeamCherry.SharedUtils",
    ];

    internal static readonly string[] Prefixed = [..PrefixInputs.Select(n => AssemblyPrefixer.PrefixedName(Prefix, n))];
}
