// BooksMetadataBaker — UI logic (v2)
// ---------------------------------------------------------------------
// Responsibilities:
// - Book details state (title, type segmented picker, API key, theme)
// - File queue with whole-window drag & drop, dedupe, 500 MB client check
// - Per-file progress via XMLHttpRequest (upload % -> indeterminate bake)
// - Concurrency queue (up to 4 parallel), cancel, retry failed, clear finished
// - Server rate-limit (429) auto-retry with Retry-After backoff + countdown
// - Applied-metadata rendering (summary card + per-file expandable details)
  // - UI localization (en/ua via vue-i18n; API data & errors stay as-is)
// - Toasts, keyboard shortcut (Ctrl+Enter), screen-reader status line
// ---------------------------------------------------------------------

(function () {
  const { createApp, reactive, ref, computed, onMounted, onBeforeUnmount } = Vue;
  const { createI18n, useI18n } = VueI18n;

  const MAX_FILE_SIZE = 500 * 1024 * 1024;      // must match server RequestSizeLimit
  const MAX_CONCURRENT = 4;
  const XHR_TIMEOUT_MS = 30 * 60 * 1000;        // safety net so the UI never hangs forever
  const THROTTLE_MAX_RETRIES = 3;
  const THROTTLE_DEFAULT_WAIT_S = 30;
  const LS = { apiKey: 'bmb.apiKey', type: 'bmb.type', theme: 'bmb.theme', locale: 'bmb.locale' };
  const ALLOWED_EXTS = ['pdf', 'epub', 'cbz', 'cbr', 'cb7', 'cbt', 'zip', 'rar', '7z', 'tar'];
  const COMIC_EXTS = ['cbz', 'cbr', 'cb7', 'cbt', 'zip', 'rar', '7z', 'tar'];
  // Raw containers the server converts to their Kavita equivalent (P9):
  // the predicted saved name shows the *output* extension.
  const KAVITA_EXT = { zip: 'cbz', '7z': 'cb7', rar: 'cbr', tar: 'cbt' };

  // `value` is the server-facing type (also the i18n key under `types.*`);
  // the display label is localized in the template via $t('types.' + value).
  const TYPES = [
    {
      value: 'Book',
      icon: '<svg viewBox="0 0 24 24"><path d="M12 6c-1.5-1.6-3.7-2.5-6-2.5H3v15h3c2.3 0 4.5.9 6 2.5 1.5-1.6 3.7-2.5 6-2.5h3v-15h-3c-2.3 0-4.5.9-6 2.5z"/><path d="M12 6v15"/></svg>'
    },
    {
      value: 'LightNovel',
      icon: '<svg viewBox="0 0 24 24"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V4H6.5A2.5 2.5 0 0 0 4 6.5v13z"/><path d="M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5"/><path d="M9 8h7M9 11h5"/></svg>'
    },
    {
      value: 'Manga',
      icon: '<svg viewBox="0 0 24 24"><path d="M21 11.5a8.5 8.5 0 0 1-8.5 8.5c-1.5 0-3-.4-4.2-1.1L3 20l1.1-5.3A8.5 8.5 0 1 1 21 11.5z"/><path d="M8.5 10.5h.01M12 10.5h.01M15.5 10.5h.01"/></svg>'
    },
    {
      value: 'Comic',
      icon: '<svg viewBox="0 0 24 24"><path d="M13 2 4 14h6l-1 8 9-12h-6l1-8z"/></svg>'
    }
  ];

  const META_ORDER = [
    'Title', 'TitleRomaji', 'TitleEnglish', 'TitleNative', 'Subtitle',
    'Authors', 'Publisher', 'PublishedDate', 'StartDate', 'EndDate', 'StartYear',
    'Genres', 'Categories', 'Tags', 'Format', 'Status',
    'Volumes', 'Chapters', 'PageCount', 'IssueCount', 'AverageScore', 'Language',
    'Description', 'Snippet', 'Source', 'SourceUrl', 'ApiDetailUrl'
  ];

  // -------------------------------------------------------------------
  // Helpers
  // -------------------------------------------------------------------
  function lsGet(key) { try { return localStorage.getItem(key) || ''; } catch { return ''; } }
  function lsSet(key, value) {
    try {
      if (value === null || value === '') localStorage.removeItem(key);
      else localStorage.setItem(key, value);
    } catch { /* private mode */ }
  }

  function pick(obj, ...keys) {
    for (const k of keys) {
      if (obj && obj[k] !== undefined && obj[k] !== null) return obj[k];
    }
    return undefined;
  }

  function formatSize(bytes) {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / 1048576).toFixed(2) + ' MB';
  }

  function formatElapsed(ms) {
    const s = Math.max(0, Math.floor(ms / 1000));
    const m = Math.floor(s / 60);
    return m + ':' + String(s % 60).padStart(2, '0');
  }

  function extOf(name) {
    const i = name.lastIndexOf('.');
    return i === -1 ? '' : name.slice(i + 1).toLowerCase();
  }

  // Normalize a parsed number like the server's NormalizeNumber:
  // "007" -> "7", "034.5" -> "34.5".
  function normNum(num) {
    const s = String(num);
    if (s.includes('.')) {
      const d = Number(s);
      return Number.isFinite(d) ? String(d) : s;
    }
    const i = parseInt(s, 10);
    return Number.isFinite(i) ? String(i) : s;
  }

  // Simple mirror of the server's ComicFilenameParser + GetArchiveSavePath for
  // comic archives (preview only — the server's saved name always wins and is
  // shown in the result row): vol marker -> "Title - Volume N", ch marker /
  // SP marker / bare trailing number -> chapter or special, else "Specials/Title SP01".
  function predictedComicName(fileName, bookTitle) {
    const ext = extOf(fileName);
    const base = ext ? fileName.slice(0, fileName.length - ext.length - 1) : fileName;
    const t = bookTitle || 'Title';
    const outExt = KAVITA_EXT[ext] || ext;
    const suffix = outExt ? '.' + outExt : '';
    const name = base.replace(/\s*\[[^\]]*\]/g, ''); // drop scanlation brackets

    const vol = name.match(/(?<![A-Za-z])(?:volume|vol\.?|tome|[vt])\s*\.?\s*(\d{1,4}(?:\.\d+)?)/i);
    if (vol) return t + ' - Volume ' + normNum(vol[1]) + suffix;

    const ch = name.match(/(?<![A-Za-z])(?:chapter|chp\.?|ch\.?|episode|ep|c)\s*\.?\s*(\d{1,4}(?:\.\d+)?)(b)?/i);
    if (ch) return t + ' - Chapter ' + normNum(ch[2] === 'b' ? Number(ch[1]) + 0.5 : ch[1]) + suffix;

    if (/(?<![A-Za-z])SP\s*\d{1,3}\b/i.test(name)) return 'Specials/' + t + ' SP01' + suffix;

    // Bare trailing number; volume markers and parenthesized groups stripped first,
    // 4-digit years (1900-2099) are not chapters.
    const residual = name
      .replace(/(?<![A-Za-z])(?:volume|vol\.?|tome|[vt])\s*\.?\s*\d{1,4}(?:\.\d+)?/gi, '')
      .replace(/\([^)]*\)/g, '');
    const trailing = residual.match(/(\d{1,4}(?:\.\d+)?)(b?)\s*$/);
    if (trailing) {
      const raw = trailing[1];
      const isYear = raw.length === 4 && Number(raw) >= 1900 && Number(raw) <= 2099;
      if (!isYear) {
        return t + ' - Chapter ' + normNum(trailing[2] === 'b' ? Number(raw) + 0.5 : raw) + suffix;
      }
    }

    return 'Specials/' + t + ' SP01' + suffix;
  }

  // Mirrors the server's naming (preview only — the server's saved name always
  // wins and is shown in the result row):
  // - PDF/EPUB: GetUniqueEBookPath/BuildVolumeName — first number -> "Title - Volume N",
  //   else just "Title".
  // - comic archives: predictedComicName (see above).
  function predictedName(fileName, bookTitle) {
    const ext = extOf(fileName);
    if (COMIC_EXTS.includes(ext)) return predictedComicName(fileName, bookTitle);
    const base = ext ? fileName.slice(0, fileName.length - ext.length - 1) : fileName;
    const m = base.match(/\d+(?:\.\d+)?/);
    const t = bookTitle || 'Title';
    if (!m) return t + (ext ? '.' + ext : '');
    return t + ' - Volume ' + normNum(m[0]) + (ext ? '.' + ext : '');
  }

  function prettyMeta(t, meta) {
    if (!meta) return [];
    const keys = Object.keys(meta);
    const ordered = META_ORDER.filter(k => keys.includes(k) && meta[k]);
    const rest = keys.filter(k => !META_ORDER.includes(k) && meta[k]);
    // Labels are localized UI chrome; the values come from the API untouched.
    return [...ordered, ...rest].map(k => ({ key: k, label: t('metaLabels.' + k, k), value: meta[k] }));
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function linkHtml(url) {
    const u = String(url || '');
    const safe = escapeHtml(u);
    return '<a href="' + safe + '" target="_blank" rel="noopener">' + safe + '</a>';
  }

  function statusLabel(t, s) { return t('status.' + s, s); }

  // Error display: a terse headline (first line) is always shown next to the
  // failed upload; the full multi-line detail (tool stderr, exit codes) is
  // revealed on demand so a large stderr dump doesn't flood the queue row.
  function errorHeadline(text) {
    if (!text) return '';
    const first = String(text).split(/\r?\n/)[0].trim();
    return first.length > 120 ? first.slice(0, 117) + '…' : first;
  }
  function errorIsDetailed(text) {
    if (!text) return false;
    const s = String(text);
    return s.includes('\n') || s.includes('\r') || s.length > 110;
  }

  // -------------------------------------------------------------------
  // Localization (vue-i18n; catalogs live in /js/i18n.js)
  // -------------------------------------------------------------------
  function detectLocale() {
    const saved = lsGet(LS.locale);
    if (BMB_I18N.locales.includes(saved)) return saved;
    if (saved === 'uk') return 'ua'; // legacy value from before the uk → ua rename
    try {
      const nav = (navigator.language || 'en').toLowerCase();
      // BCP-47 subtag for Ukrainian is 'uk' (ua is the deprecated ISO code)
      if (nav.startsWith('uk')) return 'ua';
      for (const code of BMB_I18N.locales) {
        if (code !== 'ua' && nav.startsWith(code)) return code;
      }
    } catch { /* no navigator (tests) — fall through */ }
    return 'en';
  }
  const i18n = createI18n({
    legacy: false,
    locale: detectLocale(),
    fallbackLocale: 'en',
    messages: BMB_I18N.messages,
    pluralRules: BMB_I18N.pluralRules
  });

  // -------------------------------------------------------------------
  // App
  // -------------------------------------------------------------------
  const app = createApp({
    setup() {
      const { t, locale } = useI18n();

      // -------------------------------------------------------------
      // Reactive state
      // -------------------------------------------------------------
      const title = ref('');
      const titleTouched = ref(false);
      const titleInput = ref(null);
      const type = ref(TYPES.some(t => t.value === lsGet(LS.type)) ? lsGet(LS.type) : 'LightNovel');
      const apiKey = ref(lsGet(LS.apiKey));
      const authRequired = ref(true); // safe fallback until the probe says otherwise
      const theme = ref(['light', 'dark', 'auto'].includes(lsGet(LS.theme)) ? lsGet(LS.theme) : 'light');

      const filesInput = ref(null);
      const pendingFiles = reactive([]); // { uid, file, name, size, ext, progress, status, error, attempts, skip, expanded, details (raw meta), factData, savedName, throttleRetries, retryAt, _xhr }
      const busy = ref(false);
      const windowDrag = ref(false);
      const lastIgnored = ref([]);
      const metadata = ref(null);        // series-level metadata (first non-empty response)
      const metadataTitle = ref('');
      const toasts = reactive([]);
      const now = ref(Date.now());
      const year = new Date().getFullYear();

      let uidSeq = 0;
      let toastSeq = 0;
      let dragDepth = 0;
      let cancelFlag = false;
      let elapsedStart = 0;
      let ticker = null;

      // -------------------------------------------------------------
      // Theme
      // -------------------------------------------------------------
      function applyTheme() {
        document.documentElement.setAttribute('data-theme', theme.value);
      }
      function cycleTheme() {
        const next = theme.value === 'auto' ? 'light' : theme.value === 'light' ? 'dark' : 'auto';
        theme.value = next;
        lsSet(LS.theme, next);
        applyTheme();
      }
      applyTheme();

      // -------------------------------------------------------------
      // Language
      // -------------------------------------------------------------
      function applyLocale() {
        // <html lang> takes the BCP-47 tag: Ukrainian is 'uk', not 'ua'
        document.documentElement.setAttribute('lang', locale.value === 'ua' ? 'uk' : locale.value);
        document.title = t('docTitle');
      }
      function setLocale(code) {
        if (!BMB_I18N.locales.includes(code) || code === locale.value) return;
        locale.value = code;
        lsSet(LS.locale, code);
        applyLocale();
      }
      applyLocale();

      // -------------------------------------------------------------
      // Derived state
      // -------------------------------------------------------------
      const typeLabel = computed(() => t('types.' + type.value, type.value));
      const isComicType = computed(() => type.value === 'Manga' || type.value === 'Comic');
      // Naming hint: preview the server's saved name for a sample file of the
      // selected type (comic types preview the archive naming rules).
      const nameHint = computed(() => {
        if (!title.value) return isComicType.value ? 'Title - Volume N.cbz' : 'Title - Volume N.pdf';
        return predictedName(isComicType.value ? 'sample vol 1.cbz' : 'sample 1.pdf', title.value);
      });
      const canSubmit = computed(() =>
        title.value.trim().length > 0 && pendingFiles.some(f => f.status === 'pending'));
      const pendingCount = computed(() => pendingFiles.filter(f => f.status === 'pending').length);
      const successCount = computed(() => pendingFiles.filter(f => f.status === 'success').length);
      const failCount = computed(() => pendingFiles.filter(f => f.status === 'error').length);
      const runningCount = computed(() => pendingFiles.filter(f =>
        f.status === 'uploading' || f.status === 'processing' || f.status === 'throttled').length);
      const doneCount = computed(() => pendingFiles.filter(f =>
        f.status === 'success' || f.status === 'error' || f.status === 'canceled').length);
      const overallPct = computed(() =>
        pendingFiles.length ? Math.round(doneCount.value / pendingFiles.length * 100) : 0);
      const primaryLabel = computed(() => {
        if (busy.value) return t('actionbar.baking', { done: doneCount.value, total: pendingFiles.length });
        if (pendingCount.value) return t('actionbar.bake', pendingCount.value);
        return t('actionbar.bakeAll');
      });
      const elapsedText = computed(() => busy.value ? formatElapsed(now.value - elapsedStart) : '');
      const prettyMetaList = computed(() => prettyMeta(t, metadata.value));
      const metaSource = computed(() => (metadata.value && metadata.value.Source) || '');
      const srStatus = computed(() => {
        if (!pendingFiles.length) return '';
        return t('sr.status', {
          done: doneCount.value, total: pendingFiles.length,
          ok: successCount.value, fail: failCount.value, run: runningCount.value
        });
      });

      // -------------------------------------------------------------
      // Toasts
      // -------------------------------------------------------------
      function toast(text, kind) {
        const id = ++toastSeq;
        toasts.push({ id, text, kind: kind || 'info' });
        setTimeout(() => {
          const i = toasts.findIndex(t => t.id === id);
          if (i !== -1) toasts.splice(i, 1);
        }, 4500);
      }

      // -------------------------------------------------------------
      // Server config probe (shows the API key field only when enforced)
      // -------------------------------------------------------------
      async function loadServerConfig() {
        try {
          const res = await fetch('/api/config', { cache: 'no-store' });
          if (!res.ok) return;
          const data = await res.json();
          const required = pick(data, 'authRequired', 'AuthRequired');
          authRequired.value = required !== undefined ? !!required : true;
        } catch { /* probe failed — keep the field visible */ }
      }
      loadServerConfig();

      // -------------------------------------------------------------
      // File selection
      // -------------------------------------------------------------
      function onFiles() {
        addFiles(filesInput.value && filesInput.value.files);
        if (filesInput.value) filesInput.value.value = ''; // allow re-selecting the same file
      }

      function addFiles(list) {
        const incoming = Array.from(list || []);
        if (!incoming.length) return;
        lastIgnored.value = [];
        const existing = new Set(pendingFiles.map(f => f.name + '|' + f.size));
        let added = 0;
        for (const f of incoming) {
          const ext = extOf(f.name);
          if (!ALLOWED_EXTS.includes(ext)) {
            if (!lastIgnored.value.includes(f.name)) lastIgnored.value.push(f.name);
            continue;
          }
          const dedupeKey = f.name + '|' + f.size;
          if (existing.has(dedupeKey)) continue;
          existing.add(dedupeKey);

          const item = {
            uid: ++uidSeq,
            file: f,
            name: f.name,
            size: f.size,
            ext,
            progress: 0,
            status: 'pending', // pending | uploading | processing | throttled | success | error | canceled
            error: null,
            errorExpanded: false,
            attempts: 0,
            skip: false,
            expanded: false,
            details: null,
            factData: null,
            savedName: null,
            throttleRetries: 0,
            retryAt: 0,
            _xhr: null
          };
          if (f.size > MAX_FILE_SIZE) {
            item.status = 'error';
            item.skip = true;
            item.error = t('errors.tooLarge');
          }
          pendingFiles.push(item);
          added++;
        }
        if (added) toast(t('toasts.added', added), 'info');
      }

      function removeFile(entry) {
        const i = pendingFiles.indexOf(entry);
        if (i !== -1) pendingFiles.splice(i, 1);
      }

      function hasDetails(entry) {
        return !!(entry.details && Object.keys(entry.details).length);
      }
      function toggleDetails(entry) {
        if (!hasDetails(entry)) return;
        entry.expanded = !entry.expanded;
      }
      function factsOf(entry) {
        const d = entry.factData;
        if (!d) return [];
        const facts = [t('facts.attempts', { n: d.attempts })];
        if (d.comic) {
          // Comic archives skip Calibre/Ghostscript — report the ComicInfo.xml outcome instead.
          facts.push(d.comicInfo ? t('facts.comicInfoYes') : t('facts.comicInfoNo'));
          if (d.pages) facts.push(t('facts.pages', { n: d.pages }));
        } else {
          facts.push(d.direct ? t('facts.directOk') : t('facts.directFail'));
          if (d.repair !== undefined) facts.push(d.repair ? t('facts.repairOk') : t('facts.repairFail'));
          if (d.gs) facts.push(t('facts.gsRan'));
        }
        return facts;
      }

      function toggleError(entry) {
        entry.errorExpanded = !entry.errorExpanded;
      }

      // Whole-window drag & drop (handlers live on #app)
      function dragEnter(e) {
        const types = e.dataTransfer && e.dataTransfer.types;
        if (!types || !Array.from(types).includes('Files')) return;
        dragDepth++;
        windowDrag.value = true;
      }
      function dragLeave() {
        dragDepth = Math.max(0, dragDepth - 1);
        if (!dragDepth) windowDrag.value = false;
      }
      function onDrop(e) {
        e.preventDefault();
        dragDepth = 0;
        windowDrag.value = false;
        addFiles(e.dataTransfer && e.dataTransfer.files);
      }

      // -------------------------------------------------------------
      // Upload queue
      // -------------------------------------------------------------
      function start() {
        if (busy.value) return;
        if (!title.value.trim()) {
          titleTouched.value = true;
          if (titleInput.value) titleInput.value.focus();
          toast(t('toasts.enterTitle'), 'warn');
          return;
        }
        if (!pendingCount.value) {
          toast(t('toasts.addFiles'), 'warn');
          return;
        }
        // A new title means the old metadata card no longer applies
        if (metadata.value && metadataTitle.value !== title.value) {
          metadata.value = null;
          metadataTitle.value = '';
        }

        busy.value = true;
        cancelFlag = false;
        elapsedStart = Date.now();

        const queue = [...pendingFiles];
        let qi = 0;
        let active = 0;

        function pump() {
          if (!cancelFlag) {
            while (active < MAX_CONCURRENT && qi < queue.length) {
              const item = queue[qi++];
              if (item.status !== 'pending') continue;
              item.status = 'uploading';
              active++;
              uploadOne(item).then(() => { active--; pump(); });
            }
          }
          if (active === 0) {
            busy.value = false;
            const failed = failCount.value;
            const ok = successCount.value;
            if (ok && !failed) toast(t('toasts.baked', ok), 'success');
            else if (failed) toast(t('toasts.failed', failed), 'error');
          }
        }

        pump();
      }

      function cancelAll() {
        cancelFlag = true;
        for (const f of pendingFiles) {
          if (f.status === 'uploading' || f.status === 'processing' || f.status === 'throttled') {
            f.status = 'canceled';
            f.error = null;
            if (f._xhr) {
              try { f._xhr.abort(); } catch { /* already closed */ }
            }
          }
        }
        toast(t('toasts.canceled'), 'info');
      }

      function retryFailed() {
        let n = 0;
        for (const f of pendingFiles) {
          if (f.status === 'error' && !f.skip) {
            f.status = 'pending';
            f.progress = 0;
            f.error = null;
            f.errorExpanded = false;
            f.attempts = 0;
            f.throttleRetries = 0;
            f.savedName = null;
            f.details = null;
            f.factData = null;
            n++;
          }
        }
        if (n) start();
      }

      function clearFinished() {
        for (let i = pendingFiles.length - 1; i >= 0; i--) {
          if (pendingFiles[i].status === 'success') pendingFiles.splice(i, 1);
        }
      }

      // -------------------------------------------------------------
      // Single upload (XMLHttpRequest for progress events)
      // -------------------------------------------------------------
      function uploadOne(entry) {
        return new Promise(resolve => {
          let settled = false;
          const finish = () => { if (!settled) { settled = true; resolve(); } };
          attempt(entry, finish);
        });
      }

      function attempt(entry, finish) {
        const fd = new FormData();
        fd.append('Title', title.value);
        fd.append('Type', type.value);
        fd.append('file', entry.file);

        const xhr = new XMLHttpRequest();
        entry._xhr = xhr;
        xhr.open('POST', '/api/upload');
        xhr.timeout = XHR_TIMEOUT_MS;
        if (apiKey.value) xhr.setRequestHeader('X-Api-Key', apiKey.value);

        xhr.upload.onprogress = e => {
          if (!e.lengthComputable) return;
          entry.progress = Math.min(99, Math.round((e.loaded / e.total) * 100));
        };
        xhr.upload.onload = () => {
          // Bytes sent — server is now fetching metadata and running Calibre/Ghostscript
          if (entry.status === 'uploading') entry.status = 'processing';
        };

        xhr.onload = () => {
          // Rate limited: wait (Retry-After or a sane default) and retry automatically
          if (xhr.status === 429 && !cancelFlag && entry.throttleRetries < THROTTLE_MAX_RETRIES) {
            entry.throttleRetries++;
            const wait = retryAfterSeconds(xhr) || THROTTLE_DEFAULT_WAIT_S;
            entry.retryAt = Date.now() + wait * 1000;
            entry.status = 'throttled';
            entry.progress = 0;
            toast(t('toasts.rateLimit', { name: shortName(entry.name), n: wait }), 'warn');
            setTimeout(() => {
              if (cancelFlag || entry.status !== 'throttled') { finish(); return; }
              entry.status = 'uploading';
              attempt(entry, finish);
            }, wait * 1000);
            return; // not finished yet
          }

          if (xhr.status >= 200 && xhr.status < 300) {
            try {
              handleSingleResult(entry, JSON.parse(xhr.responseText));
              if (entry.status === 'success') entry.progress = 100;
            } catch {
              entry.status = 'error';
              entry.error = t('errors.unexpected');
            }
          } else {
            entry.status = 'error';
            entry.error = httpErrorMessage(xhr);
            if (xhr.status === 429) {
              // retries exhausted
              entry.error = t('errors.rateLimit');
            }
          }
          finish();
        };

        xhr.onerror = () => {
          entry.status = 'error';
          entry.error = t('errors.network');
          finish();
        };
        xhr.ontimeout = () => {
          entry.status = 'error';
          entry.error = t('errors.timeout');
          try { xhr.abort(); } catch { /* noop */ }
          finish();
        };
        xhr.onabort = () => {
          if (entry.status === 'uploading' || entry.status === 'processing') {
            entry.status = 'canceled';
            entry.error = null;
          }
          finish();
        };

        xhr.send(fd);
      }

      function retryAfterSeconds(xhr) {
        const h = xhr.getResponseHeader('Retry-After');
        if (h) {
          const s = parseInt(h, 10);
          if (Number.isFinite(s) && s > 0) return Math.min(s, 120);
        }
        return null;
      }

      function shortName(name) {
        return name.length > 28 ? name.slice(0, 25) + '…' : name;
      }

      function httpErrorMessage(xhr) {
        if (xhr.status === 401) return t('errors.unauthorized');
        if (xhr.status === 429) return t('errors.rateLimit');
        if (xhr.status === 499) return t('errors.canceled');

        let body = (xhr.responseText || '').trim();
        if (body) {
          try {
            const j = JSON.parse(body);
            if (typeof j === 'string') body = j;
            else if (j && typeof j === 'object') {
              // ASP.NET Core ProblemDetails (e.g. model validation): surface the
              // field-level `errors` instead of the generic "One or more
              // validation errors occurred" title.
              if (j.errors && typeof j.errors === 'object' && !Array.isArray(j.errors)) {
                const parts = Object.entries(j.errors).map(([k, v]) =>
                  k + ': ' + (Array.isArray(v) ? v.join(' ') : String(v)));
                body = parts.join(' | ');
              } else {
                body = j.error || j.title || JSON.stringify(j);
              }
            }
          } catch { /* keep raw text */ }
          body = body.slice(0, 1000);
        }
        // Server-provided bodies stay in the language the server sent them.
        return body || t('errors.http', { status: xhr.status });
      }

      function handleSingleResult(entry, data) {
        // Support camelCase (default) and PascalCase
        const filesArr = pick(data, 'Files', 'files') || [];
        const fileResult = filesArr[0];
        if (!fileResult) {
          entry.status = 'error';
          entry.error = t('errors.noResult');
          return;
        }

        entry.savedName = pick(fileResult, 'File', 'file') || null;
        entry.attempts = pick(fileResult, 'Attempts', 'attempts') || 0;
        // Raw API metadata; labels are localized at render time (detailsOf).
        entry.details = pick(fileResult, 'AppliedMetadata', 'appliedMetadata') || null;

        const direct = pick(fileResult, 'DirectAttemptSuccess', 'directAttemptSuccess');
        const repair = pick(fileResult, 'RepairAttemptSuccess', 'repairAttemptSuccess');
        const gs = pick(fileResult, 'GhostscriptRan', 'ghostscriptRan');
        const comicInfo = pick(fileResult, 'ComicInfoWritten', 'comicInfoWritten');
        const pages = pick(fileResult, 'PageCount', 'pageCount');
        // Raw outcome flags; text is localized at render time (factsOf).
        entry.factData = {
          attempts: entry.attempts || 1,
          comic: COMIC_EXTS.includes(entry.ext),
          comicInfo, pages, direct, repair, gs
        };

        const md = pick(data, 'Metadata', 'metadata');
        if (md && Object.keys(md).length &&
            (!metadata.value || metadataTitle.value !== title.value)) {
          metadata.value = md;
          metadataTitle.value = title.value;
        }

        const success = pick(fileResult, 'Success', 'success');
        if (success) {
          entry.status = 'success';
          entry.error = null;
        } else {
          entry.status = 'error';
          // ErrorMessage comes from the server — shown as-is.
          entry.error = pick(fileResult, 'ErrorMessage', 'errorMessage') || t('errors.unknown');
        }
      }

      // -------------------------------------------------------------
      // Phase text per file
      // -------------------------------------------------------------
      function phaseText(f) {
        switch (f.status) {
          case 'pending': return t('phase.pending');
          case 'uploading': return t('phase.uploading');
          case 'processing': return t('phase.processing');
          case 'throttled': {
            const s = Math.max(0, Math.ceil((f.retryAt - now.value) / 1000));
            return t('phase.throttled', { n: s });
          }
          case 'success': return t('phase.success', { name: f.savedName || 'file' });
          case 'canceled': return t('phase.canceled');
          case 'error': return t('phase.error');
          default: return '';
        }
      }

      function detailsOf(entry) {
        return hasDetails(entry) ? prettyMeta(t, entry.details) : null;
      }
      function fileAriaLabel(f) {
        return f.name + ', ' + statusLabel(t, f.status) +
          (hasDetails(f) ? t('sr.detailsHint') : '');
      }

      // -------------------------------------------------------------
      // Ticker (elapsed time + throttle countdown; cheap no-op when idle)
      // -------------------------------------------------------------
      function startTicker() {
        if (ticker) return;
        ticker = setInterval(() => {
          const active = busy.value || pendingFiles.some(f => f.status === 'throttled');
          if (active) now.value = Date.now();
        }, 500);
      }
      function stopTicker() {
        if (ticker) { clearInterval(ticker); ticker = null; }
      }

      // -------------------------------------------------------------
      // Persistence + shortcuts
      // -------------------------------------------------------------
      function saveApiKey() { lsSet(LS.apiKey, apiKey.value); }
      function saveType() { lsSet(LS.type, type.value); }

      function onKeydown(e) {
        if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
          e.preventDefault();
          start();
        }
      }

      // -------------------------------------------------------------
      // Lifecycle
      // -------------------------------------------------------------
      onMounted(() => {
        startTicker();
        window.addEventListener('keydown', onKeydown);
        // Safety net: never let the browser open a dropped file outside the app
        window.addEventListener('dragover', e => e.preventDefault());
        window.addEventListener('drop', e => e.preventDefault());
      });
      onBeforeUnmount(() => {
        stopTicker();
        window.removeEventListener('keydown', onKeydown);
      });

      // -------------------------------------------------------------
      // Expose to template
      // -------------------------------------------------------------
      return {
        types: TYPES,
        locale, setLocale, locales: BMB_I18N.locales,
        title, titleTouched, titleInput, type, apiKey, authRequired, theme,
        filesInput, pendingFiles, busy, windowDrag, lastIgnored,
        metadata, toasts, year,
        typeLabel, isComicType, nameHint, canSubmit, pendingCount, successCount, failCount, runningCount,
        doneCount, overallPct, primaryLabel, elapsedText, prettyMetaList, metaSource, srStatus,
        maxConcurrent: MAX_CONCURRENT,
        onFiles, dragEnter, dragLeave, onDrop,
        start, cancelAll, retryFailed, clearFinished, removeFile, toggleDetails, toggleError,
        formatSize, statusLabel: s => statusLabel(t, s), phaseText, fileAriaLabel,
        hasDetails, detailsOf, factsOf,
        predictedName, linkHtml, errorHeadline, errorIsDetailed,
        saveApiKey, saveType, cycleTheme
      };
    }
  });
  app.use(i18n);
  app.mount('#app');
})();
