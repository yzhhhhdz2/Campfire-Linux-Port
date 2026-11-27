/* Campfire for Linux — 官网首页逻辑：读取 Release 数据、填充下载卡片、命令行区块、点击计数。 */
(function () {
  'use strict';

  var CF = window.CF;
  CF.bindCommon();

  var KINDS = ['app', 'tar', 'src'];
  var cmdMode = 'app';
  var lastData = null;
  var CLICKS_KEY = 'cf_clicks_v1';

  function pick(kind) {
    return CF.findAsset(lastData || CF.FALLBACK, kind);
  }

  function countText(n) {
    if (CF.lang() === 'en') return CF.fmtNum(n) + (n === 1 ? ' download' : ' downloads');
    return CF.t('countText', { n: CF.fmtNum(n) });
  }

  function renderKicker() {
    var el = document.getElementById('heroKicker');
    if (!el) return;
    var tag = (lastData && lastData.tag) ? lastData.tag : CF.TAG;
    el.textContent = tag + ' · ' + CF.t('heroKicker');
  }

  function renderCards() {
    KINDS.forEach(function (kind) {
      var card = document.querySelector('[data-card="' + kind + '"]');
      var a = pick(kind);
      if (!card || !a) return;

      card.querySelector('[data-file-name]').textContent = a.name;
      card.querySelector('[data-size]').textContent = CF.fmtSize(a.sizeMB);

      var btn = card.querySelector('[data-dl-btn]');
      btn.href = a.url;

      var cnt = card.querySelector('[data-count]');
      cnt.textContent = (a.count === null || a.count === undefined)
        ? CF.t('countUnknown')
        : countText(a.count);
    });
  }

  function buildCmd(kind) {
    if (kind === 'app') {
      var a = pick('app');
      var name = a ? a.name : 'Campfire-' + CF.VERSION + '-x86_64.AppImage';
      var url = a ? a.url : CF.dl(name);
      return 'curl -LO ' + url + '\nchmod +x ' + name + '\n./' + name;
    }
    var t = pick('tar');
    var tname = t ? t.name : 'campfire-' + CF.VERSION + '-linux-x64.tar.gz';
    var turl = t ? t.url : CF.dl(tname);
    var dir = tname.replace(/\.tar\.gz$/, '');
    return 'curl -LO ' + turl + '\ntar -xzf ' + tname + '\ncd ' + dir + '\nbash install.sh';
  }

  function renderCmd() {
    document.getElementById('cmdText').textContent = buildCmd(cmdMode);
  }

  function setStatus(ok) {
    var dot = document.getElementById('statusDot');
    var txt = document.getElementById('statusText');
    if (!dot || !txt) return;
    dot.classList.toggle('live', !!ok);
    txt.textContent = CF.t(ok ? 'noteLive' : 'noteFallback');
  }

  function bumpClick(kind) {
    try {
      var raw = localStorage.getItem(CLICKS_KEY);
      var obj = raw ? JSON.parse(raw) : {};
      obj[kind] = (obj[kind] || 0) + 1;
      localStorage.setItem(CLICKS_KEY, JSON.stringify(obj));
    } catch (e) { /* 隐私模式下忽略 */ }
  }

  function wire() {
    document.querySelectorAll('[data-cmd-tab]').forEach(function (tab) {
      tab.addEventListener('click', function () {
        cmdMode = tab.getAttribute('data-cmd-tab');
        document.querySelectorAll('[data-cmd-tab]').forEach(function (x) {
          x.classList.toggle('active', x === tab);
        });
        renderCmd();
      });
    });

    var copyBtn = document.getElementById('copyCmd');
    if (copyBtn) {
      copyBtn.addEventListener('click', function () {
        var text = document.getElementById('cmdText').textContent;
        var label = copyBtn.querySelector('[data-i18n]');
        var old = label ? label.textContent : '';
        CF.copyText(text).then(function () {
          if (label) label.textContent = CF.t('copied');
          setTimeout(function () { if (label) label.textContent = old; }, 1600);
        }).catch(function () { /* 复制失败就手动选吧 */ });
      });
    }

    document.querySelectorAll('[data-dl-btn]').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var card = btn.closest('[data-card]');
        bumpClick(card ? card.getAttribute('data-card') : 'unknown');
      });
    });

    window.addEventListener('cf:lang', function () {
      renderKicker();
      renderCards();
      renderCmd();
      setStatus(lastData && lastData.fetchedAt ? true : false);
    });
  }

  function load() {
    renderKicker();
    renderCards();
    renderCmd();
    wire();

    CF.fetchRelease(false).then(function (res) {
      lastData = res.data;
      renderKicker();
      renderCards();
      renderCmd();
      setStatus(true);
    }).catch(function () {
      var cache = CF.loadCache();
      if (cache) {
        lastData = cache;
        renderCards();
        renderCmd();
      }
      setStatus(false);
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', load);
  } else {
    load();
  }
})();
