using AnimatedPhotoExporter.Services;
using FrooxEngine;
using HarmonyLib;

#pragma warning disable IDE0002 // Keep explicit type names for clarity in mod logs

namespace AnimatedPhotoExporter.Hooks;

[HarmonyPatch(typeof(PhotoMetadata))]
internal static class PhotoMetadataSavePatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(PhotoMetadata.NotifyOfScreenshot))]
    private static void AfterNotify(PhotoMetadata __instance, ref Task __result)
    {
        // Only support mono screenshots; match base game behavior for this mod.
        if (__instance.Is360.Value || __instance.StereoLayout.Value != Elements.Core.StereoLayout.None)
        {
            AnimatedPhotoExporterMod.Warn("Animated photo export supports mono screenshots only; skipping.");
            return;
        }

        if (!AnimationMetadataDetector.TryGetMetadata(__instance, out AnimationMetadata animation))
        {
            return;
        }

        Task animatedSave = AnimatedPhotoSaver.SaveAnimatedPhotoAsync(__instance, animation);

        __result = __result == null ? animatedSave : Task.WhenAll(__result, animatedSave);
    }
}
