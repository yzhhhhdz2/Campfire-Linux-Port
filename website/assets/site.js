/* Campfire for Linux — 官网与数据看板共用配置 / 本地化 / GitHub Release API
 * 纯静态、无第三方依赖。下载直链指向 GitHub Release，因此下载量仍由 GitHub 官方统计。
 */
window.CF = (function () {
  'use strict';

  var OWNER = 'yzhhhhdz2';
  var REPO = 'Campfire-Linux-Port';
  var TAG = 'v1.1.32';
  var VERSION = '1.1.32';

  var REPO_URL = 'https://github.com/' + OWNER + '/' + REPO;
  var RELEASE_URL = REPO_URL + '/releases/tag/' + TAG;
  var LATEST_URL = REPO_URL + '/releases/latest';
  var API_LATEST = 'https://api.github.com/repos/' + OWNER + '/' + REPO + '/releases/latest';
  var CACHE_KEY = 'cf_release_cache_v1';
  var LANG_KEY = 'cf_lang';
  var SNAP_KEY = 'cf_admin_snapshots_v1';

  // API 不可用时的兜底数据（发布时实测值）。size 一律是字节，sizeMB 是显示用。
  function fb(name, mb, urlPath) {
    return {
      name: name,
      sizeMB: mb,
      size: Math.round(mb * 1048576),
      count: null,
      url: dl(urlPath),
      created: '2026-11-26T12:56:00Z',
      updated: '2026-11-26T12:56:00Z'
    };
  }

  var FALLBACK = {
    tag: TAG,
    name: 'v1.1.32',
    published: '2026-11-26T12:56:00Z',
    html_url: RELEASE_URL,
    fetchedAt: null,
    remaining: null,
    assets: [
      fb('Campfire-1.1.32-x86_64.AppImage', 64.11, 'Campfire-1.1.32-x86_64.AppImage'),
      fb('campfire-1.1.32-linux-x64.tar.gz', 63.67, 'campfire-1.1.32-linux-x64.tar.gz'),
      fb('campfire-1.1.32-linux-x64.tar.gz.sha256', 0.0001, 'campfire-1.1.32-linux-x64.tar.gz.sha256'),
      fb('campfire-linux.tar.gz', 0.69, 'campfire-linux.tar.gz'),
      fb('campfire-linux.tar.gz.sha256', 0.0001, 'campfire-linux.tar.gz.sha256'),
      fb('SHA256SUMS', 0.0003, 'SHA256SUMS')
    ]
  };

  function dl(name) { return REPO_URL + '/releases/download/' + TAG + '/' + name; }

  var I18N = {
    zh: {
      brand: 'Campfire', brandSub: 'for Linux',
      navRepo: 'GitHub 仓库', navDocs: '移植文档', navReleases: '全部版本', navAdmin: '数据看板',
      heroKicker: '免费开源',
      heroTitle: '在 Linux 上，和朋友一起玩',
      heroDesc: 'Windows 版 Campfire 的 Linux 移植。邀请码联机、动态主题，以及 Minecraft 26.3 自动开放局域网，都已在真机上验证通过。',
      feat1: '邀请码直连联机', feat2: 'MC 26.3 自动开局域网', feat3: '免 root · 自包含',
      dlTitle: '下载', dlDesc: '选一个就行。两个成品包都是自包含的：不需要装 .NET，也不需要 root，不会改动系统目录。',
      cardAppTitle: 'AppImage', cardAppDesc: '单文件、免安装，加执行位后直接运行。适合大多数发行版。',
      cardTarTitle: 'tar.gz 安装包', cardTarDesc: '解压后运行 install.sh，会装好桌面图标、应用菜单和 campfire 命令。',
      cardSrcTitle: '完整源码', cardSrcDesc: 'GPL-3.0 要求的完整对应源码，含 Java Agent 与 40 份 Mappings 表。',
      recommended: '推荐', btnDownload: '下载', countText: '{n} 次下载', countUnknown: '下载统计读取中',
      cliTitle: '也可以用命令行', cliDesc: '复制到终端执行，下载与安装一步到位。',
      tabApp: 'AppImage', tabTar: 'tar.gz', btnCopy: '复制命令', copied: '已复制',
      verifyTitle: '校验下载', verifyDesc: 'Release 附带 SHA256SUMS，在同一个目录里执行：',
      linkChecksum: '下载 SHA256SUMS',
      footerLicense: '基于 Xocio/Campfire v1.1.32，GPL-3.0 开源。Minecraft 与 JDK 不包含在下载包内。',
      noteLive: '下载数与文件大小来自 GitHub Release 实时接口',
      noteFallback: '实时接口暂不可用，当前显示发布时的信息，下载仍然可用',
      langLabel: '切换语言',
      admTitle: '下载数据看板', admSub: 'Campfire for Linux · Release 下载统计',
      admBack: '返回官网', admRefresh: '立即刷新', admAuto: '自动刷新（每 2 分钟）',
      statusLoading: '正在读取 GitHub API…', statusOk: '数据已更新', statusCache: '网络不可用，正在显示本机缓存',
      statusErr: '读取失败，正在显示本机缓存',
      labelSource: '数据源', labelUpdated: '更新时间', labelQuota: 'API 剩余', labelVersion: '当前版本',
      kpiTotal: '累计下载', kpiAssets: '发布文件', kpiAppShare: 'AppImage 占比', kpiDelta: '较上次采样',
      colFile: '文件', colType: '类型', colSize: '大小', colDownloads: '下载数', colShare: '占比', colUpdated: '更新于', colAction: '操作',
      typeApp: 'AppImage', typeTar: '安装包', typeSrc: '源码包', typeSum: '校验和', typeOther: '其他',
      trendTitle: '累计下载趋势', trendSub: '每次打开或刷新看板会记录一个快照',
      trendEmpty: '历史快照还不足两个。多用几次、或隔一会儿再刷新，曲线就会出现。',
      trendNote: '趋势只保存在这个浏览器里；换设备或清除浏览器数据会重新开始。',
      btnCsv: '导出 CSV', btnJson: '导出 JSON', btnClear: '清空本机快照', btnCopyLink: '复制链接',
      noteTitle: '关于这些数据',
      sectionDetails: '下载明细',
      clicksLine: '官网下载按钮点击（仅统计此浏览器）：{n} 次',
      subGitHubStat: 'GitHub 官方统计',
      firstSample: '首次采样',
      note1: '下载数取自 GitHub Releases API 的 download_count 字段，统计的是从 GitHub 直接下载的次数；官网按钮直链到 Release，所以会被计入。',
      note2: '匿名调用 GitHub API 每小时每 IP 限额 60 次，页面会自动缓存，自动刷新设为每 2 分钟一次。',
      note3: '本页没有账号体系，也不存储任何访客数据；看到的内容本身都是 GitHub 公开信息。',
      note4: '若需要跨设备的点击来源、地区、逐日趋势等，需要接一个真正的后端统计服务。',
      noData: '暂无数据', unitTimes: '次', justNow: '刚刚', minutesAgo: '{n} 分钟前', hoursAgo: '{n} 小时前',
      openOnGithub: '在 GitHub 打开', clearConfirm: '确定清空本机保存的全部历史快照吗？', cleared: '已清空'
    },
    hant: {
      brand: 'Campfire', brandSub: 'for Linux',
      navRepo: 'GitHub 儲存庫', navDocs: '移植文件', navReleases: '全部版本', navAdmin: '數據看板',
      heroKicker: '免費開源',
      heroTitle: '在 Linux 上，和朋友一起玩',
      heroDesc: 'Windows 版 Campfire 的 Linux 移植。邀請碼連線、動態主題，以及 Minecraft 26.3 自動開放區域網路，都已在實機驗證通過。',
      feat1: '邀請碼直連連線', feat2: 'MC 26.3 自動開區網', feat3: '免 root · 自包含',
      dlTitle: '下載', dlDesc: '選一個就行。兩個成品包都是自包含的：不需要裝 .NET，也不需要 root，不會改動系統目錄。',
      cardAppTitle: 'AppImage', cardAppDesc: '單檔、免安裝，加執行位後直接執行。適合大多數發行版。',
      cardTarTitle: 'tar.gz 安裝包', cardTarDesc: '解壓後執行 install.sh，會裝好桌面圖示、應用程式選單和 campfire 指令。',
      cardSrcTitle: '完整原始碼', cardSrcDesc: 'GPL-3.0 要求的完整對應原始碼，含 Java Agent 與 40 份 Mappings 表。',
      recommended: '推薦', btnDownload: '下載', countText: '{n} 次下載', countUnknown: '下載統計讀取中',
      cliTitle: '也可以用指令列', cliDesc: '複製到終端機執行，下載與安裝一步到位。',
      tabApp: 'AppImage', tabTar: 'tar.gz', btnCopy: '複製指令', copied: '已複製',
      verifyTitle: '校驗下載', verifyDesc: 'Release 附帶 SHA256SUMS，在同一個目錄裡執行：',
      linkChecksum: '下載 SHA256SUMS',
      footerLicense: '基於 Xocio/Campfire v1.1.32，GPL-3.0 開源。Minecraft 與 JDK 不包含在下載包內。',
      noteLive: '下載數與檔案大小來自 GitHub Release 即時介面',
      noteFallback: '即時介面暫不可用，目前顯示發佈時的資訊，下載仍然可用',
      langLabel: '切換語言',
      admTitle: '下載數據看板', admSub: 'Campfire for Linux · Release 下載統計',
      admBack: '返回官網', admRefresh: '立即重整', admAuto: '自動重整（每 2 分鐘）',
      statusLoading: '正在讀取 GitHub API…', statusOk: '資料已更新', statusCache: '網路不可用，正在顯示本機快取',
      statusErr: '讀取失敗，正在顯示本機快取',
      labelSource: '資料來源', labelUpdated: '更新時間', labelQuota: 'API 剩餘', labelVersion: '目前版本',
      kpiTotal: '累計下載', kpiAssets: '發佈檔案', kpiAppShare: 'AppImage 佔比', kpiDelta: '較上次取樣',
      colFile: '檔案', colType: '類型', colSize: '大小', colDownloads: '下載數', colShare: '佔比', colUpdated: '更新於', colAction: '操作',
      typeApp: 'AppImage', typeTar: '安裝包', typeSrc: '原始碼包', typeSum: '校驗和', typeOther: '其他',
      trendTitle: '累計下載趨勢', trendSub: '每次開啟或重整看板會記錄一個快照',
      trendEmpty: '歷史快照還不足兩個。多用幾次、或隔一會兒再重整，曲線就會出現。',
      trendNote: '趨勢只保存在這個瀏覽器裡；換裝置或清除瀏覽器資料會重新開始。',
      btnCsv: '匯出 CSV', btnJson: '匯出 JSON', btnClear: '清空本機快照', btnCopyLink: '複製連結',
      noteTitle: '關於這些資料',
      sectionDetails: '下載明細',
      clicksLine: '官網下載按鈕點擊（僅統計此瀏覽器）：{n} 次',
      subGitHubStat: 'GitHub 官方統計',
      firstSample: '首次取樣',
      note1: '下載數取自 GitHub Releases API 的 download_count 欄位，統計的是從 GitHub 直接下載的次數；官網按鈕直連到 Release，所以會被計入。',
      note2: '匿名呼叫 GitHub API 每小時每 IP 限額 60 次，頁面會自動快取，自動重整設為每 2 分鐘一次。',
      note3: '本頁沒有帳號體系，也不儲存任何訪客資料；看到的內容本身都是 GitHub 公開資訊。',
      note4: '若需要跨裝置的點擊來源、地區、逐日趨勢等，需要接一個真正的後端統計服務。',
      noData: '暫無資料', unitTimes: '次', justNow: '剛剛', minutesAgo: '{n} 分鐘前', hoursAgo: '{n} 小時前',
      openOnGithub: '在 GitHub 開啟', clearConfirm: '確定清空本機保存的全部歷史快照嗎？', cleared: '已清空'
    },
    en: {
      brand: 'Campfire', brandSub: 'for Linux',
      navRepo: 'GitHub repo', navDocs: 'Port docs', navReleases: 'All releases', navAdmin: 'Dashboard',
      heroKicker: 'free and open source',
      heroTitle: 'Play together, on Linux',
      heroDesc: 'A Linux port of Campfire for Windows. Invite-code multiplayer, dynamic theming, and automatic LAN opening for Minecraft 26.3 — all verified on real hardware.',
      feat1: 'Invite-code direct connect', feat2: 'MC 26.3 LAN auto-open', feat3: 'No root · self-contained',
      dlTitle: 'Download', dlDesc: 'Pick either one. Both binaries are self-contained: no .NET install, no root, nothing written outside your home directory.',
      cardAppTitle: 'AppImage', cardAppDesc: 'One file, no install. Make it executable and run it. Works on most distros.',
      cardTarTitle: 'tar.gz installer', cardTarDesc: 'Extract and run install.sh to get a desktop icon, app menu entry and the campfire command.',
      cardSrcTitle: 'Complete source', cardSrcDesc: 'The full corresponding source required by GPL-3.0, including the Java Agent and 40 Mappings tables.',
      recommended: 'Recommended', btnDownload: 'Download', countText: '{n} downloads', countUnknown: 'Loading download count',
      cliTitle: 'Prefer the terminal?', cliDesc: 'Paste into a shell to download and install in one go.',
      tabApp: 'AppImage', tabTar: 'tar.gz', btnCopy: 'Copy command', copied: 'Copied',
      verifyTitle: 'Verify your download', verifyDesc: 'The release ships a SHA256SUMS file. Run this in the same folder:',
      linkChecksum: 'Download SHA256SUMS',
      footerLicense: 'Based on Xocio/Campfire v1.1.32, GPL-3.0. Minecraft and a JDK are not included.',
      noteLive: 'Counts and file sizes come from the live GitHub Release API',
      noteFallback: 'Live API unavailable — showing published values. Downloads still work.',
      langLabel: 'Switch language',
      admTitle: 'Download dashboard', admSub: 'Campfire for Linux · Release download stats',
      admBack: 'Back to site', admRefresh: 'Refresh now', admAuto: 'Auto refresh (every 2 min)',
      statusLoading: 'Reading GitHub API…', statusOk: 'Data updated', statusCache: 'Offline — showing local cache',
      statusErr: 'Request failed — showing local cache',
      labelSource: 'Source', labelUpdated: 'Updated', labelQuota: 'API left', labelVersion: 'Version',
      kpiTotal: 'Total downloads', kpiAssets: 'Release files', kpiAppShare: 'AppImage share', kpiDelta: 'Since last sample',
      colFile: 'File', colType: 'Type', colSize: 'Size', colDownloads: 'Downloads', colShare: 'Share', colUpdated: 'Updated', colAction: 'Actions',
      typeApp: 'AppImage', typeTar: 'Installer', typeSrc: 'Source', typeSum: 'Checksum', typeOther: 'Other',
      trendTitle: 'Cumulative downloads', trendSub: 'A snapshot is saved each time you open or refresh this page',
      trendEmpty: 'Not enough snapshots yet. Come back later or refresh after a while to see the curve.',
      trendNote: 'Trend data lives in this browser only; another device or clearing browser data starts over.',
      btnCsv: 'Export CSV', btnJson: 'Export JSON', btnClear: 'Clear local snapshots', btnCopyLink: 'Copy link',
      noteTitle: 'About this data',
      sectionDetails: 'Download details',
      clicksLine: 'Clicks on the site download buttons (this browser only): {n}',
      subGitHubStat: 'counted by GitHub',
      firstSample: 'first sample',
      note1: 'Counts come from the download_count field of the GitHub Releases API. Site buttons link straight to the Release, so those downloads are counted.',
      note2: 'Anonymous GitHub API calls are limited to 60/hour per IP. This page caches responses and auto-refreshes every 2 minutes.',
      note3: 'There is no account system and no visitor tracking here; everything shown is already public on GitHub.',
      note4: 'For cross-device click sources, regions or daily breakdowns you would need a real analytics backend.',
      noData: 'No data', unitTimes: '', justNow: 'just now', minutesAgo: '{n} min ago', hoursAgo: '{n} h ago',
      openOnGithub: 'Open on GitHub', clearConfirm: 'Delete all locally stored snapshots?', cleared: 'Cleared'
    }
  };

  var LANGS = [
    { id: 'zh', short: '简' },
    { id: 'hant', short: '繁' },
    { id: 'en', short: 'EN' }
  ];

  function lang() {
    // URL 参数优先，方便分享某个语言版本的链接
    try {
      var q = new URLSearchParams(window.location.search).get('lang');
      if (q === 'zh' || q === 'hant' || q === 'en') return q;
    } catch (e) { /* ignore */ }
    var v = null;
    try { v = localStorage.getItem(LANG_KEY); } catch (e) { /* ignore */ }
    return (v === 'zh' || v === 'hant' || v === 'en') ? v : 'zh';
  }

  function setLang(id) {
    try { localStorage.setItem(LANG_KEY, id); } catch (e) { /* ignore */ }
    applyI18n();
    window.dispatchEvent(new CustomEvent('cf:lang'));
  }

  function cycleLang() {
    var cur = lang();
    var i = LANGS.findIndex(function (x) { return x.id === cur; });
    setLang(LANGS[(i + 1) % LANGS.length].id);
    syncLangButtons();
  }

  function syncLangButtons() {
    var cur = lang();
    var short = LANGS.find(function (x) { return x.id === cur; }).short;
    document.querySelectorAll('[data-lang-btn]').forEach(function (b) {
      b.textContent = short;
      b.setAttribute('title', t('langLabel'));
      b.setAttribute('aria-label', t('langLabel'));
    });
  }

  function t(key, vars) {
    var dict = I18N[lang()] || I18N.zh;
    var s = (key in dict) ? dict[key] : (I18N.zh[key] || key);
    if (vars) {
      Object.keys(vars).forEach(function (k) { s = s.split('{' + k + '}').join(String(vars[k])); });
    }
    return s;
  }

  function applyI18n() {
    document.querySelectorAll('[data-i18n]').forEach(function (el) {
      var key = el.getAttribute('data-i18n');
      var attr = el.getAttribute('data-i18n-attr');
      var val = t(key);
      if (attr) el.setAttribute(attr, val); else el.textContent = val;
    });
    document.documentElement.lang = lang() === 'en' ? 'en' : (lang() === 'hant' ? 'zh-Hant' : 'zh-CN');
  }

  function fmtNum(n) {
    if (n === null || n === undefined || isNaN(n)) return '—';
    return Number(n).toLocaleString(lang() === 'en' ? 'en-US' : 'zh-CN');
  }

  function fmtSize(mb) {
    if (mb === null || mb === undefined) return '—';
    if (mb < 0.01) return (mb * 1024).toFixed(1) + ' KB';
    if (mb < 1) return (mb * 1024).toFixed(0) + ' KB';
    return mb.toFixed(2) + ' MB';
  }

  function fmtBytes(b) {
    if (b === null || b === undefined) return '—';
    if (b < 1024) return b + ' B';
    if (b < 1048576) return (b / 1024).toFixed(1) + ' KB';
    return (b / 1048576).toFixed(2) + ' MB';
  }

  function fmtDate(iso) {
    if (!iso) return '—';
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '—';
    return d.toLocaleString(lang() === 'en' ? 'en-US' : 'zh-CN', {
      year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit'
    });
  }

  function relativeTime(iso) {
    if (!iso) return '—';
    var diff = Date.now() - new Date(iso).getTime();
    if (isNaN(diff)) return '—';
    var min = Math.floor(diff / 60000);
    if (min < 1) return t('justNow');
    if (min < 60) return t('minutesAgo', { n: min });
    var h = Math.floor(min / 60);
    if (h < 24) return t('hoursAgo', { n: h });
    return fmtDate(iso);
  }

  function escapeHtml(s) {
    return String(s === null || s === undefined ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand('copy') ? resolve() : reject(new Error('copy failed')); }
      catch (e) { reject(e); }
      document.body.removeChild(ta);
    });
  }

  function loadCache() {
    try {
      var raw = localStorage.getItem(CACHE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch (e) { return null; }
  }

  function saveCache(data) {
    try { localStorage.setItem(CACHE_KEY, JSON.stringify(data)); } catch (e) { /* ignore */ }
  }

  var _mem = null;

  function fetchRelease(force) {
    var now = Date.now();
    if (!force && _mem && (now - _mem.at) < 60000) {
      return Promise.resolve({ data: _mem.data, cached: true });
    }
    var url = API_LATEST + (force ? '?_=' + now : '');
    return fetch(url, { cache: 'no-store' }).then(function (resp) {
      if (!resp.ok) throw new Error('HTTP ' + resp.status);
      return resp.json().then(function (d) {
        var out = {
          tag: d.tag_name,
          name: d.name || d.tag_name,
          published: d.published_at,
          html_url: d.html_url,
          fetchedAt: new Date().toISOString(),
          remaining: resp.headers.get('x-ratelimit-remaining'),
          limit: resp.headers.get('x-ratelimit-limit'),
          assets: (d.assets || []).map(function (a) {
            return {
              name: a.name,
              sizeMB: a.size / 1048576,
              size: a.size,
              count: a.download_count,
              url: a.browser_download_url,
              created: a.created_at,
              updated: a.updated_at
            };
          })
        };
        saveCache(out);
        _mem = { at: now, data: out };
        return { data: out, cached: false };
      });
    });
  }

  // 把 API 数据统一成 {sizeMB, count, url} 的视图；兜底数据也走这里
  function normalize(data) {
    if (!data) return FALLBACK;
    var out = Object.assign({}, data);
    out.assets = (data.assets || []).map(function (a) {
      return Object.assign({}, a, {
        sizeMB: a.sizeMB !== undefined ? a.sizeMB : a.size,
        count: a.count === undefined ? null : a.count
      });
    });
    return out;
  }

  function findAsset(data, kind) {
    var assets = normalize(data).assets;
    var tests = {
      app: function (n) { return /\.AppImage$/i.test(n); },
      tar: function (n) { return /^campfire-[\d.]+-linux-x64\.tar\.gz$/.test(n); },
      src: function (n) { return n === 'campfire-linux.tar.gz'; },
      sums: function (n) { return n === 'SHA256SUMS'; }
    };
    return assets.find(function (a) { return tests[kind](a.name); }) || null;
  }

  function assetType(name) {
    if (/\.AppImage$/i.test(name)) return 'typeApp';
    if (/^campfire-[\d.]+-linux-x64\.tar\.gz$/.test(name)) return 'typeTar';
    if (name === 'campfire-linux.tar.gz') return 'typeSrc';
    if (/sha256|SHA256SUMS/i.test(name)) return 'typeSum';
    return 'typeOther';
  }

  function bindCommon() {
    document.querySelectorAll('[data-lang-btn]').forEach(function (b) {
      b.addEventListener('click', cycleLang);
    });
    applyI18n();
    syncLangButtons();
  }

  return {
    OWNER: OWNER, REPO: REPO, TAG: TAG, VERSION: VERSION,
    REPO_URL: REPO_URL, RELEASE_URL: RELEASE_URL, LATEST_URL: LATEST_URL,
    API_LATEST: API_LATEST, SNAP_KEY: SNAP_KEY, FALLBACK: FALLBACK,
    dl: dl, t: t, lang: lang, setLang: setLang, cycleLang: cycleLang,
    applyI18n: applyI18n, bindCommon: bindCommon,
    fmtNum: fmtNum, fmtSize: fmtSize, fmtBytes: fmtBytes, fmtDate: fmtDate,
    relativeTime: relativeTime, escapeHtml: escapeHtml, copyText: copyText,
    loadCache: loadCache, saveCache: saveCache, fetchRelease: fetchRelease,
    normalize: normalize, findAsset: findAsset, assetType: assetType
  };
})();
