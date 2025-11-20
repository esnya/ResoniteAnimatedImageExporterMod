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
