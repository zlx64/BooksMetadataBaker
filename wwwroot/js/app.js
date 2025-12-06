// BooksMetadataBaker eBook Upload UI Logic (Updated for per-file processing uploads - PDF and EPUB support)
// ---------------------------------------------------------------------
// Responsibilities:
// - Manage form state (title, type, selected files)
// - Track proportional progress bars per file
// - Perform upload via XMLHttpRequest for granular progress events
// - Render results + metadata, toggle details
// - Reset state
// ---------------------------------------------------------------------

(function(){
  const { createApp, reactive, ref, computed } = Vue;

  createApp({
    setup(){
      // -----------------------------------------------------------------
      // Reactive State
      // -----------------------------------------------------------------
      const title = ref("");
      const type = ref("LightNovel");
      const filesInput = ref(null);
      const pendingFiles = reactive([]); // [{ file, name, size, progress, status, error, attempts, resultMeta }]
      const busy = ref(false);
      const metadataUnion = ref({}); // union of metadata returned (first successful)
      const year = new Date().getFullYear();

      // -----------------------------------------------------------------
      // Derived State
      // -----------------------------------------------------------------
      // Allow upload when at least one file selected; title will still be HTML required on submit
      const canSubmit = computed(()=> pendingFiles.length > 0 && title.value.trim().length>0);
      const successCount = computed(()=> pendingFiles.filter(f=>f.status==='success').length);
      const failCount = computed(()=> pendingFiles.filter(f=>f.status==='error').length);
      const hasMetadata = computed(()=> Object.keys(metadataUnion.value).length>0);
      const metadataKeys = computed(()=> Object.keys(metadataUnion.value).join(', '));

      // -----------------------------------------------------------------
      // Handlers
      // -----------------------------------------------------------------
      function onFiles(){
        pendingFiles.splice(0);
        const files = Array.from(filesInput.value?.files || []);
        for(const f of files){
          const name = f.name.toLowerCase();
          // Accept both PDF and EPUB files
          if(!name.endsWith('.pdf') && !name.endsWith('.epub')) continue;
          pendingFiles.push({
            file: f,
            name: f.name,
            size: f.size,
            progress: 0,
            status: 'pending', // pending | uploading | processing | success | error
            error: null,
            attempts: 0,
            resultMeta: null,
            _open:false
          });
        }
      }

      function formatSize(bytes){
        if(bytes < 1024) return bytes + ' B';
        if(bytes < 1048576) return (bytes/1024).toFixed(1) + ' KB';
        return (bytes/1048576).toFixed(2) + ' MB';
      }

      function submit(){
        if(!canSubmit.value) return;
        busy.value = true; metadataUnion.value = {};
        // Limit to 4 concurrent uploads as requested
        const queue = [...pendingFiles];
        let active = 0;
        const maxConcurrent = 4;

        function next(){
          if(active >= maxConcurrent) return;
          const item = queue.find(f=> f.status==='pending');
          if(!item){
            if(active===0) busy.value = false; // all done
            return;
          }
          item.status='uploading'; active++;
          uploadOne(item).finally(()=>{ active--; next(); });
          next();
        }

        next();
      }

      function uploadOne(entry){
        return new Promise((resolve)=>{
          const fd = new FormData();
          fd.append('Title', title.value);
          fd.append('Type', type.value);
          fd.append('file', entry.file);
          const xhr = new XMLHttpRequest();
          xhr.open('POST', '/api/upload');
          xhr.upload.onprogress = e => {
            if(!e.lengthComputable) return;
            entry.progress = Math.round((e.loaded / e.total) * 100);
          };
          xhr.onreadystatechange = () => {
            if(xhr.readyState !== 4) return;
            entry.status='processing'; // upload finished, waiting parse
            try {
              if(xhr.status>=200 && xhr.status<300){
                const data = JSON.parse(xhr.responseText);
                handleSingleResult(entry, data);
              } else {
                entry.status='error';
                entry.error = 'Upload failed HTTP ' + xhr.status;
              }
            } catch(err){
              entry.status='error';
              entry.error = 'Parse error';
            }
            if(entry.status==='success') entry.progress = 100; // processing done
            resolve();
          };
          xhr.send(fd);
        });
      }

      function handleSingleResult(entry, data){
        // Support camelCase (default) and PascalCase
        const filesArr = data.Files || data.files || [];
        const fileResult = filesArr[0];
        if(!fileResult){
          entry.status='error';
          entry.error='No file result';
          return;
        }
        const success = fileResult.Success !== undefined ? fileResult.Success : fileResult.success;
        entry.attempts = fileResult.Attempts !== undefined ? fileResult.Attempts : (fileResult.attempts || 0);
        entry.resultMeta = fileResult.AppliedMetadata || fileResult.appliedMetadata || null;
        if(!Object.keys(metadataUnion.value).length){
          metadataUnion.value = (data.Metadata || data.metadata || entry.resultMeta || {});
        }
        if(success){
          entry.status='success'; entry.error=null;
        } else {
          entry.status='error';
          entry.error = fileResult.ErrorMessage || fileResult.errorMessage || 'Unknown error';
        }
      }

      function toggle(entry){ entry._open = !entry._open; }

      function reset(){
        title.value=''; type.value='LightNovel'; metadataUnion.value={};
        pendingFiles.splice(0); if(filesInput.value) filesInput.value.value='';
      }

      // -----------------------------------------------------------------
      // Expose to template
      // -----------------------------------------------------------------
      return {
        title, type, filesInput, pendingFiles, busy, metadataUnion, year,
        canSubmit, successCount, failCount, hasMetadata, metadataKeys,
        onFiles, submit, reset, formatSize, toggle
      };
    }
  }).mount('#app');
})();
