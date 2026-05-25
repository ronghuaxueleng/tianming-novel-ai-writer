using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Framework.Common.Search
{
    public static class SearchHelper
    {
        public static int CalculateMatchScore(string targetText, string keyword, params string[] additionalFields)
        {
            if (string.IsNullOrEmpty(keyword))
                return 1000;

            if (string.IsNullOrEmpty(targetText))
                return 0;

            var target = targetText.ToLower();
            var key = keyword.ToLower();

            if (target == key)
                return 10000;

            if (target.StartsWith(key))
                return 5000;

            if (target.Contains(key))
                return 2000 + (100 - target.IndexOf(key));

            var words = target.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var firstLetters = string.Join("", words.Select(w => w.Length > 0 ? w[0].ToString() : ""));

            if (firstLetters.Contains(key))
                return 500;

            if (words.Any(w => ContainsAllChars(w, key)))
                return 300;

            if (additionalFields != null && additionalFields.Length > 0)
            {
                foreach (var field in additionalFields)
                {
                    if (!string.IsNullOrEmpty(field))
                    {
                        var fieldLower = field.ToLower();

                        if (fieldLower == key)
                            return 800;

                        if (fieldLower.StartsWith(key))
                            return 600;

                        if (fieldLower.Contains(key))
                            return 400 + (50 - Math.Min(fieldLower.IndexOf(key), 50));
                    }
                }
            }

            return 0;
        }

        private static bool ContainsAllChars(string target, string keyword)
        {
            var targetChars = target.ToCharArray();
            return keyword.All(c => targetChars.Contains(c));
        }

        public static List<T> FilterAndSort<T>(
            IEnumerable<T> items, 
            string keyword, 
            Func<T, string> getTargetText,
            Func<T, string[]>? getAdditionalFields = null)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return items.ToList();

            var scoredItems = items
                .Select(item => new
                {
                    Item = item,
                    Score = CalculateMatchScore(
                        getTargetText(item),
                        keyword,
                        getAdditionalFields?.Invoke(item) ?? Array.Empty<string>()
                    )
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)
                .ToList();

            return scoredItems;
        }
    }
}

