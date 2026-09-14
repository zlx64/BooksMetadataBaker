// BooksMetadataBaker — Spanish locale
// ---------------------------------------------------------------------
// Message format: vue-i18n (Intlify) syntax —
//   {name}   named interpolation
//   a | b    plural segments (es: one|other)
// ---------------------------------------------------------------------
(function () {
  'use strict';

  function esPlural(choice) {
    return Math.abs(Math.floor(choice)) === 1 ? 0 : 1;
  }

  window.BMB_I18N_ES = {
    pluralRules: esPlural,
    messages: {
      docTitle: 'Books Metadata Baker — Añade tus Ebooks',
      brand: 'Metadatos para tus libros, horneados con amor',
      theme: {
        light: 'claro',
        dark: 'acogedor oscuro',
        auto: 'predeterminado del sistema',
        title: 'Tema: {theme}',
        aria: 'Tema de color: {theme}. Haz clic para cambiar.'
      },
      lang: {
        aria: 'Idioma de la interfaz',
        en: 'English',
        de: 'Deutsch',
        es: 'Español',
        fr: 'Français',
        ua: 'Українська',
        ja: '日本語'
      },
      drop: {
        title: 'Arrastra tus archivos PDF, EPUB o cómics aquí',
        sub: 'Los añadiremos directamente a la cola de horneado'
      },
      hero: {
        h1: 'Hornea tu colección de Ebooks',
        lede: 'Sube tus archivos PDF, EPUB o archivos de cómic (CBZ, CBR, CB7, CBT). Books Metadata Baker recopila con cariño los detalles de {anilist}, {google} y {comicvine}, los integra con Calibre o ComicInfo.xml, corrige pequeños detalles de PDF con Ghostscript y genera archivos listos para Kavita.'
      },
      step1: {
        heading: 'Detalles del libro',
        title: 'Título',
        titlePlaceholder: 'Título de la serie o libro, ej. The Saga of Tanya the Evil',
        titleRequired: 'Por favor, introduce un título.',
        hint: 'Los archivos se guardarán con cariño en la carpeta {type} como {name}.',
        typeLegend: 'Tipo de libro',
        apiKey: 'Clave API',
        apiKeyRequired: '— requerida por este servidor',
        apiKeyPlaceholder: 'Tu clave API del servidor',
        apiKeyHint: 'Guardada de forma segura solo en este navegador (localStorage).'
      },
      types: {
        Book: 'Libro',
        LightNovel: 'Novela ligera',
        Manga: 'Manga',
        Comic: 'Cómic'
      },
      step2: {
        heading: 'Archivos Ebook',
        selected: '{n} archivo listo · máx. 500 MB cada uno | {n} archivos listos · máx. 500 MB cada uno',
        browse: 'buscar en el dispositivo',
        dzTitle: 'Arrastra y suelta tus archivos aquí, o {browse}',
        dzMore: 'Añadir más libros',
        dzSub: 'PDF, EPUB o archivos de cómic (CBZ/CBR/CB7/CBT, o ZIP/RAR/7Z/TAR — los convertimos por ti) · hasta 500 MB cada uno',
        skipped: 'Se omitió {n} archivo no compatible: {list} | Se omitieron {n} archivos no compatibles: {list}',
        remove: 'Quitar {name}',
        progress: 'Preparando {name}',
        details: 'detalles ▾',
        hideDetails: 'ocultar detalles ▴',
        errorDetails: 'ver qué ha pasado ▾',
        createdArchives: 'Archivos creados',
        emptyState: 'Tu estantería está vacía por ahora — ¡suelta algunos PDF, EPUB o archivos de cómic arriba y haz clic en {bake}!',
        parallel: 'Hasta {n} archivos procesados en paralelo. Los límites del servidor se gestionan suavemente — los archivos en cola esperarán pacientemente y reintentarán automáticamente.',
        sourceTabsAria: 'Origen de los archivos',
        sourceTabLocal: 'Este dispositivo',
        sourceTabServer: 'Volumen del servidor',
        serverTag: 'servidor'
      },
      server: {
        up: 'Subir un nivel',
        root: 'raíz',
        refresh: 'Actualizar',
        loading: 'Cargando carpeta…',
        empty: 'Esta carpeta está vacía.',
        moveOriginals: 'Mover los originales a la biblioteca (se eliminarán los archivos de origen)',
        addSelected: 'Añadir {n} archivo a la cola | Añadir {n} archivos a la cola'
      },
      actionbar: {
        done: '{done} de {total} completados',
        baked: '{n} horneado',
        failed: '{n} requiere atención',
        running: '{n} en proceso',
        cancel: 'Cancelar',
        retry: 'Reintentar fallidos ({n})',
        clear: 'Limpiar completados',
        startTitle: 'Iniciar horneado (Ctrl+Enter)',
        startHint: 'Añade un título y al menos un archivo para empezar',
        baking: 'Horneando con amor… {done}/{total}',
        bake: 'Hornear {n} archivo | Hornear {n} archivos',
        bakeAll: 'Hornear todos los archivos'
      },
      footer: {
        tip: 'Consejo útil: ¡Pulsa Ctrl+Enter para empezar a hornear en cualquier momento!'
      },
      toasts: {
        added: '{n} archivo añadido a la estantería | {n} archivos añadidos a la estantería',
        enterTitle: 'Por favor, introduce primero un título de libro',
        addFiles: 'Añade al menos un archivo PDF, EPUB o cómic para continuar',
        baked: '¡{n} archivo horneado con éxito! | ¡{n} archivos horneados con éxito!',
        failed: 'No se pudo hornear {n} archivo | No se pudieron hornear {n} archivos',
        canceled: 'Cancelado — los archivos en cola se mantuvieron intactos',
        rateLimit: 'El servidor se toma un breve descanso — {name} lo reintentará en {n}s'
      },
      status: {
        pending: 'En cola',
        uploading: 'Subiendo',
        processing: 'Horneando',
        throttled: 'Esperando',
        success: '¡Horneado!',
        error: 'Requiere atención',
        canceled: 'Cancelado'
      },
      phase: {
        pending: 'Esperando cómodamente en la cola',
        uploading: 'Subiendo al servidor…',
        processing: 'Buscando metadatos y añadiendo portada…',
        throttled: 'El servidor se está descansando — reintentando en {n}s',
        success: 'Guardado en la biblioteca como {name}',
        canceled: 'Cancelado',
        error: 'No se pudo completar el proceso'
      },
      errors: {
        tooLarge: 'El archivo es un poco grande — el límite máximo es de 500 MB',
        unexpected: 'Respuesta inesperada del servidor',
        rateLimit: 'El servidor recibe demasiadas peticiones — tómate un minuto y vuelve a intentarlo',
        network: 'Conexión perdida — no se pudo contactar con el servidor',
        timeout: 'La petición tardó demasiado tiempo (límite de 30 min)',
        http: 'La subida no pudo completarse (HTTP {status})',
        unauthorized: 'Se requiere autenticación — por favor, comprueba tu clave API',
        canceled: 'La petición fue cancelada',
        noResult: 'No se encontraron detalles para este archivo',
        unknown: 'Ha ocurrido un problema inesperado'
      },
      facts: {
        attempts: 'Intentos: {n}',
        comicInfoYes: 'ComicInfo.xml: integrado con cariño',
        comicInfoNo: 'ComicInfo.xml: no integrado',
        pages: 'Páginas: {n}',
        split: 'dividido en {n} archivo|dividido en {n} archivos',
        directOk: 'Integración directa: con éxito',
        directFail: 'Integración directa: fallida',
        repairOk: 'Reparación de PDF: con éxito',
        repairFail: 'Reparación de PDF: fallida',
        gsRan: 'Limpieza con Ghostscript: completada'
      },
      sr: {
        detailsHint: '. Pulsa para desplegar detalles',
        status: '{done} de {total} archivos completados. {ok} horneados, {fail} fallidos, {run} actualmente en proceso.'
      },
      metaLabels: {
        Title: 'Título',
        TitleEnglish: 'Título (Inglés)',
        TitleRomaji: 'Título (Romaji)',
        TitleNative: 'Título (Original)',
        Subtitle: 'Subtítulo',
        Authors: 'Autores',
        Publisher: 'Editorial',
        PublishedDate: 'Fecha de publicación',
        StartDate: 'Fecha de inicio',
        EndDate: 'Fecha de fin',
        StartYear: 'Año de inicio',
        Genres: 'Géneros',
        Categories: 'Categorías',
        Tags: 'Etiquetas',
        Format: 'Formato',
        Status: 'Estado de publicación',
        AverageScore: 'Puntuación media de lectores',
        Volumes: 'Volúmenes',
        Chapters: 'Capítulos',
        PageCount: 'Número de páginas',
        IssueCount: 'Números',
        Language: 'Idioma',
        Description: 'Descripción',
        Snippet: 'Fragmento',
        Source: 'Fuente',
        SourceUrl: 'Enlace de la fuente',
        ApiDetailUrl: 'URL de API'
      }
    }
  };
})();