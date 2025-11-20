using System.Globalization;
using System.Reflection;
using AnimatedPhotoExporter.Configuration;
using FrooxEngine;

#pragma warning disable IDE0002 // Keep explicit type and namespace usage for clarity in mod logs

namespace AnimatedPhotoExporter.Services;

internal static class AnimatedPhotoSaver
{
    internal static Task SaveAnimatedPhotoAsync(
        PhotoMetadata metadata,
        AnimationMetadata animation,
        bool autoSave
    )
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(animation);

        Slot? slot = metadata.Slot;
        StaticTexture2D? texture = animation.Texture;
        Uri? url = texture.URL.Value;
        Engine engine = metadata.Engine;
        DateTime timeTaken = metadata.TimeTaken.Value.ToLocalTime();
        _ = autoSave; // kept for future format branching
        if (slot == null || texture == null || url == null)
        {
            AnimatedPhotoExporterMod.Warn("Texture or slot missing; cannot export.");
            return Task.CompletedTask;
        }

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        metadata.StartGlobalTask(async () =>
        {
            try
            {
                string? atlasPath = await engine.AssetManager.GatherAssetFile(url, 100f).ConfigureAwait(false);
                if (string.IsNullOrEmpty(atlasPath))
                {
                    AnimatedPhotoExporterMod.Warn("Failed to gather atlas file for animated photo.");
                    return;
                }

                while (texture.Asset == null || texture.Asset.LoadState != AssetLoadState.FullyLoaded)
                {
                    await default(NextUpdate);
                }

                AnimatedImageFormat format = AnimatedPhotoExporterConfiguration.Format;

                string platformName = ResolvePlatformName(engine);
                // Disk export mirrors vanilla Windows flow: My Pictures/<PlatformName> with timestamped filename.
                string outputPath = BuildDiskPath(platformName, timeTaken, GetExtension(format));
                if (AnimatedImageWriter.TryWrite(animation, format, outputPath, atlasPath, out string? written)
                    && !string.IsNullOrEmpty(written))
                {
                    AnimatedPhotoExporterMod.Msg($"Animated photo saved to {written}.");

                    engine.PlatformInterface.NotifyOfScreenshot(
                        metadata.World,
                        written,
                        ScreenshotType.Mono,
                        metadata.TimeTaken.Value
                    );
                }
            }
            catch (Exception ex)
            {
                AnimatedPhotoExporterMod.Warn($"Animated photo export failed: {ex}");
            }
            finally
            {
                completion.TrySetResult();
            }
        });

        return completion.Task;
    }

    private static string BuildDiskPath(string platformName, DateTime timeTaken, string extension)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            string.IsNullOrWhiteSpace(platformName) ? "Resonite" : platformName
        );
        Directory.CreateDirectory(root);

        string baseName = timeTaken.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
        string candidate = Path.Combine(root, baseName + extension);
        int i = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(root, $"{baseName}-{i}{extension}");
            i++;
        }

        return candidate;
    }

    private static string GetExtension(AnimatedImageFormat format)
    {
        return format switch
        {
            AnimatedImageFormat.WebP => ".webp",
            AnimatedImageFormat.Mng => ".mng",
            _ => ".anim",
        };
    }

    private static string ResolvePlatformName(Engine engine)
    {
        try
        {
            object? cloud = engine
                .GetType()
                .GetProperty("Cloud", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(engine);

            object? platform = cloud?
                .GetType()
                .GetProperty("Platform", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(cloud);

            object? name = platform?
                .GetType()
                .GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(platform);

            string? nameString = name as string ?? name?.ToString();
            return string.IsNullOrWhiteSpace(nameString) ? "Resonite" : nameString;
        }
        catch
        {
            return "Resonite";
        }
    }
}
