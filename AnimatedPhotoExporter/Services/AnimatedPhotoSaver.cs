using System.Globalization;

using AnimatedPhotoExporter.Configuration;
using FrooxEngine;

#pragma warning disable IDE0002 // Keep explicit type and namespace usage for clarity in mod logs

namespace AnimatedPhotoExporter.Services;

internal static class AnimatedPhotoSaver
{
    private static readonly TimeSpan TextureLoadTimeout = TimeSpan.FromSeconds(30);

    private sealed record SaveTarget(AnimatedImageFormat Format, string Path);

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
        if (url == null)
        {
            AnimatedPhotoExporterMod.Warn("Texture URL missing; cannot export animated photo.");
            return Task.CompletedTask;
        }

        return metadata.StartGlobalTask(async () =>
        {
            try
            {
                AnimatedImageWriter.EnsureNativePathPrimed();

                (bool atlasSuccess, string? atlasPath, string? atlasFailure) = await ResolveAtlasPathAsync(
                    engine,
                    url,
                    cancellationToken
                ).ConfigureAwait(false);
                if (!atlasSuccess || string.IsNullOrEmpty(atlasPath))
                {
                    AnimatedPhotoExporterMod.Warn(
                        $"Failed to gather atlas file for animated photo: {atlasFailure ?? "unknown reason"}."
                    );
                    return;
                }

                (TextureLoadOutcome outcome, AssetLoadState? loadState) = await WaitForTextureLoadAsync(
                    texture,
                    TextureLoadTimeout,
                    cancellationToken
                ).ConfigureAwait(false);
                if (outcome != TextureLoadOutcome.Loaded)
                {
                    string detail = outcome switch
                    {
                        TextureLoadOutcome.TimedOut => $"timed out after {TextureLoadTimeout.TotalSeconds:F0}s",
                        TextureLoadOutcome.Cancelled => "cancelled",
                        TextureLoadOutcome.Failed => $"asset load state {loadState?.ToString() ?? "unknown"}",
                        TextureLoadOutcome.Loaded => "unexpectedly reported loaded",
                        _ => $"asset load state {loadState?.ToString() ?? "unknown"}"
                    };
                    AnimatedPhotoExporterMod.Warn($"Animated photo export aborted: texture {detail}.");
                    return;
                }

                AnimatedImageFormat format = AnimatedPhotoExporterConfiguration.Format;
                bool exportGif = AnimatedPhotoExporterConfiguration.ExportGif;
                string platformName = ResolvePlatformName(engine);

                foreach (SaveTarget target in EnumerateOutputs(platformName, timeTaken, format, exportGif))
                {
                    WriteOutput(
                        metadata,
                        animation,
                        atlasPath,
                        target
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
        for (int i = 0; ; i++)
        {
            string suffix = i == 0 ? string.Empty : $"-{i}";
            string candidate = Path.Combine(root, $"{baseName}{suffix}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
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

    private static IEnumerable<SaveTarget> EnumerateOutputs(
        string platformName,
        DateTime timeTaken,
        AnimatedImageFormat primaryFormat,
        bool exportGif
    )
    {
        yield return new SaveTarget(primaryFormat, BuildDiskPath(platformName, timeTaken, GetExtension(primaryFormat)));

        if (exportGif && primaryFormat != AnimatedImageFormat.Gif)
        {
            yield return new SaveTarget(AnimatedImageFormat.Gif, BuildDiskPath(platformName, timeTaken, ".gif"));
        }
    }

    private static async Task<(bool Success, string? AtlasPath, string? FailureReason)> ResolveAtlasPathAsync(
        Engine engine,
        Uri url,
        CancellationToken cancellationToken
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? atlasPath = await engine.AssetManager.GatherAssetFile(url, 100f).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return string.IsNullOrEmpty(atlasPath)
                ? (false, null, "AssetManager returned no path")
                : (true, atlasPath, null);
        }
        catch (OperationCanceledException)
        {
            return (false, null, "cancelled");
        }
    }

    private static void WriteOutput(
        PhotoMetadata metadata,
        AnimationMetadata animation,
        string atlasPath,
        SaveTarget target
    )
    {
        bool success = AnimatedImageWriter.TryWrite(
                animation,
                target.Format,
                target.Path,
                atlasPath,
                out string? written,
                out string? failureReason
            ) && !string.IsNullOrEmpty(written);

        if (success)
        {
            ScreenshotExtensionsIntegration.TryEmbed(metadata, written!);
            AnimatedPhotoExporterMod.Msg(
                $"Animated photo saved ({target.Format}) -> {written}."
            );
            return;
        }

        string reason = string.IsNullOrEmpty(failureReason) ? "unknown reason" : failureReason!;
        AnimatedPhotoExporterMod.Warn($"Animated photo save failed for {target.Path}: {reason}");
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
        while (!cancellationToken.IsCancellationRequested)
        {
            AssetLoadState? state = texture.Asset?.LoadState;
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

            await default(NextUpdate);
        }

        return (TextureLoadOutcome.Cancelled, texture.Asset?.LoadState);
    }

    private enum TextureLoadOutcome
    {
        Loaded,
        Failed,
        TimedOut,
        Cancelled
    }
}
