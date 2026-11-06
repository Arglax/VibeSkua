using Newtonsoft.Json;
using Skua.Core.Models;
using Skua.Core.Models.Monsters;
using System.Diagnostics;

namespace Skua.Core.Scripts;

/// <summary>
/// Keeps the monster IDs seen per map in monster-ids.json (Skua's folder), so a MonID or MapID found once
/// by Auto Attack/Hunt or the Auto page's monster list can be looked up later for scripts and manual targeting.
/// </summary>
public static class MonsterIdLog
{
    public class Entry
    {
        public int MonID { get; set; }
        public SortedSet<int> MapIDs { get; set; } = new();
        public SortedSet<string> Cells { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? LastSeen { get; set; }
    }

    private static readonly object _lock = new();
    private static Dictionary<string, Dictionary<string, Entry>>? _maps;

    public static string FilePath => Path.Combine(ClientFileSources.SkuaDIR, "monster-ids.json");

    public static void Record(string? map, Monster monster) => RecordAll(map, new[] { monster });

    public static void RecordAll(string? map, IEnumerable<Monster> monsters)
    {
        if (string.IsNullOrWhiteSpace(map))
            return;
        try
        {
            lock (_lock)
            {
                _maps ??= Load();
                if (!_maps.TryGetValue(map, out Dictionary<string, Entry>? byName))
                    _maps[map] = byName = new(StringComparer.OrdinalIgnoreCase);
                bool changed = false;
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                foreach (Monster m in monsters)
                {
                    if (string.IsNullOrWhiteSpace(m.Name))
                        continue;
                    string key = $"{m.Name.Trim()} #{m.ID}";
                    if (!byName.TryGetValue(key, out Entry? e))
                        byName[key] = e = new Entry { MonID = m.ID };
                    changed |= e.MapIDs.Add(m.MapID);
                    if (!string.IsNullOrEmpty(m.Cell))
                        changed |= e.Cells.Add(m.Cell);
                    e.LastSeen = now;
                }
                if (changed)
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(_maps, Formatting.Indented));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MonsterIdLog] {ex.Message}");
        }
    }

    private static Dictionary<string, Dictionary<string, Entry>> Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, Entry>>>(File.ReadAllText(FilePath)) is { } loaded)
                return new(loaded.ToDictionary(kv => kv.Key, kv => new Dictionary<string, Entry>(kv.Value, StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MonsterIdLog] load: {ex.Message}");
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }
}
