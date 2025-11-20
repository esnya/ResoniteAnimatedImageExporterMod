using FrooxEngine;

namespace AnimatedPhotoExporter.Services;

internal static class AnimationMetadataDetector
{
    internal static bool TryGetMetadata(PhotoMetadata metadata, out AnimationMetadata animation)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        animation = null!;

        Slot? slot = metadata.Slot;
        if (slot == null)
        {
            return false;
        }

        StaticTexture2D? texture = slot.GetComponent<StaticTexture2D>();
        AtlasInfo? atlas = slot.GetComponentInChildren<AtlasInfo>();
        UVAtlasAnimator? animator = slot
            .GetComponentsInChildren<UVAtlasAnimator>()
            .FirstOrDefault(a => a.AtlasInfo.Target == atlas);

        if (texture == null || atlas == null || animator == null)
        {
            return false;
        }

        TimeIntDriver? driver = animator
            .Slot.GetComponents<TimeIntDriver>()
            .FirstOrDefault(d => d.Target.Target == animator.Frame);

        float frameRate = FrameRateResolver.Resolve(driver, atlas.Frames);
        // Export sequentially; ignore ping-pong/repeat for file output to avoid back-and-forth playback.
        bool pingPong = false;
        int frameCount = atlas.Frames;

        animation = new AnimationMetadata(texture, atlas, frameRate, pingPong, frameCount);
        return true;
    }
}
