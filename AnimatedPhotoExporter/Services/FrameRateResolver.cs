using System.Reflection;
using FrooxEngine;

namespace AnimatedPhotoExporter.Services;

internal static class FrameRateResolver
{
    internal static float Resolve(TimeIntDriver? driver, int frameCount)
    {
        if (driver == null)
        {
            return 30f;
        }

        float? interval = TryGetSyncValue(driver, "Interval");
        if (interval is > 0)
        {
            return 1f / interval.Value;
        }

        float? duration = TryGetSyncValue(driver, "Duration");
        if (duration is > 0 && frameCount > 0)
        {
            return frameCount / duration.Value;
        }

        float scale = driver.Scale?.Value ?? 0f;
        return scale > 0 ? scale : 30f;
    }

    private static float? TryGetSyncValue(object instance, string propertyName)
    {
        PropertyInfo? prop = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        object? sync = prop?.GetValue(instance);
        if (sync == null)
        {
            return null;
        }

        PropertyInfo? valueProp = sync.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        object? value = valueProp?.GetValue(sync);
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            _ => null,
        };
    }
}
