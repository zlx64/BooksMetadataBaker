// BooksMetadataBaker — UI logic (v2)
// ---------------------------------------------------------------------
// Responsibilities:
// - Book details state (title, type segmented picker, API key, theme)
// - File queue with whole-window drag & drop, dedupe, 500 MB client check
// - Per-file progress via XMLHttpRequest (upload % -> indeterminate bake)
// - Concurrency queue (up to 4 parallel), cancel, retry failed, clear finished
// - Server rate-limit (429) auto-retry with Retry-After backoff + countdown
// - Applied-metadata rendering (summary card + per-file expandable details)
// - Toasts, keyboard shortcut (Ctrl+Enter), screen-reader status line
// ---------------------------------------------------------------------

(function () {
  const { createApp, reactive, ref, computed, onMounted, onBeforeUnmount } = Vue;

  const MAX_FILE_SIZE = 500 * 1024 * 1024;      // must match server RequestSizeLimit
  const MAX_CONCURRENT = 4;
  const XHR_TIMEOUT_MS = 30 * 60 * 1000;        // safety net so the UI never hangs forever
  const THROTTLE_MAX_RETRIES = 3;
  const THROTTLE_DEFAULT_WAIT_S = 30;
  const LS = { apiKey: 'bmb.apiKey', type: 'bmb.type', theme: 'bmb.theme' };

  const TYPES = [
    {
      value: 'Book', label: 'Book',
      icon: '<svg viewBox="0 0 24 24"><path d="M12 6c-1.5-1.6-3.7-2.5-6-2.5H3v15h3c2.3 0 4.5.9 6 2.5 1.5-1.6 3.7-2.5 6-2.5h3v-15h-3c-2.3 0-4.5.9-6 2.5z"/><path d="M12 6v15"/></svg>'
    },
    {
      value: 'LightNovel', label: 'Light Novel',
      icon: '<svg viewBox="0 0 24 24"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V4H6.5A2.5 2.5 0 0 0 4 6.5v13z"/><path d="M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5"/><path d="M9 8h7M9 11h5"/></svg>'
    },
    {
      value: 'Manga', label: 'Manga',
      icon: '<svg viewBox="0 0 24 24"><path d="M21 11.5a8.5 8.5 0 0 1-8.5 8.5c-1.5 0-3-.4-4.2-1.1L3 20l1.1-5.3A8.5 8.5 0 1 1 21 11.5z"/><path d="M8.5 10.5h.01M12 10.5h.01M15.5 10.5h.01"/></svg>'
    },
    {
      value: 'Comic', label: 'Comic',
      icon: '<svg viewBox="0 0 24 24"><path d="M13 2 4 14h6l-1 8 9-12h-6l1-8z"/></svg>'
    }
  ];

  const STATUS_LABELS = {
    pending: 'Queued',
    uploading: 'Uploading',
    processing: 'Baking',
    throttled: 'Rate-limited',
    success: 'Baked',
    error: 'Failed',
    canceled: 'Canceled'
  };

  const META_LABELS = {
    Title: 'Title',
    TitleEnglish: 'Title (English)',
    TitleRomaji: 'Title (Romaji)',
    TitleNative: 'Title (Native)',
    Subtitle: 'Subtitle',
    Authors: 'Authors',
    Publisher: 'Publisher',
    PublishedDate: 'Published',
    StartDate: 'Started',
    EndDate: 'Ended',
    StartYear: 'Start year',
    Genres: 'Genres',
    Categories: 'Categories',
    Tags: 'Tags',
    Format: 'Format',
    Status: 'Status',
    AverageScore: 'Average score',
    Volumes: 'Volumes',
    Chapters: 'Chapters',
    PageCount: 'Pages',
    IssueCount: 'Issues',
    Language: 'Language',
    Description: 'Description',
    Snippet: 'Snippet',
    Source: 'Source',
    SourceUrl: 'Source URL',
    ApiDetailUrl: 'API URL'
  };

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

  // Mirrors the server's GetUniqueEBookPath/BuildVolumeName:
  // first number in the filename becomes "Title - Volume N", else just "Title".
  function predictedName(fileName, bookTitle) {
    const ext = extOf(fileName);
    const base = ext ? fileName.slice(0, fileName.length - ext.length - 1) : fileName;
    const m = base.match(/\d+(?:\.\d+)?/);
    const t = bookTitle || 'Title';
    if (!m) return t + (ext ? '.' + ext : '');
    let num = m[0];
    if (num.includes('.')) {
      const d = Number(num);
      num = Number.isFinite(d) ? String(d) : num;
    } else {
      const i = parseInt(num, 10);
      num = Number.isFinite(i) ? String(i) : num;
    }
    return t + ' - Volume ' + num + (ext ? '.' + ext : '');
  }

  function prettyMeta(meta) {
    if (!meta) return [];
    const keys = Object.keys(meta);
    const ordered = META_ORDER.filter(k => keys.includes(k) && meta[k]);
    const rest = keys.filter(k => !META_ORDER.includes(k) && meta[k]);
    return [...ordered, ...rest].map(k => ({ key: k, label: META_LABELS[k] || k, value: meta[k] }));
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

  function statusLabel(s) { return STATUS_LABELS[s] || s; }

  // -------------------------------------------------------------------
  // App
  // -------------------------------------------------------------------
  createApp({
    setup() {
      // -------------------------------------------------------------
      // Reactive state
      // -------------------------------------------------------------
      const title = ref('');
      const titleTouched = ref(false);
      const titleInput = ref(null);
      const type = ref(TYPES.some(t => t.value === lsGet(LS.type)) ? lsGet(LS.type) : 'LightNovel');
      const apiKey = ref(lsGet(LS.apiKey));
      const authRequired = ref(true); // safe fallback until the probe says otherwise
      const theme = ref(['light', 'dark', 'auto'].includes(lsGet(LS.theme)) ? lsGet(LS.theme) : 'auto');

      const filesInput = ref(null);
      const pendingFiles = reactive([]); // { uid, file, name, size, ext, progress, status, error, attempts, skip, expanded, details, facts, savedName, throttleRetries, retryAt, _xhr }
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
      // Derived state
      // -------------------------------------------------------------
      const typeLabel = computed(() => (TYPES.find(t => t.value === type.value) || {}).label || type.value);
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
        if (busy.value) return 'Baking… ' + doneCount.value + '/' + pendingFiles.length;
        if (pendingCount.value) return 'Bake ' + pendingCount.value + (pendingCount.value === 1 ? ' file' : ' files');
        return 'Bake files';
      });
      const elapsedText = computed(() => busy.value ? formatElapsed(now.value - elapsedStart) : '');
      const prettyMetaList = computed(() => prettyMeta(metadata.value));
      const metaSource = computed(() => (metadata.value && metadata.value.Source) || '');
      const srStatus = computed(() => {
        if (!pendingFiles.length) return '';
        return doneCount.value + ' of ' + pendingFiles.length + ' files done. ' +
          successCount.value + ' baked, ' + failCount.value + ' failed, ' +
          runningCount.value + ' running.';
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
          if (ext !== 'pdf' && ext !== 'epub') {
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
            attempts: 0,
            skip: false,
            expanded: false,
            details: null,
            facts: [],
            savedName: null,
            throttleRetries: 0,
            retryAt: 0,
            _xhr: null
          };
          if (f.size > MAX_FILE_SIZE) {
            item.status = 'error';
            item.skip = true;
            item.error = 'File too large — max 500 MB';
          }
          pendingFiles.push(item);
          added++;
        }
        if (added) toast(added + (added === 1 ? ' file added' : ' files added'), 'info');
      }

      function removeFile(entry) {
        const i = pendingFiles.indexOf(entry);
        if (i !== -1) pendingFiles.splice(i, 1);
      }

      function toggleDetails(entry) {
        if (!entry.details || !entry.details.length) return;
        entry.expanded = !entry.expanded;
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
          toast('Enter the book title first', 'warn');
          return;
        }
        if (!pendingCount.value) {
          toast('Add at least one PDF or EPUB file', 'warn');
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
            if (ok && !failed) toast(ok + (ok === 1 ? ' file' : ' files') + ' baked successfully', 'success');
            else if (failed) toast(failed + (failed === 1 ? ' file failed' : ' files failed'), 'error');
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
        toast('Canceled — queued files were not started', 'info');
      }

      function retryFailed() {
        let n = 0;
        for (const f of pendingFiles) {
          if (f.status === 'error' && !f.skip) {
            f.status = 'pending';
            f.progress = 0;
            f.error = null;
            f.attempts = 0;
            f.throttleRetries = 0;
            f.savedName = null;
            f.details = null;
            f.facts = [];
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
            toast('Rate limit hit — ' + shortName(entry.name) + ' retries in ' + wait + 's', 'warn');
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
              entry.error = 'Unexpected server response';
            }
          } else {
            entry.status = 'error';
            entry.error = httpErrorMessage(xhr);
            if (xhr.status === 429) {
              // retries exhausted
              entry.error = 'Rate limit exceeded — wait a minute, then retry';
            }
          }
          finish();
        };

        xhr.onerror = () => {
          entry.status = 'error';
          entry.error = 'Network error — could not reach the server';
          finish();
        };
        xhr.ontimeout = () => {
          entry.status = 'error';
          entry.error = 'Request timed out (30 min)';
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
        if (xhr.status === 401) return 'Unauthorized — set the correct API key';
        if (xhr.status === 429) return 'Rate limit exceeded — wait a minute, then retry';
        if (xhr.status === 499) return 'Request was canceled';

        let body = (xhr.responseText || '').trim();
        if (body) {
          try {
            const j = JSON.parse(body);
            if (typeof j === 'string') body = j;
            else if (j && typeof j === 'object') body = j.error || j.title || JSON.stringify(j);
          } catch { /* keep raw text */ }
          body = body.slice(0, 300);
        }
        return body || ('Upload failed (HTTP ' + xhr.status + ')');
      }

      function handleSingleResult(entry, data) {
        // Support camelCase (default) and PascalCase
        const filesArr = pick(data, 'Files', 'files') || [];
        const fileResult = filesArr[0];
        if (!fileResult) {
          entry.status = 'error';
          entry.error = 'No file result in response';
          return;
        }

        entry.savedName = pick(fileResult, 'File', 'file') || null;
        entry.attempts = pick(fileResult, 'Attempts', 'attempts') || 0;
        entry.details = prettyMeta(pick(fileResult, 'AppliedMetadata', 'appliedMetadata'));

        const direct = pick(fileResult, 'DirectAttemptSuccess', 'directAttemptSuccess');
        const repair = pick(fileResult, 'RepairAttemptSuccess', 'repairAttemptSuccess');
        const gs = pick(fileResult, 'GhostscriptRan', 'ghostscriptRan');
        const facts = [];
        facts.push('Attempts: ' + (entry.attempts || 1));
        facts.push(direct ? 'Direct embed: ok' : 'Direct embed: failed');
        if (repair !== undefined) facts.push('Repair pass: ' + (repair ? 'ok' : 'failed'));
        if (gs) facts.push('Ghostscript repair: ran');
        entry.facts = facts;

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
          entry.error = pick(fileResult, 'ErrorMessage', 'errorMessage') || 'Unknown error';
        }
      }

      // -------------------------------------------------------------
      // Phase text per file
      // -------------------------------------------------------------
      function phaseText(f) {
        switch (f.status) {
          case 'pending': return 'Waiting in queue';
          case 'uploading': return 'Uploading…';
          case 'processing': return 'Fetching metadata & embedding…';
          case 'throttled': {
            const s = Math.max(0, Math.ceil((f.retryAt - now.value) / 1000));
            return 'Rate limit — retrying in ' + s + 's';
          }
          case 'success': return 'Saved as ' + (f.savedName || 'file');
          case 'canceled': return 'Canceled';
          case 'error': return 'Failed';
          default: return '';
        }
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
        title, titleTouched, titleInput, type, apiKey, authRequired, theme,
        filesInput, pendingFiles, busy, windowDrag, lastIgnored,
        metadata, toasts, year,
        typeLabel, canSubmit, pendingCount, successCount, failCount, runningCount,
        doneCount, overallPct, primaryLabel, elapsedText, prettyMetaList, metaSource, srStatus,
        maxConcurrent: MAX_CONCURRENT,
        onFiles, dragEnter, dragLeave, onDrop,
        start, cancelAll, retryFailed, clearFinished, removeFile, toggleDetails,
        formatSize, statusLabel, phaseText, predictedName, linkHtml,
        saveApiKey, saveType, cycleTheme
      };
    }
  }).mount('#app');
})();
