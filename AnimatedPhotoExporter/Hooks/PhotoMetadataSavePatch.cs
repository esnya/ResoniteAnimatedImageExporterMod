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
            AnimatedPhotoExporterMod.Msg("Non-mono screenshot detected; skipping animated export.");
            return;
        }

        if (!AnimationMetadataDetector.TryGetMetadata(__instance, out AnimationMetadata animation))
        {
            AnimatedPhotoExporterMod.Msg("No atlas animation detected; using default photo save.");
            return;
        }

        AnimatedPhotoExporterMod.Msg("Atlas animation detected; exporting animated photo alongside default save.");
        Task animatedSave = AnimatedPhotoSaver.SaveAnimatedPhotoAsync(__instance, animation);

        __result = __result == null ? animatedSave : Task.WhenAll(__result, animatedSave);
    }
}
