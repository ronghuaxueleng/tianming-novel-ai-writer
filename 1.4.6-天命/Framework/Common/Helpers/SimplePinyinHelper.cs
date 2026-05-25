using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TM.Framework.Common.Helpers
{
    public static class SimplePinyinHelper
    {
        public static string? ToPinyinKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var sb = new StringBuilder(text.Length * 3);
            var hasChinese = false;

            foreach (var c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    if (PinyinMap.TryGetValue(c, out var py))
                    {
                        sb.Append(py);
                        hasChinese = true;
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return hasChinese ? sb.ToString() : null;
        }

        public static string? ToPinyinInitials(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var sb = new StringBuilder(text.Length);
            var hasChinese = false;

            foreach (var c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    if (PinyinMap.TryGetValue(c, out var py))
                    {
                        sb.Append(py[0]);
                        hasChinese = true;
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return hasChinese ? sb.ToString() : null;
        }

        public static bool LooksLikePinyin(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
                return false;

            var hasLetter = false;
            foreach (var c in token)
            {
                if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z')
                {
                    hasLetter = true;
                }
                else if (c != '_' && c != '-' && c != ' ' && c != '.')
                {
                    return false;
                }
            }

            return hasLetter;
        }

        public static string NormalizePinyinToken(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (var c in token)
            {
                if (c >= 'a' && c <= 'z')
                    sb.Append(c);
                else if (c >= 'A' && c <= 'Z')
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static readonly Dictionary<char, string> PinyinMap = new()
        {
            {'赵',"zhao"},{'钱',"qian"},{'孙',"sun"},{'李',"li"},{'周',"zhou"},
            {'吴',"wu"},{'郑',"zheng"},{'王',"wang"},{'冯',"feng"},{'陈',"chen"},
            {'褚',"chu"},{'卫',"wei"},{'蒋',"jiang"},{'沈',"shen"},{'韩',"han"},
            {'杨',"yang"},{'朱',"zhu"},{'秦',"qin"},{'尤',"you"},{'许',"xu"},
            {'何',"he"},{'吕',"lv"},{'施',"shi"},{'张',"zhang"},{'孔',"kong"},
            {'曹',"cao"},{'严',"yan"},{'华',"hua"},{'金',"jin"},{'魏',"wei"},
            {'陶',"tao"},{'姜',"jiang"},{'戚',"qi"},{'谢',"xie"},{'邹',"zou"},
            {'喻',"yu"},{'柏',"bai"},{'窦',"dou"},{'章',"zhang"},{'云',"yun"},
            {'苏',"su"},{'潘',"pan"},{'葛',"ge"},{'奚',"xi"},{'范',"fan"},
            {'彭',"peng"},{'郎',"lang"},{'鲁',"lu"},{'韦',"wei"},{'昌',"chang"},
            {'马',"ma"},{'苗',"miao"},{'方',"fang"},{'俞',"yu"},{'任',"ren"},
            {'袁',"yuan"},{'柳',"liu"},{'邓',"deng"},{'鲍',"bao"},{'史',"shi"},
            {'唐',"tang"},{'费',"fei"},{'廉',"lian"},{'岑',"cen"},{'薛',"xue"},
            {'雷',"lei"},{'贺',"he"},{'倪',"ni"},{'汤',"tang"},{'滕',"teng"},
            {'殷',"yin"},{'罗',"luo"},{'毕',"bi"},{'郝',"hao"},{'邬',"wu"},
            {'安',"an"},{'常',"chang"},{'乐',"le"},{'于',"yu"},{'时',"shi"},
            {'傅',"fu"},{'皮',"pi"},{'卞',"bian"},{'齐',"qi"},{'康',"kang"},
            {'伍',"wu"},{'余',"yu"},{'元',"yuan"},{'卜',"bu"},{'顾',"gu"},
            {'孟',"meng"},{'黄',"huang"},{'穆',"mu"},{'萧',"xiao"},{'尹',"yin"},
            {'姚',"yao"},{'邵',"shao"},{'湛',"zhan"},{'汪',"wang"},{'祁',"qi"},
            {'禹',"yu"},{'狄',"di"},{'贝',"bei"},{'明',"ming"},{'臧',"zang"},
            {'计',"ji"},{'成',"cheng"},{'戴',"dai"},{'宋',"song"},{'茅',"mao"},
            {'庞',"pang"},{'纪',"ji"},{'舒',"shu"},{'屈',"qu"},{'项',"xiang"},
            {'祝',"zhu"},{'董',"dong"},{'梁',"liang"},{'杜',"du"},{'阮',"ruan"},
            {'蓝',"lan"},{'闵',"min"},{'席',"xi"},{'季',"ji"},{'贾',"jia"},
            {'路',"lu"},{'娄',"lou"},{'危',"wei"},{'刘',"liu"},{'童',"tong"},
            {'颜',"yan"},{'郭',"guo"},{'高',"gao"},{'林',"lin"},{'丁',"ding"},
            {'叶',"ye"},{'田',"tian"},{'崔',"cui"},{'龚',"gong"},{'程',"cheng"},
            {'嵇',"ji"},{'邢',"xing"},{'滑',"hua"},{'裴',"pei"},{'陆',"lu"},
            {'荣',"rong"},{'翁',"weng"},{'荀',"xun"},{'羊',"yang"},{'甄',"zhen"},
            {'曲',"qu"},{'庄',"zhuang"},{'晏',"yan"},{'瞿',"qu"},{'阎',"yan"},
            {'连',"lian"},{'习',"xi"},{'段',"duan"},{'石',"shi"},{'侯',"hou"},
            {'天',"tian"},{'地',"di"},{'人',"ren"},{'龙',"long"},{'凤',"feng"},
            {'虎',"hu"},{'鹰',"ying"},{'狼',"lang"},{'鹤',"he"},{'麟',"lin"},
            {'玄',"xuan"},{'灵',"ling"},{'幽',"you"},{'冥',"ming"},{'空',"kong"},
            {'尘',"chen"},{'风',"feng"},{'雪',"xue"},{'霜',"shuang"},{'月',"yue"},
            {'星',"xing"},{'日',"ri"},{'阳',"yang"},{'阴',"yin"},{'光',"guang"},
            {'暗',"an"},{'影',"ying"},{'梦',"meng"},{'幻',"huan"},{'真',"zhen"},
            {'离',"li"},{'寒',"han"},{'冰',"bing"},{'火',"huo"},{'水',"shui"},
            {'木',"mu"},{'土',"tu"},{'清',"qing"},{'浊',"zhuo"},{'辰',"chen"},
            {'宇',"yu"},{'轩',"xuan"},{'泽',"ze"},{'瑞',"rui"},{'博',"bo"},
            {'文',"wen"},{'武',"wu"},{'才',"cai"},{'德',"de"},{'仁',"ren"},
            {'义',"yi"},{'礼',"li"},{'智',"zhi"},{'信',"xin"},{'忠',"zhong"},
            {'孝',"xiao"},{'勇',"yong"},{'刚',"gang"},{'柔',"rou"},{'静',"jing"},
            {'婉',"wan"},{'慧',"hui"},{'雅',"ya"},{'芳',"fang"},{'兰',"lan"},
            {'菊',"ju"},{'莲',"lian"},{'蕊',"rui"},{'薇',"wei"},{'颖',"ying"},
            {'若',"ruo"},{'如',"ru"},{'子',"zi"},{'之',"zhi"},{'一',"yi"},
            {'飞',"fei"},{'翔',"xiang"},{'鸣',"ming"},{'啸',"xiao"},{'傲',"ao"},
            {'霸',"ba"},{'绝',"jue"},{'破',"po"},{'灭',"mie"},{'战',"zhan"},
            {'修',"xiu"},{'炼',"lian"},{'道',"dao"},{'佛',"fo"},{'仙',"xian"},
            {'魔',"mo"},{'妖',"yao"},{'鬼',"gui"},{'神',"shen"},{'圣',"sheng"},
            {'皇',"huang"},{'帝',"di"},{'尊',"zun"},{'主',"zhu"},{'君',"jun"},
            {'剑',"jian"},{'刀',"dao"},{'枪',"qiang"},{'棍',"gun"},{'弓',"gong"},
            {'盾',"dun"},{'甲',"jia"},{'阵',"zhen"},{'符',"fu"},{'丹',"dan"},
            {'药',"yao"},{'器',"qi"},{'宝',"bao"},{'珠',"zhu"},{'玉',"yu"},
            {'印',"yin"},{'塔',"ta"},{'殿',"dian"},{'宫',"gong"},{'阁',"ge"},
            {'门',"men"},{'派',"pai"},{'宗',"zong"},{'盟',"meng"},{'会',"hui"},
            {'城',"cheng"},{'山',"shan"},{'海',"hai"},{'河',"he"},{'谷',"gu"},
            {'林',"lin"},{'原',"yuan"},{'岛',"dao"},{'洞',"dong"},{'崖',"ya"},
            {'峰',"feng"},{'渊',"yuan"},{'域',"yu"},{'界',"jie"},{'境',"jing"},
            {'国',"guo"},{'族',"zu"},{'部',"bu"},{'营',"ying"},{'寨',"zhai"},
            {'血',"xue"},{'魂',"hun"},{'骨',"gu"},{'心',"xin"},{'眼',"yan"},
            {'手',"shou"},{'掌',"zhang"},{'拳',"quan"},{'指',"zhi"},{'腿',"tui"},
            {'气',"qi"},{'力',"li"},{'势',"shi"},{'意',"yi"},{'念',"nian"},
            {'术',"shu"},{'法',"fa"},{'功',"gong"},{'技',"ji"},{'招',"zhao"},
            {'式',"shi"},{'步',"bu"},{'身',"shen"},{'形',"xing"},{'体',"ti"},
            {'命',"ming"},{'运',"yun"},{'劫',"jie"},{'数',"shu"},{'缘',"yuan"},
            {'恨',"hen"},{'爱',"ai"},{'情',"qing"},{'怒',"nu"},{'哀',"ai"},
            {'惧',"ju"},{'喜',"xi"},{'怨',"yuan"},{'悲',"bei"},{'欢',"huan"},
            {'生',"sheng"},{'死',"si"},{'存',"cun"},{'亡',"wang"},{'兴',"xing"},
            {'衰',"shuai"},{'盛',"sheng"},{'败',"bai"},{'胜',"sheng"},{'负',"fu"},
            {'长',"chang"},{'短',"duan"},{'大',"da"},{'小',"xiao"},{'深',"shen"},
            {'浅',"qian"},{'远',"yuan"},{'近',"jin"},{'上',"shang"},{'下',"xia"},
            {'东',"dong"},{'西',"xi"},{'南',"nan"},{'北',"bei"},{'中',"zhong"},
            {'白',"bai"},{'黑',"hei"},{'红',"hong"},{'青',"qing"},{'紫',"zi"},
            {'碧',"bi"},{'苍',"cang"},{'墨',"mo"},{'银',"yin"},{'铜',"tong"},
            {'铁',"tie"},{'钢',"gang"},{'霞',"xia"},{'烟',"yan"},{'雾',"wu"},
            {'露',"lu"},{'雨',"yu"},{'电',"dian"},{'雷',"lei"},{'鹏',"peng"},
        };
    }
}
