#nullable enable
using System;
using System.IO;

namespace SilverpineMods.CustomEnemyLoader;

// World-space visual displacement. The entity, occupied turf and colliders stay put.
internal sealed class VisualOffset
{
    public float x;
    public float y;

    internal static float ShiftPivot(float normalizedPivot, float pixels, float scale, float offset) =>
        normalizedPivot - offset * 32f / (pixels * scale);

    internal void Validate()
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) ||
            float.IsInfinity(y) || Math.Abs(x) > 100 || Math.Abs(y) > 100)
            throw new InvalidDataException("Visual offsets must be finite and within -100 to 100 tiles.");
    }
}

internal sealed class VisualOffsets
{
    public VisualOffset? down;
    public VisualOffset? left;
    public VisualOffset? up;
    public VisualOffset? right;

    internal VisualOffset Get(int direction) => direction switch
    {
        1 => left ?? new VisualOffset(),
        2 => up ?? new VisualOffset(),
        3 => right ?? new VisualOffset(),
        _ => down ?? new VisualOffset()
    };

    internal void Set(int direction, float x, float y)
    {
        var value = new VisualOffset { x = x, y = y };
        value.Validate();
        switch (direction)
        {
            case 1: left = value; break;
            case 2: up = value; break;
            case 3: right = value; break;
            default: down = value; break;
        }
    }

    internal void Validate() { down?.Validate(); left?.Validate(); up?.Validate(); right?.Validate(); }
}

internal static class ArtDirections
{
    // The shared library orders Front, Right, Back, Left. EntityMover orders Down, Left, Up, Right.
    internal static T[] SwapLibrarySides<T>(T[] views) => views.Length == 4
        ? new[] { views[0], views[3], views[2], views[1] }
        : (T[])views.Clone();
}
