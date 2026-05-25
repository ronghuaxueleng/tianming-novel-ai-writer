using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;

namespace TM.Framework.Common.Helpers
{
    public static class EntityNameResolver
    {
        private static readonly object _lock = new();
        private static Dictionary<string, string>? _characterMap;
        private static Dictionary<string, string>? _locationMap;
        private static Dictionary<string, string>? _factionMap;
        private static Dictionary<string, string>? _plotRuleMap;
        private static Dictionary<string, string>? _foreshadowingMap;
        private static Dictionary<string, string>? _conflictMap;
        private static Dictionary<string, string>? _worldRuleMap;
        private static Dictionary<string, string>? _volumeDesignMap;
        private static Dictionary<string, string>? _chapterPlanMap;
        private static Dictionary<string, string>? _blueprintMap;
        private static Dictionary<string, string>? _outlineMap;
        private static DateTime _lastLoadTime = DateTime.MinValue;
        private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

        public static string Resolve(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return entityId;

            EnsureLoaded();

            lock (_lock)
            {
                if (_foreshadowingMap?.TryGetValue(entityId, out var fName) == true && !string.IsNullOrEmpty(fName))
                    return fName;
                if (_conflictMap?.TryGetValue(entityId, out var cName) == true && !string.IsNullOrEmpty(cName))
                    return cName;
                if (_characterMap?.TryGetValue(entityId, out var charName) == true && !string.IsNullOrEmpty(charName))
                    return charName;
                if (_locationMap?.TryGetValue(entityId, out var locName) == true && !string.IsNullOrEmpty(locName))
                    return locName;
                if (_factionMap?.TryGetValue(entityId, out var facName) == true && !string.IsNullOrEmpty(facName))
                    return facName;
                if (_plotRuleMap?.TryGetValue(entityId, out var plotName) == true && !string.IsNullOrEmpty(plotName))
                    return plotName;
                if (_worldRuleMap?.TryGetValue(entityId, out var worldName) == true && !string.IsNullOrEmpty(worldName))
                    return worldName;
                if (_volumeDesignMap?.TryGetValue(entityId, out var volDesignName) == true && !string.IsNullOrEmpty(volDesignName))
                    return volDesignName;
                if (_chapterPlanMap?.TryGetValue(entityId, out var chapterName) == true && !string.IsNullOrEmpty(chapterName))
                    return chapterName;
                if (_blueprintMap?.TryGetValue(entityId, out var bpName) == true && !string.IsNullOrEmpty(bpName))
                    return bpName;
                if (_outlineMap?.TryGetValue(entityId, out var outlineName) == true && !string.IsNullOrEmpty(outlineName))
                    return outlineName;
            }

            return entityId;
        }

