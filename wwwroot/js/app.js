// BooksMetadataBaker eBook Upload UI Logic
// ---------------------------------------------------------------------
// Responsibilities:
// - Manage form state (title, type, API key, selected files)
// - Track per-file progress (upload %) and overall progress
// - Upload via XMLHttpRequest with granular progress + full error handling
//   (network errors, timeouts, 401/429/400 with server-provided messages)
// - Concurrency queue (up to 4 parallel uploads), cancel, retry failed
// - Render per-file status + error messages
// ---------------------------------------------------------------------

(function(){
  const { createApp, reactive, ref, computed } = Vue;

  const MAX_FILE_SIZE = 500 * 1024 * 1024; // must match server RequestSizeLimit
  const MAX_CONCURRENT = 4;
  const XHR_TIMEOUT_MS = 30 * 60 * 1000;   // safety net so the UI can never hang forever
  const LS_API_KEY = 'bmb.apiKey';

  function lsGet(key){ try { return localStorage.getItem(key) || ''; } catch { return ''; } }
  function lsSet(key, value){ try { localStorage.setItem(key, value); } catch { /* private mode */ } }

  const STATUS_LABELS = {
    pending: 'Queued',
    uploading: 'Uploading',
    processing: 'Processing',
    success: 'Success',
    error: 'Failed',
    canceled: 'Canceled'
  };

  createApp({
    setup(){
      // -----------------------------------------------------------------
      // Reactive State
      // -----------------------------------------------------------------
      const title = ref("");
      const type = ref("LightNovel");
      const apiKey = ref(lsGet(LS_API_KEY));
      const authRequired = ref(true); // shown until the server probe says otherwise (safe fallback)
      const filesInput = ref(null);
      const pendingFiles = reactive([]); // [{ uid, file, name, size, progress, status, error, attempts, skip, _xhr }]
      const busy = ref(false);
      const dragging = ref(false);
      const ignoredFiles = ref([]);      // names of files dropped that are not PDF/EPUB
      const metadataUnion = ref({});     // union of metadata returned (first response)
      const year = new Date().getFullYear();

      let uidSeq = 0;
      let dragDepth = 0;
      let cancelFlag = false;

      // -----------------------------------------------------------------
      // Derived State
      // -----------------------------------------------------------------
      const canSubmit = computed(()=>
        title.value.trim().length > 0 && pendingFiles.some(f => f.status === 'pending'));
      const successCount = computed(()=> pendingFiles.filter(f => f.status === 'success').length);
      const failCount = computed(()=> pendingFiles.filter(f => f.status === 'error').length);
      const doneCount = computed(()=> pendingFiles.filter(f =>
        f.status === 'success' || f.status === 'error' || f.status === 'canceled').length);
      const overallPct = computed(()=>
        pendingFiles.length ? Math.round(doneCount.value / pendingFiles.length * 100) : 0);
      const hasMetadata = computed(()=> Object.keys(metadataUnion.value).length > 0);
      const metadataCount = computed(()=> Object.keys(metadataUnion.value).length);
      const submitLabel = computed(()=>
        busy.value ? 'Working… ' + doneCount.value + '/' + pendingFiles.length : 'Upload & Process');

      // -----------------------------------------------------------------
      // Server config probe (shows the API key field only when enforced)
      // -----------------------------------------------------------------
      async function loadServerConfig(){
        try {
          const res = await fetch('/api/config', { cache: 'no-store' });
          if (!res.ok) return;
          const data = await res.json();
          const required = data.authRequired !== undefined ? data.authRequired : data.AuthRequired;
          authRequired.value = !!required;
        } catch { /* probe failed — keep the field visible */ }
      }
      loadServerConfig();

      // -----------------------------------------------------------------
      // File selection
      // -----------------------------------------------------------------
      function onFiles(){
        addFiles(filesInput.value?.files);
        if (filesInput.value) filesInput.value.value = ''; // allow re-selecting the same file
      }

      function addFiles(list){
        const incoming = Array.from(list || []);
        const existing = new Set(pendingFiles.map(f => f.name + '|' + f.size));
        for (const f of incoming){
          const name = f.name.toLowerCase();
          if (!name.endsWith('.pdf') && !name.endsWith('.epub')){
            if (!ignoredFiles.value.includes(f.name)) ignoredFiles.value.push(f.name);
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
            progress: 0,
            status: 'pending', // pending | uploading | processing | success | error | canceled
            error: null,
            attempts: 0,
            skip: false,
            _xhr: null
          };
          if (f.size > MAX_FILE_SIZE){
            item.status = 'error';
            item.skip = true;
            item.error = 'File too large — max 500 MB';
          }
          pendingFiles.push(item);
        }
      }

      function removeFile(entry){
        const i = pendingFiles.indexOf(entry);
        if (i !== -1) pendingFiles.splice(i, 1);
      }

      // Drag & drop
      function dragEnter(e){
        e.preventDefault();
        dragDepth++;
        dragging.value = true;
      }
      function dragLeave(){
        dragDepth = Math.max(0, dragDepth - 1);
        if (!dragDepth) dragging.value = false;
      }
      function onDrop(e){
        e.preventDefault();
        dragDepth = 0;
        dragging.value = false;
        addFiles(e.dataTransfer?.files);
      }

      // -----------------------------------------------------------------
      // Upload queue
      // -----------------------------------------------------------------
      function submit(){
        if (!canSubmit.value || busy.value) return;
        busy.value = true;
        cancelFlag = false;
        metadataUnion.value = {};

        const queue = [...pendingFiles];
        let qi = 0;
        let active = 0;

        function pump(){
          if (!cancelFlag){
            while (active < MAX_CONCURRENT && qi < queue.length){
              const item = queue[qi++];
              if (item.status !== 'pending') continue;
              item.status = 'uploading';
              active++;
              uploadOne(item).then(()=>{ active--; pump(); });
            }
          }
          if (active === 0) busy.value = false;
        }

        pump();
      }

      function cancelAll(){
        cancelFlag = true;
        for (const f of pendingFiles){
          if ((f.status === 'uploading' || f.status === 'processing') && f._xhr){
            try { f._xhr.abort(); } catch { /* already closed */ }
          }
        }
      }

      function retryFailed(){
        let n = 0;
        for (const f of pendingFiles){
          if (f.status === 'error' && !f.skip){
            f.status = 'pending';
            f.progress = 0;
            f.error = null;
            f.attempts = 0;
            n++;
          }
        }
        if (n) submit();
      }

      function clearFinished(){
        for (let i = pendingFiles.length - 1; i >= 0; i--){
          if (pendingFiles[i].status === 'success') pendingFiles.splice(i, 1);
        }
      }

      // -----------------------------------------------------------------
      // Single upload (XMLHttpRequest for progress events)
      // -----------------------------------------------------------------
      function uploadOne(entry){
        return new Promise(resolve=>{
          let settled = false;
          const finish = ()=>{ if (!settled){ settled = true; resolve(); } };

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
          xhr.upload.onload = ()=>{
            // Bytes sent, server is now processing (metadata + Calibre can take a while)
            entry.status = 'processing';
          };
          xhr.onload = ()=>{
            if (xhr.status >= 200 && xhr.status < 300){
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
            }
            finish();
          };
          xhr.onerror = ()=>{
            entry.status = 'error';
            entry.error = 'Network error — could not reach the server';
            finish();
          };
          xhr.ontimeout = ()=>{
            entry.status = 'error';
            entry.error = 'Request timed out (30 min)';
            try { xhr.abort(); } catch { /* noop */ }
            finish();
          };
          xhr.onabort = ()=>{
            if (entry.status === 'uploading' || entry.status === 'processing'){
              entry.status = 'canceled';
              entry.error = 'Canceled';
            }
            finish();
          };

          xhr.send(fd);
        });
      }

      function httpErrorMessage(xhr){
        if (xhr.status === 401) return 'Unauthorized — set the correct API key';
        if (xhr.status === 429) return 'Rate limit exceeded — wait a minute, then retry';
        if (xhr.status === 499) return 'Request was canceled';

        let body = (xhr.responseText || '').trim();
        if (body){
          try {
            const j = JSON.parse(body);
            if (typeof j === 'string') body = j;
            else if (j && typeof j === 'object') body = j.error || j.title || JSON.stringify(j);
          } catch { /* keep raw text */ }
          body = body.slice(0, 300);
        }
        return body || ('Upload failed (HTTP ' + xhr.status + ')');
      }

      function handleSingleResult(entry, data){
        // Support camelCase (default) and PascalCase
        const filesArr = data.Files || data.files || [];
        const fileResult = filesArr[0];
        if (!fileResult){
          entry.status = 'error';
          entry.error = 'No file result in response';
          return;
        }
        const success = fileResult.Success !== undefined ? fileResult.Success : fileResult.success;
        entry.attempts = fileResult.Attempts !== undefined ? fileResult.Attempts : (fileResult.attempts || 0);
        if (!Object.keys(metadataUnion.value).length){
          const md = data.Metadata || data.metadata;
          metadataUnion.value = (md && Object.keys(md).length) ? md : {};
        }
        if (success){
          entry.status = 'success';
          entry.error = null;
        } else {
          entry.status = 'error';
          entry.error = fileResult.ErrorMessage || fileResult.errorMessage || 'Unknown error';
        }
      }

      // -----------------------------------------------------------------
      // Misc
      // -----------------------------------------------------------------
      function formatSize(bytes){
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(1) + ' KB';
        return (bytes / 1048576).toFixed(2) + ' MB';
      }

      function statusLabel(s){ return STATUS_LABELS[s] || s; }

      function saveApiKey(){ lsSet(LS_API_KEY, apiKey.value); }

      function reset(){
        title.value = '';
        type.value = 'LightNovel';
        apiKey.value = lsGet(LS_API_KEY);
        metadataUnion.value = {};
        ignoredFiles.value = [];
        pendingFiles.splice(0);
        if (filesInput.value) filesInput.value.value = '';
      }

      // -----------------------------------------------------------------
      // Expose to template
      // -----------------------------------------------------------------
      return {
        title, type, apiKey, authRequired, filesInput, pendingFiles, busy, dragging,
        ignoredFiles, metadataUnion, year,
        canSubmit, submitLabel, successCount, failCount, doneCount, overallPct,
        hasMetadata, metadataCount,
        onFiles, dragEnter, dragLeave, onDrop,
        submit, reset, cancelAll, retryFailed, clearFinished, removeFile,
        formatSize, statusLabel, saveApiKey
      };
    }
  }).mount('#app');
})();
