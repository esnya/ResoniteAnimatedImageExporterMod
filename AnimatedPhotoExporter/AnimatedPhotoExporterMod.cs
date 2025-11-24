using System.Reflection;
using AnimatedPhotoExporter.Configuration;
using AnimatedPhotoExporter.Services;
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
    private static ModConfiguration? configuration;
    private static bool patchesApplied;

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
    internal static readonly ModConfigurationKey<bool> WebpLosslessKey =
        new(
            "WebpLossless",
            "Use lossless WebP encoding (turn off for faster, smaller lossy output)",
            () => false,
            false,
            null
        );

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<int> WebpQualityKey =
        new(
            "WebpQuality",
            "WebP quality when lossless is disabled (1-100)",
            () => 90,
            false,
            null
        );

    [AutoRegisterConfigKey]
    internal static readonly ModConfigurationKey<int> WebpMethodKey =
        new(
            "WebpMethod",
            "WebP encoding method 0 (fastest) – 6 (best)",
            () => 3,
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
        configuration = GetConfiguration();

        // Some RML builds return null until a config file exists. Use defaults in that case.
        if (configuration == null)
        {
            Warn("Configuration not found; using defaults until it is created or saved.");
        }

        AnimatedPhotoExporterConfiguration.Initialize(configuration);
        RegisterConfigurationWatch(configuration);
        InitializeMod(this);
    }

#if USE_RESONITE_HOT_RELOAD_LIB
    /// <summary>Removes Harmony patches before hot reload.</summary>
    public static void BeforeHotReload()
    {
        Harmony.UnpatchAll(HarmonyId);
        patchesApplied = false;
    }

    /// <summary>Reapplies Harmony patches after hot reload.</summary>
    /// <param name="mod">The reloaded mod.</param>
    public static void OnHotReload(ResoniteMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        configuration = mod.GetConfiguration();
        AnimatedPhotoExporterConfiguration.Initialize(configuration);
        RegisterConfigurationWatch(configuration);

        InitializeMod(mod);
    }
#endif

    private static void InitializeMod(ResoniteMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
#if USE_RESONITE_HOT_RELOAD_LIB
        HotReloader.RegisterForHotReload(mod);
#endif
        ScreenshotExtensionsIntegration.Refresh();
        RefreshPatchState();
    }

    private static void RegisterConfigurationWatch(ModConfiguration? config)
    {
        if (config == null)
        {
            return;
        }

        config.OnThisConfigurationChanged -= OnConfigurationChanged;
        config.OnThisConfigurationChanged += OnConfigurationChanged;
    }

    private static void OnConfigurationChanged(ConfigurationChangedEvent change)
    {
        if (change.Key.Name == EnabledKey.Name)
        {
            RefreshPatchState();
        }

        if (change.Key.Name == IntegrateScreenshotExtensionsKey.Name)
        {
            ScreenshotExtensionsIntegration.Refresh();
        }
    }

    private static void RefreshPatchState()
    {
        bool enabled = AnimatedPhotoExporterConfiguration.IsEnabled;
        if (enabled && !patchesApplied)
        {
            Harmony.PatchAll();
            patchesApplied = true;
            return;
        }

        if (!enabled && patchesApplied)
        {
            Harmony.UnpatchAll(HarmonyId);
            patchesApplied = false;
        }
    }
}
