namespace TM.Modules.Design.SmartParsing.BookAnalysis.Crawler
{
    public static class ContentExtractor
    {
        public static string GetChapterListScript()
        {
            return @"
(function() {
    var vipKeywords = ['VIP', 'vip', '付费', '订阅', '锁'];
    var freeKeywords = ['免费', '公众章节'];

    var UI_NOISE_TEXT = ['登录','注册','下载','首页','排行','书架','返回','更多','收藏','书签',
                         '举报','投票','打赏','分享','评论','APP','客户端','导航','设置',
                         '新书','上架','感言','请假','公告','作品相关','作者的话','声明','通知'];
    var UI_NOISE_HREF_KW = ['javascript:','mailto:','tel:','login','register','download',
                             'search','about','contact','help','faq','privacy','terms'];
    var CHAPTER_HREF_KW = ['/chapter','/read/','/content/','/chap','/cha/','/view/',
                            'chapterId','readChapter'];
    var CHAPTER_TITLE_STRICT = [
        /^第[一二三四五六七八九十百千万零〇\d]+[章节回卷集部篇]/,
        /^[第]?\s*\d+\s*[章节回卷集部篇]/,
        /^Chapter\s*\d+/i,
        /^卷[一二三四五六七八九十百千\d]+/,
        /^序[章幕]$|^楔子$|^引子$|^尾声$/
    ];
    var CHAPTER_TITLE_LOOSE = [
        /第[一二三四五六七八九十百千万零〇\d]+/,
        /^\d+[.、:：]\s*.{2}/
    ];

    function getPathPrefix(href) {
        try {
            var u = new URL(href);
            if (u.host !== location.host) return null;
            var p = u.pathname.replace(/\/+$/, '');
            var slash = p.lastIndexOf('/');
            return slash > 0 ? p.slice(0, slash) : p;
        } catch(e) { return null; }
    }

    function isUiNoise(text, href) {
        for (var i = 0; i < UI_NOISE_TEXT.length; i++) {
            if (text.indexOf(UI_NOISE_TEXT[i]) >= 0) return true;
        }
        var h = href.toLowerCase();
        for (var i = 0; i < UI_NOISE_HREF_KW.length; i++) {
            if (h.indexOf(UI_NOISE_HREF_KW[i]) >= 0) return true;
        }
        return false;
    }

    function isChapterHrefKw(href) {
        var h = href.toLowerCase();
        for (var i = 0; i < CHAPTER_HREF_KW.length; i++) {
            if (h.indexOf(CHAPTER_HREF_KW[i]) >= 0) return true;
        }
        return /\/\d+(\.html?)?\/?$/.test(href);
    }

    function titleScore(text) {
        for (var i = 0; i < CHAPTER_TITLE_STRICT.length; i++) {
            if (CHAPTER_TITLE_STRICT[i].test(text)) return 2;
        }
        for (var i = 0; i < CHAPTER_TITLE_LOOSE.length; i++) {
            if (CHAPTER_TITLE_LOOSE[i].test(text)) return 1;
        }
        return 0;
    }

    function isVipLink(text, el) {
        for (var v = 0; v < vipKeywords.length; v++) {
            if (text.indexOf(vipKeywords[v]) >= 0) return true;
            if (el.className && el.className.toLowerCase().indexOf(vipKeywords[v].toLowerCase()) >= 0) return true;
        }
        return false;
    }

    var allLinks = document.querySelectorAll('a[href]');
    var candidates = [];
    var seenHref = {};
    for (var i = 0; i < allLinks.length; i++) {
        var a = allLinks[i];
        var href = a.href;
        var text = (a.innerText || '').replace(/\s+/g, ' ').trim();
        if (!href || !text || text.length < 1) continue;
        if (seenHref[href]) continue;
        if (isUiNoise(text, href)) continue;
        seenHref[href] = true;
        var prefix = getPathPrefix(href);
        if (prefix === null) continue;
        var isKw = isChapterHrefKw(href);
        var ts = titleScore(text);
        candidates.push({ href: href, text: text, el: a, prefix: prefix, isKw: isKw, ts: ts });
    }

    var prefixCount = {};
    for (var i = 0; i < candidates.length; i++) {
        var pf = candidates[i].prefix;
        prefixCount[pf] = (prefixCount[pf] || 0) + 1;
    }
    var bestPrefix = null;
    var bestPfCount = 2;
    for (var pf in prefixCount) {
        if (prefixCount[pf] > bestPfCount) {
            bestPfCount = prefixCount[pf];
            bestPrefix = pf;
        }
    }

    var clusterSet = [], kwSet = [], titleSet = [];
    for (var i = 0; i < candidates.length; i++) {
        var c = candidates[i];
        if (bestPrefix && c.prefix === bestPrefix) clusterSet.push(c);
        else if (c.isKw) kwSet.push(c);
        else if (c.ts >= 2) titleSet.push(c);
    }

    var pool;
    if (clusterSet.length >= 3) {
        pool = clusterSet;
        var clusterHit = 0;
        for (var i = 0; i < pool.length; i++) {
            if (pool[i].isKw || pool[i].ts > 0) clusterHit++;
        }
        if (pool.length > 10 && clusterHit / pool.length < 0.05) {
            var filtered = [];
            for (var i = 0; i < pool.length; i++) {
                if (pool[i].isKw || pool[i].ts > 0) filtered.push(pool[i]);
            }
            if (filtered.length >= 3) pool = filtered;
        }
    } else if (kwSet.length >= 3) {
        pool = clusterSet.concat(kwSet);
    } else if (titleSet.length >= 3) {
        pool = titleSet;
    } else {
        pool = clusterSet.concat(kwSet).concat(titleSet);
        if (pool.length < 3) return JSON.stringify([]);
    }

    if (pool.length > 0 && bestPrefix) {
        var containerScores = {}, containerEls = {};
        for (var i = 0; i < pool.length; i++) {
            var el = pool[i].el;
            var p = el.parentElement;
            var depth = 0;
            while (p && p !== document.body && depth < 5) {
                var cid = p.tagName + (p.id ? '#' + p.id : '') + (p.className ? '.' + (p.className + '').split(' ')[0] : '');
                containerScores[cid] = (containerScores[cid] || 0) + 1;
                containerEls[cid] = p;
                p = p.parentElement; depth++;
            }
        }
        var bestCid = null, bestCs = 0;
        for (var cid in containerScores) {
            if (containerScores[cid] > bestCs) { bestCs = containerScores[cid]; bestCid = cid; }
        }
        if (bestCid && bestCs >= pool.length * 0.6) {
            var container = containerEls[bestCid];
            var containerLinks = container.querySelectorAll('a[href]');
            var extraSeen = {};
            for (var i = 0; i < pool.length; i++) extraSeen[pool[i].href] = true;
            for (var i = 0; i < containerLinks.length; i++) {
                var a = containerLinks[i];
                var href = a.href;
                if (!href || extraSeen[href]) continue;
                var prefix = getPathPrefix(href);
                if (prefix !== bestPrefix) continue;
                var text = (a.innerText || '').replace(/\s+/g, ' ').trim();
                if (!text) continue;
                extraSeen[href] = true;
                pool.push({ href: href, text: text, el: a, prefix: prefix, isKw: isChapterHrefKw(href), ts: titleScore(text) });
            }
        }
    }

    function extractTrailingNum(href) {
        var m = href.match(/\/(\d+)(?:\.html?)?\/?(\?[^#]*)?(#.*)?$/);
        return m ? parseInt(m[1], 10) : null;
    }

    var numberedItems = [];
    var unnumberedItems = [];
    for (var i = 0; i < pool.length; i++) {
        var n = extractTrailingNum(pool[i].href);
        if (n !== null) numberedItems.push({ item: pool[i], num: n, domIdx: i });
        else unnumberedItems.push({ item: pool[i], domIdx: i });
    }

    if (numberedItems.length >= 3 && numberedItems.length >= pool.length * 0.6) {
        numberedItems.sort(function(a, b) { return a.num - b.num; });
        var isStrictlyIncreasing = true;
        for (var i = 1; i < numberedItems.length; i++) {
            if (numberedItems[i].num <= numberedItems[i - 1].num) {
                isStrictlyIncreasing = false;
                break;
            }
        }
        if (isStrictlyIncreasing) {
            var merged = [];
            var ui = 0;
            for (var i = 0; i < numberedItems.length; i++) {
                while (ui < unnumberedItems.length && unnumberedItems[ui].domIdx < numberedItems[i].domIdx) {
                    merged.push(unnumberedItems[ui].item);
                    ui++;
                }
                merged.push(numberedItems[i].item);
            }
            while (ui < unnumberedItems.length) {
                merged.push(unnumberedItems[ui].item);
                ui++;
            }
            pool = merged;
        }
    }

    var finalSeen = {};
    var finalResults = [];
    for (var i = 0; i < pool.length; i++) {
        var c = pool[i];
        var key = c.href;
        if (finalSeen[key]) continue;
        finalSeen[key] = true;
        var cleanText = c.text.replace(/^\d+[\)）\.]\s*/, '');
        for (var j = 0; j < freeKeywords.length; j++) {
            cleanText = cleanText.replace(freeKeywords[j], '').trim();
        }
        finalResults.push({
            index: 0,
            title: cleanText.substring(0, 100),
            url: c.href,
            isVip: isVipLink(c.text, c.el)
        });
    }

    for (var i = 0; i < finalResults.length; i++) { finalResults[i].index = i + 1; }
    return JSON.stringify(finalResults);
})();
";
        }

        public static string GetExpandCatalogScript()
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
    var NEXT_PAGE_TEXT = ['下一页', '下页', 'next page', 'nextpage', 'next'];
    var NEXT_PAGE_SYMBOL = ['»', '›', '>'];
    var EXCLUDE_TEXT = ['下一章', '下一节', 'next chapter'];

    function isNextPage(t) {
        var lower = t.toLowerCase();
        for (var e = 0; e < EXCLUDE_TEXT.length; e++) {
            if (lower.indexOf(EXCLUDE_TEXT[e]) >= 0) return false;
        }
        for (var n = 0; n < NEXT_PAGE_TEXT.length; n++) {
            if (lower.indexOf(NEXT_PAGE_TEXT[n]) >= 0) return true;
        }
        for (var s = 0; s < NEXT_PAGE_SYMBOL.length; s++) {
            if (t === NEXT_PAGE_SYMBOL[s]) return true;
        }
        return false;
    }

    function findNext() {
        var links = document.querySelectorAll('a[href]');
        for (var i = 0; i < links.length; i++) {
            var a = links[i];
            var t = a.innerText ? a.innerText.trim() : '';
            if (!t) continue;
            if (isNextPage(t)) {
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

        public static string GetAdBlockScript()
        {
            return @"(function(){
    var selectors = [
        'iframe',
        'ins.adsbygoogle',
        '[class~=ad],[class~=ads],[class~=advertisement],[class~=AD]',
        '[id=ad],[id=ads],[id^=ad-],[id^=ad_],[id$=-ad],[id$=_ad]',
        '[id*=banner],[class*=banner]',
        '[id*=pop],[class*=pop]',
        '.recommend,.share,.comment-box,.copyright-info,.sidebar,.aside'
    ];
    selectors.forEach(function(sel){
        try{
            document.querySelectorAll(sel).forEach(function(el){
                var txt = el.innerText ? el.innerText.trim() : '';
                if(txt.length < 500) el.style.display='none';
            });
        }catch(e){}
    });
})();";
        }

        public static string GetContentScript()
        {
            return @"(function() {
    var CUT_MARKERS = [
        '温馨提示','上一页','上一章','目录','下一页','下一章',
        '投票推荐','加入书签','手机阅读','推荐阅读','收藏本站',
        '请记住本书','本站小说','免责声明','章节有错','报告错误',
        '返回书页','章节目录','开始阅读','手机版阅读','笔趣阁'
    ];
    var AD_LINE_KW = [
        '最新网址','最新章节','记住地址','手机版阅读网址','天才一秒',
        'shuquta.com','xheiyan.info','bqgde.de','17k.com'
    ];
    var BAD_LINE_KW = [
        '登录','注册','手机版','下载APP','扫码','二维码',
        '广告','advertisement','google','baidu','taobao',
        '点击这里','请点击','关注微信','公众号'
    ];
    var BAD_CLASS = ['header','footer','nav','menu','sidebar','ad','ads','share','comment','copyright','recommend'];

    function hasAnyKw(text, arr) {
        for (var i = 0; i < arr.length; i++) {
            if (text.indexOf(arr[i]) >= 0) return true;
        }
        return false;
    }

    function scoreNode(el) {
        if (!el || !el.innerText) return -999999;
        var text = el.innerText.trim();
        if (text.length < 200) return -999999;
        var cls = (el.className || '').toLowerCase() + ' ' + (el.id || '').toLowerCase();
        for (var i = 0; i < BAD_CLASS.length; i++) {
            if (cls.indexOf(BAD_CLASS[i]) >= 0) return -999999;
        }
        var links = el.querySelectorAll ? el.querySelectorAll('a[href]') : [];
        var linkTextLen = 0;
        for (var j = 0; j < links.length; j++) {
            linkTextLen += (links[j].innerText || '').length;
        }
        var linkDensity = text.length > 0 ? linkTextLen / text.length : 1;
        if (linkDensity > 0.3) return -999999;
        var pTags = el.querySelectorAll ? el.querySelectorAll('p') : [];
        var substantialP = 0;
        for (var pi = 0; pi < pTags.length; pi++) {
            if ((pTags[pi].innerText || '').trim().length > 10) substantialP++;
        }
        return text.length * (1 - linkDensity) + substantialP * 80;
    }

    function findBestContent(root) {
        var best = null;
        var bestScore = 0;
        var candidates = root.querySelectorAll('div,article,section,td');
        for (var i = 0; i < candidates.length; i++) {
            var s = scoreNode(candidates[i]);
            if (s > bestScore) { bestScore = s; best = candidates[i]; }
        }
        return best;
    }

    var DIRECT_SELECTORS = [
        '#TextContent','#htmlContent','#content','#chaptercontent',
        '#chapter-content','#nr1','#text','#booktext',
        '.chapter-content','.read-content','.article-content',
        'article','.content'
    ];
    var root = null;
    for (var si = 0; si < DIRECT_SELECTORS.length; si++) {
        var el = document.querySelector(DIRECT_SELECTORS[si]);
        if (el && el.innerText && el.innerText.trim().length > 200) {
            root = el; break;
        }
    }
    if (!root) root = findBestContent(document.body);
    if (!root) root = document.body;

    var clone = root.cloneNode(true);
    var noisy = clone.querySelectorAll('script,style,iframe,noscript,button,input,.ad,.ads,#ad,#ads');
    for (var ni = 0; ni < noisy.length; ni++) {
        if (noisy[ni].parentNode) noisy[ni].parentNode.removeChild(noisy[ni]);
    }

    var rawText;
    var pNodes = clone.querySelectorAll('p');
    var substantialPNodes = [];
    for (var pi = 0; pi < pNodes.length; pi++) {
        var pt = (pNodes[pi].innerText || pNodes[pi].textContent || '').trim();
        if (pt.length > 10) substantialPNodes.push(pt);
    }
    if (substantialPNodes.length >= 3) {
        rawText = substantialPNodes.join('\n');
    } else {
        rawText = clone.innerText || clone.textContent || '';
    }

    var cutIdx = -1;
    for (var ci = 0; ci < CUT_MARKERS.length; ci++) {
        var idx = rawText.indexOf(CUT_MARKERS[ci]);
        if (idx > 200 && idx > rawText.length * 0.35) {
            cutIdx = cutIdx < 0 ? idx : Math.min(cutIdx, idx);
        }
    }
    if (cutIdx >= 0) rawText = rawText.substring(0, cutIdx);

    var lines = rawText.split(/\n/);
    var kept = [];
    for (var li = 0; li < lines.length; li++) {
        var line = lines[li].replace(/\r/,'').trimRight();
        if (!line.trim()) { if (kept.length > 0) kept.push(''); continue; }
        if (line.trim().length <= 2) continue;
        if (hasAnyKw(line, BAD_LINE_KW)) continue;
        if (hasAnyKw(line, AD_LINE_KW)) continue;
        if (/^\d{1,2}$/.test(line.trim())) continue;
        if ((line.match(/>/g)||[]).length >= 2 && line.length < 150) continue;
        kept.push(line);
    }
    var content = kept.join('\n').replace(/\n{3,}/g,'\n\n').trim();

    var title = '';
    var titlePatterns = [
        /^第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷]/,
        /^(楔子|引子|序章|番外|尾声|前言)/
    ];
    var h1s = document.querySelectorAll('h1,h2,.chapter-title,.title,#title');
    for (var hi = 0; hi < h1s.length; hi++) {
        var ht = h1s[hi].innerText ? h1s[hi].innerText.trim() : '';
        for (var pi = 0; pi < titlePatterns.length; pi++) {
            if (titlePatterns[pi].test(ht)) { title = ht; break; }
        }
        if (title) break;
    }
    if (!title) {
        var dtMatch = (document.title||'').match(/第[一二三四五六七八九十百千万零〇\d]+[章节回篇部集卷][^_-]*/);
        if (dtMatch) title = dtMatch[0].trim();
    }

    if (content.length < 100) {
        return JSON.stringify({ success: false, error: '正文内容过短，可能未加载完成' });
    }
    return JSON.stringify({ success: true, title: title, content: content, wordCount: content.length });
})();
";
        }

