# Localisation — the browser proposes, the account decides

The webmail speaks English and only English: every label, button, tooltip, toast and error is a
string literal sitting in the component that draws it. This adds French beside it, picks a language
from the browser by default, and lets the account override that choice from Settings.

The whole interface is translated in one go — login, shell, mail, composer, contacts, every settings
page and the administration tabs. A half-translated screen is worse than an untranslated one: it
reads as a rendering fault rather than as a language.

## What this is not

It does not translate content. A received message's body, subject and sender are the correspondent's
words; a folder the user created is the user's own name for it. Neither is interface.

It does not localise the microservice. The API keeps answering in English, and the client turns its
stable error codes into French — see *Errors* below.

It adds no third language. `en` and `fr`, with a resolution chain and a catalogue shape that a third
would slot into without redesign, but nothing speculative built for one.

## Resolving the language

One preference, `ui.language`, taking `auto`, `en` or `fr`, with `auto` the default. The effective
locale resolves through four links:

1. the stored preference, when it is not `auto`
2. the localStorage mirror
3. `navigator.languages`, first entry whose primary subtag is supported
4. `en`

The rule lives in one pure function, `src/lib/locale.ts`:

```ts
export type Locale = 'en' | 'fr'

/** `stored` is the server preference when one has arrived, else the mirror, else undefined —
    the caller reads both, this decides. `preferred` is `navigator.languages`. */
export function resolveLocale(
  stored: string | undefined,
  mirrored: string | undefined,
  preferred: readonly string[],
): Locale
```

Pure over its three inputs, so every path is testable without a DOM, a network call or a fake clock.
`auto` and an unrecognised value are treated alike at each link: both fall through to the next.

**The localStorage mirror is not a convenience copy.** It covers two cases the server cannot. The
login page has no session and therefore no preferences to read, yet it is the first screen a new
device shows. And the app's first render precedes the answer to `GET /api/Preferences`, so without
the mirror the interface would paint in English and swap to French under the reader — the same class
of defect the pre-paint theme script in `index.html` exists to prevent.

The mirror is written whenever preferences arrive or the setting changes. It is **not** cleared when
a session ends. Clearing it would send the login page back to the browser's language on every sign
out, which is the opposite of the service it renders; it carries nothing secret, and the next
session's first `GET /api/Preferences` overwrites it. This is a deliberate departure from
`AuthContext`'s flush-everything-on-`isLoggedIn` rule, which exists for cached account data.

`index.html`'s pre-paint script sets `document.documentElement.lang` from the mirror, beside the
theme and palette it already sets. That also corrects the hard-coded `<html lang="fr">` shipping
today, which has always been wrong — the interface it describes is English.

## The brick

`i18next` plus `react-i18next`, and deliberately **not** `i18next-browser-languagedetector`. The
detector knows nothing about `GET /api/Preferences`; it would read the browser and hold an opinion
that disagrees with the chain above. One source of truth: the chain resolves, and the app calls
`i18n.changeLanguage` itself.

Catalogues live under `src/locales/<locale>/<namespace>.json`, the namespaces following the module
tree: `common`, `auth`, `mail`, `compose`, `contacts`, `settings`, `admin`, `errors`. Keys are
`zone.element` within a namespace — `mail:reader.actions.delete`.

**One catalogue is loaded, the active one**, through a dynamic `import()` awaited before the first
render. Neither language weighs on the main bundle, and there is no flash. Switching language at
runtime imports the other catalogue and then calls `changeLanguage`.

**There is no runtime `fallbackLng`**, because a key-parity test makes one unnecessary: a key present
in one catalogue and absent from the other fails the build. The repo already runs exactly this check
over its six palette files.

`t()` is typed: `CustomTypeOptions['resources']` is bound to `typeof en`, so a key that does not
exist is a compile error. `tsc --noEmit` already runs in CI, so this replaces a key-extraction tool
rather than adding one.

## Tests — the real execution risk

121 test files carry **1817 assertions against visible text** (`getByText`, `getByRole({ name })`,
`getByLabelText`, `getByPlaceholderText`, `getByTitle`). Translating naively turns all of them red,
and rewriting them would be both the bulk of the work and a net loss of coverage.

