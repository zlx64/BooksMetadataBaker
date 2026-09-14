// BooksMetadataBaker — Japanese locale
// ---------------------------------------------------------------------
// Message format: vue-i18n (Intlify) syntax —
//   {name}   named interpolation
//   a        plural segments (ja: other)
// ---------------------------------------------------------------------
(function () {
  'use strict';

  function jaPlural() {
    return 0; // Japanese does not use plural forms
  }

  window.BMB_I18N_JA = {
    pluralRules: jaPlural,
    messages: {
      docTitle: 'Books Metadata Baker — 電子書籍の追加',
      brand: '愛を込めて焼き上げる、あなたのための本棚メタデータ',
      theme: {
        light: 'ライト',
        dark: '落ち着いたダーク',
        auto: 'システム設定に合わせる',
        title: 'テーマ: {theme}',
        aria: 'カラーテーマ: {theme}。クリックして変更できます。'
      },
      lang: {
        aria: '表示言語',
        en: 'English',
        de: 'Deutsch',
        es: 'Español',
        fr: 'Français',
        ua: 'Українська',
        ja: '日本語'
      },
      drop: {
        title: 'PDF、EPUB、またはマンガファイルをここにドロップ',
        sub: 'すぐに焼き上げキューに追加します'
      },
      hero: {
        h1: 'お気に入りの電子書籍を焼き上げましょう',
        lede: 'PDF、EPUB、またはコミックアーカイブ（CBZ、CBR、CB7、CBT）をアップロードできます。Books Metadata Bakerが {anilist}、{google}、{comicvine} から情報を丁寧に収集し、CalibreやComicInfo.xmlを使って組み込みます。GhostscriptによるPDFの修復にも対応し、Kavitaでそのまま楽しめるファイルを準備します。'
      },
      step1: {
        heading: '書籍の詳細情報',
        title: 'タイトル',
        titlePlaceholder: '作品名または書籍タイトル（例: 幼女戦記）',
        titleRequired: 'タイトルを入力してください。',
        hint: 'ファイルは {type} フォルダに {name} として大切に保存されます。',
        typeLegend: '書籍のタイプ',
        apiKey: 'APIキー',
        apiKeyRequired: '— このサーバーで必須です',
        apiKeyPlaceholder: 'サーバーAPIキー',
        apiKeyHint: 'このブラウザ内（localStorage）にのみ安全に保存されます。'
      },
      types: {
        Book: '書籍',
        LightNovel: 'ライトノベル',
        Manga: 'マンガ',
        Comic: 'コミック'
      },
      step2: {
        heading: '焼き上げるファイル',
        selected: '{n} 個のファイルを選択中（1ファイルにつき最大500MB）',
        browse: '端末から選択する',
        dzTitle: 'ファイルをここにドラッグ＆ドロップ、または {browse}',
        dzMore: 'さらに本を追加する',
        dzSub: 'PDF、EPUB、またはコミックアーカイブ（CBZ/CBR/CB7/CBT、ZIP/RAR/7Z/TARは自動変換されます）· 各最大500MBまで',
        skipped: '未対応のファイル {n} 個をスキップしました: {list}',
        remove: '{name} を取り除く',
        progress: '{name} を準備中',
        details: '詳細を見る ▾',
        hideDetails: '詳細を隠す ▴',
        errorDetails: 'エラーの理由を確認 ▾',
        createdArchives: '作成されたアーカイブ',
        emptyState: '本棚はまだ空っぽです — 上のエリアにPDF、EPUB、マンガファイルをドロップして {bake} を押してください！',
        parallel: '最大 {n} 個のファイルを並行して処理します。サーバーの制限に合わせて優しく調整されるため、順番待ちのファイルも自動的に再試行されます。',
        sourceTabsAria: 'ファイルのソース',
        sourceTabLocal: 'この端末',
        sourceTabServer: 'サーバー上のフォルダー',
        serverTag: 'サーバー'
      },
      server: {
        up: '上一階層へ',
        root: 'ルート',
        refresh: '更新',
        loading: 'フォルダーを読み込み中…',
        empty: 'このフォルダーは空です。',
        moveOriginals: '元のファイルをライブラリへ移動する（元のファイルは削除されます）',
        addSelected: '選択した{n}件をキューに追加'
      },
      actionbar: {
        done: '{total} 件中 {done} 件完了',
        baked: '{n} 件焼き上がり',
        failed: '{n} 件の確認が必要',
        running: '{n} 件処理中',
        cancel: 'キャンセル',
        retry: '失敗したファイルを再試行 ({n})',
        clear: '完了したものをクリア',
        startTitle: '焼き上げを開始 (Ctrl+Enter)',
        startHint: '最初にタイトルとファイルを1つ以上追加してください',
        baking: '心を込めて焼き上げ中… {done}/{total}',
        bake: '{n} 個のファイルを焼き上げる',
        bakeAll: 'すべてのファイルを焼き上げる'
      },
      footer: {
        tip: 'ワンポイントヒント: Ctrl+Enter を押すと、いつでもすぐに焼き上げを開始できます！'
      },
      toasts: {
        added: '{n} 個のファイルを本棚に追加しました',
        enterTitle: '最初に書籍タイトルを入力してください',
        addFiles: '続けるには、PDF、EPUB、またはマンガファイルを1つ以上追加してください',
        baked: '{n} 個のファイルの焼き上げが成功しました！',
        failed: '{n} 個のファイルの焼き上げに失敗しました',
        canceled: 'キャンセルされました — キュー内のファイルは変更されていません',
        rateLimit: 'サーバーが少し休憩中です — {name} は {n} 秒後に再試行します'
      },
      status: {
        pending: '順番待ち',
        uploading: 'アップロード中',
        processing: '焼き上げ中',
        throttled: '待機中',
        success: '焼き上がり！',
        error: '確認が必要',
        canceled: 'キャンセル済み'
      },
      phase: {
        pending: 'キューの中でゆったり待機中',
        uploading: 'サーバーへアップロード中…',
        processing: 'メタデータを検索してカバー画像を挿入中…',
        throttled: 'サーバーのお休み中 — {n} 秒後に再試行します',
        success: 'ライブラリに {name} として保存されました',
        canceled: 'キャンセルされました',
        error: '処理を完了できませんでした'
      },
      errors: {
        tooLarge: 'ファイルが少し大きすぎます — 上限は500MBです',
        unexpected: 'サーバーから予期しない応答を受け取りました',
        rateLimit: 'リクエストが集中しています — 少し時間をおいてから再試行してください',
        network: '通信エラー — サーバーに接続できませんでした',
        timeout: 'タイムアウトしました（上限30分）',
        http: 'アップロードを完了できませんでした (HTTP {status})',
        unauthorized: '認証が必要です — APIキーを確認してください',
        canceled: 'リクエストがキャンセルされました',
        noResult: 'このファイルのデータが見つかりませんでした',
        unknown: '予期せぬ問題が発生しました'
      },
      facts: {
        attempts: '試行回数: {n}',
        comicInfoYes: 'ComicInfo.xml: 丁寧に埋め込み済み',
        comicInfoNo: 'ComicInfo.xml: 未埋め込み',
        pages: 'ページ数: {n}',
        split: '{n} 個のアーカイブに分割しました',
        directOk: '直接埋め込み: 成功',
        directFail: '直接埋め込み: 失敗',
        repairOk: 'PDF修復: 成功',
        repairFail: 'PDF修復: 失敗',
        gsRan: 'Ghostscriptクリーンアップ: 完了'
      },
      sr: {
        detailsHint: '. 押すと詳細が開きます',
        status: '{total} 件中 {done} 件完了。成功 {ok} 件、失敗 {fail} 件、現在処理中 {run} 件。'
      },
      metaLabels: {
        Title: 'タイトル',
        TitleEnglish: 'タイトル（英語）',
        TitleRomaji: 'タイトル（ローマ字）',
        TitleNative: 'タイトル（原題）',
        Subtitle: 'サブタイトル',
        Authors: '著者',
        Publisher: '出版社',
        PublishedDate: '刊行日',
        StartDate: '連載開始日',
        EndDate: '連載終了日',
        StartYear: '開始年',
        Genres: 'ジャンル',
        Categories: 'カテゴリー',
        Tags: 'タグ',
        Format: 'フォーマット',
        Status: '刊行状況',
        AverageScore: '読者の平均評価',
        Volumes: '巻数',
        Chapters: '話数',
        PageCount: 'ページ数',
        IssueCount: '号数',
        Language: '言語',
        Description: 'あらすじ・概要',
        Snippet: '抜粋',
        Source: '情報源',
        SourceUrl: '情報源リンク',
        ApiDetailUrl: 'API URL'
      }
    }
  };
})();