        public static string GetBookInfoScript()
        {
            return @"
(function() {
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

    title = pickFirstText(['h1', '.book-title', '.novel-title', '#title', '.title', '.book-name']);
    if (!title) {
        title = pickMeta(['property=""og:title""'], 'content') || (document.title || '').trim();
        if (title) {
            title = title.replace(/最新章节.*$/g, '').replace(/全文阅读.*$/g, '').replace(/_.*$/g, '').trim();
        }
    }

    if (!author) {
        author = pickFirstText(['.author', '.writer', '.book-author', '.info .author', '.book-info .author']);
    }
    if (author) author = author.replace(/作者[：:]/g, '').trim();

    if (!author) {
        author = pickMeta(['property=""og:novel:author""'], 'content');
    }

    if (!author) {
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
        var authorMatch = bodyText.match(/作\s*者\s*[：:]\s*([^\n\r|｜]+)/);
        if (!authorMatch) authorMatch = bodyText.match(/作者\s*[：:]\s*([^\n\r|｜]+)/);
        if (authorMatch) author = authorMatch[1].trim();
    }

    if (!author) {
        var nearTitle = pickFirstText(['.info', '.book-info', '.con_top', '.breadcrumb', '.crumbs']);
        if (nearTitle) {
            var m = nearTitle.match(/作者[：:]\s*([^\n\r]+)/);
            if (m) author = m[1].trim();
        }
    }

    if (!author) {
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
        author = author.replace(/[（(]著[)）]$/g, '').trim();
        author = author.replace(/\s*著$/g, '').trim();
    }

    if (!author) {
        var pageTitleForAuthor = (document.title || '').trim();
        var titleAuthorM = pageTitleForAuthor.match(/[\(（]([^)）]{1,20})[\)）]/);
        if (titleAuthorM) author = titleAuthorM[1].trim();
    }

    if (title && author && title.endsWith(')') || title && author && title.endsWith('）')) {
        title = title.replace(/[\(（][^)）]+[\)）]\s*$/, '').trim();
    }

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
        var match = bodyText.match(/(?:类\s*别|分\s*类|类\s*型|题\s*材)[：:]\s*([^\n\r,，|｜]+)/);
        if (!match) match = bodyText.match(/(?:类型|分类|类别|题材)[：:]\s*([^\n\r,，|｜]+)/);
        if (match) genre = match[1].trim();
    }

    if (!genre) {
        var breadcrumb = pickFirstText(['.con_top', '.bread', '.breadcrumb', '.crumbs', '#breadcrumb']);
        if (breadcrumb) {
            var parts = breadcrumb.split(/>|\//).map(function(s){ return (s||'').trim(); }).filter(function(s){ return !!s; });
            for (var bi = 0; bi < parts.length; bi++) {
                if (parts[bi].indexOf('小说') >= 0 && parts[bi].length <= 20) { genre = parts[bi]; break; }
            }
        }
    }

    if (!genre) {
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

    var tagElements = [];
    var excludeWords = ['目录', '章节', '简介', '作者', '类型', '字数', '更新', '收藏', '推荐', '点击', '阅读', '登录', '注册'];

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
