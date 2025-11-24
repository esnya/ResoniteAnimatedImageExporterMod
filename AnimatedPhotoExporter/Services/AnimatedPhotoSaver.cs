using System.Globalization;
using AnimatedPhotoExporter.Configuration;
using FrooxEngine;
#if DEBUG
using System.Diagnostics;
#endif
using System.Threading;

#pragma warning disable IDE0002 // Keep explicit type and namespace usage for clarity in mod logs

namespace AnimatedPhotoExporter.Services;

internal static class AnimatedPhotoSaver
{
    private static readonly TimeSpan TextureLoadTimeout = TimeSpan.FromSeconds(30);

    internal static Task SaveAnimatedPhotoAsync(
        PhotoMetadata metadata,
        AnimationMetadata animation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(animation);

        if (animation.Texture is not StaticTexture2D texture)
        {
            AnimatedPhotoExporterMod.Warn("Texture missing; cannot export animated photo.");
            return Task.CompletedTask;
        }

        Uri? url = texture.URL.Value;
        Engine engine = metadata.Engine;
        DateTime timeTaken = metadata.TimeTaken.Value.ToLocalTime();
#if DEBUG
        Stopwatch total = Stopwatch.StartNew();
        Stopwatch gather = Stopwatch.StartNew();
#endif
        if (url == null)
        {
            AnimatedPhotoExporterMod.Warn("Texture URL missing; cannot export animated photo.");
            return Task.CompletedTask;
        }

        return metadata.StartGlobalTask(async () =>
        {
            try
            {
                (bool atlasSuccess, string? atlasPath, string? atlasFailure) = await ResolveAtlasPathAsync(
                    engine,
                    url,
                    cancellationToken
                ).ConfigureAwait(false);
#if DEBUG
                gather.Stop();
#endif
                if (!atlasSuccess || string.IsNullOrEmpty(atlasPath))
                {
                    AnimatedPhotoExporterMod.Warn(
                        $"Failed to gather atlas file for animated photo: {atlasFailure ?? "unknown reason"}."
                    );
                    return;
                }

#if DEBUG
                Stopwatch wait = Stopwatch.StartNew();
#endif
                (TextureLoadOutcome outcome, AssetLoadState? loadState) = await WaitForTextureLoadAsync(
                    texture,
                    TextureLoadTimeout,
                    cancellationToken
                ).ConfigureAwait(false);
#if DEBUG
                wait.Stop();
#endif
                if (outcome != TextureLoadOutcome.Loaded)
                {
                    string detail =
                        outcome switch
                        {
                            TextureLoadOutcome.TimedOut => $"timed out after {TextureLoadTimeout.TotalSeconds:F0}s",
                            TextureLoadOutcome.Cancelled => "cancelled",
                            _ => $"asset load state {loadState?.ToString() ?? "unknown"}"
                        };
                    AnimatedPhotoExporterMod.Warn($"Animated photo export aborted: texture {detail}.");
                    return;
                }

                AnimatedImageFormat format = AnimatedPhotoExporterConfiguration.Format;
                bool exportGif = AnimatedPhotoExporterConfiguration.ExportGif;
                string platformName = ResolvePlatformName(engine);

                string primaryPath = BuildDiskPath(platformName, timeTaken, GetExtension(format));
                WriteIfPossible(
                    metadata,
                    animation,
                    format,
                    primaryPath,
                    atlasPath
#if DEBUG
                    ,
                    total,
                    gather,
                    wait
#endif
                );

                if (exportGif && format != AnimatedImageFormat.Gif)
                {
                    string gifPath = BuildDiskPath(platformName, timeTaken, ".gif");
                    WriteIfPossible(
                        metadata,
                        animation,
                        AnimatedImageFormat.Gif,
                        gifPath,
                        atlasPath
#if DEBUG
                        ,
                        total,
                        gather,
                        wait
#endif
                    );
                }
            }
            catch (Exception ex)
            {
                AnimatedPhotoExporterMod.Warn($"Animated photo export failed: {ex}");
            }
        });
    }

