using System.Collections.Generic;

namespace TM.Framework.Common.Helpers.Utility
{
    public static class RegionDataHelper
    {
        private static readonly Dictionary<string, List<string>> CountryProvincesMap = new()
        {
            {
                "中国", new List<string>
                {
                    "北京市", "天津市", "上海市", "重庆市",
                    "河北省", "山西省", "辽宁省", "吉林省", "黑龙江省",
                    "江苏省", "浙江省", "安徽省", "福建省", "江西省", "山东省",
                    "河南省", "湖北省", "湖南省", "广东省", "海南省",
                    "四川省", "贵州省", "云南省", "陕西省", "甘肃省", "青海省", "台湾省",
                    "内蒙古自治区", "广西壮族自治区", "西藏自治区", "宁夏回族自治区", "新疆维吾尔自治区",
                    "香港特别行政区", "澳门特别行政区"
                }
            },
            {
                "美国", new List<string>
                {
                    "加利福尼亚州", "纽约州", "德克萨斯州", "佛罗里达州", "伊利诺伊州",
                    "宾夕法尼亚州", "俄亥俄州", "佐治亚州", "北卡罗来纳州", "密歇根州"
                }
            },
            {
                "日本", new List<string>
                {
                    "东京都", "大阪府", "京都府", "北海道", "神奈川县",
                    "爱知县", "埼玉县", "千叶县", "兵库县", "福冈县"
                }
            },
            {
                "英国", new List<string>
                {
                    "英格兰", "苏格兰", "威尔士", "北爱尔兰"
                }
            },
            {
                "法国", new List<string>
                {
                    "法兰西岛", "普罗旺斯-阿尔卑斯-蓝色海岸", "奥弗涅-罗讷-阿尔卑斯",
                    "新阿基坦", "奥克西塔尼", "上法兰西", "大东部"
                }
            },
            {
                "德国", new List<string>
                {
                    "巴伐利亚州", "北莱茵-威斯特法伦州", "巴登-符腾堡州",
                    "下萨克森州", "黑森州", "萨克森州", "柏林"
                }
            },
            {
                "加拿大", new List<string>
                {
                    "安大略省", "魁北克省", "不列颠哥伦比亚省", "阿尔伯塔省",
                    "曼尼托巴省", "萨斯喀彻温省", "新斯科舍省"
                }
            },
            {
                "澳大利亚", new List<string>
                {
                    "新南威尔士州", "维多利亚州", "昆士兰州", "西澳大利亚州",
                    "南澳大利亚州", "塔斯马尼亚州", "北领地", "首都领地"
                }
            },
            {
                "韩国", new List<string>
                {
                    "首尔特别市", "釜山广域市", "仁川广域市", "大邱广域市",
                    "光州广域市", "大田广域市", "蔚山广域市", "世宗特别自治市",
                    "京畿道", "江原道", "忠清北道", "忠清南道", "全罗北道", "全罗南道",
                    "庆尚北道", "庆尚南道", "济州特别自治道"
                }
            },
            {
                "新加坡", new List<string> { "新加坡" }
            },
            {
                "其他", new List<string> { "未指定" }
            }
        };

        public static List<string> GetCountries()
        {
            return new List<string>(CountryProvincesMap.Keys);
        }

        public static List<string> GetProvinces(string country)
        {
            if (string.IsNullOrWhiteSpace(country))
                return new List<string>();

            if (CountryProvincesMap.TryGetValue(country, out var provinces))
            {
                return new List<string>(provinces);
            }

            return new List<string>();
        }

        public static bool IsValidCountry(string country)
        {
            return CountryProvincesMap.ContainsKey(country);
        }

        public static bool IsValidProvince(string country, string province)
        {
            if (!CountryProvincesMap.TryGetValue(country, out var provinces))
                return false;

            return provinces.Contains(province);
        }
    }
}

