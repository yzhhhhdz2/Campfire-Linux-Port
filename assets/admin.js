/* Campfire for Linux — 下载数据看板
 * 数据来源：GitHub Releases API（公开、匿名可读）。
 * 累计值来自 GitHub 官方 download_count；趋势快照保存在本浏览器 localStorage。
 */
(function () {
  'use strict';

  var CF = window.CF;
  CF.bindCommon();

  var CLICKS_KEY = 'cf_clicks_v1';
  var current = null;
  var timer = null;
  var SVG_NS = 'http://www.w3.org/2000/svg';

  /* ---------------- 快照 ---------------- */
  function loadSnaps() {
    try {
      var raw = localStorage.getItem(CF.SNAP_KEY);
      var list = raw ? JSON.parse(raw) : [];
      return Array.isArray(list) ? list : [];
    } catch (e) { return []; }
  }

  function saveSnaps(list) {
    try { localStorage.setItem(CF.SNAP_KEY, JSON.stringify(list.slice(-300))); } catch (e) { /* ignore */ }
  }

  function recordSnapshot(total, per) {
    var snaps = loadSnaps();
    var last = snaps[snaps.length - 1];
    var iso = new Date().toISOString();
    if (last && (Date.now() - new Date(last.t).getTime()) < 60000) {
      last.t = iso; last.total = total; last.per = per;
    } else {
      snaps.push({ t: iso, total: total, per: per });
    }
    saveSnaps(snaps);
    return snaps;
  }

  function getClicks() {
    try {
      var raw = localStorage.getItem(CLICKS_KEY);
      var obj = raw ? JSON.parse(raw) : {};
      return Object.keys(obj).reduce(function (s, k) { return s + (Number(obj[k]) || 0); }, 0);
    } catch (e) { return 0; }
  }

  /* ---------------- 渲染 ---------------- */
  function setStatus(kind) {
    var dot = document.getElementById('statusDot');
    var txt = document.getElementById('statusText');
    dot.classList.toggle('live', kind === 'ok');
    txt.textContent = CF.t(kind === 'loading' ? 'statusLoading' : (kind === 'ok' ? 'statusOk' : (kind === 'cache' ? 'statusCache' : 'statusErr')));
  }

  function totals(assets) {
    var total = 0;
    assets.forEach(function (a) { total += (a.count || 0); });
    return total;
  }

  function renderKpis(data, snaps) {
    var assets = CF.normalize(data).assets;
    var total = totals(assets);
    var app = CF.findAsset(data, 'app');
    var appCount = app && app.count ? app.count : 0;
    var share = total ? (appCount / total * 100) : 0;

    document.getElementById('kpiTotal').textContent = CF.fmtNum(total);
    document.getElementById('kpiTotalSub').textContent = CF.t('subGitHubStat') + ' · ' + data.tag;

    document.getElementById('kpiAssets').textContent = CF.fmtNum(assets.length);
    document.getElementById('kpiAssetsSub').textContent = CF.fmtSize(
      assets.reduce(function (s, a) { return s + (a.sizeMB || 0); }, 0)
    );

    document.getElementById('kpiApp').textContent = total ? share.toFixed(1) + '%' : '—';
    document.getElementById('kpiAppSub').textContent = CF.fmtNum(appCount) + ' / ' + CF.fmtNum(total);

    // 较上次采样：找最近一个更小的累计值
    var prev = null;
    for (var i = snaps.length - 1; i >= 0; i--) {
      if (snaps[i].total < total) { prev = snaps[i]; break; }
    }
    var deltaEl = document.getElementById('kpiDelta');
    var deltaSub = document.getElementById('kpiDeltaSub');
    if (prev) {
      var d = total - prev.total;
      deltaEl.textContent = (d > 0 ? '+' : '') + CF.fmtNum(d);
      deltaSub.textContent = CF.relativeTime(prev.t);
    } else {
      deltaEl.textContent = '—';
      deltaSub.textContent = snaps.length ? CF.t('noData') : CF.t('firstSample');
    }
  }

  function renderRows(data) {
    var assets = CF.normalize(data).assets.slice().sort(function (a, b) {
      return (b.count || 0) - (a.count || 0);
    });
    var total = totals(assets);
    var tbody = document.getElementById('rows');

    tbody.innerHTML = assets.map(function (a) {
      var pct = total ? ((a.count || 0) / total * 100) : 0;
      var pageUrl = CF.RELEASE_URL;
      return '<tr>' +
        '<td><span class="fname">' + CF.escapeHtml(a.name) + '</span></td>' +
        '<td><span class="tag-type">' + CF.t(CF.assetType(a.name)) + '</span></td>' +
        '<td class="num">' + CF.fmtSize(a.sizeMB) + '</td>' +
        '<td class="num"><strong>' + CF.fmtNum(a.count) + '</strong></td>' +
        '<td><div class="share"><div class="bar"><i style="width:' + pct.toFixed(1) + '%"></i></div><span>' + pct.toFixed(1) + '%</span></div></td>' +
        '<td class="num" title="' + CF.escapeHtml(CF.fmtDate(a.updated)) + '">' + CF.escapeHtml(CF.relativeTime(a.updated)) + '</td>' +
        '<td><div class="row-actions">' +
          '<button class="btn btn-sm" data-copy="' + CF.escapeHtml(a.url) + '"><span>' + CF.t('btnCopyLink') + '</span></button>' +
          '<a class="btn btn-sm" href="' + CF.escapeHtml(pageUrl) + '" target="_blank" rel="noopener">' + CF.t('openOnGithub') + '</a>' +
        '</div></td>' +
      '</tr>';
    }).join('');

    tbody.querySelectorAll('[data-copy]').forEach(function (b) {
      b.addEventListener('click', function () {
        var url = b.getAttribute('data-copy');
        var span = b.querySelector('span');
        var old = span.textContent;
        CF.copyText(url).then(function () {
          span.textContent = CF.t('copied');
          setTimeout(function () { span.textContent = old; }, 1500);
        }).catch(function () { window.prompt(url); });
      });
    });
  }

  function mk(tag, attrs) {
    var el = document.createElementNS(SVG_NS, tag);
    Object.keys(attrs || {}).forEach(function (k) { el.setAttribute(k, attrs[k]); });
    return el;
  }

  function drawChart(snaps) {
    var svg = document.getElementById('chart');
    var empty = document.getElementById('chartEmpty');
    while (svg.firstChild) svg.removeChild(svg.firstChild);

    var pts = snaps.slice(-60);
    if (pts.length < 2) {
      svg.style.display = 'none';
      empty.hidden = false;
      return;
    }
    svg.style.display = '';
    empty.hidden = true;

    var W = 800, H = 190, PL = 52, PR = 16, PT = 14, PB = 26;
    var vals = pts.map(function (p) { return p.total; });
    var max = Math.max.apply(null, vals);
    var min = Math.min.apply(null, vals);
    if (max === min) { max = max + 1; min = Math.max(0, min - 1); }

    function X(i) { return PL + (pts.length === 1 ? 0 : i * (W - PL - PR) / (pts.length - 1)); }
    function Y(v) { return PT + (H - PT - PB) * (1 - (v - min) / (max - min)); }

    [0, 0.5, 1].forEach(function (f) {
      var y = PT + (H - PT - PB) * f;
      svg.appendChild(mk('line', { x1: PL, y1: y, x2: W - PR, y2: y, 'class': 'grid' }));
      var txt = mk('text', { x: PL - 8, y: y + 4, 'text-anchor': 'end' });
      txt.textContent = CF.fmtNum(Math.round(max - (max - min) * f));
      svg.appendChild(txt);
    });

    var d = pts.map(function (p, i) {
      return (i ? 'L' : 'M') + X(i).toFixed(1) + ' ' + Y(p.total).toFixed(1);
    }).join(' ');
    svg.appendChild(mk('path', {
      d: d + ' L' + X(pts.length - 1).toFixed(1) + ' ' + (H - PB) + ' L' + X(0).toFixed(1) + ' ' + (H - PB) + ' Z',
      'class': 'area'
    }));
    svg.appendChild(mk('path', { d: d, 'class': 'line' }));

    pts.forEach(function (p, i) {
      svg.appendChild(mk('circle', { cx: X(i), cy: Y(p.total), r: i === pts.length - 1 ? 4 : 2, 'class': 'pt' }));
    });

    var last = pts[pts.length - 1];
    var lbl = mk('text', { x: X(pts.length - 1), y: Math.max(14, Y(last.total) - 12), 'text-anchor': 'end' });
    lbl.textContent = CF.fmtNum(last.total);
    svg.appendChild(lbl);

    var x1 = mk('text', { x: PL, y: H - 8 });
    x1.textContent = CF.fmtDate(pts[0].t);
    svg.appendChild(x1);
    var x2 = mk('text', { x: W - PR, y: H - 8, 'text-anchor': 'end' });
    x2.textContent = CF.fmtDate(last.t);
    svg.appendChild(x2);
  }

  function renderStatus(data) {
    document.getElementById('stVersion').textContent = data.tag || '—';
    document.getElementById('stUpdated').textContent = data.fetchedAt
      ? CF.relativeTime(data.fetchedAt) + ' (' + CF.fmtDate(data.fetchedAt) + ')'
      : CF.fmtDate(data.published);
    document.getElementById('stQuota').textContent = (data.remaining === null || data.remaining === undefined)
      ? '—'
      : (data.remaining + (data.limit ? ' / ' + data.limit : ''));
    document.getElementById('clicksLine').textContent =
      CF.t('clicksLine', { n: CF.fmtNum(getClicks()) });
  }

  function renderAll(data, snaps) {
    current = data;
    renderStatus(data);
    renderKpis(data, snaps);
    renderRows(data);
    drawChart(snaps);
  }

  /* ---------------- 数据获取 ---------------- */
  function refresh(force) {
    setStatus('loading');
    return CF.fetchRelease(!!force).then(function (res) {
      var assets = CF.normalize(res.data).assets;
      var per = {};
      assets.forEach(function (a) { per[a.name] = a.count || 0; });
      var snaps = recordSnapshot(totals(assets), per);
      renderAll(res.data, snaps);
      setStatus('ok');
    }).catch(function () {
      var cache = CF.loadCache();
      if (cache) {
        var snaps = loadSnaps();
        renderAll(cache, snaps);
        setStatus('cache');
      } else {
        renderAll(CF.FALLBACK, loadSnaps());
        setStatus('err');
      }
    });
  }

  /* ---------------- 导出 ---------------- */
  function download(filename, content, mime) {
    var blob = new Blob([content], { type: mime });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 1500);
  }

  function exportCsv() {
    if (!current) return;
    var assets = CF.normalize(current).assets.slice().sort(function (a, b) { return (b.count || 0) - (a.count || 0); });
    var total = totals(assets);
    var rows = [['file', 'type', 'size_bytes', 'downloads', 'share_percent', 'updated_at', 'url']];
    assets.forEach(function (a) {
      rows.push([
        a.name,
        CF.t(CF.assetType(a.name)),
        Math.round(a.size || 0),
        a.count === null ? '' : a.count,
        total ? ((a.count || 0) / total * 100).toFixed(2) : '0.00',
        a.updated || '',
        a.url
      ]);
    });
    var csv = rows.map(function (r) {
      return r.map(function (c) { return '"' + String(c).replace(/"/g, '""') + '"'; }).join(',');
    }).join('\r\n');
    download('campfire-downloads-' + (current.tag || 'latest') + '.csv', '\ufeff' + csv, 'text/csv;charset=utf-8');
  }

  function exportJson() {
    if (!current) return;
    var payload = {
      exportedAt: new Date().toISOString(),
      repository: CF.OWNER + '/' + CF.REPO,
      release: current,
      totalDownloads: totals(CF.normalize(current).assets),
      snapshots: loadSnaps(),
      localClicks: getClicks()
    };
    download('campfire-downloads-' + (current.tag || 'latest') + '.json',
      JSON.stringify(payload, null, 2), 'application/json');
  }

  /* ---------------- 事件 ---------------- */
  function wire() {
    document.getElementById('refreshBtn').addEventListener('click', function () { refresh(true); });

    document.getElementById('autoRefresh').addEventListener('change', function (e) {
      if (timer) { clearInterval(timer); timer = null; }
      if (e.target.checked) {
        timer = setInterval(function () { refresh(true); }, 120000);
      }
    });

    document.getElementById('btnCsv').addEventListener('click', exportCsv);
    document.getElementById('btnJson').addEventListener('click', exportJson);
    document.getElementById('btnClear').addEventListener('click', function () {
      if (!window.confirm(CF.t('clearConfirm'))) return;
      saveSnaps([]);
      drawChart([]);
      renderKpis(current || CF.FALLBACK, []);
    });

    window.addEventListener('cf:lang', function () {
      var snaps = loadSnaps();
      renderAll(current || CF.FALLBACK, snaps);
    });
  }

  function load() {
    wire();
    refresh(false);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', load);
  } else {
    load();
  }
})();
