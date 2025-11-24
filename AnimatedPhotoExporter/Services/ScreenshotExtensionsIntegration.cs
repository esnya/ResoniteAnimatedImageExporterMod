using System.Reflection;
using System.Threading;
using AnimatedPhotoExporter.Configuration;
using FrooxEngine;

namespace AnimatedPhotoExporter.Services;

#pragma warning disable IDE0002 // Keep explicit type names for clarity in mod logs

/// <summary>
/// Cached detection and integration with ScreenshotExtensions to avoid repeated reflection per save.
/// </summary>
internal static class ScreenshotExtensionsIntegration
{
    private static readonly IntegrationSnapshot NotPresent = new(
        null,
        null,
        null,
        null,
        isPresent: false,
        digPreference: true
    );

    private static IntegrationSnapshot snapshot = NotPresent;

    internal static bool IsPresent
    {
        get
        {
            EnsureSnapshotInitialized();
            return snapshot.IsPresent;
        }
    }

    internal static bool ShouldDigByMonth
    {
        get
        {
            EnsureSnapshotInitialized();
            return snapshot.IsPresent && snapshot.DigPreference;
        }
    }

    internal static void Refresh()
    {
        IntegrationSnapshot next = BuildSnapshot();
        Interlocked.Exchange(ref snapshot, next);
    }

    private static void EnsureSnapshotInitialized()
    {
        if (ReferenceEquals(snapshot, NotPresent))
        {
            Refresh();
        }
    }

    internal static void TryEmbed(PhotoMetadata metadata, string outputPath)
    {
        EnsureSnapshotInitialized();

        IntegrationSnapshot local = snapshot;
        if (
            !AnimatedPhotoExporterConfiguration.IntegrateScreenshotExtensions ||
            !local.IsPresent ||
            local.MetadataType == null ||
            local.BitmapType == null ||
            local.UpsertMethod == null
        )
        {
            return;
        }

        try
        {
            object rseMetadata = Activator.CreateInstance(local.MetadataType, metadata)!;
            object bitmap = Activator.CreateInstance(local.BitmapType, outputPath)!;
            try
            {
                local.UpsertMethod.Invoke(null, [bitmap, rseMetadata]);
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

    private static IntegrationSnapshot BuildSnapshot()
    {
        Type? metadataType = Type.GetType("ResoniteScreenshotExtensions.Metadata, ResoniteScreenshotExtensions");
        Type? xmpType = Type.GetType("ResoniteScreenshotExtensions.XmpMetadata, ResoniteScreenshotExtensions");
        Type? bitmapType = Type.GetType("FreeImageAPI.FreeImageBitmap, FreeImageNET")
            ?? Type.GetType("FreeImageAPI.FreeImageBitmap, FreeImageAPI");

        bool isPresent = metadataType != null && xmpType != null && bitmapType != null;
        MethodInfo? upsertMethod = null;
        bool digPreference = true;

        if (!isPresent)
        {
            return NotPresent;
        }

        Type[] signature = [bitmapType!, metadataType!];
        upsertMethod = xmpType!.GetMethod(
            "UpsertPhotoMetadata",
            BindingFlags.Public | BindingFlags.Static,
            null,
            signature,
            null
        );

        digPreference = TryReadScreenshotExtensionsBool("DigFolderWhenSavingKey") ?? true;

        return new IntegrationSnapshot(metadataType, xmpType, bitmapType, upsertMethod, isPresent, digPreference);
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

            object? result = getValue.Invoke(config, [keyInstance]);
            return result as bool?;
        }
        catch
        {
            return null;
        }
    }

    private sealed record IntegrationSnapshot(
        Type? MetadataType,
        Type? XmpType,
        Type? BitmapType,
        MethodInfo? UpsertMethod,
        bool IsPresent,
        bool DigPreference
    );
}
