## Development

### Initial setup

[HornetInHallownest.csproj](Source/HornetInHallownest.csproj) automatically sets the
`GamePath` and `SilksongGamePath` to the default steam install location.
If they don't work for your setup, create a `LocalConfig.props` with this content:

```xml
<Project>
  <PropertyGroup>
    <!-- Path to the base directory of Hollow Knight -->
    <GamePath>/path/to/Hollow Knight</GamePath>
    <!-- Optional, defaults to next to Hollow Knight -->
    <SilksongGamePath>/path/to/silksong</SilksongGamePath>
  </PropertyGroup>
</Project>
```

If you want to be able to view silksong sources in your IDE, instead of the stubbed CI definitions, run
```sh
dotnet msbuild -t GenerateLocalRefs
```
once in order to generate the `Source/lib/Silksong.*.dll` libs from the silksong install.

### Building

On build, the mod will automatically be copied to the game's `Mods/HornetInHallownest` directory:
```sh
dotnet build
```
triggering hot reload if enabled in the modding API (if using [hk-modding/api#160](https://github.com/hk-modding/api/pull/160)).

### Distribution

To create a zip for distribution, run
```sh
dotnet publish
# Source/bin/Release/HornetInHallownest.zip
```
