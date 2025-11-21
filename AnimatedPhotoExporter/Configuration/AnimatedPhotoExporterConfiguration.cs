using ResoniteModLoader;

namespace AnimatedPhotoExporter.Configuration;

internal static class AnimatedPhotoExporterConfiguration
{
    private static ModConfiguration? configuration;

    internal static void Initialize(ModConfiguration? config)
    {
        configuration = config;
    }

    internal static bool IsEnabled => ReadOrDefault(AnimatedPhotoExporterMod.EnabledKey, defaultValue: true);

    internal static AnimatedImageFormat Format =>
        ReadOrDefault(AnimatedPhotoExporterMod.FormatKey, defaultValue: AnimatedImageFormat.WebP);

    internal static bool ExportGif =>
        ReadOrDefault(AnimatedPhotoExporterMod.ExportGifKey, defaultValue: true);

    internal static bool IntegrateScreenshotExtensions =>
        ReadOrDefault(AnimatedPhotoExporterMod.IntegrateScreenshotExtensionsKey, defaultValue: true);

    internal static bool WebpLossless =>
        ReadOrDefault(AnimatedPhotoExporterMod.WebpLosslessKey, defaultValue: false);

    internal static int WebpQuality =>
        ReadOrDefault(AnimatedPhotoExporterMod.WebpQualityKey, defaultValue: 90);

    internal static int WebpMethod =>
        ReadOrDefault(AnimatedPhotoExporterMod.WebpMethodKey, defaultValue: 3);

    private static T ReadOrDefault<T>(ModConfigurationKey<T> key, T defaultValue)
    {
        if (configuration != null && configuration.TryGetValue(key, out T? value) && value != null)
        {
            return value;
        }

        // fall back to the key's own default computation, otherwise the supplied default
        return key.Value ?? defaultValue;
    }
}
