using Elements.Core;
using ImageMagick;
using ImageMagick.Formats;

#pragma warning disable IDE0002 // Resonite style keeps fully-qualified names for clarity in logs
#pragma warning disable IDE0028 // Collection initializer suggestion not applicable with dynamic frame loop

namespace AnimatedPhotoExporter.Services;

internal static class AnimatedImageWriter
{
    static AnimatedImageWriter()
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
        }
        catch (Exception ex)
        {
            AnimatedPhotoExporterMod.Warn($"Failed to prime native search path for Magick.NET: {ex}");
        }
    }

    internal static bool TryWrite(
        AnimationMetadata animation,
        AnimatedImageFormat format,
        string outputPath,
        string atlasPath,
        out string? writtenPath
    )
    {
        ArgumentNullException.ThrowIfNull(animation);
        writtenPath = null;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (string.IsNullOrEmpty(atlasPath) || !File.Exists(atlasPath))
        {
            AnimatedPhotoExporterMod.Warn("Atlas asset path not resolved; ensure texture is fully loaded.");
            return false;
        }

        using MagickImage atlas = new(atlasPath);
        using MagickImageCollection collection = new();
        int frames = Math.Max(1, animation.FrameCount);

        int atlasFrames = Math.Max(1, animation.Atlas.Frames);
        List<int> orderedAtlasIndices =
        [
            .. Enumerable
                .Range(0, atlasFrames)
                .Select(idx => (idx, uv: animation.Atlas.GetFrame(idx)))
                .OrderBy(t => t.uv.Min.y) // bottom to top (fix reversed row order)
                .ThenBy(t => t.uv.Min.x)  // left to right
                .Select(t => t.idx)
        ];

        float frameRate = animation.FrameRate <= 0 ? 30f : animation.FrameRate;
        for (int i = 0; i < frames; i++)
        {
            int atlasIndex = orderedAtlasIndices[i % orderedAtlasIndices.Count]; // forward only, row-major top-down
            BoundingBox2D uv = animation.Atlas.GetFrame(atlasIndex);
            int width = Math.Max(1, (int)(uv.Size.x * atlas.Width));
            int height = Math.Max(1, (int)(uv.Size.y * atlas.Height));
            int x = (int)(uv.Min.x * atlas.Width);
            int y = (int)(uv.Min.y * atlas.Height);

            MagickGeometry geometry = new() { X = x, Y = y, Width = (uint)width, Height = (uint)height };

            MagickImage frame = new(atlas);
            frame.Crop(geometry);
            frame.Page = new MagickGeometry { Width = (uint)width, Height = (uint)height };

            int delayCentiseconds = (int)Math.Max(1, Math.Round(100f / frameRate));
            frame.AnimationDelay = (ushort)delayCentiseconds;
            frame.AnimationIterations = ushort.MinValue; // loop forever
            collection.Add(frame);
        }

        collection.Coalesce();
        if (format == AnimatedImageFormat.WebP)
        {
            WebPWriteDefines defines = new() { Lossless = true };
            collection.Write(outputPath, defines);
        }
        else if (format == AnimatedImageFormat.Mng)
        {
            collection.Write(outputPath, MagickFormat.Mng);
        }
        else
        {
            AnimatedPhotoExporterMod.Warn($"Unsupported animated format {format}.");
            return false;
        }
        writtenPath = outputPath;
        return true;
    }

}
