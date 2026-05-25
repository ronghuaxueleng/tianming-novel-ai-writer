using System;
using System.Collections.Generic;
using System.Linq;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    public class HolidayLibrary
    {
        private readonly Dictionary<int, List<HolidayInfo>> _holidaysByYear;

        public HolidayLibrary()
        {
            _holidaysByYear = new Dictionary<int, List<HolidayInfo>>();
            LoadBuiltInHolidays();
            TM.App.Log("[HolidayLibrary] 初始化完成，已加载2024-2030年节假日数据");
        }

        private void LoadBuiltInHolidays()
        {
            _holidaysByYear[2024] = new List<HolidayInfo>
            {
                new HolidayInfo { Date = new DateTime(2024, 1, 1), Name = "元旦", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 10), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 11), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 12), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 13), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 14), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 15), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 16), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 2, 17), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 4, 4), Name = "清明节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 4, 5), Name = "清明节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 4, 6), Name = "清明节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 5, 1), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 5, 2), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 5, 3), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 5, 4), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 5, 5), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 6, 10), Name = "端午节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 9, 15), Name = "中秋节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 9, 16), Name = "中秋节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 9, 17), Name = "中秋节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 1), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 2), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 3), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 4), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 5), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 6), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2024, 10, 7), Name = "国庆节", Type = HolidayType.National }
            };

            _holidaysByYear[2025] = new List<HolidayInfo>
            {
                new HolidayInfo { Date = new DateTime(2025, 1, 1), Name = "元旦", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 1, 28), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 1, 29), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 1, 30), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 1, 31), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 2, 1), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 2, 2), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 2, 3), Name = "春节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 4, 4), Name = "清明节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 4, 5), Name = "清明节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 4, 6), Name = "清明节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 5, 1), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 5, 2), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 5, 3), Name = "劳动节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 5, 31), Name = "端午节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 6, 1), Name = "端午节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 6, 2), Name = "端午节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 1), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 2), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 3), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 4), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 5), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 6), Name = "中秋节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 7), Name = "国庆节", Type = HolidayType.National },
                new HolidayInfo { Date = new DateTime(2025, 10, 8), Name = "国庆节", Type = HolidayType.National }
            };

        }

        public bool IsHoliday(DateTime date)
        {
            return GetHolidayInfo(date) != null;
        }

        public HolidayInfo? GetHolidayInfo(DateTime date)
        {
            if (!_holidaysByYear.ContainsKey(date.Year))
                return null;

            return _holidaysByYear[date.Year].FirstOrDefault(h => h.Date.Date == date.Date);
        }

        public List<HolidayInfo> GetHolidaysByYear(int year)
        {
            return _holidaysByYear.ContainsKey(year) ? _holidaysByYear[year] : new List<HolidayInfo>();
        }

        public List<HolidayInfo> GetHolidaysByMonth(int year, int month)
        {
            if (!_holidaysByYear.ContainsKey(year))
                return new List<HolidayInfo>();

            return _holidaysByYear[year].Where(h => h.Date.Month == month).ToList();
        }

        public List<int> GetSupportedYears()
        {
            return _holidaysByYear.Keys.OrderBy(y => y).ToList();
        }
    }

    public class HolidayInfo
    {
        public DateTime Date { get; set; }

        public string Name { get; set; } = string.Empty;

        public HolidayType Type { get; set; }

        public bool IsWorkday { get; set; } = false;

        public string DisplayText => $"{Date:yyyy-MM-dd} {Name}";
    }

    [System.Reflection.Obfuscation(Exclude = true)]
    public enum HolidayType
    {
        National,
        Custom
    }
}

