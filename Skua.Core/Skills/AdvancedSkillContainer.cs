using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Skills;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skua.Core.Skills;

public class AdvancedSkillContainer : ObservableRecipient, IAdvancedSkillContainer, IDisposable
{
    private List<AdvancedSkill> _loadedSkills = new();
    private readonly string _defaultSkillsSetsPath;
    private readonly string _userSkillsSetsPath;
    private CancellationTokenSource? _saveCts;
    private Task? _saveTask;
    private AdvancedSkillsConfigJson? _jsonConfig;
    private string? _loadedFilePath;

    public List<AdvancedSkill> LoadedSkills
    {
        get => _loadedSkills;
        set => SetProperty(ref _loadedSkills, value, true);
    }

    public AdvancedSkillContainer()
    {
        _defaultSkillsSetsPath = ClientFileSources.SkuaAdvancedSkillsFile;
        _userSkillsSetsPath = Path.Combine(ClientFileSources.SkuaDIR, "UserAdvancedSkills.json");

        string rootDefaultSkills = Path.Combine(AppContext.BaseDirectory, "AdvancedSkills.json");
        if (File.Exists(rootDefaultSkills) && !File.Exists(_defaultSkillsSetsPath))
        {
            File.Copy(rootDefaultSkills, _defaultSkillsSetsPath, true);
        }
        LoadSkills();

        // Refresh source skills in the background on startup so users receive new classes without deleting local files.
        _ = RefreshDefaultSkillsAsync();
    }

