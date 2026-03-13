/**
 * Jellyfin Trailer Plugin — Client Side  v2.0
 *
 * Injects a "Трейлер" button on movie detail pages.
 * Clicking the button opens a modal with YouTube search results.
 * User picks a trailer from the list → it plays inside the modal.
 *
 * Flow:
 *   1. Button click → GET /Trailer/{itemId}/search → list of YouTube results
 *   2. Modal shows scrollable list with thumbnails, titles, channel names
 *   3. User clicks a result → try YouTube embed in same modal
 *   4. If embed fails → "Открыть на YouTube" fallback link
 */
(function () {
  'use strict';

  // ── Constants ──────────────────────────────────────────────────────────────

  var BUTTON_ID       = 'trailer-plugin-btn';
  var MODAL_ID        = 'trailer-plugin-modal';
  var API_PATH        = '/Trailer/';
  var DEBOUNCE_MS     = 400;
  var WAIT_TIMEOUT_MS = 5000;
  var WAIT_POLL_MS    = 150;

  // ── State ──────────────────────────────────────────────────────────────────

  var lastItemId    = null;
  var debounceTimer = null;

  // ── URL helpers ────────────────────────────────────────────────────────────

  function getYouTubeVideoId(url) {
    try {
      var match = url.match(/[?&]v=([A-Za-z0-9_-]{11})/)
              || url.match(/youtu\.be\/([A-Za-z0-9_-]{11})/);
      return match ? match[1] : null;
    } catch (e) { return null; }
  }

  /**
   * Build embed URL for a YouTube video.
   * Tries multiple embed hosts in case some are blocked:
   *   1. youtube-nocookie.com (default, privacy-friendly)
   *   2. youtube.com (standard)
   *   3. piped.video (open-source frontend, usually not blocked)
   */
  var EMBED_HOSTS = [
    'https://www.youtube-nocookie.com/embed/',
    'https://www.youtube.com/embed/',
    'https://piped.video/embed/'
  ];

  function toEmbedUrl(videoId, hostIndex) {
    var idx = hostIndex || 0;
    if (idx >= EMBED_HOSTS.length) idx = 0;
    var origin = window.location.protocol + '//' + window.location.host;
    return EMBED_HOSTS[idx] + videoId + '?autoplay=1&rel=0&origin=' + encodeURIComponent(origin);
  }

  /** Format ISO date → "12 Декабря 2014" */
  function formatDate(isoStr) {
    if (!isoStr) return '';
    try {
      var d = new Date(isoStr);
      var months = ['Января','Февраля','Марта','Апреля','Мая','Июня',
                    'Июля','Августа','Сентября','Октября','Ноября','Декабря'];
      return d.getDate() + ' ' + months[d.getMonth()] + ' ' + d.getFullYear();
    } catch (e) { return ''; }
  }

  // ── Auth helper ────────────────────────────────────────────────────────────

  function getAuthHeader() {
    try {
      var token = window.ApiClient && window.ApiClient.accessToken();
      if (!token) return null;
      return 'MediaBrowser Client="Jellyfin Web", Token="' + token + '"';
    } catch (e) { return null; }
  }

  // ── DOM poller ─────────────────────────────────────────────────────────────

  function waitForElement(selector, timeoutMs, cb) {
    var el = document.querySelector(selector);
    if (el) { cb(el); return; }
    var started = Date.now();
    var timer = setInterval(function () {
      el = document.querySelector(selector);
      if (el) { clearInterval(timer); cb(el); }
      else if (Date.now() - started > timeoutMs) { clearInterval(timer); }
    }, WAIT_POLL_MS);
  }

  // ── Inject styles once ─────────────────────────────────────────────────────

  (function injectStyles() {
    if (document.getElementById('trailer-plugin-styles')) return;
    var style = document.createElement('style');
    style.id = 'trailer-plugin-styles';
    style.textContent = [
      '@keyframes trailerFadeIn{from{opacity:0}to{opacity:1}}',
      '@keyframes trailerSlideIn{from{opacity:0;transform:translateY(20px)}to{opacity:1;transform:translateY(0)}}',
      '.trailer-result-item{display:flex;gap:12px;padding:10px 14px;cursor:pointer;border-radius:6px;transition:background 0.15s;align-items:flex-start}',
      '.trailer-result-item:hover{background:rgba(255,255,255,0.1)}',
      '.trailer-result-item.active{background:rgba(255,255,255,0.15)}',
      '.trailer-result-thumb{width:168px;min-width:168px;height:94px;border-radius:4px;object-fit:cover;background:#222}',
      '.trailer-result-info{flex:1;min-width:0;display:flex;flex-direction:column;gap:4px}',
      '.trailer-result-title{color:#fff;font-size:14px;font-weight:500;line-height:1.3;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden}',
      '.trailer-result-channel{color:#aaa;font-size:12px}',
      '.trailer-result-date{color:#888;font-size:11px}',
      '.trailer-modal-header{display:flex;align-items:center;justify-content:space-between;padding:16px 20px 12px;border-bottom:1px solid rgba(255,255,255,0.1)}',
      '.trailer-modal-title{color:#fff;font-size:18px;font-weight:600;margin:0}',
      '.trailer-modal-close{background:none;border:none;color:#aaa;font-size:24px;cursor:pointer;padding:4px 8px;border-radius:4px;line-height:1}',
      '.trailer-modal-close:hover{color:#fff;background:rgba(255,255,255,0.1)}',
      '.trailer-modal-list{overflow-y:auto;max-height:calc(80vh - 60px);padding:8px 6px}',
      '.trailer-modal-loading{color:#aaa;text-align:center;padding:40px;font-size:15px}',
      '.trailer-modal-empty{color:#888;text-align:center;padding:40px;font-size:15px}',
      '.trailer-player-wrap{position:relative;width:100%;aspect-ratio:16/9;background:#000}',
      '.trailer-player-back{display:flex;align-items:center;gap:6px;background:none;border:none;color:#aaa;font-size:13px;cursor:pointer;padding:10px 16px}',
      '.trailer-player-back:hover{color:#fff}',
      '.trailer-player-fallback{text-align:center;padding:8px;font-size:13px}',
      '.trailer-player-fallback a{color:#aaa;text-decoration:underline}'
    ].join('\n');
    document.head.appendChild(style);
  })();

  // ── Modal ──────────────────────────────────────────────────────────────────

  function closeModal() {
    var m = document.getElementById(MODAL_ID);
    if (m) {
      var iframe = m.querySelector('iframe');
      if (iframe) iframe.src = '';
      document.body.removeChild(m);
    }
    document.removeEventListener('keydown', onEscKey);
  }

  function onEscKey(e) {
    if (e.key === 'Escape' || e.keyCode === 27) closeModal();
  }

  /** Show the modal with a loading spinner, then fetch results */
  function showSearchModal(itemId) {
    closeModal();

    // ── Overlay
    var overlay = document.createElement('div');
    overlay.id = MODAL_ID;
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', 'YouTube — Трейлеры');
    overlay.style.cssText = [
      'position:fixed', 'inset:0', 'z-index:9999',
      'display:flex', 'align-items:center', 'justify-content:center',
      'background:rgba(0,0,0,0.88)',
      'animation:trailerFadeIn 0.18s ease'
    ].join(';');

    // ── Panel
    var panel = document.createElement('div');
    panel.style.cssText = [
      'width:min(94vw,540px)', 'max-height:85vh',
      'background:#1a1a1a', 'border-radius:10px',
      'display:flex', 'flex-direction:column',
      'overflow:hidden',
      'box-shadow:0 12px 50px rgba(0,0,0,0.8)',
      'animation:trailerSlideIn 0.22s ease'
    ].join(';');

    // ── Header
    var header = document.createElement('div');
    header.className = 'trailer-modal-header';
    var titleEl = document.createElement('h2');
    titleEl.className = 'trailer-modal-title';
    titleEl.textContent = 'YouTube — Трейлеры';
    var closeBtn = document.createElement('button');
    closeBtn.className = 'trailer-modal-close';
    closeBtn.type = 'button';
    closeBtn.setAttribute('aria-label', 'Закрыть');
    closeBtn.textContent = '✕';
    closeBtn.addEventListener('click', function (e) { e.stopPropagation(); closeModal(); });
    header.appendChild(titleEl);
    header.appendChild(closeBtn);

    // ── Content area (list or player)
    var content = document.createElement('div');
    content.className = 'trailer-modal-list';

    // Loading state
    var loading = document.createElement('div');
    loading.className = 'trailer-modal-loading';
    loading.textContent = 'Поиск трейлеров...';
    content.appendChild(loading);

    panel.appendChild(header);
    panel.appendChild(content);
    overlay.appendChild(panel);

    overlay.addEventListener('click', function (e) {
      if (e.target === overlay) closeModal();
    });
    document.addEventListener('keydown', onEscKey);
    document.body.appendChild(overlay);

    // ── Fetch search results
    var auth = getAuthHeader();
    if (!auth) {
      loading.textContent = 'Нет токена авторизации';
      return;
    }

    fetch(API_PATH + itemId + '/search', { headers: { 'Authorization': auth } })
      .then(function (r) {
        if (!r.ok) throw new Error('HTTP ' + r.status);
        return r.json();
      })
      .then(function (items) {
        content.innerHTML = '';
        if (!items || items.length === 0) {
          var empty = document.createElement('div');
          empty.className = 'trailer-modal-empty';
          empty.textContent = 'Трейлеры не найдены';
          content.appendChild(empty);
          return;
        }
        renderResultList(content, panel, header, items);
      })
      .catch(function (err) {
        console.error('[TrailerPlugin] Search error:', err);
        content.innerHTML = '';
        var errEl = document.createElement('div');
        errEl.className = 'trailer-modal-empty';
        errEl.textContent = 'Ошибка поиска: ' + err.message;
        content.appendChild(errEl);
      });
  }

  /** Render list of YouTube search results in the content area */
  function renderResultList(content, panel, header, items) {
    items.forEach(function (item, idx) {
      var row = document.createElement('div');
      row.className = 'trailer-result-item';
      row.setAttribute('role', 'button');
      row.setAttribute('tabindex', '0');

      // Thumbnail — use proxied URL (client may not reach i.ytimg.com directly)
      var thumb = document.createElement('img');
      thumb.className = 'trailer-result-thumb';
      thumb.src = item.proxyThumbnailUrl || item.thumbnailUrl || '';
      thumb.alt = item.title || 'Трейлер';
      thumb.loading = idx < 3 ? 'eager' : 'lazy';

      // Info block
      var info = document.createElement('div');
      info.className = 'trailer-result-info';

      var title = document.createElement('div');
      title.className = 'trailer-result-title';
      title.textContent = item.title || 'Без названия';

      var channel = document.createElement('div');
      channel.className = 'trailer-result-channel';
      channel.textContent = item.channelTitle || '';

      var date = document.createElement('div');
      date.className = 'trailer-result-date';
      date.textContent = formatDate(item.publishedAt);

      info.appendChild(title);
      info.appendChild(channel);
      info.appendChild(date);

      row.appendChild(thumb);
      row.appendChild(info);

      row.addEventListener('click', function () {
        showPlayer(content, panel, header, items, item);
      });
      row.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') showPlayer(content, panel, header, items, item);
      });

      content.appendChild(row);
    });
  }

  /** Switch the modal content to the YouTube player for the selected video */
  function showPlayer(contentArea, panel, header, allItems, selectedItem) {
    // Widen the panel for the player
    panel.style.width = 'min(94vw,960px)';
    panel.style.maxHeight = '95vh';

    contentArea.innerHTML = '';
    contentArea.className = '';
    contentArea.style.cssText = 'overflow-y:auto;';

    // Back button
    var backBtn = document.createElement('button');
    backBtn.className = 'trailer-player-back';
    backBtn.type = 'button';
    backBtn.innerHTML = '&#8592; Назад к списку';
    backBtn.addEventListener('click', function () {
      // Reset panel width
      panel.style.width = 'min(94vw,540px)';
      panel.style.maxHeight = '85vh';
      contentArea.innerHTML = '';
      contentArea.className = 'trailer-modal-list';
      contentArea.style.cssText = '';
      renderResultList(contentArea, panel, header, allItems);
    });

    // Player container
    var playerWrap = document.createElement('div');
    playerWrap.className = 'trailer-player-wrap';

    var currentHostIdx = 0;

    function createIframe(hostIdx) {
      var iframe = document.createElement('iframe');
      iframe.src = toEmbedUrl(selectedItem.videoId, hostIdx);
      iframe.allow = 'autoplay; fullscreen; encrypted-media; picture-in-picture';
      iframe.setAttribute('frameborder', '0');
      iframe.style.cssText = 'width:100%;height:100%;display:block;border:0;';
      iframe.title = selectedItem.title || 'Трейлер';

      // If embed fails to load (DNS blocked), try the next host
      iframe.addEventListener('error', function () {
        if (currentHostIdx < EMBED_HOSTS.length - 1) {
          currentHostIdx++;
          playerWrap.innerHTML = '';
          playerWrap.appendChild(createIframe(currentHostIdx));
        }
      });

      return iframe;
    }

    playerWrap.appendChild(createIframe(0));

    // Fallback link
    var fallback = document.createElement('div');
    fallback.className = 'trailer-player-fallback';
    var link = document.createElement('a');
    link.href = selectedItem.videoUrl || ('https://www.youtube.com/watch?v=' + selectedItem.videoId);
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    link.textContent = 'Открыть на YouTube \u2197';
    fallback.appendChild(link);

    // Also add a piped.video link as additional fallback
    var pipedLink = document.createElement('a');
    pipedLink.href = 'https://piped.video/watch?v=' + selectedItem.videoId;
    pipedLink.target = '_blank';
    pipedLink.rel = 'noopener noreferrer';
    pipedLink.style.marginLeft = '16px';
    pipedLink.textContent = 'Piped.video \u2197';
    fallback.appendChild(pipedLink);

    // Now playing title
    var nowPlaying = document.createElement('div');
    nowPlaying.style.cssText = 'padding:10px 16px 4px;color:#fff;font-size:14px;font-weight:500;';
    nowPlaying.textContent = selectedItem.title || '';

    var channelInfo = document.createElement('div');
    channelInfo.style.cssText = 'padding:0 16px 10px;color:#888;font-size:12px;';
    channelInfo.textContent = [selectedItem.channelTitle, formatDate(selectedItem.publishedAt)].filter(Boolean).join(' \u2022 ');

    contentArea.appendChild(backBtn);
    contentArea.appendChild(playerWrap);
    contentArea.appendChild(nowPlaying);
    contentArea.appendChild(channelInfo);
    contentArea.appendChild(fallback);
  }

  // ── Button ─────────────────────────────────────────────────────────────────

  function removeExistingButton() {
    var old = document.getElementById(BUTTON_ID);
    if (old && old.parentNode) old.parentNode.removeChild(old);
  }

  function createButton(itemId) {
    var btn = document.createElement('button');
    btn.id = BUTTON_ID;
    btn.type = 'button';
    btn.className = 'emby-button raised';
    btn.setAttribute('title', 'Смотреть трейлер');
    btn.style.cssText = [
      'background-color:#c0392b', 'color:#fff',
      'margin:0 8px 0 0', 'padding:0 16px', 'height:36px',
      'display:inline-flex', 'align-items:center', 'gap:6px',
      'font-size:0.875em', 'letter-spacing:0.02em',
      'cursor:pointer', 'border:none', 'border-radius:3px',
      'vertical-align:middle'
    ].join(';');

    btn.innerHTML =
      '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">' +
        '<path d="M8 5v14l11-7z"/>' +
      '</svg>' +
      '<span>\u0422\u0440\u0435\u0439\u043B\u0435\u0440</span>';

    btn.addEventListener('click', function (e) {
      e.preventDefault();
      e.stopPropagation();
      showSearchModal(itemId);
    });

    return btn;
  }

  function injectButton(itemId) {
    removeExistingButton();

    var selectors = [
      '.mainDetailButtons',
      '.mainDetailButtons.focuscontainer-x',
      '.itemDetailPage .itemActions',
      '#itemDetailPage .itemActions',
      '.detailPageContent .itemActions',
      '.detailRibbonButtons',
      '.itemDetailPage .detailSectionContent .itemActions'
    ];

    var container = null;
    for (var i = 0; i < selectors.length; i++) {
      container = document.querySelector(selectors[i]);
      if (container) break;
    }

    if (!container) {
      console.warn('[TrailerPlugin] Could not find button container');
      return;
    }

    var btn = createButton(itemId);
    var firstBtn = container.querySelector('button, .emby-button');
    if (firstBtn) {
      container.insertBefore(btn, firstBtn);
    } else {
      container.appendChild(btn);
    }
  }

  // ── Check if YouTube API key configured (quick check via search endpoint) ──

  /** Check if search returns results — if yes, show button */
  function checkAndInject(itemId) {
    removeExistingButton();

    var auth = getAuthHeader();
    if (!auth) return;

    // Always show the button — the search will be done when clicked
    // We just need to verify the item exists and YouTube key is configured
    // A lightweight check: just inject the button, search on click
    injectButton(itemId);
  }

  // ── Navigation detection ───────────────────────────────────────────────────

  function getItemIdFromHash() {
    var hash = window.location.hash;
    var match = hash.match(/[?&]id=([a-f0-9-]{32,36})/i);
    return match ? match[1] : null;
  }

  function isDetailPage() {
    return /\/(details|item)(\?|$)/.test(window.location.hash);
  }

  function onMaybeNavigated() {
    if (!isDetailPage()) {
      closeModal();
      removeExistingButton();
      lastItemId = null;
      return;
    }

    var itemId = getItemIdFromHash();
    if (!itemId || itemId === lastItemId) return;
    lastItemId = itemId;

    var containerSel = [
      '.mainDetailButtons',
      '.itemActions',
      '.detailPageContent .itemActions',
      '.mainDetailButtons.focuscontainer-x'
    ].find(function (s) { return !!document.querySelector(s); }) || '.mainDetailButtons';

    waitForElement(containerSel, WAIT_TIMEOUT_MS, function () {
      if (getItemIdFromHash() === itemId) {
        checkAndInject(itemId);
      }
    });
  }

  function debounced(fn) {
    return function () {
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(fn, DEBOUNCE_MS);
    };
  }

  // ── Bootstrap ──────────────────────────────────────────────────────────────

  var debouncedCheck = debounced(onMaybeNavigated);

  window.addEventListener('hashchange', debouncedCheck);

  var observer = new MutationObserver(debouncedCheck);
  observer.observe(document.body, {
    childList: true, subtree: true,
    attributes: false, characterData: false
  });

  onMaybeNavigated();

  console.log('[TrailerPlugin] Loaded v2.1 — proxied thumbnails + multi-host embed');

})();
