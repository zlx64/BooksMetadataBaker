// BooksMetadataBaker — Ukrainian locale
// ---------------------------------------------------------------------
// Message format: vue-i18n (Intlify) syntax —
//   {name}   named interpolation
//   a | b | c  plural segments (ua: one|few|many)
// ---------------------------------------------------------------------
(function () {
  'use strict';

  function uaPlural(choice) {
    const n = Math.abs(Math.floor(choice));
    const d = n % 10;
    const h = n % 100;
    if (d === 1 && h !== 11) return 0;
    if (d >= 2 && d <= 4 && !(h >= 12 && h <= 14)) return 1;
    return 2;
  }

  window.BMB_I18N_UA = {
    pluralRules: uaPlural,
    messages: {
      docTitle: 'Books Metadata Baker — завантаження книжок',
      brand: 'Метадані для ваших книг, запечені з турботою',
      theme: {
        light: 'світла',
        dark: 'затишна темна',
        auto: 'як у системі',
        title: 'Тема: {theme}',
        aria: 'Кольорова тема: {theme}. Натисніть, щоб змінити.'
      },
      lang: {
        aria: 'Мова інтерфейсу',
        en: 'English',
        de: 'Deutsch',
        es: 'Español',
        fr: 'Français',
        ua: 'Українська',
        ja: '日本語'
      },
      drop: {
        title: 'Закидайте сюди PDF, EPUB чи улюблені комікси',
        sub: 'Ми одразу додамо їх до черги запікання'
      },
      hero: {
        h1: 'Запечіть свої електронки',
        lede: 'Завантажуйте ваші PDF, EPUB чи комікс-архіви (CBZ, CBR, CB7, CBT). Books Metadata Baker дбайливо збере метадані з {anilist}, {google} та {comicvine}, впорядкує їх через Calibre або ComicInfo.xml, виправить дрібні огріхи у PDF через Ghostscript і підготує ідеальні файли для Kavita.'
      },
      step1: {
        heading: 'Деталі про книгу',
        title: 'Назва',
        titlePlaceholder: 'Назва серії або книги, напр. The Saga of Tanya the Evil',
        titleRequired: 'Будь ласка, вкажіть назву книги.',
        hint: 'Файли будуть дбайливо збережені в папку {type} як {name}.',
        typeLegend: 'Формат видання',
        apiKey: 'Ключ доступу',
        apiKeyRequired: '— потрібен для цього сервера',
        apiKeyPlaceholder: 'Ваш API-ключ сервера',
        apiKeyHint: 'Безпечно зберігається лише у вашому браузері (localStorage).'
      },
      types: {
        Book: 'Книга',
        LightNovel: 'Ранобе',
        Manga: 'Манґа',
        Comic: 'Комікс'
      },
      step2: {
        heading: 'Файли для запікання',
        selected: 'Обрано {n} файл (до 500 МБ) | Обрано {n} файли (до 500 МБ) | Обрано {n} файлів (до 500 МБ)',
        browse: 'оберіть з пристрою',
        dzTitle: 'Перетягніть файли сюди або {browse}',
        dzMore: 'Додати ще книжок',
        dzSub: 'PDF, EPUB або комікс-архіви (CBZ/CBR/CB7/CBT, або ZIP/RAR/7Z/TAR — ми конвертуємо їх у зручний формат) · до 500 МБ кожен',
        skipped: 'Пропущено {n} непідтримуваний файл: {list} | Пропущено {n} непідтримувані файли: {list} | Пропущено {n} непідтримуваних файлів: {list}',
        remove: 'Прибрати {name}',
        progress: 'Готуємо {name}',
        details: 'деталі ▾',
        hideDetails: 'сховати деталі ▴',
        errorDetails: 'подивитися, що пішло не так ▾',
        createdArchives: 'Створені архіви',
        emptyState: 'Ваша полиця поки порожня — перетягніть PDF, EPUB чи комікс-архіви (CBZ/CBR/CB7/CBT, ZIP, RAR, 7Z, TAR) і натисніть {bake}.',
        parallel: 'Паралельно готується до {n} файлів. Обмеження сервера (10 завантажень/хв) обробляються м’яко — файли зачекають своєї черги й спробують знову.',
        sourceTabsAria: 'Джерело файлів',
        sourceTabLocal: 'Цей пристрій',
        sourceTabServer: 'Том сервера',
        serverTag: 'сервер'
      },
      server: {
        up: 'На рівень вище',
        root: 'корінь',
        refresh: 'Оновити',
        loading: 'Завантаження папки…',
        empty: 'Ця папка порожня.',
        moveOriginals: 'Перемістити оригінали до бібліотеки (початкові файли буде видалено)',
        addSelected: 'Додати {n} файл до черги | Додати {n} файли до черги | Додати {n} файлів до черги'
      },
      actionbar: {
        done: 'Готово {done} з {total}',
        baked: '{n} запечено',
        failed: '{n} потребує уваги',
        running: '{n} у процесі',
        cancel: 'Скасувати',
        retry: 'Спробувати знову ({n})',
        clear: 'Очистити готові',
        startTitle: 'Розпочати запікання (Ctrl+Enter)',
        startHint: 'Спочатку додайте назву та хоча б один файл',
        baking: 'Запікаємо з любов’ю… {done}/{total}',
        bake: 'Запекти {n} файл | Запекти {n} файли | Запекти {n} файлів',
        bakeAll: 'Запекти всі файли'
      },
      footer: {
        tip: 'Маленька підказка: натисніть Ctrl+Enter, щоб швидко розпочати запікання'
      },
      toasts: {
        added: 'Додано {n} файл | Додано {n} файли | Додано {n} файлів',
        enterTitle: 'Будь ласка, вкажіть назву книги',
        addFiles: 'Додайте хоча б один PDF, EPUB чи комікс',
        baked: 'Успішно запечено {n} файл! | Успішно запечено {n} файли! | Успішно запечено {n} файлів!',
        failed: 'На жаль, не вдалося запекти {n} файл | На жаль, не вдалося запекти {n} файли | На жаль, не вдалося запекти {n} файлів',
        canceled: 'Скасовано — файли в черзі відпочивають',
        rateLimit: 'Сервер трохи відпочиває — {name} спробує ще раз через {n} с'
      },
      status: {
        pending: 'У черзі',
        uploading: 'Завантаження',
        processing: 'Запікання',
        throttled: 'Очікування',
        success: 'Готово!',
        error: 'Помилка',
        canceled: 'Скасовано'
      },
      phase: {
        pending: 'Затишно чекає в черзі',
        uploading: 'Завантажуємо на сервер…',
        processing: 'Шукаємо метадані та додаємо обкладинки…',
        throttled: 'Невеликий перепочинок сервера — повторимо через {n} с',
        success: 'Збережено до колекції як {name}',
        canceled: 'Скасовано',
        error: 'Не вдалося обробити'
      },
      errors: {
        tooLarge: 'Файл трохи завеликий — максимальний розмір 500 МБ',
        unexpected: 'Отримано несподівану відповідь від сервера',
        rateLimit: 'Сервер трохи перевантажений — дайте йому хвилинку відпочити й спробуйте знову',
        network: 'Зв’язок втрачено — перевірте підключення до мережі',
        timeout: 'Час очікування минув (30 хв)',
        http: 'Не вдалося завантажити файл (HTTP {status})',
        unauthorized: 'Потрібна авторизація — будь ласка, вкажіть правильний API-ключ',
        canceled: 'Запит було скасовано',
        noResult: 'Не вдалося знайти інформацію про цей файл',
        unknown: 'Трапилася невідома халепа'
      },
      facts: {
        attempts: 'Спроб обробки: {n}',
        comicInfoYes: 'ComicInfo.xml: дбайливо вбудовано',
        comicInfoNo: 'ComicInfo.xml: не вбудовано',
        pages: 'Сторінок: {n}',
        split: 'розбито на {n} архів|розбито на {n} архіви|розбито на {n} архівів',
        directOk: 'Пряме вбудовування: успішно',
        directFail: 'Пряме вбудовування: не вдалося',
        repairOk: 'Відновлення PDF: успішно',
        repairFail: 'Відновлення PDF: не вдалося',
        gsRan: 'Очищення через Ghostscript: виконано'
      },
      sr: {
        detailsHint: '. Натисніть, щоб розкрити деталі',
        status: 'Опрацьовано {done} з {total} файлів. Успішно: {ok}, з помилками: {fail}, у процесі: {run}.'
      },
      metaLabels: {
        Title: 'Назва',
        TitleEnglish: 'Назва (англ.)',
        TitleRomaji: 'Назва (ромаджі)',
        TitleNative: 'Назва (оригінал)',
        Subtitle: 'Підзаголовок',
        Authors: 'Автори',
        Publisher: 'Видавництво',
        PublishedDate: 'Дата виходу',
        StartDate: 'Початок виходу',
        EndDate: 'Завершення',
        StartYear: 'Рік початку',
        Genres: 'Жанри',
        Categories: 'Категорії',
        Tags: 'Теги',
        Format: 'Формат',
        Status: 'Статус серії',
        AverageScore: 'Середня оцінка читачів',
        Volumes: 'Томів',
        Chapters: 'Розділів',
        PageCount: 'Кількість сторінок',
        IssueCount: 'Випусків',
        Language: 'Мова',
        Description: 'Опис книги',
        Snippet: 'Короткий уривок',
        Source: 'Джерело',
        SourceUrl: 'Посилання на джерело',
        ApiDetailUrl: 'API URL'
      }
    }
  };
})();