    private static async Task WriteFileAtomicallyAsync(string filePath, string contents)
    {
        string fullPath = Path.GetFullPath(filePath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"Could not determine the directory for '{fullPath}'.");
        Directory.CreateDirectory(directory);

        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, contents);

            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, null);
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task RefreshDefaultSkillsAsync()
    {
        try
        {
            ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
            if (!settings.Get<bool>("CheckAdvanceSkillSetsUpdates", false) ||
                !settings.Get<bool>("AutoUpdateAdvanceSkillSetsUpdates", false))
                return;

            // The defaults before this update tell which user modes were never customised.
            JsonObject? previousDefaults = null;
            try
            {
                if (File.Exists(_defaultSkillsSetsPath))
                    previousDefaults = JsonNode.Parse(await File.ReadAllTextAsync(_defaultSkillsSetsPath)) as JsonObject;
            }
            catch { }

            IGetScriptsService getScripts = Ioc.Default.GetRequiredService<IGetScriptsService>();
            if (!await getScripts.UpdateSkillSetsFile() || !File.Exists(_defaultSkillsSetsPath))
                return;

            string defaultJson = await File.ReadAllTextAsync(_defaultSkillsSetsPath);
            if (!File.Exists(_userSkillsSetsPath))
            {
                await WriteFileAtomicallyAsync(_userSkillsSetsPath, defaultJson);
            }
            else
            {
                JsonObject defaults = JsonNode.Parse(defaultJson) as JsonObject
                    ?? throw new JsonException("AdvancedSkills.json did not contain a JSON object.");
                string userJson = await File.ReadAllTextAsync(_userSkillsSetsPath);
                JsonObject user = JsonNode.Parse(userJson) as JsonObject
                    ?? throw new JsonException("UserAdvancedSkills.json did not contain a JSON object.");

                // Add newly published classes/modes, and take updated rotations for modes the user never
                // changed (they still equal the previous defaults), while keeping user customizations.
                // Class names match ignoring case: upstream renamed "Lord Of Order" to "Lord of Order".
                foreach (KeyValuePair<string, JsonNode?> classEntry in defaults.ToList())
                {
                    if (classEntry.Value is not JsonObject defaultModes)
                        continue;

                    string? userKey = FindKey(user, classEntry.Key);
                    if (userKey is null || user[userKey] is not JsonObject userModes)
                    {
                        user[classEntry.Key] = defaultModes.DeepClone();
                        continue;
                    }

                    JsonObject? oldModes = previousDefaults is null ? null
                        : FindKey(previousDefaults, classEntry.Key) is { } oldKey ? previousDefaults[oldKey] as JsonObject : null;
                    foreach (KeyValuePair<string, JsonNode?> modeEntry in defaultModes.ToList())
                    {
                        if (modeEntry.Value == null)
                            continue;
                        JsonNode? userMode = userModes[modeEntry.Key];
                        if (userMode == null)
                            userModes[modeEntry.Key] = modeEntry.Value.DeepClone();
                        else if (oldModes?[modeEntry.Key] is { } oldMode && JsonNode.DeepEquals(userMode, oldMode)
                                 && !JsonNode.DeepEquals(userMode, modeEntry.Value))
                            userModes[modeEntry.Key] = modeEntry.Value.DeepClone();
                    }

                    // Take the published spelling of the class name.
                    if (userKey != classEntry.Key)
                    {
                        user.Remove(userKey);
                        user[classEntry.Key] = userModes;
                    }
                }

                string mergedJson = user.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                await WriteFileAtomicallyAsync(_userSkillsSetsPath, mergedJson);
            }

            LoadSkills();
        }
        catch
        {
            // Keep the last known-good local skills if the source cannot be reached or parsed.
        }
    }

    /// <summary>
    /// MVS core rotations (Skills/MvsCoreSkills.json, built in): sets checked against the class's own
    /// mechanics, used for a class mode the player never customised (it is missing or still equals the
    /// published default). ArchMage's published "Base" cast Arcane Sigil only above 70% HP and below 70%
    /// mana, so the Corporeal Ascension heal over time (sized by mana when cast) never came when it was
    /// needed and the class died farming; Glacial Impact (-30% foe damage) only ran below 30% mana.
    /// </summary>
    private string ApplyCoreSkills(string userJson)
    {
        try
        {
            using Stream? stream = typeof(AdvancedSkillContainer).Assembly.GetManifestResourceStream("MvsCoreSkills.json");
            if (stream is null || JsonNode.Parse(stream) is not JsonObject core)
                return userJson;
            if (JsonNode.Parse(userJson) is not JsonObject user)
                return userJson;
            JsonObject? defaults = null;
            if (File.Exists(_defaultSkillsSetsPath))
                defaults = JsonNode.Parse(File.ReadAllText(_defaultSkillsSetsPath)) as JsonObject;

            bool changed = false;
            // Earlier published rotations a core set replaces even when today's default differs.
            JsonObject? replaces = core["_replaces"] as JsonObject;
            foreach (KeyValuePair<string, JsonNode?> classEntry in core)
            {
                if (classEntry.Key.StartsWith('_') || classEntry.Value is not JsonObject coreModes)
                    continue;
                string classKey = FindKey(user, classEntry.Key) ?? classEntry.Key;
                if (user[classKey] is not JsonObject userModes)
                {
                    user[classKey] = coreModes.DeepClone();
                    changed = true;
                    continue;
                }
                JsonObject? defaultModes = defaults is not null && FindKey(defaults, classEntry.Key) is { } dk ? defaults[dk] as JsonObject : null;
                foreach (KeyValuePair<string, JsonNode?> modeEntry in coreModes)
                {
                    JsonNode? userMode = userModes[modeEntry.Key];
                    JsonNode? defaultMode = defaultModes?[modeEntry.Key];
                    if (modeEntry.Value is null)
                        continue;
                    string? userCanon = Canon(userMode);
                    bool replaced = userCanon is not null && replaces?[classEntry.Key]?[modeEntry.Key] is JsonArray old
                        && old.Any(o => Canon(o) == userCanon);
                    if (userMode is null || replaced || (defaultMode is not null && Canon(defaultMode) == userCanon))
                    {
                        userModes[modeEntry.Key] = modeEntry.Value.DeepClone();
                        changed = true;
                        System.Diagnostics.Trace.WriteLine($"[Skills] MVS core rotation used for {classKey} / {modeEntry.Key}");
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine($"[Skills] {classKey} / {modeEntry.Key}: your own rotation kept (MVS core not applied): {userCanon}");
                    }
                }
            }
            return changed ? user.ToJsonString() : userJson;
        }
        catch
        {
            return userJson;
        }
    }

    // A rotation by meaning, not by text: the in-app Skills editor re-saves the file with other number
    // formatting and field order, so an untouched rotation no longer matched the published one byte for byte.
    private string? Canon(JsonNode? mode)
    {
        if (mode is null)
            return null;
        try
        {
            SkillModeJson? m = JsonSerializer.Deserialize<SkillModeJson>(mode.ToJsonString());
            return m is null ? null : $"{m.SkillUseMode}|{m.SkillTimeout}|{ConvertSkillsToString(m.Skills)}";
        }
        catch
        {
            return null;
        }
    }

    private static string? FindKey(JsonObject obj, string key)
    {
        if (obj.ContainsKey(key))
            return key;
        foreach (KeyValuePair<string, JsonNode?> entry in obj)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return entry.Key;
        }
        return null;
    }

    public void Add(AdvancedSkill skill)
    {
        _loadedSkills.Add(skill);
        Save();
    }

    public void Remove(AdvancedSkill skill)
    {
        _loadedSkills.Remove(skill);
        Save();
    }

    public void TryOverride(AdvancedSkill skill)
    {
        if (!_loadedSkills.Contains(skill))
        {
            _loadedSkills.Add(skill);
            Save();
            return;
        }

        int index = _loadedSkills.IndexOf(skill);
        _loadedSkills[index] = skill;
        Save();
    }

    private void _CopyDefaultSkills()
    {
        IGetScriptsService getScripts = Ioc.Default.GetRequiredService<IGetScriptsService>();
        if (!File.Exists(_defaultSkillsSetsPath))
            getScripts.UpdateSkillSetsFile().GetAwaiter().GetResult();

        if (File.Exists(_userSkillsSetsPath))
            File.Delete(_userSkillsSetsPath);

        File.Copy(_defaultSkillsSetsPath, _userSkillsSetsPath);
    }

    public async void SyncSkills()
    {
        try
        {
            _saveCts?.Cancel();
            await (_saveTask ?? Task.CompletedTask);
            _saveCts?.Dispose();
            _saveCts = new CancellationTokenSource();

            await Task.Factory.StartNew(() =>
            {
                _CopyDefaultSkills();
                LoadSkills();
            }, _saveCts.Token);
        }
        catch {/* ignored */}
    }

    public void LoadSkills()
    {
        List<AdvancedSkill> loadedSkills = new();
        AdvancedSkillsConfigJson? jsonConfig = null;

        IScriptOption? options = Ioc.Default.GetService<IScriptOption>();
        if (options == null || !options.UseFunctionBasedSkills)
        {
            _loadedFilePath = _userSkillsSetsPath;

            if (!File.Exists(_userSkillsSetsPath))
                _CopyDefaultSkills();

            if (File.Exists(_userSkillsSetsPath))
            {
                string fileContent = ApplyCoreSkills(File.ReadAllText(_userSkillsSetsPath));
                loadedSkills = LoadFromJson(fileContent, out jsonConfig);
            }
        }

        // Swap in a complete snapshot; consumers holding the old list can finish safely.
        _jsonConfig = jsonConfig;
        LoadedSkills = loadedSkills;
    }

    private List<AdvancedSkill> LoadFromJson(string jsonContent, out AdvancedSkillsConfigJson? jsonConfig)
    {
        List<AdvancedSkill> loadedSkills = new();
        jsonConfig = null;

        try
        {
            jsonConfig = JsonSerializer.Deserialize<AdvancedSkillsConfigJson>(jsonContent);
            if (jsonConfig == null)
                return loadedSkills;

            foreach (KeyValuePair<string, Dictionary<string, SkillModeJson>> classEntry in jsonConfig)
            {
                string className = classEntry.Key;
                foreach (KeyValuePair<string, SkillModeJson> modeEntry in classEntry.Value)
                {
                    string classUseMode = modeEntry.Key;
                    SkillModeJson skillMode = modeEntry.Value;
                    string skillsStr = ConvertSkillsToString(skillMode.Skills);

                    loadedSkills.Add(new AdvancedSkill(
                        className,
                        skillsStr,
                        skillMode.SkillTimeout,
                        classUseMode,
                        skillMode.SkillUseMode == "UseIfAvailable" ? SkillUseMode.UseIfAvailable : SkillUseMode.WaitForCooldown,
                        skillMode.ResetComboOnTargetChange
                    ));
                }
            }
        }
        catch
        {
            // Never publish a partially parsed skill set.
            loadedSkills.Clear();
            jsonConfig = null;
        }

        return loadedSkills;
    }

    private string ConvertSkillsToString(List<AdvancedSkillJson> skills)
    {
        List<string> parts = new();
        foreach (AdvancedSkillJson skill in skills)
        {
            string skillStr = skill.SkillId.ToString();
            if (skill.Rules?.Count > 0)
            {
                skillStr += " " + ConvertRulesToString(skill.Rules, skill.MultiAura, skill.MultiAuraOperator);
            }
            if (skill.SkipOnMatch)
            {
                skillStr += "S";
            }
            parts.Add(skillStr);
        }
        return string.Join(" | ", parts);
    }

    private string ConvertRulesToString(List<SkillRuleJson> rules, bool isMultiAura, string? multiAuraOperator)
    {
        List<string> ruleParts = new();
        List<SkillRuleJson> multiAuraRules = new();
        List<SkillRuleJson> singleAuraRules = new();
        List<SkillRuleJson> otherRules = new();

        foreach (SkillRuleJson rule in rules)
        {
            if (isMultiAura && rule.Type == "MultiAura")
                multiAuraRules.Add(rule);
            else if (!isMultiAura && rule.Type == "Aura")
                singleAuraRules.Add(rule);
            else if (rule.Type is not "MultiAura" and not "Aura" and not "Skip")
                otherRules.Add(rule);
        }

        foreach (SkillRuleJson? rule in otherRules)
        {
            string percentIndicator = (rule.IsPercentage ?? true) ? "%" : "#";
            switch (rule.Type)
            {
                case "Health":
                    ruleParts.Add($"H{(rule.Comparison == "greater" ? ">" : "<")}{rule.Value}{percentIndicator}");
                    break;

                case "Mana":
                    ruleParts.Add($"M{(rule.Comparison == "greater" ? ">" : "<")}{rule.Value}{percentIndicator}");
                    break;

                case "PartyHealth":
                    ruleParts.Add($"PH{(rule.Comparison == "greater" ? ">" : "<")}{rule.Value}{percentIndicator}");
                    break;

                case "Wait":
                    ruleParts.Add($"WW{rule.Timeout}");
                    break;
            }
        }

        if (isMultiAura && multiAuraRules.Count > 0)
        {
            string opChar = "&";
            if (!string.IsNullOrEmpty(multiAuraOperator))
            {
                opChar = multiAuraOperator switch
                {
                    "OR" => ":",
                    _ => "&"
                };
            }

            for (int i = 0; i < multiAuraRules.Count; i++)
            {
                SkillRuleJson rule = multiAuraRules[i];
                string suffix = i < multiAuraRules.Count - 1 ? opChar : "";
                ruleParts.Add($"MA{(rule.Comparison == "greater" ? ">" : "<")}\"{rule.AuraName}\" {rule.Value}{(rule.AuraTarget == "target" ? " TARGET" : "")}{suffix}");
            }
        }
        else if (singleAuraRules.Count > 1)
        {
            for (int i = 0; i < singleAuraRules.Count; i++)
            {
                SkillRuleJson rule = singleAuraRules[i];
                string suffix = i < singleAuraRules.Count - 1 ? "&" : "";
                ruleParts.Add($"MA{(rule.Comparison == "greater" ? ">" : "<")}\"{rule.AuraName}\" {rule.Value}{(rule.AuraTarget == "target" ? " TARGET" : "")}{suffix}");
            }
        }
        else if (singleAuraRules.Count == 1)
        {
            SkillRuleJson rule = singleAuraRules[0];
            ruleParts.Add($"A{(rule.Comparison == "greater" ? ">" : "<")}\"{rule.AuraName}\" {rule.Value}{(rule.AuraTarget == "target" ? " TARGET" : "")}");
        }

        return string.Join(" ", ruleParts);
    }

    public Dictionary<string, List<string>> GetAvailableClassModes()
    {
        if (_jsonConfig == null)
        {
            string jsonPath = Path.ChangeExtension(_userSkillsSetsPath, ".json");
            if (File.Exists(jsonPath))
            {
                string fileContent = File.ReadAllText(jsonPath);
                _jsonConfig = JsonSerializer.Deserialize<AdvancedSkillsConfigJson>(fileContent);
            }
        }

        Dictionary<string, List<string>> result = new();
        if (_jsonConfig != null)
        {
            foreach (KeyValuePair<string, Dictionary<string, SkillModeJson>> classEntry in _jsonConfig)
            {
                result[classEntry.Key] = new List<string>(classEntry.Value.Keys);
            }
        }
        return result;
    }

    public AdvancedSkill? GetClassModeSkills(string className, string mode)
    {
        return _loadedSkills.FirstOrDefault(s => s.ClassName == className && s.ClassUseMode.ToString() == mode);
    }

    public void ResetSkillsSets()
    {
        SyncSkills();
    }

    public async void Save()
    {
        _saveCts?.Cancel();
        await (_saveTask ?? Task.CompletedTask);
        _saveCts?.Dispose();
        _saveCts = new CancellationTokenSource();

        _saveTask = Task.Factory.StartNew(() =>
        {
            try
            {
                string jsonPath = _loadedFilePath ?? Path.ChangeExtension(_userSkillsSetsPath, ".json");
                if (!jsonPath.EndsWith(".json"))
                    jsonPath = Path.ChangeExtension(jsonPath, ".json");
                SaveToJson(jsonPath);

                if (!_saveCts.Token.IsCancellationRequested)
                {
                    LoadSkills();
                }
            }
            catch
            {
            }
        }, _saveCts.Token);
    }

    private void SaveToJson(string filePath)
    {
        AdvancedSkillsConfigJson config = new();

        foreach (AdvancedSkill skill in _loadedSkills)
        {
            if (!config.ContainsKey(skill.ClassName))
                config[skill.ClassName] = new Dictionary<string, SkillModeJson>();

            SkillModeJson skillMode = new()
            {
                SkillUseMode = skill.SkillUseMode == SkillUseMode.UseIfAvailable ? "UseIfAvailable" : "WaitForCooldown",
                SkillTimeout = skill.SkillTimeout,
                ResetComboOnTargetChange = skill.ResetComboOnTargetChange,
                Skills = AdvancedSkillsParser.ParseSkillString(skill.Skills)
            };

            config[skill.ClassName][skill.ClassUseMode.ToString()] = skillMode;
        }

        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(filePath, json);
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _saveCts?.Cancel();
            try
            {
                _saveTask?.Wait(1000);
            }
            catch { }
            _saveCts?.Dispose();
            _loadedSkills.Clear();
        }

        _disposed = true;
    }

    ~AdvancedSkillContainer()
    {
        Dispose(false);
    }
}