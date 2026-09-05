// BooksMetadataBaker — French locale
// ---------------------------------------------------------------------
// Message format: vue-i18n (Intlify) syntax —
//   {name}   named interpolation
//   a | b    plural segments (fr: one|other)
// ---------------------------------------------------------------------
(function () {
  'use strict';

  function frPlural(choice) {
    const n = Math.abs(Math.floor(choice));
    return n <= 1 ? 0 : 1;
  }

  window.BMB_I18N_FR = {
    pluralRules: frPlural,
    messages: {
      docTitle: 'Books Metadata Baker — Ajouter vos eBooks',
      brand: 'Des métadonnées d’eBooks, cuites avec amour',
      theme: {
        light: 'lumineux',
        dark: 'sombre et cocooning',
        auto: 'par défaut du système',
        title: 'Thème : {theme}',
        aria: 'Thème de couleur : {theme}. Cliquer pour changer.'
      },
      lang: {
        aria: 'Langue de l’interface',
        en: 'English',
        de: 'Deutsch',
        es: 'Español',
        fr: 'Français',
        ua: 'Українська',
        ja: '日本語'
      },
      drop: {
        title: 'Déposez vos fichiers PDF, EPUB ou comics ici',
        sub: 'Nous les ajouterons directement à la file de cuisson'
      },
      hero: {
        h1: 'Faites cuire votre collection d’eBooks',
        lede: 'Téléversez vos fichiers PDF, EPUB ou archives de comics (CBZ, CBR, CB7, CBT). Books Metadata Baker récupère délicatement les détails depuis {anilist}, {google} et {comicvine}, les intègre avec Calibre ou ComicInfo.xml, répare les petits défauts des PDF avec Ghostscript et prépare des fichiers prêts pour Kavita.'
      },
      step1: {
        heading: '1. Détails du livre',
        title: 'Titre',
        titlePlaceholder: 'Titre de la série ou du livre, ex. The Saga of Tanya the Evil',
        titleRequired: 'Veuillez saisir un titre de livre.',
        hint: 'Les fichiers seront enregistrés avec soin dans le dossier {type} sous le nom {name}.',
        typeLegend: 'Type de livre',
        apiKey: 'Clé API',
        apiKeyRequired: '— requise par ce serveur',
        apiKeyPlaceholder: 'Votre clé API serveur',
        apiKeyHint: 'Stockée en toute sécurité dans ce navigateur uniquement (localStorage).'
      },
      types: {
        Book: 'Livre',
        LightNovel: 'Light Novel',
        Manga: 'Manga',
        Comic: 'Comic'
      },
      step2: {
        heading: '2. Fichiers eBook',
        selected: '{n} fichier prêt · max 500 Mo chacun | {n} fichiers prêts · max 500 Mo chacun',
        browse: 'parcourir votre appareil',
        dzTitle: 'Glissez-déposez vos fichiers n’importe où, ou {browse}',
        dzMore: 'Ajouter d’autres livres',
        dzSub: 'PDF, EPUB ou archives de comics (CBZ/CBR/CB7/CBT, ou ZIP/RAR/7Z/TAR — convertis pour vous) · jusqu’à 500 Mo chacun',
        skipped: '{n} fichier non pris en charge ignoré : {list} | {n} fichiers non pris en charge ignorés : {list}',
        remove: 'Retirer {name}',
        progress: 'Préparation de {name}',
        details: 'détails ▾',
        hideDetails: 'masquer les détails ▴',
        errorDetails: 'voir ce qu’il s’est passé ▾',
        emptyState: 'Votre étagère est vide pour l’instant — déposez des PDF, EPUB ou archives de comics ci-dessus, puis cliquez sur {bake} !',
        parallel: 'Jusqu’à {n} fichiers traités en parallèle. Les limites du serveur sont gérées en douceur — les fichiers en attente patienteront et réessaieront automatiquement.'
      },
      meta: {
        heading: 'Métadonnées fraîchement cuites',
        from: 'provenant de {source}'
      },
      actionbar: {
        done: '{done} sur {total} terminés',
        baked: '{n} cuit',
        failed: '{n} demande une attention',
        running: '{n} en cours',
        cancel: 'Annuler',
        retry: 'Réessayer les échecs ({n})',
        clear: 'Effacer les terminés',
        startTitle: 'Lancer la cuisson (Ctrl+Entrée)',
        startHint: 'Ajoutez d’abord un titre et au moins un fichier',
        baking: 'Cuisson avec amour… {done}/{total}',
        bake: 'Cuire {n} fichier | Cuire {n} fichiers',
        bakeAll: 'Cuire tous les fichiers'
      },
      footer: {
        tip: 'Petit conseil : appuyez sur Ctrl+Entrée pour lancer la cuisson à tout moment !'
      },
      toasts: {
        added: '{n} fichier ajouté à l’étagère | {n} fichiers ajoutés à l’étagère',
        enterTitle: 'Veuillez d’abord saisir un titre de livre',
        addFiles: 'Ajoutez au moins un fichier PDF, EPUB ou comic pour continuer',
        baked: '{n} fichier cuit avec succès ! | {n} fichiers cuits avec succès !',
        failed: 'Impossible de cuire {n} fichier | Impossible de cuire {n} fichiers',
        canceled: 'Annulé — les fichiers en file d’attente sont restés intacts',
        rateLimit: 'Le serveur fait une petite pause — {name} réessaiera dans {n}s'
      },
      status: {
        pending: 'En file d’attente',
        uploading: 'Téléversement',
        processing: 'Cuisson',
        throttled: 'En attente',
        success: 'Cuit !',
        error: 'Demande une attention',
        canceled: 'Annulé'
      },
      phase: {
        pending: 'Attend confortablement en file d’attente',
        uploading: 'Téléversement vers le serveur…',
        processing: 'Recherche de métadonnées et ajout de la couverture…',
        throttled: 'Le serveur se repose — nouvelle tentative dans {n}s',
        success: 'Enregistré dans la bibliothèque sous {name}',
        canceled: 'Annulé',
        error: 'Le processus n’a pas pu se terminer'
      },
      errors: {
        tooLarge: 'Le fichier est un peu trop grand — limite max de 500 Mo',
        unexpected: 'Réponse inattendue du serveur',
        rateLimit: 'Le serveur reçoit trop de requêtes — prenez une petite minute et réessayez',
        network: 'Connexion perdue — impossible de joindre le serveur',
        timeout: 'La requête a pris un peu trop de temps (limite de 30 min)',
        http: 'Le téléversement n’a pas pu se terminer (HTTP {status})',
        unauthorized: 'Authentification requise — veuillez vérifier votre clé API',
        canceled: 'La requête a été annulée',
        noResult: 'Aucune information trouvée pour ce fichier',
        unknown: 'Un problème inattendu est survenu'
      },
      facts: {
        attempts: 'Tentatives : {n}',
        comicInfoYes: 'ComicInfo.xml : intégré avec soin',
        comicInfoNo: 'ComicInfo.xml : non intégré',
        pages: 'Pages : {n}',
        directOk: 'Intégration directe : réussie',
        directFail: 'Intégration directe : échouée',
        repairOk: 'Réparation PDF : réussie',
        repairFail: 'Réparation PDF : échouée',
        gsRan: 'Nettoyage Ghostscript : terminé'
      },
      sr: {
        detailsHint: '. Appuyez pour afficher les détails',
        status: '{done} sur {total} fichiers terminés. {ok} cuits, {fail} échoués, {run} en cours.'
      },
      metaLabels: {
        Title: 'Titre',
        TitleEnglish: 'Titre (Anglais)',
        TitleRomaji: 'Titre (Romaji)',
        TitleNative: 'Titre (Original)',
        Subtitle: 'Sous-titre',
        Authors: 'Auteurs',
        Publisher: 'Éditeur',
        PublishedDate: 'Date de parution',
        StartDate: 'Date de début',
        EndDate: 'Date de fin',
        StartYear: 'Année de début',
        Genres: 'Genres',
        Categories: 'Catégories',
        Tags: 'Étiquettes',
        Format: 'Format',
        Status: 'Statut de parution',
        AverageScore: 'Note moyenne des lecteurs',
        Volumes: 'Volumes',
        Chapters: 'Chapitres',
        PageCount: 'Nombre de pages',
        IssueCount: 'Numéros',
        Language: 'Langue',
        Description: 'Description',
        Snippet: 'Extrait',
        Source: 'Source',
        SourceUrl: 'Lien source',
        ApiDetailUrl: 'URL API'
      }
    }
  };
})();