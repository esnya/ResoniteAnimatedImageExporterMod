using System.Globalization;
using System.Reflection;
using AnimatedPhotoExporter.Configuration;
using FrooxEngine;
#if DEBUG
using System.Diagnostics;
#endif

#pragma warning disable IDE0002 // Keep explicit type and namespace usage for clarity in mod logs

namespace AnimatedPhotoExporter.Services;

internal static class AnimatedPhotoSaver
{
    internal static Task SaveAnimatedPhotoAsync(
        PhotoMetadata metadata,
        AnimationMetadata animation
    )
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(animation);

        StaticTexture2D? texture = animation.Texture;
        Uri? url = texture.URL.Value;
        Engine engine = metadata.Engine;
        DateTime timeTaken = metadata.TimeTaken.Value.ToLocalTime();
#if DEBUG
        Stopwatch total = Stopwatch.StartNew();
        Stopwatch gather = Stopwatch.StartNew();
#endif
        if (texture == null || url == null)
        {
            AnimatedPhotoExporterMod.Warn("Texture or slot missing; cannot export.");
            return Task.CompletedTask;
        }

        return metadata.StartGlobalTask(async () =>
        {
            try
            {
                string? atlasPath = await engine.AssetManager.GatherAssetFile(url, 100f).ConfigureAwait(false);
#if DEBUG
                gather.Stop();
#endif
                if (string.IsNullOrEmpty(atlasPath))
                {
                    AnimatedPhotoExporterMod.Warn("Failed to gather atlas file for animated photo.");
                    return;
                }

#if DEBUG
                Stopwatch wait = Stopwatch.StartNew();
#endif
                while (texture.Asset == null || texture.Asset.LoadState != AssetLoadState.FullyLoaded)
                {
                    await default(NextUpdate);
                }
#if DEBUG
                wait.Stop();
#endif

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
        if (AnimatedPhotoExporterConfiguration.IntegrateScreenshotExtensions && IsScreenshotExtensionsPresent())
        {
            bool dig = TryReadScreenshotExtensionsBool("DigFolderWhenSavingKey") ?? true; // mod default is true
            if (dig)
            {
                root = Path.Combine(root, timeTaken.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            }
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
        if (AnimatedImageWriter.TryWrite(animation, format, outputPath, atlasPath, out string? written) &&
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
            TryIntegrateWithScreenshotExtensions(metadata, written);
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

    private static void TryIntegrateWithScreenshotExtensions(PhotoMetadata metadata, string outputPath)
    {
        if (!AnimatedPhotoExporterConfiguration.IntegrateScreenshotExtensions)
        {
            return;
        }

        // Only run when the companion mod is present and its dependencies exist.
        Type? metadataType = Type.GetType("ResoniteScreenshotExtensions.Metadata, ResoniteScreenshotExtensions");
        Type? xmpType = Type.GetType("ResoniteScreenshotExtensions.XmpMetadata, ResoniteScreenshotExtensions");
        Type? bitmapType = Type.GetType("FreeImageAPI.FreeImageBitmap, FreeImageNET")
            ?? Type.GetType("FreeImageAPI.FreeImageBitmap, FreeImageAPI");

        if (metadataType == null || xmpType == null || bitmapType == null)
        {
            return;
        }

        try
        {
            object rseMetadata = Activator.CreateInstance(metadataType, metadata)!;
            object bitmap = Activator.CreateInstance(bitmapType, outputPath)!;
            try
            {
                Type[] signature = [bitmapType, metadataType];
                object[] args = [bitmap, rseMetadata];
                MethodInfo? upsert = xmpType.GetMethod(
                    "UpsertPhotoMetadata",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    signature,
                    null
                );
                upsert?.Invoke(null, args);
            }
            finally
            {
                (bitmap as IDisposable)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            AnimatedPhotoExporterMod.Warn($"ScreenshotExtensions integration failed for {outputPath}: {ex}");
        }
    }

    private static bool IsScreenshotExtensionsPresent()
    {
        return Type.GetType("ResoniteScreenshotExtensions.ResoniteScreenshotExtensions, ResoniteScreenshotExtensions") != null;
    }

    private static bool? TryReadScreenshotExtensionsBool(string keyFieldName)
    {
        try
        {
            Type? rseType = Type.GetType("ResoniteScreenshotExtensions.ResoniteScreenshotExtensions, ResoniteScreenshotExtensions");
            if (rseType == null)
            {
                return null;
            }

            FieldInfo? configField = rseType.GetField("_config", BindingFlags.NonPublic | BindingFlags.Static);
            object? config = configField?.GetValue(null);
            if (config == null)
            {
                return null;
            }

            FieldInfo? keyField = rseType.GetField(keyFieldName, BindingFlags.Public | BindingFlags.Static);
            object? keyInstance = keyField?.GetValue(null);
            if (keyInstance == null)
            {
                return null;
            }

            MethodInfo? getValue = config
                .GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetValue" && m.GetParameters().Length == 1);
            if (getValue == null)
            {
                return null;
            }

            object[] args = [keyInstance];
            object? result = getValue.Invoke(config, args);
            return result as bool?;
        }
        catch
        {
            return null;
        }
    }
}
