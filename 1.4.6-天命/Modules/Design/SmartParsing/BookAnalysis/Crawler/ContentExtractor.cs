namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public static class ContentExtractor
    {
        public static string GetChapterListScript()
        {
            return @"
(function() {
    var host = (location && location.host) ? location.host.toLowerCase() : '';
    var isShuquta = host.indexOf('shuquta.com') >= 0;
    var isXheiyan = host.indexOf('xheiyan.info') >= 0;
    var isBqgde = host.indexOf('bqgde.de') >= 0 || /bqg\d*\./.test(host);
    var vipKeywords = ['VIP', 'vip', '付费', '订阅', '锁'];
    var freeKeywords = ['免费', '公众章节'];

    if (isShuquta) {
        function isLikelyChapterTitle(text) {
            if (!text) return false;
            var t = text.replace(/\s+/g, ' ').trim();
            if (!t) return false;

            // 排除明显非正文章节
            var exclude = ['新书', '上架', '上架感言', '感言', '请假', '公告', '作品相关', '作者的话', '说明', '声明', '更新说明', '通知', '番外'];
            for (var i = 0; i < exclude.length; i++) {
                if (t.indexOf(exclude[i]) >= 0) return false;
            }

            // 常见章节标题模式
            if (/^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]/.test(t)) return true;
            if (/^[第]?\s*\d+\s*[\.、:：]?\s*.+/.test(t)) return true;
            if (/^(楔子|引子|序章?|前言|尾声)/.test(t)) return true;

            return false;
        }

        function isChapterHref(href) {
            if (!href) return false;
            // shuquta章节页形如 /book/{categoryId}/{bookId}/{chapterId}.html
            return /\/book\/\d+\/\d+\/\d+\.html/.test(href);
        }

        var scope = document;
        // 目录页常包含大量章节链接，优先选择章节链接最多的容器
        var containerSelectors = ['.listmain', '#list', '#chapterlist', '.chapterlist', '.mulu', 'dl', 'ul'];
        var best = null;
        var bestCount = 0;
        for (var ci = 0; ci < containerSelectors.length; ci++) {
            var c = document.querySelector(containerSelectors[ci]);
            if (!c) continue;
            var links = c.querySelectorAll('a[href]');
            var count = 0;
            for (var i = 0; i < links.length; i++) {
                if (isChapterHref(links[i].href)) count++;
            }
            if (count > bestCount) {
                best = c;
                bestCount = count;
            }
        }
        scope = best || document;

        var candidates = scope.querySelectorAll('a[href]');
        var results = [];
        var seen = {};

        for (var i = 0; i < candidates.length; i++) {
            var a = candidates[i];
            var href = a.href;
            if (!isChapterHref(href)) continue;

            var text = a.innerText ? a.innerText.trim() : '';
            if (!text || text.length < 1) continue;
            if (!isLikelyChapterTitle(text)) continue;

            // 如果标题只是纯章节号（如 第4章），尝试补全完整标题
            if (/^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]\s*$/.test(text)) {
                var titleAttr = a.getAttribute ? a.getAttribute('title') : '';
                if (titleAttr && titleAttr.trim().length > text.length) {
                    text = titleAttr.trim();
                } else {
                    var parent = a.parentElement;
                    if (parent) {
                        var parentText = parent.innerText ? parent.innerText.trim() : '';
                        if (parentText.length > text.length && parentText.length < 200) {
                            text = parentText.replace(/\s+/g, ' ').trim();
                        }
                    }
                }
            }

            var key = text + '|' + href;
            if (seen[key]) continue;
            seen[key] = true;

            var isVip = false;
            var originalText = a.innerText ? a.innerText.trim() : '';
            for (var v = 0; v < vipKeywords.length; v++) {
                if (originalText.indexOf(vipKeywords[v]) >= 0 || (a.className && a.className.toLowerCase().indexOf(vipKeywords[v].toLowerCase()) >= 0)) {
                    isVip = true; break;
                }
            }

            results.push({ index: 0, title: text.substring(0, 100), url: href, isVip: isVip });
        }

        for (var i = 0; i < results.length; i++) { results[i].index = i + 1; }
        return JSON.stringify(results);
    }

    if (isXheiyan) {
        function xhIsLikelyChapterTitle(text) {
            if (!text) return false;
            var t = text.replace(/\s+/g, ' ').trim();
            if (!t) return false;
            var exclude = ['新书', '上架', '感言', '请假', '公告', '作品相关', '作者的话', '说明', '声明', '通知', '下载', '作品'];
            for (var i = 0; i < exclude.length; i++) {
                if (t.indexOf(exclude[i]) >= 0) return false;
            }
            if (/^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]/.test(t)) return true;
            if (/^[第]?\s*\d+\s*[\.、:：]?\s*.+/.test(t)) return true;
            if (/^(楔子|引子|序章?|前言|尾声)/.test(t)) return true;
            return false;
        }

        function xhIsChapterHref(href) {
            if (!href) return false;
            if (href.indexOf('/download/') >= 0) return false;
            if (href.indexOf('.html') < 0) return false;
            // xheiyan: /{slug}/{数字ID}.html
            return /\/[^\/]+\/\d+\.html/.test(href);
        }

        var xhScope = document;
        var xhSelectors = ['.listmain', '#list', '#chapterlist', '.chapterlist', 'dl', 'ul'];
        var xhBest = null;
        var xhBestCount = 0;
        for (var ci = 0; ci < xhSelectors.length; ci++) {
            var c = document.querySelector(xhSelectors[ci]);
            if (!c) continue;
            var links = c.querySelectorAll('a[href]');
            var count = 0;
            for (var i = 0; i < links.length; i++) {
                if (xhIsChapterHref(links[i].href)) count++;
            }
            if (count > xhBestCount) {
                xhBest = c;
                xhBestCount = count;
            }
        }
        xhScope = xhBest || document;

        var xhCandidates = xhScope.querySelectorAll('a[href]');
        var xhResults = [];
        var xhSeen = {};

        for (var i = 0; i < xhCandidates.length; i++) {
            var a = xhCandidates[i];
            var href = a.href;
            if (!xhIsChapterHref(href)) continue;

            var text = a.innerText ? a.innerText.trim() : '';
            if (!text || text.length < 1) continue;
            if (!xhIsLikelyChapterTitle(text)) continue;

            var key = text + '|' + href;
            if (xhSeen[key]) continue;
            xhSeen[key] = true;

            var isVip = false;
            for (var v = 0; v < vipKeywords.length; v++) {
                if (text.indexOf(vipKeywords[v]) >= 0) { isVip = true; break; }
            }

            xhResults.push({ index: 0, title: text.substring(0, 100), url: href, isVip: isVip });
        }

        for (var i = 0; i < xhResults.length; i++) { xhResults[i].index = i + 1; }
        return JSON.stringify(xhResults);
    }

    if (isBqgde) {
        function bqIsLikelyChapterTitle(text) {
            if (!text) return false;
            var t = text.replace(/\s+/g, ' ').trim();
            if (!t) return false;
            var exclude = ['新书', '上架', '感言', '请假', '公告', '作品相关', '说明', '声明', '通知', '查看更多', '加入书架', '开始阅读', '更新报错'];
            for (var i = 0; i < exclude.length; i++) {
                if (t.indexOf(exclude[i]) >= 0) return false;
            }
            if (/^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]/.test(t)) return true;
            if (/^[第]?\s*\d+\s*[\.、:：]?\s*.+/.test(t)) return true;
            if (/^(楔子|引子|序章?|前言|尾声|番外)/.test(t)) return true;
            return false;
        }

        function bqIsChapterHref(href) {
            if (!href) return false;
            // bqgde: /book/{bookId}/{chapterId}.html
            if (/\/book\/\d+\/\d+\.html/.test(href)) {
                // 排除 list.html
                if (/\/list\.html/.test(href)) return false;
                return true;
            }
            return false;
        }

        var bqScope = document;
        var bqSelectors = ['.listmain', '#list', '#chapterlist', '.chapterlist', 'dl', 'ul', '.chapter-list'];
        var bqBest = null;
        var bqBestCount = 0;
        for (var ci = 0; ci < bqSelectors.length; ci++) {
            var c = document.querySelector(bqSelectors[ci]);
            if (!c) continue;
            var links = c.querySelectorAll('a[href]');
            var count = 0;
            for (var i = 0; i < links.length; i++) {
                if (bqIsChapterHref(links[i].href)) count++;
            }
            if (count > bqBestCount) {
                bqBest = c;
                bqBestCount = count;
            }
        }
        bqScope = bqBest || document;

        var bqCandidates = bqScope.querySelectorAll('a[href]');
        var bqResults = [];
        var bqSeen = {};

        for (var i = 0; i < bqCandidates.length; i++) {
            var a = bqCandidates[i];
            var href = a.href;
            if (!bqIsChapterHref(href)) continue;

            var text = a.innerText ? a.innerText.trim() : '';
            if (!text || text.length < 1) continue;
            // 清理列表编号前缀，如 '1)' '2）' '1.'
            text = text.replace(/^\d+[\)）\.]\s*/, '');
            if (!bqIsLikelyChapterTitle(text)) continue;

            var key = text + '|' + href;
            if (bqSeen[key]) continue;
            bqSeen[key] = true;

            var isVip = false;
            for (var v = 0; v < vipKeywords.length; v++) {
                if (text.indexOf(vipKeywords[v]) >= 0) { isVip = true; break; }
            }

            bqResults.push({ index: 0, title: text.substring(0, 100), url: href, isVip: isVip });
        }

        for (var i = 0; i < bqResults.length; i++) { bqResults[i].index = i + 1; }
        return JSON.stringify(bqResults);
    }

    var strictChapterPatterns = [
        /^第[一二三四五六七八九十百千万零〇\d]+[章节回卷集部篇]/,
        /^[第]?\s*\d+\s*[章节回卷集部篇]/,
        /^Chapter\s*\d+/i,
        /^卷[一二三四五六七八九十百千\d]+/,
        /^序[章幕]|^楔子|^引子|^尾声/
    ];

    var looseChapterPatterns = [
        /第[一二三四五六七八九十百千万零〇\d]+/,
        /^\d+[\.、:：]\s*.{2,}/
    ];

    var seenUrls = {};
    var strictMatches = [];
    var looseMatches = [];
    var urlMatches = [];

    // 扫描所有链接
    var links = document.querySelectorAll('a[href]');

    for (var i = 0; i < links.length; i++) {
        var link = links[i];
        var text = link.innerText ? link.innerText.trim() : '';
        var href = link.href;

        if (!text || text.length < 2 || !href || seenUrls[href]) continue;
        if (href.indexOf('javascript:') >= 0 || href === '#') continue;
        if (text.indexOf('登录') >= 0 || text.indexOf('注册') >= 0 || text.indexOf('下载') >= 0 ||
            text.indexOf('首页') >= 0 || text.indexOf('排行') >= 0 || text.indexOf('书架') >= 0 ||
            text.indexOf('目录') >= 0 || text.indexOf('返回') >= 0 || text.indexOf('更多') >= 0) continue;

        for (var j = 0; j < freeKeywords.length; j++) {
            text = text.replace(freeKeywords[j], '').trim();
        }

        var isStrictChapter = false;
        var isLooseChapter = false;
        for (var k = 0; k < strictChapterPatterns.length; k++) {
            if (strictChapterPatterns[k].test(text)) { isStrictChapter = true; break; }
        }
        if (!isStrictChapter) {
            for (var k = 0; k < looseChapterPatterns.length; k++) {
                if (looseChapterPatterns[k].test(text)) { isLooseChapter = true; break; }
            }
        }
        var isUrlChapter = href.indexOf('/chapter') >= 0 || href.indexOf('/read') >= 0 || 
                          href.indexOf('/content') >= 0 || href.indexOf('chapterId') >= 0 ||
                          /\/\d+\.html?$/.test(href) || /chapter[_-]?\d+/i.test(href);

        var originalText = link.innerText ? link.innerText.trim() : '';
        var isVip = false;
        for (var v = 0; v < vipKeywords.length; v++) {
            if (originalText.indexOf(vipKeywords[v]) >= 0 || (link.className && link.className.toLowerCase().indexOf(vipKeywords[v].toLowerCase()) >= 0)) {
                isVip = true; break;
            }
        }

        if (!isStrictChapter && !isLooseChapter && !isUrlChapter) continue;

        seenUrls[href] = true;
        var item = { index: 0, title: text.substring(0, 100), url: href, isVip: isVip };

        if (isStrictChapter) strictMatches.push(item);
        else if (isLooseChapter) looseMatches.push(item);
        else if (isUrlChapter) urlMatches.push(item);
    }

    // 确定最终结果
    var finalResults = [];

    if (strictMatches.length >= 5) finalResults = strictMatches;
    else if (strictMatches.length + looseMatches.length >= 5) finalResults = strictMatches.concat(looseMatches);
    else if (strictMatches.length >= 3 || urlMatches.length >= 3) 
        finalResults = strictMatches.concat(looseMatches).concat(urlMatches);
    else if (strictMatches.length > 0) finalResults = strictMatches;
    else return JSON.stringify([]);

    // 去重
    var uniqueResults = [];
    var seen = {};
    for (var i = 0; i < finalResults.length; i++) {
        var item = finalResults[i];
        var key = item.title + '|' + item.url;
        if (!seen[key]) { seen[key] = true; uniqueResults.push(item); }
    }

    for (var i = 0; i < uniqueResults.length; i++) { uniqueResults[i].index = i + 1; }
    return JSON.stringify(uniqueResults);
})();
";
        }

        public static string GetBqgdeFullListNavigationScript()
        {
            return @"
(function() {
    var keywords = ['查看更多章节', '更多章节', '全部章节', '完整目录', '查看目录'];
    var allEls = document.querySelectorAll('a, button, div, span, p');
    for (var i = 0; i < allEls.length; i++) {
        var el = allEls[i];
        var text = el.innerText ? el.innerText.trim() : '';
        if (!text || text.length > 30) continue;
        var matched = false;
        for (var k = 0; k < keywords.length; k++) {
            if (text.indexOf(keywords[k]) >= 0) { matched = true; break; }
        }
        if (!matched) continue;

        if (el.tagName === 'A' && el.href && el.href.indexOf('javascript:') < 0) {
            return JSON.stringify({ type: 'url', value: el.href });
        }

        var parent = el.parentElement;
        while (parent && parent.tagName !== 'A' && parent !== document.body) {
            parent = parent.parentElement;
        }
        if (parent && parent.tagName === 'A' && parent.href && parent.href.indexOf('javascript:') < 0) {
            return JSON.stringify({ type: 'url', value: parent.href });
        }

        el.click();
        return JSON.stringify({ type: 'clicked', value: '' });
    }
    return JSON.stringify({ type: 'none', value: '' });
})();
";
        }

        public static string GetNextPageScript()
        {
            return @"
(function() {
    function findNext() {
        var links = document.querySelectorAll('a[href]');
        for (var i = 0; i < links.length; i++) {
            var a = links[i];
            var t = a.innerText ? a.innerText.trim() : '';
            if (!t) continue;
            // 仅识别分页“下一页”，避免把“下一章”当成分页
            if ((t.indexOf('下一页') >= 0 || t === '下一页' || t.indexOf('下页') >= 0) && t.indexOf('下一章') < 0) {
                var href = a.href;
                if (href && href.indexOf('javascript:') < 0 && href !== '#') {
                    return href;
                }
            }
        }
        return '';
    }

    var next = findNext();
    return JSON.stringify({ nextUrl: next || '' });
})();
";
        }

        public static string GetContentScript()
        {
            return @"
(function() {
    var host = (location && location.host) ? location.host.toLowerCase() : '';
    var isShuquta = host.indexOf('shuquta.com') >= 0;
    var isXheiyan = host.indexOf('xheiyan.info') >= 0;
    var isBqgde = host.indexOf('bqgde.de') >= 0 || /bqg\d*\./.test(host);

    var selectors = [
        '#content', '#chaptercontent', '#chapter-content',
        '.content', '.chapter-content', '.article-content',
        '.read-content', '.text-content', '.novel-content',
        'article', '.article', '#article', '.main-text', '#main-text'
    ];

    var removeSelectors = ['script', 'style', 'iframe', 'noscript', '.ad', '.ads', '.share', '.comment'];

    var bestElement = null;
    var maxLength = 0;

    // shuquta 优先直接取 #content（该站章节正文通常位于此容器）
    if (isShuquta) {
        function normalizeText(t) {
            if (!t) return '';
            t = t.replace(/\r\n/g, '\n');
            t = t.replace(/\n{3,}/g, '\n\n');
            return t.trim();
        }

        function countBadHits(t, badKeywords) {
            var hit = 0;
            if (!t) return 999;
            for (var i = 0; i < badKeywords.length; i++) {
                if (t.indexOf(badKeywords[i]) >= 0) hit++;
            }
            return hit;
        }

        function scoreNode(el, badKeywords) {
            if (!el || !el.innerText) return -999999;
            var text = el.innerText.trim();
            if (text.length < 200) return -999999;

            var links = el.querySelectorAll ? el.querySelectorAll('a[href]') : [];
            var linkCount = links ? links.length : 0;
            var badHit = countBadHits(text, badKeywords);

            // 长度越大越好，链接越多越差，噪声词越多越差
            return text.length - linkCount * 80 - badHit * 800;
        }

        function tryPickBestContent(root) {
            if (!root) return null;
            var badKeywords = [
                '账号', '密码', '用户登录', '用户注册', '将本站设为首页', '收藏笔趣阁手机版',
                '玄幻小说', '武侠小说', '都市小说', '历史小说', '网游小说', '科幻小说', '女生小说',
                '全本小说', '阅读记录',
                '背景颜色', '底色', '文字颜色', '字号', '字体大小', '字色', '滚屏',
                '章节列表', '加入书签', '投推荐票', '上一章', '下一章', '上一页', '下一页', '投票推荐',
                '最新网址', 'www.shuquta.com', 'shuquta.com', '说说520', '全文字更新', '牢记网址',
                '黑色', '红色', '绿色', '蓝色', '棕色', '淡蓝', '淡灰', '灰色', '深灰', '暗灰', '明黄',
                '小号', '较小', '中号', '较大', '大号', '默认', '白色',
                '推荐阅读', '护花兵王', '白袍总管', '弄仙成魔', '飞剑问道', '诛仙', '修仙归来',
                '正在手打中', '请稍等片刻', '内容更新后', '请重新刷新页面', '即可获取最新',
                '热门'
            ];

            var best = null;
            var bestScore = -999999;

            // 优先尝试常见正文节点
            var preferred = root.querySelectorAll ? root.querySelectorAll('#chaptercontent, .read-content, .content, article') : [];
            for (var i = 0; i < preferred.length; i++) {
                var s = scoreNode(preferred[i], badKeywords);
                if (s > bestScore) { bestScore = s; best = preferred[i]; }
            }

            // 再尝试 root 的较深层 div/section
            var nodes = root.querySelectorAll ? root.querySelectorAll('div, section, article') : [];
            for (var i = 0; i < nodes.length; i++) {
                var el = nodes[i];
                var cls = (el.className || '').toLowerCase();
                if (cls.indexOf('header') >= 0 || cls.indexOf('footer') >= 0 || cls.indexOf('nav') >= 0 || cls.indexOf('menu') >= 0 || cls.indexOf('sidebar') >= 0) continue;
                var s = scoreNode(el, badKeywords);
                if (s > bestScore) { bestScore = s; best = el; }
            }

            return best;
        }

        function filterLines(t) {
            if (!t) return '';
            var badLine = [
                '账号', '密码', '用户登录', '用户注册', '将本站设为首页', '收藏笔趣阁手机版',
                '首页', '我的书架', '笔趣阁', '全本小说', '阅读记录',
                '背景颜色', '底色', '文字颜色', '字号', '字体大小', '字色', '滚屏',
                '章节列表', '加入书签', '投推荐票', '上一页', '下一页', '投票推荐',
                '最新网址', 'www.shuquta.com', 'shuquta.com', '说说520', '全文字更新', '牢记网址',
                '黑色', '红色', '绿色', '蓝色', '棕色', '淡蓝', '淡灰', '灰色', '深灰', '暗灰', '明黄',
                '小号', '较小', '中号', '较大', '大号', '默认', '白色',
                '推荐阅读', '护花兵王', '白袍总管', '弄仙成魔', '飞剑问道', '诛仙', '修仙归来',
                '正在手打中', '请稍等片刻', '内容更新后', '请重新刷新页面', '即可获取最新',
                '热门'
            ];

            var lines = t.split(/\n+/);
            var kept = [];
            for (var i = 0; i < lines.length; i++) {
                var line = (lines[i] || '').trim();
                if (!line) continue;
                // skip short lines (menu items)
                if (line.length <= 4) continue;

                var drop = false;
                for (var j = 0; j < badLine.length; j++) {
                    if (line.indexOf(badLine[j]) >= 0) { drop = true; break; }
                }
                if (drop) continue;

                // menu line like >category
                if (/^>\s*\S+/.test(line) && line.length < 30) continue;
                // skip pure digit lines (scroll 1-10)
                if (/^\d{1,2}$/.test(line)) continue;
                // skip ad lines starting with special char
                if (line.indexOf('》') === 0 && line.length < 200) continue;
                // skip breadcrumb (multiple >)
                if ((line.match(/>/g) || []).length >= 2 && line.length < 150) continue;
                // skip chapter nav line with zhengwen
                if (line.indexOf('正文') >= 0 && line.indexOf('章') >= 0 && line.length < 100) continue;

                kept.push(line);
            }

            return kept.join('\n\n').replace(/\n{3,}/g, '\n\n').trim();
        }

        var shuqutaRoot = document.querySelector('#chaptercontent') || document.querySelector('#content') || document.body;
        var shuqutaBest = tryPickBestContent(shuqutaRoot);

        if (shuqutaBest && shuqutaBest.innerText && shuqutaBest.innerText.trim().length > 200) {
            var direct = shuqutaBest.innerText;
            if (direct) {
                direct = normalizeText(direct);
            }

            // shuquta：清理导航尾巴
            var cutMarkers = [
                '温馨提示',
                '上一页',
                '上一章',
                '目录',
                '下一页',
                '下一章',
                '投票推荐',
                '加入书签',
                '手机阅读',
                '推荐阅读',
                '说说520',
                'shuquta.com'
            ];
            var cutIndex = -1;
            for (var i = 0; i < cutMarkers.length; i++) {
                var idx = direct.indexOf(cutMarkers[i]);
                // 仅在标记出现在正文靠后时才截断，避免导航词出现在正文前导致清空
                if (idx >= 0 && idx > 300 && idx > direct.length * 0.4) {
                    cutIndex = (cutIndex < 0) ? idx : Math.min(cutIndex, idx);
                }
            }
            if (cutIndex >= 0) {
                direct = direct.substring(0, cutIndex).trim();
            }

            direct = filterLines(direct);

            // 若结果太短或仍包含大量菜单/设置文本，则放弃该容器，走通用算法
            var badKeywords = ['账号', '密码', '用户登录', '用户注册', '背景颜色', '文字颜色', '字号', '字体大小'];
            var badHit = 0;
            for (var bi = 0; bi < badKeywords.length; bi++) {
                if (direct.indexOf(badKeywords[bi]) >= 0) badHit++;
            }
            if (direct && direct.length > 300 && badHit <= 1) {
                // shuquta: 多重策略提取章节标题
                var shuqutaTitle = '';

                // 策略1: 从h1提取
                var allH1 = document.querySelectorAll('h1');
                for (var hi = 0; hi < allH1.length; hi++) {
                    var h1t = allH1[hi].innerText ? allH1[hi].innerText.trim() : '';
                    if (h1t && /^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]/.test(h1t)) {
                        shuqutaTitle = h1t;
                        break;
                    }
                }

                // 策略2: 从document.title解析（格式: 书名 - 第X章 标题 - 说说520）
                if (!shuqutaTitle) {
                    var dt = document.title || '';
                    var m = dt.match(/第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷][^-]*/);
                    if (m) shuqutaTitle = m[0].trim();
                }

                console.log('[ContentExtractor] shuquta title=' + shuqutaTitle + ' h1Count=' + allH1.length + ' docTitle=' + (document.title || ''));
                return JSON.stringify({ success: true, title: shuqutaTitle, content: direct, wordCount: direct.length });
            }
        }
    }

    // xheiyan.info 专用正文提取
    if (isXheiyan) {
        var xhContent = '';
        var xhEl = document.querySelector('#content') || document.querySelector('#chaptercontent') || document.querySelector('.content');
        if (xhEl && xhEl.innerText) {
            xhContent = xhEl.innerText.trim();
        }

        // xheiyan 正文可能由JS动态加载，尝试从最大文本块获取
        if (!xhContent || xhContent.length < 200) {
            var xhDivs = document.querySelectorAll('div');
            var xhMaxLen = 0;
            for (var di = 0; di < xhDivs.length; di++) {
                var d = xhDivs[di];
                var cls = (d.className || '').toLowerCase();
                if (cls.indexOf('header') >= 0 || cls.indexOf('footer') >= 0 || cls.indexOf('nav') >= 0 || cls.indexOf('menu') >= 0 || cls.indexOf('sidebar') >= 0) continue;
                var dt = d.innerText ? d.innerText.trim() : '';
                var links = d.querySelectorAll ? d.querySelectorAll('a[href]') : [];
                if (dt.length > xhMaxLen && dt.length > 300 && links.length < 20) {
                    xhMaxLen = dt.length;
                    xhContent = dt;
                }
            }
        }

        if (xhContent && xhContent.length > 200) {
            // 清理导航尾巴
            var xhCut = ['小提示：按', '上一章', '下一章', '返回目录', '加入书签', '投票推荐', '推荐阅读', '抖音小说', 'xheiyan.info', '黑岩阅读网', '本站小说为转载', '免责声明', '收藏本站'];
            var xhCutIdx = -1;
            for (var ci = 0; ci < xhCut.length; ci++) {
                var idx = xhContent.indexOf(xhCut[ci]);
                if (idx >= 0 && idx > 200 && idx > xhContent.length * 0.3) {
                    xhCutIdx = (xhCutIdx < 0) ? idx : Math.min(xhCutIdx, idx);
                }
            }
            if (xhCutIdx >= 0) xhContent = xhContent.substring(0, xhCutIdx).trim();

            // 逐行过滤噪声
            var xhBadLine = ['登录', '注册', '手机版', '收藏', '抖音小说', '推荐阅读', '本章有错误', 'xheiyan', '黑岩阅读网', '黑岩网'];
            var xhLines = xhContent.split(/\n+/);
            var xhKept = [];
            for (var li = 0; li < xhLines.length; li++) {
                var ln = (xhLines[li] || '').trim();
                if (!ln || ln.length <= 4) continue;
                var drop = false;
                for (var bi = 0; bi < xhBadLine.length; bi++) {
                    if (ln.indexOf(xhBadLine[bi]) >= 0 && ln.length < 80) { drop = true; break; }
                }
                if ((ln.match(/>/g) || []).length >= 2 && ln.length < 150) drop = true;
                if (!drop) xhKept.push(ln);
            }
            xhContent = xhKept.join('\n\n').replace(/\n{3,}/g, '\n\n').trim();

            // 提取标题：xheiyan h1 格式 书名|第X章 标题，取|后面部分
            var xhTitle = '';
            var xhH1 = document.querySelector('h1');
            if (xhH1 && xhH1.innerText) {
                var h1Raw = xhH1.innerText.trim();
                var pipeIdx = h1Raw.indexOf('|');
                if (pipeIdx >= 0) {
                    xhTitle = h1Raw.substring(pipeIdx + 1).trim();
                } else if (/^第/.test(h1Raw)) {
                    xhTitle = h1Raw;
                }
            }
            // 回退：从document.title解析（格式: 书名_第X章 标题_黑岩阅读网）
            if (!xhTitle) {
                var xhDt = document.title || '';
                var xhM = xhDt.match(/第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷][^_]*/);
                if (xhM) xhTitle = xhM[0].trim();
            }

            if (xhContent.length > 200) {
                return JSON.stringify({ success: true, title: xhTitle, content: xhContent, wordCount: xhContent.length });
            }
        }
    }

    // bqgde.de 专用正文提取
    if (isBqgde) {
        var bqContent = '';
        var bqEl = document.querySelector('#chaptercontent') || document.querySelector('#content') || document.querySelector('.content');
        if (bqEl && bqEl.innerText) {
            bqContent = bqEl.innerText.trim();
        }

        // bqgde 页面由JS动态渲染，尝试最大文本块
        if (!bqContent || bqContent.length < 200) {
            var bqDivs = document.querySelectorAll('div');
            var bqMaxLen = 0;
            for (var di = 0; di < bqDivs.length; di++) {
                var d = bqDivs[di];
                var cls = (d.className || '').toLowerCase();
                if (cls.indexOf('header') >= 0 || cls.indexOf('footer') >= 0 || cls.indexOf('nav') >= 0 || cls.indexOf('menu') >= 0 || cls.indexOf('sidebar') >= 0) continue;
                var dt = d.innerText ? d.innerText.trim() : '';
                var links = d.querySelectorAll ? d.querySelectorAll('a[href]') : [];
                if (dt.length > bqMaxLen && dt.length > 300 && links.length < 20) {
                    bqMaxLen = dt.length;
                    bqContent = dt;
                }
            }
        }

        if (bqContent && bqContent.length > 200) {
            // 清理导航尾巴
            var bqCut = ['上一章', '下一章', '返回目录', '加入书签', '加入书架', '投票推荐', '推荐阅读', '笔趣阁', 'bqgde.de', '免责声明', '我的书架', '阅读记录', '首页', '请收藏', 'bqg78', 'bqg', '.com'];
            var bqCutIdx = -1;
            for (var ci = 0; ci < bqCut.length; ci++) {
                var idx = bqContent.indexOf(bqCut[ci]);
                if (idx >= 0 && idx > 200 && idx > bqContent.length * 0.3) {
                    bqCutIdx = (bqCutIdx < 0) ? idx : Math.min(bqCutIdx, idx);
                }
            }
            if (bqCutIdx >= 0) bqContent = bqContent.substring(0, bqCutIdx).trim();

            // 逐行过滤噪声
            var bqBadLine = ['登录', '注册', '手机版', '收藏', '笔趣阁', 'bqgde', 'bqg78', 'bqg', '.com', '最新网址', '推荐阅读', '手机阅读', '底色', '字色', '字号', '滚屏', '加入书架', '阅读记录', '请收藏'];
            var bqLines = bqContent.split(/\n+/);
            var bqKept = [];
            for (var li = 0; li < bqLines.length; li++) {
                var ln = (bqLines[li] || '').trim();
                if (!ln || ln.length <= 4) continue;
                var drop = false;
                for (var bi = 0; bi < bqBadLine.length; bi++) {
                    if (ln.indexOf(bqBadLine[bi]) >= 0 && ln.length < 80) { drop = true; break; }
                }
                if ((ln.match(/>/g) || []).length >= 2 && ln.length < 150) drop = true;
                if (ln.length <= 3 && /^\d+$/.test(ln)) drop = true;
                if (!drop) bqKept.push(ln);
            }
            bqContent = bqKept.join('\n\n').replace(/\n{3,}/g, '\n\n').trim();

            // 提取标题
            var bqTitle = '';
            var bqH1 = document.querySelector('h1');
            if (bqH1 && bqH1.innerText) {
                var h1Raw = bqH1.innerText.trim();
                if (/^第/.test(h1Raw) || /^(楔子|引子|序章|番外)/.test(h1Raw)) {
                    bqTitle = h1Raw;
                }
            }
            // 回退：从 document.title 解析（格式: 第X章 标题_书名_笔趣阁）
            if (!bqTitle) {
                var bqDt = document.title || '';
                var bqM = bqDt.match(/第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷][^_]*/);
                if (bqM) bqTitle = bqM[0].trim();
                if (!bqTitle) {
                    var bqM2 = bqDt.match(/^(番外[^_]*|楔子|引子|序章[^_]*|尾声[^_]*)/);
                    if (bqM2) bqTitle = bqM2[0].trim();
                }
            }

            if (bqContent.length > 200) {
                return JSON.stringify({ success: true, title: bqTitle, content: bqContent, wordCount: bqContent.length });
            }
        }
    }

    if (!bestElement) {
        for (var i = 0; i < selectors.length; i++) {
            var el = document.querySelector(selectors[i]);
            if (el) {
                var text = el.innerText ? el.innerText.trim() : '';
                if (text.length > maxLength && text.length > 500) {
                    maxLength = text.length;
                    bestElement = el;
                }
            }
        }
    }

    if (!bestElement || maxLength < 1000) {
        var containers = document.querySelectorAll('div, article, section');
        for (var i = 0; i < containers.length; i++) {
            var el = containers[i];
            var className = (el.className || '').toLowerCase();
            if (className.indexOf('header') >= 0 || className.indexOf('footer') >= 0 ||
                className.indexOf('nav') >= 0 || className.indexOf('sidebar') >= 0 ||
                className.indexOf('menu') >= 0 || className.indexOf('comment') >= 0) continue;

            var text = el.innerText ? el.innerText.trim() : '';
            if (text.length > maxLength && text.length > 500) {
                var paragraphs = el.querySelectorAll('p');
                if (paragraphs.length >= 3 || text.length > 2000) {
                    maxLength = text.length;
                    bestElement = el;
                }
            }
        }
    }

    if (!bestElement) return JSON.stringify({ success: false, error: '未找到正文内容' });

    var clone = bestElement.cloneNode(true);
    for (var i = 0; i < removeSelectors.length; i++) {
        var toRemove = clone.querySelectorAll(removeSelectors[i]);
        for (var j = 0; j < toRemove.length; j++) {
            toRemove[j].parentNode.removeChild(toRemove[j]);
        }
    }

    var content = '';
    var walker = document.createTreeWalker(clone, NodeFilter.SHOW_TEXT, null, false);
    var prevNode = null;

    while (walker.nextNode()) {
        var text = walker.currentNode.textContent ? walker.currentNode.textContent.trim() : '';
        if (text) {
            var parent = walker.currentNode.parentElement;
            if (parent && (parent.tagName === 'P' || parent.tagName === 'BR' || 
                (parent.tagName === 'DIV' && prevNode))) content += '\n\n';
            content += text;
            prevNode = walker.currentNode;
        }
    }

    content = content.replace(/\n{3,}/g, '\n\n').trim();

    // shuquta：清理导航尾巴
    if (isShuquta) {
        var cutMarkers = [
            '温馨提示',
            '上一页',
            '上一章',
            '目录',
            '下一页',
            '下一章',
            '投票推荐',
            '加入书签',
            '手机阅读',
            '推荐阅读',
            '说说520',
            'shuquta.com'
        ];
        var cutIndex = -1;
        for (var i = 0; i < cutMarkers.length; i++) {
            var idx = content.indexOf(cutMarkers[i]);
            if (idx >= 0 && idx > 300 && idx > content.length * 0.4) {
                cutIndex = (cutIndex < 0) ? idx : Math.min(cutIndex, idx);
            }
        }
        if (cutIndex >= 0) {
            content = content.substring(0, cutIndex).trim();
        }
    }

    var title = '';
    var titleEl = document.querySelector('h1, .chapter-title, .title, #title');
    if (titleEl) title = titleEl.innerText ? titleEl.innerText.trim() : '';

    // shuquta回退: 从document.title解析章节标题
    if (isShuquta && (!title || !/^第/.test(title))) {
        var allH1g = document.querySelectorAll('h1');
        for (var hgi = 0; hgi < allH1g.length; hgi++) {
            var ht = allH1g[hgi].innerText ? allH1g[hgi].innerText.trim() : '';
            if (ht && /^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]/.test(ht)) {
                title = ht; break;
            }
        }
        if (!title || !/^第/.test(title)) {
            var dtg = document.title || '';
            var mg = dtg.match(/第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷][^-]*/);
            if (mg) title = mg[0].trim();
        }
    }

    return JSON.stringify({ success: true, title: title, content: content, wordCount: content.length });
})();
";
        }

        public static string GetBookInfoScript()
        {
            return @"
(function() {
    var host = (location && location.host) ? location.host.toLowerCase() : '';
    var isShuquta = host.indexOf('shuquta.com') >= 0;
    var isBqgde = host.indexOf('bqgde.de') >= 0 || /bqg\d*\./.test(host);
    var title = '', author = '', genre = '', tags = '';

    function pickFirstText(selectors) {
        for (var i = 0; i < selectors.length; i++) {
            try {
                var el = document.querySelector(selectors[i]);
                if (el && el.innerText && el.innerText.trim()) return el.innerText.trim();
            } catch(e) { continue; }
        }
        return '';
    }

    function pickMeta(names, attr) {
        attr = attr || 'content';
        for (var i = 0; i < names.length; i++) {
            try {
                var sel = 'meta[' + names[i] + ']';
                var el = document.querySelector(sel);
                if (el) {
                    var v = el.getAttribute(attr);
                    if (v && v.trim()) return v.trim();
                }
            } catch(e) { continue; }
        }
        return '';
    }

    function uniq(arr) {
        var seen = {};
        var out = [];
        for (var i = 0; i < arr.length; i++) {
            var t = (arr[i] || '').trim();
            if (!t) continue;
            if (seen[t]) continue;
            seen[t] = true;
            out.push(t);
        }
        return out;
    }

    // 书名
    title = pickFirstText(['h1', '.book-title', '.novel-title', '#title', '.title', '.book-name']);
    if (!title) {
        // og:title / title 标签
        title = pickMeta(['property=""og:title""'], 'content') || (document.title || '').trim();
        if (title) {
            title = title.replace(/最新章节.*$/g, '').replace(/全文阅读.*$/g, '').replace(/_.*$/g, '').trim();
        }
    }

    // bqgde: 页面标题格式 书名(作者)最新章节_书名全文免费阅读_笔趣阁
    if (isBqgde && !author) {
        var bqPageTitle = (document.title || '').trim();
        var bqAuthorM = bqPageTitle.match(/[\(（]([^)）]+)[\)）]/);
        if (bqAuthorM) author = bqAuthorM[1].trim();
    }
    // bqgde: 清理标题中的 (作者) 后缀
    if (isBqgde && title) {
        title = title.replace(/[\(（][^)）]+[\)）]\s*$/, '').trim();
    }

    // 作者
    if (!author) {
        author = pickFirstText(['.author', '.writer', '.book-author', '.info .author', '.book-info .author']);
    }
    if (author) author = author.replace(/作者[：:]/g, '').trim();

    if (!author) {
        // OpenGraph/结构化字段（很多站点只在 meta 提供）
        author = pickMeta(['property=""og:novel:author""'], 'content');
    }

    if (!author) {
        // 少数站点用 name 而非 property
        author = pickMeta(['name=""og:novel:author""'], 'content');
    }

    if (!author) {
        author = pickMeta(['property=""og:author""'], 'content');
    }

    if (!author) {
        author = pickMeta(['name=""author""'], 'content');
    }

    if (!author) {
        var bodyText = document.body.innerText || '';
        // match author pattern with spaces
        var authorMatch = bodyText.match(/作\s*者\s*[：:]\s*([^\n\r|｜]+)/);
        if (!authorMatch) authorMatch = bodyText.match(/作者\s*[：:]\s*([^\n\r|｜]+)/);
        if (authorMatch) author = authorMatch[1].trim();
    }

    if (!author) {
        // 从标题附近文本提取
        var nearTitle = pickFirstText(['.info', '.book-info', '.con_top', '.breadcrumb', '.crumbs']);
        if (nearTitle) {
            var m = nearTitle.match(/作者[：:]\s*([^\n\r]+)/);
            if (m) author = m[1].trim();
        }
    }

    if (!author) {
        // biquge style: #info p contains author
        var ps = document.querySelectorAll('#info p, .info p, .book-info p');
        for (var pi = 0; pi < ps.length; pi++) {
            var pt = ps[pi].innerText ? ps[pi].innerText.trim() : '';
            if (!pt) continue;
            var m = pt.match(/作\s*者\s*[：:]\s*([^\n\r|｜]+)/) || pt.match(/作者\s*[：:]\s*([^\n\r|｜]+)/);
            if (m) { author = m[1].trim(); break; }
        }
    }

    if (author) {
        author = author.replace(/\s+/g, ' ').trim();
        author = author.replace(/\|.*$/g, '').trim();
        author = author.replace(/\s*作品.*$/g, '').trim();
    }

    // 类型/题材
    var genreSelectors = ['.genre', '.category', '.type', '.book-type', '.book-category', '.info .type', '.book-info .type'];
    for (var i = 0; i < genreSelectors.length; i++) {
        try {
            var el = document.querySelector(genreSelectors[i]);
            if (el && el.innerText && el.innerText.trim()) {
                var text = el.innerText.trim().replace(/类型[：:]/g, '').replace(/分类[：:]/g, '').replace(/题材[：:]/g, '').trim();
                if (text.length < 30 && text.indexOf('目录') < 0 && text.indexOf('章节') < 0) {
                    genre = text; break;
                }
            }
        } catch(e) { continue; }
    }

    if (!genre) {
        // OpenGraph/结构化字段
        genre = pickMeta(['property=""og:novel:category""'], 'content');
    }

    if (!genre) {
        genre = pickMeta(['name=""og:novel:category""'], 'content');
    }

    if (!genre) {
        genre = pickMeta(['property=""og:novel:genre""'], 'content');
    }

    if (!genre) {
        genre = pickMeta(['name=""og:novel:genre""'], 'content');
    }

    if (!genre) {
        var bodyText = document.body.innerText || '';
        // match patterns like: lei bie / fen lei / lei xing / ti cai
        var match = bodyText.match(/(?:类\s*别|分\s*类|类\s*型|题\s*材)[：:]\s*([^\n\r,，|｜]+)/);
        if (!match) match = bodyText.match(/(?:类型|分类|类别|题材)[：:]\s*([^\n\r,，|｜]+)/);
        if (match) genre = match[1].trim();
    }

    if (!genre && isShuquta) {
        // shuquta: breadcrumb contains category (e.g. shuoshuo520 > lishijunshi > bookname)
        var breadcrumb = pickFirstText(['.con_top', '.bread', '.breadcrumb', '.crumbs', '#breadcrumb']);
        if (breadcrumb) {
            var parts = breadcrumb.split(/>|\//).map(function(s){ return (s||'').trim(); }).filter(function(s){ return !!s; });
            // pick novel category
            for (var bi = 0; bi < parts.length; bi++) {
                if (parts[bi].indexOf('小说') >= 0 && parts[bi].length <= 20) { genre = parts[bi]; break; }
            }
        }
    }

    if (!genre) {
        // 面包屑链接：优先取包含""小说""的链接，否则取第2个链接作为分类
        var bcLinks = document.querySelectorAll('.con_top a, .bread a, .breadcrumb a, .crumbs a, #breadcrumb a');
        if (bcLinks && bcLinks.length >= 2) {
            var picked = '';
            for (var i = 0; i < bcLinks.length; i++) {
                var t = bcLinks[i].innerText ? bcLinks[i].innerText.trim() : '';
                if (t && t.indexOf('小说') >= 0 && t.length <= 20) { picked = t; break; }
            }
            if (!picked) {
                var t2 = bcLinks[1].innerText ? bcLinks[1].innerText.trim() : '';
                if (t2 && t2.length <= 30) picked = t2;
            }
            if (picked) genre = picked;
        }
    }

    // 标签/关键词
    var tagElements = [];
    var excludeWords = ['目录', '章节', '简介', '作者', '类型', '字数', '更新', '收藏', '推荐', '点击', '阅读', '登录', '注册'];

    // meta keywords
    var mk = pickMeta(['name=""keywords""'], 'content');
    if (mk) {
        mk.split(/[，,\s]+/).forEach(function(x){ if (x && x.trim()) tagElements.push(x.trim()); });
    }

    var tagSelectors = ['.tags a', '.tag-list a', '.keywords a', '.book-tags a', '.tags', '.tag-list', '.keywords', '.book-tags'];
    for (var i = 0; i < tagSelectors.length; i++) {
        try {
            var els = document.querySelectorAll(tagSelectors[i]);
            for (var j = 0; j < els.length; j++) {
                var text = els[j].innerText ? els[j].innerText.trim() : '';
                if (text && text.length > 1 && text.length < 20) {
                    var excluded = false;
                    for (var k = 0; k < excludeWords.length; k++) {
                        if (text.indexOf(excludeWords[k]) >= 0) { excluded = true; break; }
                    }
                    if (!excluded) tagElements.push(text);
                }
            }
            if (tagElements.length >= 10) break;
        } catch(e) { continue; }
    }

    tagElements = uniq(tagElements);
    // 去掉与书名/作者/类型重复的词
    tagElements = tagElements.filter(function(x){
        if (title && x === title) return false;
        if (author && x === author) return false;
        if (genre && x === genre) return false;
        return true;
    });
    tags = tagElements.slice(0, 10).join('、');

    return JSON.stringify({
        title: title.substring(0, 200),
        author: author.substring(0, 100),
        genre: genre.substring(0, 100),
        tags: tags.substring(0, 200)
    });
})();
";
        }
    }
}