    private static string BuildDiskPath(string platformName, DateTime timeTaken, string extension)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            string.IsNullOrWhiteSpace(platformName) ? "Resonite" : platformName
        );
        if (
            AnimatedPhotoExporterConfiguration.IntegrateScreenshotExtensions &&
            ScreenshotExtensionsIntegration.IsPresent &&
            ScreenshotExtensionsIntegration.ShouldDigByMonth
        )
        {
            root = Path.Combine(root, timeTaken.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
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
            AnimatedImageFormat.Gif => ".gif",
            _ => ".anim"
        };
    }

    private static async Task<(bool Success, string? AtlasPath, string? FailureReason)> ResolveAtlasPathAsync(
        Engine engine,
        Uri url,
        CancellationToken cancellationToken
    )
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return (false, null, "cancelled");
        }

        string? atlasPath = await engine.AssetManager.GatherAssetFile(url, 100f).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            return (false, null, "cancelled");
        }

        return string.IsNullOrEmpty(atlasPath)
            ? (false, null, "AssetManager returned no path")
            : (true, atlasPath, null);
    }

    private static void WriteIfPossible(
        PhotoMetadata metadata,
        AnimationMetadata animation,
        AnimatedImageFormat format,
        string outputPath,
        string atlasPath
#if DEBUG
        ,
        Stopwatch total,
        Stopwatch gather,
        Stopwatch wait
#endif
    )
    {
#if DEBUG
        Stopwatch write = Stopwatch.StartNew();
#endif
        if (AnimatedImageWriter.TryWrite(animation, format, outputPath, atlasPath, out string? written, out string? failureReason) &&
            !string.IsNullOrEmpty(written))
        {
#if DEBUG
            write.Stop();
            AnimatedPhotoExporterMod.Msg(
                $"Animated photo saved to {written}. " +
                $"Gather {gather.Elapsed.TotalSeconds:F2}s Wait {wait.Elapsed.TotalSeconds:F2}s Encode {write.Elapsed.TotalSeconds:F2}s Total {total.Elapsed.TotalSeconds:F2}s " +
                $"Frames {animation.FrameCount} Rate {animation.FrameRate:F1} AtlasFrames {animation.Atlas.Frames}"
            );
#else
            AnimatedPhotoExporterMod.Msg($"Animated photo saved to {written}.");
#endif
            ScreenshotExtensionsIntegration.TryEmbed(metadata, written);
        }
        else if (!string.IsNullOrEmpty(failureReason))
        {
            AnimatedPhotoExporterMod.Warn($"Animated photo write failed for {outputPath}: {failureReason}");
        }
        else
        {
            AnimatedPhotoExporterMod.Warn($"Animated photo write failed for {outputPath}: unknown reason.");
        }
    }

    private static string ResolvePlatformName(Engine engine)
    {
        try
        {
            object? cloud = engine.GetType().GetProperty("Cloud")?.GetValue(engine);
            object? platform = cloud?.GetType().GetProperty("Platform")?.GetValue(cloud);
            object? nameObj = platform?.GetType().GetProperty("Name")?.GetValue(platform);
            string? name = nameObj as string ?? nameObj?.ToString();
            return string.IsNullOrWhiteSpace(name) ? "Resonite" : name;
        }
        catch
        {
            return "Resonite";
        }
    }

    private static async Task<(TextureLoadOutcome Outcome, AssetLoadState? LoadState)> WaitForTextureLoadAsync(
        StaticTexture2D texture,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            Asset? asset = texture.Asset;
            AssetLoadState? state = asset?.LoadState;
            if (state == AssetLoadState.FullyLoaded)
            {
                return (TextureLoadOutcome.Loaded, state);
            }

            if (state == AssetLoadState.Failed)
            {
                return (TextureLoadOutcome.Failed, state);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return (TextureLoadOutcome.TimedOut, state);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return (TextureLoadOutcome.Cancelled, state);
            }

            await default(NextUpdate);
        }
    }

    private enum TextureLoadOutcome
    {
        Loaded,
        Failed,
        TimedOut,
        Cancelled
    }
}
