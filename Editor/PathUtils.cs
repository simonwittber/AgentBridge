using System.IO;
using UnityEngine;

namespace LLMDevTools
{
    internal static class PathUtils
    {
        static readonly string _projectRoot =
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        // Returns a safe write path guaranteed to be under Assets/ or Temp/.
        // Returns null if the path is outside both allowed roots.
        internal static string SafeWritePath(string path, string allowedPrefix)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var full = Path.GetFullPath(path);
            var allowed = Path.GetFullPath(Path.Combine(_projectRoot, allowedPrefix));
            return full.StartsWith(allowed) ? full : null;
        }

        internal static bool IsUnderAssets(string path)  => SafeWritePath(path, "Assets") != null;
        internal static bool IsUnderTemp(string path)    => SafeWritePath(path, "Temp")   != null;
        internal static bool IsWritable(string path)     => IsUnderAssets(path) || IsUnderTemp(path);
    }
}
