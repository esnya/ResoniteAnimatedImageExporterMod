using System.Reflection;
using AnimatedPhotoExporter.Configuration;
using HarmonyLib;
using ResoniteModLoader;
#if USE_RESONITE_HOT_RELOAD_LIB
using ResoniteHotReloadLib;
#endif

namespace AnimatedPhotoExporter;

/// <summary>Entry point for the mod.</summary>
public class AnimatedPhotoExporterMod : ResoniteMod
{
    private static readonly Assembly Assembly = typeof(AnimatedPhotoExporterMod).Assembly;
    private static readonly string HarmonyId = $"com.nekometer.esnya.{Assembly.GetName().Name}";
    private static readonly Harmony Harmony = new(HarmonyId);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> EnabledKey =
        new("Enabled", "Enable animated photo export", () => true, false, null);

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<AnimatedImageFormat> FormatKey =
        new(
            "AnimatedFormat",
            "Animated export container",
            () => AnimatedImageFormat.WebP,
            false,
            null
        );

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> IntegrateScreenshotExtensionsKey =
        new(
            "IntegrateScreenshotExtensions",
            "Embed ScreenshotExtensions metadata into exported files when that mod is present",
            () => true,
            false,
            null
        );

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<bool> ExportGifKey =
        new(
            "ExportGif",
            "Also export a GIF copy (lossy but widely supported)",
            () => true,
            false,
            null
        );

    /// <inheritdoc />
    public override string Name =>
        Assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? string.Empty;

    /// <inheritdoc />
    public override string Author =>
        Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

    /// <inheritdoc />
    public override string Version =>
        (
            Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? string.Empty
        ).Split('+')[0];

    /// <inheritdoc />
    public override string Link =>
        Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(meta => meta.Key == "RepositoryUrl")
            ?.Value ?? string.Empty;

    /// <inheritdoc />
    public override void OnEngineInit()
    {
        ModConfiguration? configuration = GetConfiguration();

        // Some RML builds return null until a config file exists. Use defaults in that case.
        if (configuration == null)
        {
            Warn("Configuration not found; using defaults until it is created or saved.");
        }

        AnimatedPhotoExporterConfiguration.Initialize(configuration);
        InitializeMod(this);
    }

#if USE_RESONITE_HOT_RELOAD_LIB
    /// <summary>Removes Harmony patches before hot reload.</summary>
    public static void BeforeHotReload()
    {
        Harmony.UnpatchAll(HarmonyId);
    }

    /// <summary>Reapplies Harmony patches after hot reload.</summary>
    /// <param name="mod">The reloaded mod.</param>
    public static void OnHotReload(ResoniteMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        ModConfiguration? configuration = mod.GetConfiguration();
        AnimatedPhotoExporterConfiguration.Initialize(configuration);

        InitializeMod(mod);
    }
#endif

    private static void InitializeMod(ResoniteMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
#if USE_RESONITE_HOT_RELOAD_LIB
        HotReloader.RegisterForHotReload(mod);
#endif
        Harmony.PatchAll();
    }
}
