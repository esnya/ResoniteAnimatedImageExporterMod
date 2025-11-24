using AnimatedPhotoExporter.Configuration;
using Elements.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ImageMagick;
using ImageMagick.Formats;

#pragma warning disable IDE0002 // Resonite style keeps fully-qualified names for clarity in logs
#pragma warning disable IDE0028 // Collection initializer suggestion not applicable with dynamic frame loop

namespace AnimatedPhotoExporter.Services;

internal static class AnimatedImageWriter
{
    private static readonly Lock WriteGate = new();

    private static readonly Lazy<PathPrimeResult> NativePathPrime = new(PrimeNativeSearchPath, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static bool TryWrite(
        AnimationMetadata animation,
        AnimatedImageFormat format,
        string outputPath,
        string atlasPath,
        out string? writtenPath,
        out string? failureReason
    )
    {
        ArgumentNullException.ThrowIfNull(animation);
        writtenPath = null;
        failureReason = null;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (string.IsNullOrEmpty(atlasPath) || !File.Exists(atlasPath))
        {
            AnimatedPhotoExporterMod.Warn("Atlas asset path not resolved; ensure texture is fully loaded.");
            failureReason = "Atlas asset path missing or not resolved.";
            return false;
        }

        using (WriteGate.EnterScope())
        {
            using MagickImage atlas = new(atlasPath);
            using MagickImageCollection collection = new();
            int frames = Math.Max(1, animation.FrameCount);

            int atlasFrames = Math.Max(1, animation.Atlas.Frames);
            List<int> orderedAtlasIndices = BuildAtlasOrder(animation, atlasFrames);

            float frameRate = animation.FrameRate <= 0 ? 30f : animation.FrameRate;
            for (int i = 0; i < frames; i++)
            {
                int atlasIndex = orderedAtlasIndices[i % orderedAtlasIndices.Count]; // forward only, row-major top-down
                MagickImage frame = SliceFrame(atlas, animation, atlasIndex);

                int delayCentiseconds = (int)Math.Max(1, Math.Round(100f / frameRate));
                frame.AnimationDelay = (ushort)delayCentiseconds;
                frame.AnimationIterations = ushort.MinValue; // loop forever
                collection.Add(frame);
            }

            switch (format)
            {
                case AnimatedImageFormat.WebP:
                    WebPWriteDefines defines = new()
                    {
                        Lossless = AnimatedPhotoExporterConfiguration.WebpLossless,
                        Method = Math.Clamp(AnimatedPhotoExporterConfiguration.WebpMethod, 0, 6)
                    };
                    collection.Write(outputPath, defines);
                    break;
                case AnimatedImageFormat.Mng:
                    collection.Write(outputPath, MagickFormat.Mng);
                    break;
                case AnimatedImageFormat.Gif:
                    collection.Write(outputPath, MagickFormat.Gif);
                    break;
                default:
                    AnimatedPhotoExporterMod.Warn($"Unsupported animated format {format}.");
                    failureReason = $"Unsupported animated format {format}.";
                    return false;
            }
            writtenPath = outputPath;
            return true;
        }
    }

    internal static void EnsureNativePathPrimed()
    {
        PathPrimeResult pathPrime = NativePathPrime.Value;
        if (!pathPrime.Success && pathPrime.Error != null)
        {
            AnimatedPhotoExporterMod.Warn($"Magick.NET native search path may be incomplete: {pathPrime.Error}");
        }
    }

    private static List<int> BuildAtlasOrder(AnimationMetadata animation, int atlasFrames)
    {
        return
        [
            .. Enumerable
                .Range(0, atlasFrames)
                .Select(idx => (idx, uv: animation.Atlas.GetFrame(idx)))
                .OrderBy(t => t.uv.Min.y) // bottom to top (fix reversed row order)
                .ThenBy(t => t.uv.Min.x)  // left to right
                .Select(t => t.idx)
        ];
    }

    private static MagickImage SliceFrame(MagickImage atlas, AnimationMetadata animation, int atlasIndex)
    {
        BoundingBox2D uv = animation.Atlas.GetFrame(atlasIndex);
        int width = Math.Max(1, (int)(uv.Size.x * atlas.Width));
        int height = Math.Max(1, (int)(uv.Size.y * atlas.Height));
        int x = (int)(uv.Min.x * atlas.Width);
        int y = (int)(uv.Min.y * atlas.Height);

        MagickGeometry geometry = new() { X = x, Y = y, Width = (uint)width, Height = (uint)height };

        MagickImage frame = (MagickImage)atlas.CloneArea(geometry);
        frame.Page = new MagickGeometry { Width = (uint)width, Height = (uint)height };
        if (!AnimatedPhotoExporterConfiguration.WebpLossless)
        {
            frame.Quality = (byte)Math.Clamp(AnimatedPhotoExporterConfiguration.WebpQuality, 1, 100);
        }

        return frame;
    }

    private static PathPrimeResult PrimeNativeSearchPath()
    {
        try
        {
            string? managedDir = Path.GetDirectoryName(typeof(MagickImage).Assembly.Location);
            string? modDir = Path.GetDirectoryName(typeof(AnimatedImageWriter).Assembly.Location);

            IEnumerable<string?> candidateDirs =
            [
                managedDir,
                modDir,
                managedDir != null ? Path.Combine(managedDir, "runtimes", "win-x64", "native") : null,
                modDir != null ? Path.Combine(modDir, "runtimes", "win-x64", "native") : null,
            ];

            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            HashSet<string> existing = currentPath
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> additions = new();
            foreach (string? dir in candidateDirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    continue;
                }

                string full = Path.GetFullPath(dir);
                if (existing.Add(full))
                {
                    additions.Add(full);
                }
            }

            if (additions.Count > 0)
            {
                Environment.SetEnvironmentVariable(
                    "PATH",
                    string.Join(Path.PathSeparator, additions) + Path.PathSeparator + currentPath
                );
            }

            return new PathPrimeResult(true, additions, null);
        }
        catch (Exception ex)
        {
            return new PathPrimeResult(false, Array.Empty<string>(), ex);
        }
    }

    private sealed record PathPrimeResult(bool Success, IReadOnlyCollection<string> AddedPaths, Exception? Error);
}
