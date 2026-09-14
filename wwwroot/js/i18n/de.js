// BooksMetadataBaker — German locale
// ---------------------------------------------------------------------
// Message format: vue-i18n (Intlify) syntax —
//   {name}   named interpolation
//   a | b    plural segments (de: one|other)
// ---------------------------------------------------------------------
(function () {
  'use strict';

  function dePlural(choice) {
    return Math.abs(Math.floor(choice)) === 1 ? 0 : 1;
  }

  window.BMB_I18N_DE = {
    pluralRules: dePlural,
    messages: {
      docTitle: 'Books Metadata Baker — Ebooks hinzufügen',
      brand: 'Ebook-Metadaten, mit Liebe gebacken',
      theme: {
        light: 'hell',
        dark: 'gemütlich dunkel',
        auto: 'Systemstandard',
        title: 'Design: {theme}',
        aria: 'Farbdesign: {theme}. Klicken zum Ändern.'
      },
      lang: {
        aria: 'Sprache der Benutzeroberfläche',
        en: 'English',
        de: 'Deutsch',
        es: 'Español',
        fr: 'Français',
        ua: 'Українська',
        ja: '日本語'
      },
      drop: {
        title: 'Ziehe deine PDF-, EPUB- oder Comic-Dateien hierher',
        sub: 'Wir fügen sie direkt zur Back-Warteschlange hinzu'
      },
      hero: {
        h1: 'Backe deine Ebook-Sammlung',
        lede: 'Lade deine PDF-, EPUB- oder Comic-Archive (CBZ, CBR, CB7, CBT) hoch. Books Metadata Baker sammelt sorgfältig Details von {anilist}, {google} und {comicvine}, bettet sie mit Calibre oder ComicInfo.xml ein, behebt PDF-Fehler mit Ghostscript und erstellt Kavita-bereite Dateien.'
      },
      step1: {
        heading: 'Buchdetails',
        title: 'Titel',
        titlePlaceholder: 'Reihe oder Buchtitel, z. B. The Saga of Tanya the Evil',
        titleRequired: 'Bitte gib einen Buchtitel ein.',
        hint: 'Dateien werden liebevoll im Ordner {type} als {name} gespeichert.',
        typeLegend: 'Buchtyp',
        apiKey: 'API-Schlüssel',
        apiKeyRequired: '— von diesem Server benötigt',
        apiKeyPlaceholder: 'Dein Server-API-Schlüssel',
        apiKeyHint: 'Sicher nur in diesem Browser gespeichert (localStorage).'
      },
      types: {
        Book: 'Buch',
        LightNovel: 'Light Novel',
        Manga: 'Manga',
        Comic: 'Comic'
      },
      step2: {
        heading: 'Ebook-Dateien',
        selected: '{n} Datei bereit · max. 500 MB je Datei | {n} Dateien bereit · max. 500 MB je Datei',
        browse: 'vom Gerät auswählen',
        dzTitle: 'Ziehe deine Dateien hierher oder {browse}',
        dzMore: 'Weitere Bücher hinzufügen',
        dzSub: 'PDF, EPUB oder Comic-Archive (CBZ/CBR/CB7/CBT oder ZIP/RAR/7Z/TAR — werden für dich umgewandelt) · bis zu 500 MB je Datei',
        skipped: '{n} nicht unterstützte Datei übersprungen: {list} | {n} nicht unterstützte Dateien übersprungen: {list}',
        remove: '{name} entfernen',
        progress: 'Bereite {name} vor',
        details: 'Details ▾',
        hideDetails: 'Details verbergen ▴',
        errorDetails: 'Nachsehen, was schiefging ▾',
        createdArchives: 'Erstellte Archive',
        emptyState: 'Dein Bücherregal ist noch leer — ziehe oben einige PDFs, EPUBs oder Comic-Archive hinein und klicke auf {bake}!',
        parallel: 'Bis zu {n} Dateien werden parallel verarbeitet. Server-Limits werden sanft gehandhabt — wartende Dateien gedulden sich und versuchen es automatisch erneut.',
        sourceTabsAria: 'Dateiquelle',
        sourceTabLocal: 'Dieses Gerät',
        sourceTabServer: 'Server-Volume',
        serverTag: 'Server'
      },
      server: {
        up: 'Ein Ordner höher',
        root: 'Stamm',
        refresh: 'Aktualisieren',
        loading: 'Ordner wird geladen …',
        empty: 'Dieser Ordner ist leer.',
        moveOriginals: 'Originaldateien in die Bibliothek verschieben (Quelldateien werden gelöscht)',
        addSelected: 'Füge {n} Datei zur Warteschlange hinzu | Füge {n} Dateien zur Warteschlange hinzu'
      },
      actionbar: {
        done: '{done} von {total} fertig',
        baked: '{n} gebacken',
        failed: '{n} benötigt Aufmerksamkeit',
        running: '{n} läuft',
        cancel: 'Abbrechen',
        retry: 'Fehlgeschlagene erneut versuchen ({n})',
        clear: 'Fertige entfernen',
        startTitle: 'Backen starten (Strg+Eingabe)',
        startHint: 'Füge zuerst einen Titel und mindestens eine Datei hinzu',
        baking: 'Mit Liebe am Backen… {done}/{total}',
        bake: '{n} Datei backen | {n} Dateien backen',
        bakeAll: 'Alle Dateien backen'
      },
      footer: {
        tip: 'Handlicher Tipp: Drücke jederzeit Strg+Eingabe, um das Backen zu starten!'
      },
      toasts: {
        added: '{n} Datei zum Regal hinzugefügt | {n} Dateien zum Regal hinzugefügt',
        enterTitle: 'Bitte gib zuerst einen Buchtitel ein',
        addFiles: 'Füge mindestens eine PDF-, EPUB- oder Comic-Datei hinzu, um fortzufahren',
        baked: '{n} Datei erfolgreich gebacken! | {n} Dateien erfolgreich gebacken!',
        failed: '{n} Datei konnte nicht gebacken werden | {n} Dateien konnten nicht gebacken werden',
        canceled: 'Abgebrochen — Dateien in der Warteschlange wurden nicht berührt',
        rateLimit: 'Kurze Durchschnaufpause für den Server — {name} versucht es in {n}s erneut'
      },
      status: {
        pending: 'In Warteschlange',
        uploading: 'Lädt hoch',
        processing: 'Backen',
        throttled: 'Warten',
        success: 'Gebacken!',
        error: 'Braucht Aufmerksamkeit',
        canceled: 'Abgebrochen'
      },
      phase: {
        pending: 'Wartet gemütlich in der Warteschlange',
        uploading: 'Lädt zum Server hoch…',
        processing: 'Sucht Metadaten & bettet Cover ein…',
        throttled: 'Server kühlt ab — erneuter Versuch in {n}s',
        success: 'In der Bibliothek als {name} gespeichert',
        canceled: 'Abgebrochen',
        error: 'Vorgang konnte nicht abgeschlossen werden'
      },
      errors: {
        tooLarge: 'Datei ist etwas zu groß — maximales Limit ist 500 MB',
        unexpected: 'Unerwartete Antwort vom Server',
        rateLimit: 'Server erhält zu viele Anfragen — bitte warte eine kurze Minute und versuche es erneut',
        network: 'Verbindung verloren — Server konnte nicht erreicht werden',
        timeout: 'Anfrage hat zu lange gedauert (30 Min. Limit)',
        http: 'Upload konnte nicht abgeschlossen werden (HTTP {status})',
        unauthorized: 'Authentifizierung erforderlich — bitte überprüfe deinen API-Schlüssel',
        canceled: 'Anfrage wurde abgebrochen',
        noResult: 'Keine Details für diese Datei gefunden',
        unknown: 'Etwas ist unerwartet schiefgelaufen'
      },
      facts: {
        attempts: 'Versuche: {n}',
        comicInfoYes: 'ComicInfo.xml: sorgfältig eingebettet',
        comicInfoNo: 'ComicInfo.xml: nicht eingebettet',
        pages: 'Seiten: {n}',
        split: 'in {n} Archiv aufgeteilt|in {n} Archive aufgeteilt',
        directOk: 'Direktes Einbetten: erfolgreich',
        directFail: 'Direktes Einbetten: fehlgeschlagen',
        repairOk: 'PDF-Reparatur: erfolgreich',
        repairFail: 'PDF-Reparatur: fehlgeschlagen',
        gsRan: 'Ghostscript-Bereinigung: abgeschlossen'
      },
      sr: {
        detailsHint: '. Drücken, um Details anzuzeigen',
        status: '{done} von {total} Dateien fertig. {ok} gebacken, {fail} fehlgeschlagen, {run} laufen derzeit.'
      },
      metaLabels: {
        Title: 'Titel',
        TitleEnglish: 'Titel (Englisch)',
        TitleRomaji: 'Titel (Romaji)',
        TitleNative: 'Titel (Original)',
        Subtitle: 'Untertitel',
        Authors: 'Autoren',
        Publisher: 'Verlag',
        PublishedDate: 'Erscheinungsdatum',
        StartDate: 'Startdatum',
        EndDate: 'Enddatum',
        StartYear: 'Startjahr',
        Genres: 'Genres',
        Categories: 'Kategorien',
        Tags: 'Tags',
        Format: 'Format',
        Status: 'Veröffentlichungsstatus',
        AverageScore: 'Durchschnittliche Leserwertung',
        Volumes: 'Bände',
        Chapters: 'Kapitel',
        PageCount: 'Seitenzahl',
        IssueCount: 'Ausgaben',
        Language: 'Sprache',
        Description: 'Beschreibung',
        Snippet: 'Auszug',
        Source: 'Quelle',
        SourceUrl: 'Quelllink',
        ApiDetailUrl: 'API-URL'
      }
    }
  };
})();