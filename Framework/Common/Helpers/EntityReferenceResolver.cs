using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;

namespace TM.Framework.Common.Helpers
{
    public static class EntityReferenceResolver
    {
        public static string NameToId(
            string nameOrId,
            Dictionary<string, string> nameToIdMap,
            Dictionary<string, string>? idToNameMap = null)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return string.Empty;
            var normalized = EntityNameNormalizeHelper.StripBracketAnnotation(nameOrId).Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(normalized)) return string.Empty;
            if (ShortIdGenerator.IsLikelyId(normalized))
                return idToNameMap?.ContainsKey(normalized) == true ? normalized : string.Empty;
            if (nameToIdMap.TryGetValue(normalized, out var id)) return id;
            return string.Empty;
        }

        public static string NameToIdNormalized(
            string nameOrId,
            Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(nameOrId)) return string.Empty;
            var normalized = EntityNameNormalizeHelper.StripBracketAnnotation(nameOrId).Trim();
            if (EntityNameNormalizeHelper.IsIgnoredValue(normalized)) return string.Empty;
            if (nameToIdMap.TryGetValue(normalized, out var id)) return id;
            if (ShortIdGenerator.IsLikelyId(normalized)) return normalized;
            return string.Empty;
        }

        public static string NamesToIds(string names, Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(names)) return string.Empty;
            return string.Join("、", names
                .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => NameToId(s.Trim(), nameToIdMap))
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        public static string IdToName(
            string idOrName,
            Dictionary<string, string> idToNameMap,
            Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return string.Empty;
            if (idToNameMap.TryGetValue(idOrName, out var name)) return name;
            if (nameToIdMap.ContainsKey(idOrName)) return idOrName;
            if (ShortIdGenerator.IsLikelyId(idOrName)) return string.Empty;
            return idOrName;
        }

        public static string IdsToNames(
            string idsOrNames,
            Dictionary<string, string> idToNameMap,
            Dictionary<string, string> nameToIdMap)
        {
            if (string.IsNullOrWhiteSpace(idsOrNames)) return string.Empty;
            return string.Join("、", idsOrNames
                .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => IdToName(s.Trim(), idToNameMap, nameToIdMap))
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }
}
