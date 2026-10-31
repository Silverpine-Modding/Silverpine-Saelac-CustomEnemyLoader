#nullable enable
using System;
using System.Globalization;

namespace SilverpineMods.CustomEnemyLoader;

internal static class EditorTerrainReference
{
    internal const string GrassPrefab = "prefab_tile_grass";

    internal static bool IsGrassGround(string? objectName)
    {
        if (objectName == null) return false;
        if (objectName.Equals(GrassPrefab, StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals(GrassPrefab + "(Clone)", StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals(GrassPrefab + " (Clone)", StringComparison.OrdinalIgnoreCase)) return true;

        // Allow Unity's numbered scene copies, but never grass_overgrowth,
        // grass_swamp, or arbitrary objects/sprites merely named "grass".
        string prefix = GrassPrefab + " (";
        return objectName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            objectName.EndsWith(")", StringComparison.Ordinal) &&
            int.TryParse(objectName.Substring(prefix.Length, objectName.Length - prefix.Length - 1),
                NumberStyles.None, CultureInfo.InvariantCulture, out int copy) && copy >= 0;
    }
}
