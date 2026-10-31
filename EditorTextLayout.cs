using System;

namespace SilverpineMods.CustomEnemyLoader;

internal static class EditorTextLayout
{
    internal const float ControlHeight = 44f;

    internal static float Width(float allocatedWidth, float maximumWidth, float minimumWidth) =>
        Math.Max(Math.Max(1f, minimumWidth), Math.Min(maximumWidth,
            allocatedWidth > 0f ? allocatedWidth : maximumWidth));

    internal static float Height(float contentHeight, float minimumHeight) =>
        Math.Max(ControlHeight, (float)Math.Ceiling(Math.Max(contentHeight, minimumHeight) + 2f));

    // A final glyph makes native font measurement include empty/trailing lines.
    // This is only for measurement, never for display or serialization.
    internal static string Measurement(string value)
    {
        string text = value.Replace("\r\n", "\n").Replace("\r", "\n");
        return text.Length == 0 || text.EndsWith("\n", StringComparison.Ordinal) ? text + "M" : text;
    }

    internal static string InputValue(string value, bool allowNewlines) =>
        allowNewlines ? value : value.Replace("\r", "").Replace("\n", "");
}
