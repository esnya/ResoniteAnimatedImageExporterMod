using FrooxEngine;

namespace AnimatedPhotoExporter.Services;

/// <summary>Describes how to slice and animate an atlas-backed screenshot.</summary>
internal sealed class AnimationMetadata
{
    internal AnimationMetadata(
        StaticTexture2D texture,
        AtlasInfo atlas,
        float frameRate,
        bool pingPong,
        int frameCount
    )
    {
        Texture = texture ?? throw new ArgumentNullException(nameof(texture));
        Atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        FrameRate = frameRate;
        PingPong = pingPong;
        FrameCount = frameCount;
    }

    internal StaticTexture2D Texture { get; }

    internal AtlasInfo Atlas { get; }

    internal float FrameRate { get; }

    internal bool PingPong { get; }

    internal int FrameCount { get; }
}
