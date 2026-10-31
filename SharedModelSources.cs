#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using Silverpine.ModdingTools;

namespace SilverpineMods.CustomEnemyLoader;

internal static class SharedModelSources
{
    internal static string Root => Path.Combine(SharedArtLibrary.Root, "models");

    internal static bool RemoveEmbeddedCopy(string source, string libraryCopy, bool stillReferenced)
    {
        if (stillReferenced || !File.Exists(source) || !File.Exists(libraryCopy)) return false;
        if (SharedArtLibrary.FileHash(source) != SharedArtLibrary.FileHash(libraryCopy)) return false;
        File.Delete(source);
        return true;
    }

    internal static string Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || Path.IsPathRooted(reference))
            throw new InvalidDataException("Model paths must be relative to the model library.");
        string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, reference));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model path leaves the model library.");
        return path;
    }

    internal static string Import(string selected)
    {
        if (!Path.GetExtension(selected).Equals(".glb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select a .glb model.");
        string hash = SharedArtLibrary.FileHash(selected);
        string suffix = "__" + hash.Substring(0, 16);
        Directory.CreateDirectory(Root);
        // Compatible with Item Loader's readable-name + 16-digit hash convention.
        foreach (string candidate in Directory.GetFiles(Root, "*" + suffix + ".glb").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            if (SharedArtLibrary.FileHash(candidate) == hash) return candidate;

        string source = Path.GetFileNameWithoutExtension(selected).Trim();
        if (source.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) source = source.Substring(0, source.Length - suffix.Length);
        var stem = new StringBuilder();
        bool separator = false;
        foreach (char c in source)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (separator && stem.Length > 0) stem.Append('_');
                stem.Append(char.ToLowerInvariant(c)); separator = false;
            }
            else separator = stem.Length > 0;
        }
        string name = stem.Length == 0 ? "model" : stem.ToString();
        if (name.Length > 64) name = name.Substring(0, 64).TrimEnd('_');
        string destination = Resolve(name + suffix + ".glb");
        if (File.Exists(destination))
        {
            if (SharedArtLibrary.FileHash(destination) != hash)
                throw new IOException("Model destination has different contents: " + destination);
        }
        else File.Copy(selected, destination, overwrite: false);
        return destination;
    }
}