        public static string ResolveCharacter(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                return _characterMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name) ? name : entityId;
            }
        }

        public static string ResolveForeshadowing(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                return _foreshadowingMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name) ? name : entityId;
            }
        }

        public static string ResolveConflict(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                return _conflictMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name) ? name : entityId;
            }
        }

        public static void Invalidate()
        {
            lock (_lock)
            {
                _lastLoadTime = DateTime.MinValue;
            }
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (DateTime.Now - _lastLoadTime < CacheExpiry &&
                    _characterMap != null && _foreshadowingMap != null && _conflictMap != null)
                {
                    return;
                }

                _characterMap = new Dictionary<string, string>();
                _locationMap = new Dictionary<string, string>();
                _factionMap = new Dictionary<string, string>();
                _plotRuleMap = new Dictionary<string, string>();
                _foreshadowingMap = new Dictionary<string, string>();
                _conflictMap = new Dictionary<string, string>();
                _worldRuleMap = new Dictionary<string, string>();
                _volumeDesignMap = new Dictionary<string, string>();
                _chapterPlanMap = new Dictionary<string, string>();
                _blueprintMap = new Dictionary<string, string>();
                _outlineMap = new Dictionary<string, string>();

                try
                {
                    LoadFromElements();
                    LoadFromGlobalSettings();
                    LoadFromGenerateElements();
                    LoadFromGuides();
                    _lastLoadTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityNameResolver] 加载映射失败: {ex.Message}");
                }
            }
        }

        private static void LoadFromElements()
        {
            var elementsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Design", "elements.json");
            if (!File.Exists(elementsPath)) return;

            var json = File.ReadAllText(elementsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var data)) return;

            if (data.TryGetProperty("characterrules", out var charModule) &&
                charModule.TryGetProperty("character_rules", out var characters))
            {
                foreach (var item in characters.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        _characterMap![id] = name;
                }
            }

            if (data.TryGetProperty("locationrules", out var locModule) &&
                locModule.TryGetProperty("location_rules", out var locations))
            {
                foreach (var item in locations.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        _locationMap![id] = name;
                }
            }

            if (data.TryGetProperty("factionrules", out var facModule) &&
                facModule.TryGetProperty("faction_rules", out var factions))
            {
                foreach (var item in factions.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        _factionMap![id] = name;
                }
            }

            if (data.TryGetProperty("plotrules", out var plotModule) &&
                plotModule.TryGetProperty("plot_rules", out var plotRules))
            {
                foreach (var item in plotRules.EnumerateArray())
                {
                    var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                    var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        _plotRuleMap![id] = name;
                }
            }
        }

        private static void LoadFromGuides()
        {
            var guidesPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "guides");
            if (!Directory.Exists(guidesPath)) return;

            var foreshadowingPath = Path.Combine(guidesPath, "foreshadowing_status_guide.json");
            if (File.Exists(foreshadowingPath))
            {
                try
                {
                    var json = File.ReadAllText(foreshadowingPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Foreshadowings", out var foreshadowings))
                    {
                        foreach (var prop in foreshadowings.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                _foreshadowingMap![id] = name;
                        }
                    }
                }
                catch {}
            }

            var conflictFiles = Directory.Exists(guidesPath)
                ? Directory.GetFiles(guidesPath, "conflict_progress_guide_vol*.json")
                : Array.Empty<string>();
            foreach (var conflictPath in conflictFiles)
            {
                try
                {
                    var json = File.ReadAllText(conflictPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Conflicts", out var conflicts))
                    {
                        foreach (var prop in conflicts.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                _conflictMap![id] = name;
                        }
                    }
                }
                catch {}
            }

            var outlinePath = Path.Combine(guidesPath, "outline_guide.json");
            if (File.Exists(outlinePath))
            {
                try
                {
                    var json = File.ReadAllText(outlinePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("Volumes", out var volumes))
                    {
                        foreach (var prop in volumes.EnumerateObject())
                        {
                            var id = prop.Name;
                            var name = prop.Value.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                                _outlineMap![id] = name;
                        }
                    }
                }
                catch {}
            }
        }

        private static void LoadFromGlobalSettings()
        {
            var settingsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Design", "globalsettings.json");
            if (!File.Exists(settingsPath)) return;

            try
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return;

                if (data.TryGetProperty("worldrules", out var worldModule) &&
                    worldModule.TryGetProperty("world_rules", out var worldRules))
                {
                    foreach (var item in worldRules.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            _worldRuleMap![id] = name;
                    }
                }
            }
            catch {}
        }

        private static void LoadFromGenerateElements()
        {
            var elementsPath = Path.Combine(StoragePathHelper.GetProjectConfigPath(), "Generate", "elements.json");
            if (!File.Exists(elementsPath)) return;

            try
            {
                var json = File.ReadAllText(elementsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data)) return;

                if (data.TryGetProperty("volumedesign", out var volModule) &&
                    volModule.TryGetProperty("volume_design_data", out var volDesigns))
                {
                    foreach (var item in volDesigns.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            _volumeDesignMap![id] = name;
                    }
                }

                if (data.TryGetProperty("chapter", out var chapterModule) &&
                    chapterModule.TryGetProperty("chapter_data", out var chapters))
                {
                    foreach (var item in chapters.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            _chapterPlanMap![id] = name;
                    }
                }

                if (data.TryGetProperty("blueprint", out var bpModule) &&
                    bpModule.TryGetProperty("blueprint_data", out var blueprints))
                {
                    foreach (var item in blueprints.EnumerateArray())
                    {
                        var id = item.TryGetProperty("Id", out var idProp) ? idProp.GetString() : null;
                        var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            _blueprintMap![id] = name;
                    }
                }
            }
            catch {}
        }
    }
}
