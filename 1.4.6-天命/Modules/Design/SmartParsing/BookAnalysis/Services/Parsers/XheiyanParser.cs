using System;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TM.Modules.Design.SmartParsing.BookAnalysis.Services
{
    public class XheiyanParser : INovelParser
    {
        public string[] SupportedDomains => new[]
        {
            "www.xheiyan.info",
            "xheiyan.info",
            "m.xheiyan.info"
        };

        public string SiteName => "黑岩阅读网";

        public NovelInfo? ParseNovelInfo(string html)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(html)) return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var info = new NovelInfo();

                var title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()
                         ?? doc.DocumentNode.SelectSingleNode("//div[@id='info']/h1")?.InnerText?.Trim();
                info.Title = HtmlEntity.DeEntitize(title ?? string.Empty);

                var authorNode = doc.DocumentNode.SelectSingleNode("//div[@id='info']/p[contains(text(),'作')]")
                              ?? doc.DocumentNode.SelectSingleNode("//meta[@name='author']");
                if (authorNode != null)
                {
                    var authorText = authorNode.Name == "meta"
                        ? authorNode.GetAttributeValue("content", string.Empty)
                        : authorNode.InnerText;
                    var match = Regex.Match(authorText ?? string.Empty, @"作\s*者[：:]\s*(.+)");
                    info.Author = match.Success ? match.Groups[1].Value.Trim() : HtmlEntity.DeEntitize(authorText ?? string.Empty).Trim();
                }

                var desc = doc.DocumentNode.SelectSingleNode("//div[@id='intro']")?.InnerText?.Trim()
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'intro')]")?.InnerText?.Trim()
                        ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty)?.Trim();
                info.Description = HtmlEntity.DeEntitize(desc ?? string.Empty);

                return info;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[XheiyanParser] 解析小说信息失败: {ex.Message}");
                return null;
            }
        }

        public NovelCatalog? ParseCatalog(string html)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(html)) return null;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var catalog = new NovelCatalog
                {
                    Title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim()
                         ?? doc.DocumentNode.SelectSingleNode("//div[@id='info']/h1")?.InnerText?.Trim()
                         ?? string.Empty
                };
                catalog.Title = HtmlEntity.DeEntitize(catalog.Title);

                var links = doc.DocumentNode.SelectNodes("//div[@id='list']//dd/a[@href]")
                         ?? doc.DocumentNode.SelectNodes("//div[contains(@class,'listmain')]//dd/a[@href]")
                         ?? doc.DocumentNode.SelectNodes("//div[contains(@class,'chapter')]//a[@href]")
                         ?? doc.DocumentNode.SelectNodes("//ul[contains(@class,'chapter')]//a[@href]")
                         ?? doc.DocumentNode.SelectNodes("//a[@href]");

                if (links == null) return catalog;

                foreach (var a in links)
                {
                    var href = a.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrWhiteSpace(href)) continue;

                    if (!Regex.IsMatch(href, @"/[^/]+/\d+\.html", RegexOptions.IgnoreCase)) continue;
                    if (href.Contains("/download/", StringComparison.OrdinalIgnoreCase)) continue;

                    var title = HtmlEntity.DeEntitize(a.InnerText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    if (!IsLikelyChapterTitle(title)) continue;

                    if (!Uri.TryCreate(href, UriKind.Absolute, out _))
                    {
                        if (href.StartsWith("/"))
                        {
                            href = "http://www.xheiyan.info" + href;
                        }
                        else
                        {
                            href = "http://www.xheiyan.info/" + href;
                        }
                    }

                    catalog.Chapters.Add(new ChapterLink
                    {
                        Title = title,
                        Url = href
                    });
                }

                catalog.Chapters = catalog.Chapters
                    .GroupBy(c => c.Url)
                    .Select(g => g.First())
                    .ToList();

                return catalog;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[XheiyanParser] 解析目录失败: {ex.Message}");
                return null;
            }
        }

        public string ParseChapterContent(string html)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(html)) return string.Empty;

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var node = doc.DocumentNode.SelectSingleNode("//div[@id='content']")
                        ?? doc.DocumentNode.SelectSingleNode("//div[@id='chaptercontent']")
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'content')]")
                        ?? doc.DocumentNode.SelectSingleNode("//div[contains(@class,'chapter-content')]");

                if (node == null)
                {
                    return string.Empty;
                }

                var text = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty);
                text = text.Replace("\r\n", "\n");

                var adPatterns = new[]
                {
                    "上一章", "下一章", "上一页", "下一页", "返回目录", "加入书签", "投票推荐",
                    "xheiyan.info", "黑岩阅读网", "黑岩网", "最新网址", "牢记网址",
                    "推荐阅读", "抖音小说", "手机阅读",
                    "底色", "字色", "字号", "滚屏",
                    "正在手打中", "请稍等片刻", "内容更新后", "请重新刷新页面",
                    "登录", "注册", "收藏",
                    "本章有错误"
                };
                var lines = text.Split('\n');
                var cleanedLines = new System.Collections.Generic.List<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    if (trimmed.Length <= 4) continue;
                    var skip = false;
                    foreach (var ad in adPatterns)
                    {
                        if (trimmed.Contains(ad, StringComparison.OrdinalIgnoreCase) && trimmed.Length < 80)
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (trimmed.Contains(" > ") && trimmed.Length < 150) skip = true;
                    if (trimmed.Count(c => c == '>') >= 2 && trimmed.Length < 150) skip = true;
                    if (trimmed.Length <= 3 && int.TryParse(trimmed, out _)) skip = true;
                    if (trimmed.StartsWith("》") && trimmed.Length < 200) skip = true;
                    if (!skip) cleanedLines.Add(trimmed);
                }
                text = string.Join("\n", cleanedLines);

                var markers = new[]
                {
                    "小提示：按",
                    "上一章",
                    "下一章",
                    "返回目录",
                    "加入书签",
                    "投票推荐",
                    "推荐阅读",
                    "抖音小说",
                    "xheiyan.info",
                    "黑岩阅读网",
                    "免责声明",
                    "本站小说为转载作品"
                };

                var cutIndex = -1;
                foreach (var m in markers)
                {
                    var idx = text.IndexOf(m, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        cutIndex = cutIndex < 0 ? idx : Math.Min(cutIndex, idx);
                    }
                }

                if (cutIndex >= 0)
                {
                    text = text.Substring(0, cutIndex);
                }

                text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
                return text;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[XheiyanParser] 解析章节内容失败: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool IsLikelyChapterTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;

            var t = title.Trim();
            var exclude = new[]
            {
                "新书", "上架", "上架感言", "感言", "请假", "公告", "作品相关", "作者的话", "说明", "声明", "更新说明", "通知", "番外"
            };

            if (exclude.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase))) return false;

            if (Regex.IsMatch(t, @"^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]")) return true;
            if (Regex.IsMatch(t, @"^[第]?\s*\d+\s*[\.、:：]?\s*.+")) return true;
            if (Regex.IsMatch(t, @"^(楔子|引子|序章?|前言|尾声)")) return true;

            return false;
        }
    }
}