`src/test-setup.js` therefore initialises i18next **synchronously, in English, with the real
catalogues**. Every existing assertion passes unchanged, and the whole suite becomes the English
catalogue's coverage as a side effect. No provider is added to any test — `react-i18next` reads the
global instance. The synchronous initialisation is the one place the dynamic `import()` above is
bypassed: a test must not await a catalogue before it can query the DOM.

New tests:

- key parity between `en` and `fr`, per namespace
- `resolveLocale`, every branch of the chain
- switching language at runtime updates the rendered strings and `documentElement.lang`
- date, size and collation formatting per locale
- the settings selector writes the preference and the mirror

## Formats and collation

Dates pass `undefined` as their locale today, so they follow the **browser**, not the application.
An account whose browser is English and whose choice is French would read a French interface printing
English dates. `modules/mail/list/formatDate.ts`, `modules/mail/reader/formatReaderDate.ts` and
`ConnectedAccountsPage` take the active locale instead. `formatSize.ts` translates its units (`KB` →
`Ko`).

`Intl` formatter construction is expensive and these run per list row, so formatters are memoised per
locale.

Collation follows too: `folderNodes.sortFolders` and `contacts/contactSearch.ts` already call
`localeCompare`, and pass the active locale rather than the ambient one.

Plurals go through i18next's `Intl.PluralRules` integration. French has three CLDR categories
(`one`, `many`, `other`), not two; a hand-rolled `count === 1` test would be wrong.

## Errors

`src/lib/apiErrorMessage.ts` holds one table mapping the stable codes `ApiError` already carries —
`credentials_unavailable`, `account_not_found`, `connected_credentials_invalid`, `Message not
found` — to translation keys.

The 38 sites currently written `err.message || 'Failed to delete user'` call it instead, and its
fallback is the local message, which those sites already spell out as the second operand of that
`||`. Server prose stops reaching the screen; it stays in the console and the logs, where a stable
symbol is what a developer wants anyway.

## The selector

A third radio group on `AppearancePage`, above Theme: **Automatic (browser) / English / Français**.
The two languages are written **in their own language** — someone stranded in an English interface
does not search for "French". Applying is immediate, with no reload.

`modules/mail/roleLabel.ts` — the seam the codebase reserved for this — becomes `roleLabel(role, t)`.
It stays a pure function taking `t` rather than becoming a hook, because two of its six callers are
not components.

## Two things to measure in a browser, not reason about

`src/frontend/CLAUDE.md` warns that the settings navigation leaves 155px of text once the row icon
and its gap are taken, that "Connected accounts" spends 125 of them, and that this is the number to
check before translating that nav. French labels must be **measured in Chrome**; if one wraps, the
fix is the column width, not the icons.

The same applies to the quick-action chips on `GeneralPage` and to `SelectionToolbar`, where French
typically runs 15–20% longer than English. jsdom computes no layout, so no test in this repo can
catch either.

## Backend

One line in `UserPreferences.cs`:

```csharp
public const string UiLanguage = "ui.language";
new(UiLanguage, "auto", ["auto", "en", "fr"]),
```

The registry already refuses an unknown key and falls back to the default on a value it no longer
accepts, so nothing else is needed. `PREFERENCE_KEYS` in `usePreferences.ts` gains the matching
entry and a `languageOf` accessor following the shape of its neighbours.

## Order of work

1. Backend registry key, frontend preference accessor, `resolveLocale`, the mirror, the pre-paint
   change, i18next wiring, typed `t()`, the test-setup initialisation and the parity test. Nothing
   user-visible changes yet — the app runs on the English catalogue only.
2. Formats and collation.
3. The error table and its 38 call sites.
4. The selector on `AppearancePage`, and `roleLabel`.
5. String extraction and French translation, namespace by namespace: `common` and `auth`, then
   `settings` and `admin`, then `mail`, `compose` and `contacts`.
6. Browser measurement of the settings nav, the action chips and the selection toolbar.
