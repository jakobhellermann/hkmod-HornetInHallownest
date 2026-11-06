## Development

### Initial setup

[HornetInHallownest.csproj](Source/HornetInHallownest.csproj) automatically sets the
`GamePath` and `SilksongGamePath` to the default steam install location.
If they don't work for your setup, copy `HollowKnightPath.props.example` and adapt it to your setup.

If you want to be able to view silksong sources in your IDE, instead of the stubbed CI definitions, run
```sh
dotnet msbuild -t GenerateLocalRefs
```
once in order to generate the `Source/lib/Silksong.*.dll` libs from the silksong install.

### Building

On build, the mod will automatically be copied to the game's `Mods/HornetInHallownest` directory:
```sh
dotnet build
# build only one game version
dotnet build -f hk1578
dotnet build -f hk1512620
```
triggering hot reload if enabled in the modding API (if using [hk-modding/api#160](https://github.com/hk-modding/api/pull/160)).

### Distribution

To create a zip for distribution, run
```sh
dotnet publish
# Source/bin/Release/HornetInHallownest.zip
```

### How does 1.5.78 support work?

Silksong uses unity `6000.0.50`, Hollow Knight 1.5.12620 uses `6000.0.61` and Hollow Knight 1.5.78 uses `2020.2.2f1`.

Loading silksong game files into the older Hollow Knight comes with a couple of problems:

**Scripts**:
`MonoBehaviour`s from Silksong reference new Unity APIs, that don't exist on older versions or were renamed.
When silksong assemblies are copied into HK, `UnityDowngrader` will go ahead and translate renames in place, and redirect APIs to a shim dll built in [UnityCompat](./UnityCompat).

**SerializedFile incompatibilities**:
Silksong addressables contain embedded typetrees, so for the most part the resources can be load by unity in the old version. We just need to change the header version of the Bundles from v8 to v7.
However, animation curves are stored in a slightly different way, leading to crashes at runtime. Since the changes don't change the length of the data, they can be performed in place.

In order to prevent copying the gigabytes of assetbundles, they are rewritten on the fly using `AssetsTools.NET` in [UnityFsBundles](./UnityCompat/UnityFsBundles).

**Shaders**:

The shader format has changed between `2022.2.2f1` and `6000`, leading to shaders rendering as magenta.
The current fix is [ShaderFallbackModule](Source/Modules/Hk1578/ShaderFallbackModule), which replaces the silksong shaders with matching HK equivalents if possible.
This is very hacky, but could be removed in favor of re-encoding the shaders in the expected format.
