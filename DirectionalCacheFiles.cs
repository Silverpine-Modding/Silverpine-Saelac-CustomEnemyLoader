#nullable enable
using System;
using System.IO;

namespace SilverpineMods.CustomEnemyLoader;

internal static class DirectionalCacheFiles
{
    private static readonly string[] Names = { "down", "left", "up", "right" };

    // Stage the complete cache before replacing a previous release. If the swap fails,
    // restore the original directory; never leave a mixture of old and new directions.
    internal static void Write(string cache, byte[][] pngs, string manifest, Action? ensureCurrent = null)
    {
        if (pngs.Length != 4) throw new InvalidDataException("A directional cache requires four PNGs.");
        string target = Path.GetFullPath(cache);
        string parent = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Cache needs a parent directory.");
        Directory.CreateDirectory(parent);
        string stage = target + ".render-" + Guid.NewGuid().ToString("N");
        string backup = target + ".previous-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stage);
        bool replaced = false;
        try
        {
            for (int i = 0; i < 4; i++) File.WriteAllBytes(Path.Combine(stage, Names[i] + ".png"), pngs[i]);
            File.WriteAllText(Path.Combine(stage, "manifest.json"), manifest);
            ensureCurrent?.Invoke();
            if (Directory.Exists(target)) { Directory.Move(target, backup); replaced = true; }
            try { Directory.Move(stage, target); }
            catch
            {
                if (replaced) Directory.Move(backup, target);
                throw;
            }
            // A cleanup failure must not turn a successful commit into a failed render.
            if (replaced) TryRemove(backup);
        }
        finally { TryRemove(stage); }
    }

    private static void TryRemove(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
