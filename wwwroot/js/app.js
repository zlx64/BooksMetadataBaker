// BooksMetadataBaker — UI logic (v2)
// ---------------------------------------------------------------------
// Responsibilities:
// - Book details state (title, type segmented picker, API key, theme)
// - File queue with whole-window drag & drop, dedupe, 500 MB client check
// - Per-file progress via XMLHttpRequest (upload % -> indeterminate bake)
// - Concurrency queue (up to 4 parallel), cancel, retry failed, clear finished
// - Server rate-limit (429) auto-retry with Retry-After backoff + countdown
// - Applied-metadata rendering (per-file expandable details)
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
  const IMAGE_EXTS = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'avif', 'tiff', 'tif', 'heic', 'heif'];
  // Raw containers the server converts to their Kavita equivalent (P9):
  // the predicted saved name shows the *output* extension.
  const KAVITA_EXT = { zip: 'cbz', '7z': 'cb7', rar: 'cbr', tar: 'cbt' };

  // `value` is the server-facing type (also the i18n key under `types.*`);
  // the display label is localized in the template via $t('types.' + value).
  const TYPES = [
    {
      value: 'Book',
      icon: '<svg viewBox="0 0 448 512"><path fill="currentColor" d="M96 512l320 0c17.7 0 32-14.3 32-32s-14.3-32-32-32l0-66.7c18.6-6.6 32-24.4 32-45.3l0-288c0-26.5-21.5-48-48-48l-48 0 0 169.4c0 12.5-10.1 22.6-22.6 22.6-6 0-11.8-2.4-16-6.6L272 144 230.6 185.4c-4.2 4.2-10 6.6-16 6.6-12.5 0-22.6-10.1-22.6-22.6L192 0 96 0C43 0 0 43 0 96L0 416c0 53 43 96 96 96zM64 416c0-17.7 14.3-32 32-32l256 0 0 64-256 0c-17.7 0-32-14.3-32-32z"/></svg>'
    },
    {
      value: 'LightNovel',
      icon: '<svg viewBox="0 0 448 512"><path fill="currentColor" d="M384 512L96 512c-53 0-96-43-96-96L0 96C0 43 43 0 96 0L400 0c26.5 0 48 21.5 48 48l0 288c0 20.9-13.4 38.7-32 45.3l0 66.7c17.7 0 32 14.3 32 32s-14.3 32-32 32l-32 0zM96 384c-17.7 0-32 14.3-32 32s14.3 32 32 32l256 0 0-64-256 0zm32-232c0 13.3 10.7 24 24 24l176 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-176 0c-13.3 0-24 10.7-24 24zm24 72c-13.3 0-24 10.7-24 24s10.7 24 24 24l176 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-176 0z"/></svg>'
    },
    {
      value: 'Manga',
      icon: '<svg viewBox="0 0 448 512"><g transform="translate(448,0) scale(-1,1)"><path fill="currentColor" d="M384 512L96 512c-53 0-96-43-96-96L0 96C0 43 43 0 96 0L400 0c26.5 0 48 21.5 48 48l0 288c0 20.9-13.4 38.7-32 45.3l0 66.7c17.7 0 32 14.3 32 32s-14.3 32-32 32l-32 0zM96 384c-17.7 0-32 14.3-32 32s14.3 32 32 32l256 0 0-64-256 0zm32-232c0 13.3 10.7 24 24 24l176 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-176 0c-13.3 0-24 10.7-24 24zm24 72c-13.3 0-24 10.7-24 24s10.7 24 24 24l176 0c13.3 0 24-10.7 24-24s-10.7-24-24-24l-176 0z"/></g></svg>'
    },
    {
      value: 'Comic',
      icon: '<svg viewBox="0 0 576 512"><path fill="currentColor" d="M384 144c0 97.2-86 176-192 176-26.7 0-52.1-5-75.2-14L35.2 349.2c-9.3 4.9-20.7 3.2-28.2-4.2s-9.2-18.9-4.2-28.2l35.6-67.2C14.3 220.2 0 183.6 0 144 0 46.8 86-32 192-32S384 46.8 384 144zm0 368c-94.1 0-172.4-62.1-188.8-144 120-1.5 224.3-86.9 235.8-202.7 83.3 19.2 145 88.3 145 170.7 0 39.6-14.3 76.2-38.4 105.6l35.6 67.2c4.9 9.3 3.2 20.7-4.2 28.2s-18.9 9.2-28.2 4.2L459.2 498c-23.1 9-48.5 14-75.2 14z"/></svg>'
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
      const imageFolderInput = ref(null);
      const pendingFiles = reactive([]); // { uid, file, name, size, ext, progress, status, error, attempts, skip, expanded, details (raw meta), factData, savedName, throttleRetries, retryAt, _xhr, source ('local'|'server'), serverPath, moveOriginals, dedupeKey }
      const busy = ref(false);
      const windowDrag = ref(false);
      const lastIgnored = ref([]);
      const toasts = reactive([]);
      const now = ref(Date.now());
      const year = new Date().getFullYear();

      // Server volume source: the browsed folder lives on the server (e.g. a
      // Docker bind mount), so selected files are processed in place — no upload.
      const sourceTab = ref('local'); // 'local' | 'server'
      const serverEnabled = ref(false);
      const serverRoot = ref('');
      const serverPath = ref(''); // '' = root
      const serverEntries = ref([]);
      const serverLoading = ref(false);
      const serverError = ref(null);
      const serverSelected = reactive(new Set()); // relative paths
      const serverMoveOriginals = ref(false);
      let serverLoadedOnce = false;

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
      const serverPathSegments = computed(() => serverPath.value ? serverPath.value.split('/') : []);
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
          const sf = pick(data, 'serverFiles', 'ServerFiles');
          if (sf) {
            const enabled = pick(sf, 'enabled', 'Enabled');
            serverEnabled.value = enabled !== undefined ? !!enabled : false;
            const root = pick(sf, 'root', 'Root');
            serverRoot.value = typeof root === 'string' ? root : '';
          }
        } catch { /* probe failed — keep the field visible */ }
      }
      loadServerConfig();

      // -------------------------------------------------------------
      // Server volume browsing
      // -------------------------------------------------------------
      async function loadServerDir(path) {
        serverLoading.value = true;
        serverError.value = null;
        try {
          const res = await fetch('/api/files?path=' + encodeURIComponent(path), {
            cache: 'no-store',
            headers: apiKey.value ? { 'X-Api-Key': apiKey.value } : {}
          });
          if (!res.ok) {
            serverEntries.value = [];
            serverError.value = await httpErrorText(res);
            return;
          }
          const data = await res.json();
          serverPath.value = pick(data, 'path', 'Path') || '';
          serverEntries.value = pick(data, 'entries', 'Entries') || [];
          serverSelected.clear();
        } catch {
          serverEntries.value = [];
          serverError.value = t('errors.network');
        } finally {
          serverLoading.value = false;
        }
      }

      function openServerTab() {
        sourceTab.value = 'server';
        if (!serverLoadedOnce) {
          serverLoadedOnce = true;
          loadServerDir('');
        }
      }
      function serverUp() {
        if (!serverPath.value) return;
        const i = serverPath.value.lastIndexOf('/');
        loadServerDir(i === -1 ? '' : serverPath.value.slice(0, i));
      }
      function serverGoRoot() { loadServerDir(''); }
      function serverOpenDir(entry) { loadServerDir(entry.path); }
      function toggleServerEntry(entry, checked) {
        if (checked) serverSelected.add(entry.path);
        else serverSelected.delete(entry.path);
      }

      function addServerFiles() {
        const chosen = serverEntries.value.filter(e => serverSelected.has(e.path) && (!e.isDir || e.hasImages));
        if (!chosen.length) return;
        const existing = new Set(pendingFiles.map(f => f.dedupeKey));
        let added = 0;
        for (const e of chosen) {
          const ext = e.isDir ? 'cbz' : extOf(e.name);
          if (!e.isDir && !ALLOWED_EXTS.includes(ext)) continue; // defensive: server marks selectable
          const dedupeKey = 'srv|' + (e.isDir ? 'dir|' : '') + e.path;
          if (existing.has(dedupeKey)) continue;
          existing.add(dedupeKey);
          const item = makeItem(e.name, e.isDir ? 0 : e.size, ext, {
            source: 'server',
            serverPath: e.path,
            isDir: e.isDir,
            moveOriginals: serverMoveOriginals.value,
            dedupeKey
          });
          if (!e.isDir && e.size > MAX_FILE_SIZE) {
            item.status = 'error';
            item.skip = true;
            item.error = t('errors.tooLarge');
          }
          pendingFiles.push(item);
          added++;
        }
        serverSelected.clear();
        if (added) toast(t('toasts.added', added), 'info');
      }

      // Server-provided error body (JSON or plain text) for fetch-based calls.
      async function httpErrorText(res) {
        let body = '';
        try {
          body = (await res.text()).trim();
          if (body) {
            try {
              const j = JSON.parse(body);
              if (typeof j === 'string') body = j;
              else if (j && typeof j === 'object') body = j.error || j.title || JSON.stringify(j);
            } catch { /* keep raw text */ }
          }
        } catch { /* no body */ }
        return body || t('errors.http', { status: res.status });
      }

      // -------------------------------------------------------------
      // File selection
      // -------------------------------------------------------------
      function onFiles() {
        addFiles(filesInput.value && filesInput.value.files);
        if (filesInput.value) filesInput.value.value = ''; // allow re-selecting the same file
      }

      function openImageFolderPicker() {
        if (imageFolderInput.value) imageFolderInput.value.click();
      }

      function onImageFolder() {
        const input = imageFolderInput.value;
        const incoming = Array.from(input && input.files || []);
        if (input) input.value = '';
        const imageFiles = incoming.filter(f => IMAGE_EXTS.includes(extOf(f.name)));
        if (!imageFiles.length) {
          toast(t('errors.noImages'), 'warn');
          return;
        }

        const relativePaths = imageFiles.map(f => f.webkitRelativePath || f.relativePath || '');
        const firstSegments = (relativePaths.find(Boolean) || '').split('/').filter(Boolean);
        const folderName = firstSegments.length > 1
          ? firstSegments[0]
          : (title.value.trim() || 'Image Folder');
        const paths = imageFiles.map((f, i) => {
          const rel = relativePaths[i] || f.name;
          const prefix = folderName + '/';
          return rel.startsWith(prefix) ? rel.slice(prefix.length) : rel;
        });

        const totalSize = imageFiles.reduce((sum, f) => sum + f.size, 0);
        const dedupeKey = 'folder|' + folderName + '|' + totalSize + '|' + paths.join('|');
        if (pendingFiles.some(f => f.dedupeKey === dedupeKey)) return;

        const item = makeItem(folderName, totalSize, 'cbz', {
          source: 'local-folder',
          files: imageFiles,
          folderName,
          folderPaths: paths,
          dedupeKey
        });
        if (totalSize > MAX_FILE_SIZE) {
          item.status = 'error';
          item.skip = true;
          item.error = t('errors.folderTooLarge');
        }
        pendingFiles.push(item);
        toast(t('toasts.folderAdded'), 'info');
      }

      // Queue row factory — shared by local (upload) and server (in-place) files.
      // status: pending | uploading | processing | throttled | success | error | canceled
      function makeItem(name, size, ext, extra) {
        return Object.assign({
          uid: ++uidSeq,
          file: null,
          files: null,
          name, size, ext,
          progress: 0,
          status: 'pending',
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
          _xhr: null,
          source: 'local',
          serverPath: null,
          folderName: null,
          folderPaths: null,
          isDir: false,
          moveOriginals: false,
          dedupeKey: name + '|' + size
        }, extra || {});
      }

      function addFiles(list) {
        const incoming = Array.from(list || []);
        if (!incoming.length) return;
        lastIgnored.value = [];
        const existing = new Set(pendingFiles.map(f => f.dedupeKey));
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

          const item = makeItem(f.name, f.size, ext, { file: f, dedupeKey });
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
          // `facts.split` is a plural message — pass the count, not an object.
          if (d.split > 1) facts.push(t('facts.split', d.split));
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
        busy.value = true;
        cancelFlag = false;
        elapsedStart = Date.now();

        const queue = [...pendingFiles];
        const hasServerFiles = queue.some(f => f.source === 'server');
        let qi = 0;
        let active = 0;

        function pump() {
          if (!cancelFlag) {
            while (active < MAX_CONCURRENT && qi < queue.length) {
              const item = queue[qi++];
              if (item.status !== 'pending') continue;
              // Server files need no upload phase — baking starts immediately.
              item.status = item.source === 'server' ? 'processing' : 'uploading';
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
            // Server-source files may have been moved out of the volume; refresh
            // the browsed folder so the list reflects what's actually there now.
            if (hasServerFiles && serverLoadedOnce) loadServerDir(serverPath.value);
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
        const xhr = new XMLHttpRequest();
        entry._xhr = xhr;
        xhr.timeout = XHR_TIMEOUT_MS;
        if (apiKey.value) xhr.setRequestHeader('X-Api-Key', apiKey.value);

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

        if (entry.source === 'server') {
          // The file already lives on the server volume — no upload, just the
          // process request (JSON body, same response shape as /api/upload).
          xhr.open('POST', '/api/files/process');
          xhr.setRequestHeader('Content-Type', 'application/json');
          xhr.send(JSON.stringify({
            title: title.value,
            type: type.value,
            path: entry.serverPath,
            moveOriginals: entry.moveOriginals
          }));
          return;
        }

        if (entry.source === 'local-folder') {
          const folderForm = new FormData();
          folderForm.append('Title', title.value);
          folderForm.append('Type', type.value);
          folderForm.append('folderName', entry.folderName || '');
          folderForm.append('paths', JSON.stringify(entry.folderPaths || []));
          for (const file of entry.files || []) folderForm.append('files', file, file.name);
          xhr.open('POST', '/api/upload/image-folder');
          xhr.upload.onprogress = e => {
            if (!e.lengthComputable) return;
            entry.progress = Math.min(99, Math.round((e.loaded / e.total) * 100));
          };
          xhr.upload.onload = () => {
            if (entry.status === 'uploading') entry.status = 'processing';
          };
          xhr.send(folderForm);
          return;
        }

        const fd = new FormData();
        fd.append('Title', title.value);
        fd.append('Type', type.value);
        fd.append('file', entry.file);
        xhr.open('POST', '/api/upload');
        xhr.upload.onprogress = e => {
          if (!e.lengthComputable) return;
          entry.progress = Math.min(99, Math.round((e.loaded / e.total) * 100));
        };
        xhr.upload.onload = () => {
          // Bytes sent — server is now fetching metadata and running Calibre/Ghostscript
          if (entry.status === 'uploading') entry.status = 'processing';
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
        // Set when a multi-volume archive was split into several archives.
        entry.splitFiles = pick(fileResult, 'SplitFiles', 'splitFiles') || [];
        // Raw outcome flags; text is localized at render time (factsOf).
        entry.factData = {
          attempts: entry.attempts || 1,
          comic: COMIC_EXTS.includes(entry.ext),
          comicInfo, pages, direct, repair, gs,
          split: entry.splitFiles.length
        };

        const success = pick(fileResult, 'Success', 'success');
        if (success) {
          entry.status = 'success';
          entry.error = null;
          // A split archive created several files — reveal them without a click.
          if (entry.splitFiles.length > 1) entry.expanded = true;
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
      // Whole-window drag & drop. Bound on window (not via the #app template)
      // because #app is the mount container: with an in-DOM template Vue compiles
      // only #app's innerHTML, so @-directives written on #app itself are inert
      // HTML attributes and never become handlers.
      function onWindowDragOver(e) { e.preventDefault(); }

      onMounted(() => {
        startTicker();
        window.addEventListener('keydown', onKeydown);
        window.addEventListener('dragenter', dragEnter);
        window.addEventListener('dragover', onWindowDragOver);
        window.addEventListener('dragleave', dragLeave);
        window.addEventListener('drop', onDrop);
      });
      onBeforeUnmount(() => {
        stopTicker();
        window.removeEventListener('keydown', onKeydown);
        window.removeEventListener('dragenter', dragEnter);
        window.removeEventListener('dragover', onWindowDragOver);
        window.removeEventListener('dragleave', dragLeave);
        window.removeEventListener('drop', onDrop);
      });

      // -------------------------------------------------------------
      // Expose to template
      // -------------------------------------------------------------
      return {
        types: TYPES,
        locale, setLocale, locales: BMB_I18N.locales,
        title, titleTouched, titleInput, type, apiKey, authRequired, theme,
        filesInput, imageFolderInput, pendingFiles, busy, windowDrag, lastIgnored,
        toasts, year,
        sourceTab, serverEnabled, serverRoot, serverPath, serverPathSegments,
        serverEntries, serverLoading, serverError, serverSelected, serverMoveOriginals,
        typeLabel, isComicType, nameHint, canSubmit, pendingCount, successCount, failCount, runningCount,
        doneCount, overallPct, primaryLabel, elapsedText, srStatus,
        maxConcurrent: MAX_CONCURRENT,
        onFiles, openImageFolderPicker, onImageFolder, dragEnter, dragLeave, onDrop,
        openServerTab, loadServerDir, serverUp, serverGoRoot, serverOpenDir, toggleServerEntry, addServerFiles,
        start, cancelAll, retryFailed, clearFinished, removeFile, toggleDetails, toggleError,
        formatSize, extOf, statusLabel: s => statusLabel(t, s), phaseText, fileAriaLabel,
        hasDetails, detailsOf, factsOf,
        predictedName, linkHtml, errorHeadline, errorIsDetailed,
        saveApiKey, saveType, cycleTheme
      };
    }
  });
  app.use(i18n);
  app.mount('#app');
})();
