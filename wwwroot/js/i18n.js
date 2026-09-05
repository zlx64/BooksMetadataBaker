// BooksMetadataBaker — UI locale registry
// ---------------------------------------------------------------------
// Assembles the per-locale catalogs (js/i18n/<code>.js) into
// the single BMB_I18N object consumed by app.js.
//
// To add a locale: create js/i18n/<code>.js exposing window.BMB_I18N_<CODE>
// = { pluralRules, messages }, load it before this file, and add <code>
// to LOCALES below.
// ---------------------------------------------------------------------
(function () {
  'use strict';

  const LOCALES = ['en', 'de', 'es', 'fr', 'ua', 'ja'];

  const catalog = {};
  for (const code of LOCALES) {
    const locale = window['BMB_I18N_' + code.toUpperCase()];
    if (!locale || !locale.messages) {
      throw new Error('BMB_I18N: missing locale catalog for "' + code + '"');
    }
    catalog[code] = locale;
  }

  window.BMB_I18N = {
    locales: LOCALES,
    pluralRules: Object.fromEntries(LOCALES.map((c) => [c, catalog[c].pluralRules])),
    messages: Object.fromEntries(LOCALES.map((c) => [c, catalog[c].messages]))
  };
})();
