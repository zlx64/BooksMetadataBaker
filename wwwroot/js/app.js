// PrepKavita PDF Upload UI Logic
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
      const fileProgress = reactive([]); // [{ name,size,start,end,displayPct }]
      const totalSize = ref(0);
      const busy = ref(false);
      const results = reactive([]); // API response mapped list
      const metadata = ref({});
      const year = new Date().getFullYear();

      // -----------------------------------------------------------------
      // Derived State
      // -----------------------------------------------------------------
      // Allow upload when at least one file selected; title will still be HTML required on submit
      const canSubmit = computed(()=> fileProgress.length > 0);
      const metadataKeys = computed(()=> Object.keys(metadata.value).join(', '));
      const successCount = computed(()=> results.filter(r=>r.Success).length);
      const failCount = computed(()=> results.filter(r=>!r.Success).length);
      const hasMetadata = computed(()=> Object.keys(metadata.value).length>0);

      // -----------------------------------------------------------------
      // Handlers
      // -----------------------------------------------------------------
      function onFiles(){
        fileProgress.splice(0); totalSize.value = 0;
        const files = Array.from(filesInput.value?.files || []);
        if(!files.length) return;
        totalSize.value = files.reduce((a,f)=>a+f.size,0) || 1;
        let cursor = 0;
        for(const f of files){
          const proportion = f.size / totalSize.value;
          fileProgress.push({
            name: f.name,
            size: f.size,
            start: cursor,
            end: cursor + proportion,
            displayPct: 0
          });
          cursor += proportion;
        }
      }

      function formatSize(bytes){
        if(bytes < 1024) return bytes + ' B';
        if(bytes < 1048576) return (bytes/1024).toFixed(1) + ' KB';
        return (bytes/1048576).toFixed(2) + ' MB';
      }

      function submit(){
        if(!canSubmit.value) return;
        // Browsers will enforce required Title automatically now.
        busy.value = true; results.splice(0); metadata.value = {};
        const fd = new FormData();
        fd.append('Title', title.value);
        fd.append('Type', type.value);
        for(const f of filesInput.value.files) fd.append('files', f);

        const xhr = new XMLHttpRequest();
        xhr.open('POST', '/api/upload');

        // Upload progress mapping to each file proportionally
        xhr.upload.onprogress = e => {
          if(!e.lengthComputable) return;
          const pct = e.loaded / e.total; // 0..1 overall
          for(const fp of fileProgress){
            const local = pct <= fp.start ? 0 : (pct >= fp.end ? 1 : (pct - fp.start) / (fp.end - fp.start));
            fp.displayPct = local * 100;
          }
        };

        xhr.onreadystatechange = () => {
          if(xhr.readyState !== 4) return;
          busy.value = false;
          if(xhr.status >= 200 && xhr.status < 300){
            try {
              const data = JSON.parse(xhr.responseText);
              handleResult(data);
            } catch(err){
              console.error('Parse error', err);
              alert('Response parse error');
            }
          } else {
            console.error('Upload failed', xhr.status, xhr.responseText);
            alert('Upload failed');
          }
        };

        xhr.send(fd);
      }

      function handleResult(data){
        metadata.value = data.Metadata || {};
        results.splice(0);
        (data.Files || []).forEach(f => { f._open = false; results.push(f); });
      }

      function toggle(r){ r._open = !r._open; }

      function reset(){
        title.value = '';
        type.value = 'LightNovel';
        fileProgress.splice(0);
        totalSize.value = 0;
        results.splice(0);
        metadata.value = {};
        if(filesInput.value) filesInput.value.value='';
      }

      // -----------------------------------------------------------------
      // Expose to template
      // -----------------------------------------------------------------
      return {
        title, type, filesInput, fileProgress, totalSize, busy, results, metadata, year,
        canSubmit, metadataKeys, successCount, failCount, hasMetadata,
        onFiles, submit, reset, formatSize, toggle
      };
    }
  }).mount('#app');
})();
