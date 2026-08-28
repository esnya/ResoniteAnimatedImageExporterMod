# ResoniteAnimatedImageExporter

AnimatedPhotoExporter is a [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader) mod for [Resonite](https://resonite.com/). It adds an "Animated photo" export path to screenshot context menus, saving animated textures as lossless files (default: WebP, optional MNG) alongside the regular still image flow. The mod uses the shipped `FrooxEngine` API surface (confirmed from assemblies: `PhotoMetadata.GenerateMenuItems(ContextMenu, List<IContextMenuItemSource>)`) and exposes format selection through the ResoniteModLoader configuration UI.

## Installation

1. Install the [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader).
1. Download the latest package from [GitHub Releases](https://github.com/esnya/ResoniteAnimatedImageExporterMod/releases/latest) and merge its `rml_mods` and `rml_libs` directories into your Resonite installation.
1. Launch the game. If you want to check that the mod is working you can check your Resonite logs.

## Development

### Requirements

- .NET 10 SDK
- A Resonite installation that exposes `FrooxEngine.dll` (the default Steam paths on Windows/WSL are discovered automatically, otherwise pass `-p:ResonitePath=/absolute/path/to/Resonite`)
- [ResoniteHotReloadLib](https://github.com/Nytra/ResoniteHotReloadLib) if you plan to use hot reload

### Installation for Development

1. Clone this repository.
2. Ensure the Resonite installation path is reachable. If it lives somewhere unusual, add `-p:ResonitePath="/path/to/Resonite"` to your build/test commands.
3. Build the project: `dotnet build`

### Development Workflow

- Before committing, run `dotnet format ResoniteAnimatedImageExporter.sln --verify-no-changes --no-restore`.
- Keep local builds/tests aligned with CI by running `dotnet build ResoniteAnimatedImageExporter.sln -c Release -p:ResonitePath="..."` and `dotnet test ResoniteAnimatedImageExporter.sln -c Release -p:ResonitePath="..."`.
- Refer to `AGENTS.md` for the authoritative checklist shared with CI.

### Animated export behavior

- Adds a context menu entry via a Harmony postfix on `PhotoMetadata.GenerateMenuItems(ContextMenu, List<IContextMenuItemSource>)`.
- Export is gated by mod configuration (`Enabled`, `AnimatedFormat`), with the format surface pluggable (default: WebP). Configuration is managed through the RML config UI.
- Animated metadata detection depends on a supported animated screenshot component present in the shipped Resonite assemblies; until that is present, the export entry stays suppressed to avoid incorrect output.

### Compatibility

Release builds are compiled against the Resonite `2026.8.27.1094` public game assemblies. Rebuild the mod after a Resonite update if the log reports a missing engine method.

### Install to `rml_mods` Directory (and `rml_mods/HotReloadMods`)

Set `CopyToMods=true` when building to mirror the compiled DLL into your Resonite install automatically:

```bash
dotnet build -p:CopyToMods=true -p:ResonitePath="C:\Program Files (x86)\Steam\steamapps\common\Resonite"
```

### Hot Reload Development

Opt-in to hot reload support by dropping [ResoniteHotReloadLib](https://github.com/Nytra/ResoniteHotReloadLib) into `$(ResonitePath)/rml_libs` and passing `-p:EnableResoniteHotReloadLib=true` (include it alongside `CopyToMods=true` if you also want the HotReloadMods copy). Without that property the project omits both the reference and compiler symbol, so developers without the DLL can still build.

### Versioning & Releases

[MinVer](https://github.com/adamralph/minver) supplies semantic versions from `vX.Y.Z` tags. Push a new tag to build, test, and publish release artifacts automatically. The corrective public release is `v0.1.4`, keeping it newer than the locally distributed `0.1.2` build and the failed `v0.1.3` publication while leaving existing tags and release assets immutable.
