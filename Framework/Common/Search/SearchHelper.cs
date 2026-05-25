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

            var cmp = StringComparison.OrdinalIgnoreCase;

            if (targetText.Equals(keyword, cmp))
                return 10000;

            if (targetText.StartsWith(keyword, cmp))
                return 5000;

            var idx = targetText.IndexOf(keyword, cmp);
            if (idx >= 0)
                return 2000 + (100 - idx);

            var targetLower = targetText.ToLower();
            var keyLower = keyword.ToLower();
            var words = targetLower.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var firstLetters = string.Join("", words.Select(w => w.Length > 0 ? w[0].ToString() : ""));

            if (firstLetters.Contains(keyLower, cmp))
                return 500;

            if (words.Any(w => ContainsAllChars(w, keyLower)))
                return 300;

            if (additionalFields != null && additionalFields.Length > 0)
            {
                foreach (var field in additionalFields)
                {
                    if (!string.IsNullOrEmpty(field))
                    {
                        if (field.Equals(keyword, cmp))
                            return 800;

                        if (field.StartsWith(keyword, cmp))
                            return 600;

                        var fIdx = field.IndexOf(keyword, cmp);
                        if (fIdx >= 0)
                            return 400 + (50 - Math.Min(fIdx, 50));
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

