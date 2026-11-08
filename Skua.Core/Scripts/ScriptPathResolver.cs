using Skua.Core.Interfaces.Services;
using Skua.Core.Models;
using Skua.Core.Models.Scripts;
using System.Collections.Concurrent;

namespace Skua.Core.Scripts;

public sealed class ScriptPathResolver : IScriptPathResolver
{
    private readonly ICustomScriptService _customScripts;
    private readonly string _repositoryRoot = Canonicalize(ClientFileSources.SkuaScriptsDIR);
    private readonly ConcurrentDictionary<(string? ownerFile, string specifier), ScriptPathResolution> _resolutionCache = new();

    public ScriptPathResolver(ICustomScriptService customScripts)
    {
        _customScripts = customScripts;
    }

    public ScriptPathResolution Resolve(string? ownerFile, string specifier)
    {
        if (string.IsNullOrWhiteSpace(specifier))
            return new(null, Array.Empty<string>());

        var cacheKey = (ownerFile, specifier);
        if (_resolutionCache.TryGetValue(cacheKey, out var cachedResolution))
            return cachedResolution;

        string normalizedSpecifier = specifier.Trim().Replace('/', Path.DirectorySeparatorChar);
        List<string> candidates = new();

        if (Path.IsPathRooted(normalizedSpecifier))
        {
            candidates.Add(normalizedSpecifier);
        }
        else if (normalizedSpecifier.StartsWith($".{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                 normalizedSpecifier.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            AddOwnerRelativeCandidate(candidates, ownerFile, normalizedSpecifier);
        }
        else if (normalizedSpecifier.StartsWith($"Scripts{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            string subPath = normalizedSpecifier[("Scripts".Length + 1)..];
            candidates.Add(Path.Combine(_repositoryRoot, subPath));

            int firstSlash = subPath.IndexOf(Path.DirectorySeparatorChar);
            if (firstSlash > 0)
            {
                string folderSegment = subPath[..firstSlash];
                string relativeInFolder = subPath[(firstSlash + 1)..];

                // Check repository root with common GitHub ZIP branch suffixes
                candidates.Add(Path.Combine(_repositoryRoot, $"{folderSegment}-main", relativeInFolder));
                candidates.Add(Path.Combine(_repositoryRoot, $"{folderSegment}-master", relativeInFolder));

                // Check relative to owner file directory and parent directory (e.g. scripts inside subfolders)
                if (!string.IsNullOrWhiteSpace(ownerFile))
                {
                    string? ownerDir = Path.GetDirectoryName(Canonicalize(ownerFile));
                    if (!string.IsNullOrEmpty(ownerDir))
                    {
                        candidates.Add(Path.Combine(ownerDir, relativeInFolder));
                        string? parentDir = Path.GetDirectoryName(ownerDir);
                        if (!string.IsNullOrEmpty(parentDir) && !parentDir.Equals(_repositoryRoot, StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add(Path.Combine(parentDir, relativeInFolder));
                        }
                    }
                }

                foreach (string customRoot in _customScripts.GetSearchRoots())
                {
                    if (Directory.Exists(customRoot))
                    {
                        candidates.Add(Path.Combine(customRoot, subPath));
                        candidates.Add(Path.Combine(customRoot, $"{folderSegment}-main", relativeInFolder));
                        candidates.Add(Path.Combine(customRoot, $"{folderSegment}-master", relativeInFolder));
                    }
                }
            }
            else
            {
                foreach (string customRoot in _customScripts.GetSearchRoots())
                {
                    if (Directory.Exists(customRoot))
                        candidates.Add(Path.Combine(customRoot, subPath));
                }
            }

            AddOwnerRelativeCandidate(candidates, ownerFile, subPath);
        }
        else if (_customScripts.IsCustomPath(ownerFile ?? string.Empty))
        {
            AddOwnerRelativeCandidate(candidates, ownerFile, normalizedSpecifier);
            candidates.Add(Path.Combine(_repositoryRoot, normalizedSpecifier));
        }
        else
        {
            candidates.Add(Path.Combine(_repositoryRoot, normalizedSpecifier));
            AddOwnerRelativeCandidate(candidates, ownerFile, normalizedSpecifier);
        }

        if (normalizedSpecifier.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, normalizedSpecifier));
            string? runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (!string.IsNullOrEmpty(runtimeDir))
                candidates.Add(Path.Combine(runtimeDir, normalizedSpecifier));
        }

        List<string> existing = candidates
            .Select(Canonicalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();

        // Android's file names are case-sensitive and Windows' aren't, so the repository has includes that
        // only work on Windows (28 scripts include "Story/QueenofMonsters/CoreQoM.cs"; the file is CoreQOM.cs,
        // so Lich and others didn't compile). Match the path ignoring case when nothing matched exactly.
        if (existing.Count == 0)
        {
            existing = candidates
                .Select(c => FindIgnoringCase(Canonicalize(c)))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        ScriptPathResolution resolution = new(existing.FirstOrDefault(), existing);
        if (resolution.SelectedPath != null)
            _resolutionCache[cacheKey] = resolution;

        return resolution;
    }

    private static void AddOwnerRelativeCandidate(List<string> candidates, string? ownerFile, string specifier)
    {
        if (!string.IsNullOrWhiteSpace(ownerFile))
            candidates.Add(Path.Combine(Path.GetDirectoryName(Canonicalize(ownerFile))!, specifier));
    }

    /// <summary>The existing file whose path equals <paramref name="path"/> ignoring letter case, or null.</summary>
    private static string? FindIgnoringCase(string path)
    {
        if (File.Exists(path))
            return path;
        try
        {
            string? root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return null;
            string current = root;
            string[] parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                bool last = i == parts.Length - 1;
                string exact = Path.Combine(current, parts[i]);
                if (last ? File.Exists(exact) : Directory.Exists(exact))
                {
                    current = exact;
                    continue;
                }
                if (!Directory.Exists(current))
                    return null;
                IEnumerable<string> entries = last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current);
                string? match = entries.FirstOrDefault(e => string.Equals(Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return null;
                current = match;
            }
            return current;
        }
        catch
        {
            return null;
        }
    }

    private static string Canonicalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
