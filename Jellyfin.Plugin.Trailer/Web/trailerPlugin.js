/**
 * Jellyfin Trailer Plugin — Client Side  v1.1
 *
 * Injects a "Трейлер" button on movie detail pages.
 *
 * Depending on the server-side PlayerMode setting the button either:
 *   modal  — shows a centered overlay with an embedded YouTube iframe (default)
 *   tab    — opens YouTube in a new browser tab (legacy behaviour)
 *
 * Deployment:
 *   1. This file is served by the C# plugin at:
 *      /web/configurationpage?name=trailerPlugin_js
 *   2. Add the loader snippet via the jellyfin-plugin-custom-javascript plugin:
 *      (function(){var s=document.createElement('script');
 *       s.src='/web/configurationpage?name=trailerPlugin_js';
 *       document.head.appendChild(s);})();
 */
(function () {
  'use strict';

  // ── Constants ──────────────────────────────────────────────────────────────

  var BUTTON_ID      = 'trailer-plugin-btn';
  var MODAL_ID       = 'trailer-plugin-modal';
  var API_PATH       = '/Trailer/';
  var CONFIG_PATH    = '/Trailer/config';
  var DEBOUNCE_MS    = 400;
  var WAIT_TIMEOUT_MS = 5000;
  var WAIT_POLL_MS   = 150;

  // ── State ──────────────────────────────────────────────────────────────────

  var lastItemId    = null;
  var debounceTimer = null;
  /** 'modal' | 'tab' — loaded from server config once */
  var playerMode    = 'modal';

  // ── URL helpers ──────────────────────────────────────────────────────────

  /** Returns true if the URL points to YouTube. */
  function isYouTubeUrl(url) {
    return /youtube\.com|youtu\.be/i.test(url || '');
  }

  /**
   * Converts any YouTube watch URL to an embed URL.
   *   https://www.youtube.com/watch?v=XXXX  →  https://www.youtube.com/embed/XXXX
   */
  function toEmbedUrl(url) {
    try {
      var match = url.match(/[?&]v=([A-Za-z0-9_-]{11})/);
      if (match) return 'https://www.youtube.com/embed/' + match[1] + '?autoplay=1&rel=0';
    } catch (e) { /* ignore */ }
    return url;
  }

  // ── Auth helper ───────────────────────────────────────────────────────────

  function getAuthHeader() {
    try {
      var token = window.ApiClient && window.ApiClient.accessToken();
      if (!token) return null;
      return 'MediaBrowser Client="Jellyfin Web", Token="' + token + '"';
    } catch (e) {
      return null;
    }
  }

  // ── DOM poller ────────────────────────────────────────────────────────────

  function waitForElement(selector, timeoutMs, cb) {
    var el = document.querySelector(selector);
    if (el) { cb(el); return; }

    var started = Date.now();
    var timer = setInterval(function () {
      el = document.querySelector(selector);
      if (el) {
        clearInterval(timer);
        cb(el);
      } else if (Date.now() - started > timeoutMs) {
        clearInterval(timer);
      }
    }, WAIT_POLL_MS);
  }

  // ── Modal ─────────────────────────────────────────────────────────────────

  function closeModal() {
    var m = document.getElementById(MODAL_ID);
    if (m) {
      // Stop playback before removing
      var iframe = m.querySelector('iframe');
      if (iframe) iframe.src = '';
      var video = m.querySelector('video');
      if (video) { video.pause(); video.src = ''; }
      document.body.removeChild(m);
    }
  }

  function showModal(trailerUrl) {
    closeModal(); // ensure no duplicate

    var youtube = isYouTubeUrl(trailerUrl);

    // ── Overlay backdrop
    var overlay = document.createElement('div');
    overlay.id = MODAL_ID;
    overlay.setAttribute('role', 'dialog');
    overlay.setAttribute('aria-modal', 'true');
    overlay.setAttribute('aria-label', 'Трейлер');
    overlay.style.cssText = [
      'position:fixed', 'inset:0', 'z-index:9999',
      'display:flex', 'align-items:center', 'justify-content:center',
      'background:rgba(0,0,0,0.85)',
      'animation:trailerFadeIn 0.18s ease'
    ].join(';');

    // ── Inner container (responsive 16 : 9)
    var wrap = document.createElement('div');
    wrap.style.cssText = [
      'position:relative',
      'width:min(92vw,960px)',
      'aspect-ratio:16/9',
      'background:#000',
      'border-radius:6px',
      'overflow:hidden',
      'box-shadow:0 8px 40px rgba(0,0,0,0.8)'
    ].join(';');

    // ── Close button
    var closeBtn = document.createElement('button');
    closeBtn.type = 'button';
    closeBtn.setAttribute('aria-label', 'Закрыть трейлер');
    closeBtn.style.cssText = [
      'position:absolute', 'top:8px', 'right:10px',
      'z-index:10', 'background:rgba(0,0,0,0.6)', 'border:none',
      'color:#fff', 'font-size:22px', 'line-height:1',
      'width:36px', 'height:36px', 'border-radius:50%',
      'cursor:pointer', 'display:flex', 'align-items:center', 'justify-content:center'
    ].join(';');
    closeBtn.textContent = '✕';
    closeBtn.addEventListener('click', function (e) {
      e.stopPropagation();
      closeModal();
    });

    // ── Media element: YouTube iframe OR HTML5 video (proxied through server)
    var media;
    if (youtube) {
      media = document.createElement('iframe');
      media.src = toEmbedUrl(trailerUrl);
      media.allow = 'autoplay; fullscreen; encrypted-media; picture-in-picture';
      media.setAttribute('allowfullscreen', '');
      media.setAttribute('frameborder', '0');
      media.style.cssText = 'width:100%;height:100%;display:block;border:0;';
      media.title = 'Трейлер';
    } else {
      // Direct video URL — proxy through Jellyfin server to avoid browser CORS/access issues
      var proxyUrl = '/Trailer/proxy?url=' + encodeURIComponent(trailerUrl);
      var auth = getAuthHeader();

      media = document.createElement('video');
      media.controls = true;
      media.autoplay = true;
      media.style.cssText = 'width:100%;height:100%;display:block;object-fit:contain;';
      media.title = 'Трейлер';

      // Fetch via proxy with auth, create blob URL for <video>
      fetch(proxyUrl, { headers: auth ? { 'Authorization': auth } : {} })
        .then(function (r) {
          if (!r.ok) throw new Error('Proxy HTTP ' + r.status);
          return r.blob();
        })
        .then(function (blob) {
          media.src = URL.createObjectURL(blob);
        })
        .catch(function (err) {
          console.error('[TrailerPlugin] Proxy fetch error:', err);
          // Show error message in modal
          media.style.display = 'none';
          var errMsg = document.createElement('div');
          errMsg.style.cssText = 'color:#fff;text-align:center;padding:40px;font-size:16px;';
          errMsg.textContent = 'Не удалось загрузить трейлер';
          wrap.appendChild(errMsg);
        });
    }

    wrap.appendChild(closeBtn);
    wrap.appendChild(media);
    overlay.appendChild(wrap);

    // Close on backdrop click (outside the video box)
    overlay.addEventListener('click', function (e) {
      if (e.target === overlay) closeModal();
    });

    // Close on Escape key
    document.addEventListener('keydown', onEscKey);

    document.body.appendChild(overlay);
  }

  function onEscKey(e) {
    if (e.key === 'Escape' || e.keyCode === 27) {
      document.removeEventListener('keydown', onEscKey);
      closeModal();
    }
  }

  // ── Inject fade-in keyframe once ──────────────────────────────────────────

  (function injectStyles() {
    if (document.getElementById('trailer-plugin-styles')) return;
    var style = document.createElement('style');
    style.id = 'trailer-plugin-styles';
    style.textContent =
      '@keyframes trailerFadeIn{from{opacity:0}to{opacity:1}}';
    document.head.appendChild(style);
  })();

  // ── Button ────────────────────────────────────────────────────────────────

  function removeExistingButton() {
    var old = document.getElementById(BUTTON_ID);
    if (old && old.parentNode) old.parentNode.removeChild(old);
  }

  function createButton(trailerUrl, mode) {
    var btn = document.createElement('button');
    btn.id = BUTTON_ID;
    btn.type = 'button';
    btn.className = 'emby-button raised';
    btn.setAttribute('title', mode === 'tab'
      ? 'Открыть трейлер на YouTube'
      : 'Смотреть трейлер');
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
      '<span>Трейлер</span>';

    btn.addEventListener('click', function (e) {
      e.preventDefault();
      e.stopPropagation();
      if (mode === 'tab') {
        window.open(trailerUrl, '_blank', 'noopener,noreferrer');
      } else {
        showModal(trailerUrl);
      }
    });

    return btn;
  }

  function injectButton(trailerUrl, mode) {
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
      console.warn('[TrailerPlugin] Could not find action button container. Selectors tried:', selectors);
      return;
    }

    var btn = createButton(trailerUrl, mode);
    var firstBtn = container.querySelector('button, .emby-button');
    if (firstBtn) {
      container.insertBefore(btn, firstBtn);
    } else {
      container.appendChild(btn);
    }
  }

  // ── API calls ─────────────────────────────────────────────────────────────

  /** Load player mode from server config (once per session). */
  function loadConfig(cb) {
    var auth = getAuthHeader();
    if (!auth) { cb(); return; }

    fetch(CONFIG_PATH, { headers: { 'Authorization': auth } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (cfg) {
        if (cfg && cfg.playerMode) playerMode = cfg.playerMode;
        cb();
      })
      .catch(function () { cb(); });
  }

  /** Fetch trailer URL from backend then inject button. */
  function fetchAndInject(itemId) {
    removeExistingButton();

    var auth = getAuthHeader();
    if (!auth) {
      console.warn('[TrailerPlugin] No Jellyfin auth token — cannot call API');
      return;
    }

    fetch(API_PATH + itemId, { headers: { 'Authorization': auth } })
      .then(function (resp) {
        if (!resp.ok) throw new Error('HTTP ' + resp.status);
        return resp.json();
      })
      .then(function (data) {
        if (data && data.found && data.trailerUrl) {
          console.log('[TrailerPlugin] Trailer found:', data.source, playerMode, data.trailerUrl);
          injectButton(data.trailerUrl, playerMode);
        }
      })
      .catch(function (err) {
        console.error('[TrailerPlugin] API error:', err);
      });
  }

  // ── Navigation detection ──────────────────────────────────────────────────

  function getItemIdFromHash() {
    var hash = window.location.hash;
    var match = hash.match(/[?&]id=([a-f0-9-]{32,36})/i);
    return match ? match[1] : null;
  }

  function isDetailPage() {
    return /\/(details|item)(\?|$)/.test(window.location.hash);
  }

  function onMaybeNavigated() {
    // Close any open trailer modal when leaving a detail page
    if (!isDetailPage()) {
      closeModal();
      removeExistingButton();
      lastItemId = null;
      return;
    }

    var itemId = getItemIdFromHash();
    if (!itemId || itemId === lastItemId) return;
    lastItemId = itemId;

    // Try multiple selectors — different Jellyfin versions use different class names
    var containerSel = [
      '.mainDetailButtons',
      '.itemActions',
      '.detailPageContent .itemActions',
      '.mainDetailButtons.focuscontainer-x'
    ].find(function (s) { return !!document.querySelector(s); }) || '.mainDetailButtons';

    waitForElement(containerSel, WAIT_TIMEOUT_MS, function () {
      if (getItemIdFromHash() === itemId) {
        fetchAndInject(itemId);
      }
    });
  }

  function debounced(fn) {
    return function () {
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(fn, DEBOUNCE_MS);
    };
  }

  // ── Bootstrap ────────────────────────────────────────────────────────────

  // Load player-mode config from server, then start listening
  loadConfig(function () {
    var debouncedCheck = debounced(onMaybeNavigated);

    window.addEventListener('hashchange', debouncedCheck);

    var observer = new MutationObserver(debouncedCheck);
    observer.observe(document.body, {
      childList: true, subtree: true,
      attributes: false, characterData: false
    });

    onMaybeNavigated();

    console.log('[TrailerPlugin] Loaded v1.5 — mode=' + playerMode);
  });

})();
