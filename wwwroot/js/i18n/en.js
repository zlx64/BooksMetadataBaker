// BooksMetadataBaker — English locale
// ---------------------------------------------------------------------
// Message format: vue-i18n (Intlify) syntax —
//   {name}   named interpolation
//   a | b    plural segments (en: one|other)
// ---------------------------------------------------------------------
(function () {
  'use strict';

  function enPlural(choice) {
    return Math.abs(Math.floor(choice)) === 1 ? 0 : 1;
  }

  window.BMB_I18N_EN = {
    pluralRules: enPlural,
    messages: {
      docTitle: 'Books Metadata Baker — Add Your Ebooks',
      brand: 'Ebook metadata, baked with care',
      theme: {
        light: 'light',
        dark: 'cozy dark',
        auto: 'system default',
        title: 'Theme: {theme}',
        aria: 'Color theme: {theme}. Click to change.'
      },
      lang: {
        aria: 'Interface language',
        en: 'English',
        de: 'Deutsch',
        es: 'Español',
        fr: 'Français',
        ua: 'Українська',
        ja: '日本語'
      },
      drop: {
        title: 'Drop your PDF, EPUB, or comic files here',
        sub: 'We’ll add them right to the baking queue'
      },
      hero: {
        h1: 'Bake your Ebook collection',
        lede: 'Upload your PDF, EPUB, or comic archive files (CBZ, CBR, CB7, CBT). Books Metadata Baker gently fetches details from {anilist}, {google}, and {comicvine}, embeds them with Calibre or ComicInfo.xml, fixes PDF quirks using Ghostscript, and creates Kavita-ready sidecars.'
      },
      step1: {
        heading: 'Book details',
        title: 'Title',
        titlePlaceholder: 'Series or book title, e.g. The Saga of Tanya the Evil',
        titleRequired: 'Please enter a book title.',
        hint: 'Files will be saved into the {type} folder as {name}.',
        typeLegend: 'Book type',
        apiKey: 'API key',
        apiKeyRequired: '— required by this server',
        apiKeyPlaceholder: 'Your server API key',
        apiKeyHint: 'Stored safely only in this browser (localStorage).'
      },
      types: {
        Book: 'Book',
        LightNovel: 'Light Novel',
        Manga: 'Manga',
        Comic: 'Comic'
      },
      step2: {
        heading: 'Ebook files',
        selected: '{n} file ready · up to 500 MB each | {n} files ready · up to 500 MB each',
        browse: 'browse from device',
        dzTitle: 'Drag & drop your files anywhere, or {browse}',
        dzMore: 'Add more books',
        dzSub: 'PDF, EPUB, or comic archives (CBZ/CBR/CB7/CBT, or ZIP/RAR/7Z/TAR — converted for you) · up to 500 MB each',
        skipped: 'Skipped {n} unsupported file: {list} | Skipped {n} unsupported files: {list}',
        remove: 'Remove {name}',
        progress: 'Preparing {name}',
        details: 'details ▾',
        hideDetails: 'hide details ▴',
        errorDetails: 'see what happened ▾',
        createdArchives: 'Created archives',
        emptyState: 'Your shelf is empty for now — drop some PDFs, EPUBs, or comic archives above, then hit {bake}!',
        parallel: 'Up to {n} files process in parallel. Server limits are handled smoothly — queued files will wait patiently and retry automatically.',
        sourceTabsAria: 'File source',
        sourceTabLocal: 'This device',
        sourceTabServer: 'Server volume',
        serverTag: 'server'
      },
      server: {
        up: 'Up one folder',
        root: 'root',
        refresh: 'Refresh',
        loading: 'Loading folder…',
        empty: 'This folder is empty.',
        moveOriginals: 'Move originals into the library (source files are deleted)',
        addSelected: 'Add {n} file to queue | Add {n} files to queue'
      },
      actionbar: {
        done: '{done} of {total} completed',
        baked: '{n} baked',
        failed: '{n} needs attention',
        running: '{n} in progress',
        cancel: 'Cancel',
        retry: 'Try failed again ({n})',
        clear: 'Clear finished',
        startTitle: 'Start baking (Ctrl+Enter)',
        startHint: 'Add a title and at least one file to get started',
        baking: 'Baking with care… {done}/{total}',
        bake: 'Bake {n} file | Bake {n} files',
        bakeAll: 'Bake all files'
      },
      footer: {
        tip: 'Handy tip: Press Ctrl+Enter to start baking anytime!'
      },
      toasts: {
        added: '{n} file added to shelf | {n} files added to shelf',
        enterTitle: 'Please enter a book title first',
        addFiles: 'Add at least one PDF, EPUB, or comic file to continue',
        baked: '{n} file baked successfully! | {n} files baked successfully!',
        failed: 'Could not bake {n} file | Could not bake {n} files',
        canceled: 'Canceled — queued files were left untouched',
        rateLimit: 'Taking a quick breather — {name} retries in {n}s'
      },
      status: {
        pending: 'In queue',
        uploading: 'Uploading',
        processing: 'Baking',
        throttled: 'Waiting',
        success: 'Baked!',
        error: 'Needs attention',
        canceled: 'Canceled'
      },
      phase: {
        pending: 'Waiting cozy in queue',
        uploading: 'Uploading to server…',
        processing: 'Fetching metadata & embedding cover art…',
        throttled: 'Server cooling down — retrying in {n}s',
        success: 'Saved to library as {name}',
        canceled: 'Canceled',
        error: 'Could not complete process'
      },
      errors: {
        tooLarge: 'File is a bit too large — max limit is 500 MB',
        unexpected: 'Unexpected response from the server',
        rateLimit: 'Server is receiving too many requests — take a quick minute and try again',
        network: 'Connection lost — couldn’t reach the server',
        timeout: 'Request took a bit too long (30 min limit)',
        http: 'Upload couldn’t finish (HTTP {status})',
        unauthorized: 'Authentication needed — please check your API key',
        canceled: 'Request was canceled',
        noResult: 'No details were found for this file',
        unknown: 'Something went wrong unexpectedly'
      },
      facts: {
        attempts: 'Attempts: {n}',
        comicInfoYes: 'ComicInfo.xml: embedded with care',
        comicInfoNo: 'ComicInfo.xml: not embedded',
        pages: 'Pages: {n}',
        split: 'split into {n} archive|split into {n} archives',
        directOk: 'Direct embed: successful',
        directFail: 'Direct embed: failed',
        repairOk: 'PDF repair pass: successful',
        repairFail: 'PDF repair pass: failed',
        gsRan: 'Ghostscript cleanup: completed'
      },
      sr: {
        detailsHint: '. Press to expand details',
        status: '{done} of {total} files completed. {ok} baked, {fail} failed, {run} currently running.'
      },
      metaLabels: {
        Title: 'Title',
        TitleEnglish: 'Title (English)',
        TitleRomaji: 'Title (Romaji)',
        TitleNative: 'Title (Original)',
        Subtitle: 'Subtitle',
        Authors: 'Authors',
        Publisher: 'Publisher',
        PublishedDate: 'Release Date',
        StartDate: 'Start Date',
        EndDate: 'End Date',
        StartYear: 'Start Year',
        Genres: 'Genres',
        Categories: 'Categories',
        Tags: 'Tags',
        Format: 'Format',
        Status: 'Release Status',
        AverageScore: 'Average Reader Score',
        Volumes: 'Volumes',
        Chapters: 'Chapters',
        PageCount: 'Page Count',
        IssueCount: 'Issues',
        Language: 'Language',
        Description: 'Description',
        Snippet: 'Snippet',
        Source: 'Source',
        SourceUrl: 'Source Link',
        ApiDetailUrl: 'API URL'
      }
    }
  };
})();