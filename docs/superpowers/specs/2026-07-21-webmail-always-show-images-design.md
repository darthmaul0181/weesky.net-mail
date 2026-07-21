# Always show remote images

A setting that loads remote images in every message, so the reader never shows the
"N remote images were blocked" banner.

## The problem

The backend rewrites every remote `src` into `data-blocked-src`, and `MessageReader` restores
them per message, on an explicit click, resetting on the next message. That is the right default
— loading a remote image tells the sender the message was opened — but it is a click on every
single message, forever, for a reader who has already decided they do not care.

## The preference

One new entry in the backend registry (`Models/UserPreferences.cs`), which is the only place a
preference is declared:

```csharp
public const string MailAlwaysShowImages = "mail.alwaysShowImages";
new(MailAlwaysShowImages, "false", Booleans),
```

The default is `false`. Blocking stays what an account that has chosen nothing gets, for the same
reason silence is the notification default: the safe state is the one nobody has to opt out of.

The client side mirrors it in `PREFERENCE_KEYS` plus an accessor on `notifySoundOf`'s pattern —
off unless the stored value is exactly `'true'`, so a typo'd or absent row blocks rather than
reveals:

```ts
export function alwaysShowImagesOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.alwaysShowImages] === 'true'
}
```

**Nothing else changes on the backend.** The sanitiser keeps blocking images and keeps culling
CSS declarations containing `url(`, exactly as today. The preference never enters the sanitising
pipeline, so a message body is the same document whatever the account has chosen and the message
cache does not depend on it. The consequence is deliberate and unchanged from today's behaviour:
CSS background images stay absent even with the setting on, because the current "Show images"
button does not restore them either. This setting removes a click; it does not widen the
sanitiser.

## The reader

`MessageReader` reads the preferences it needs directly (`usePreferences()`, the way
`MessageList` does) and **derives** rather than seeding state:

```ts
const showImages = imagesShown || alwaysShowImagesOf(preferences)
```

Deriving, not initialising `imagesShown` from the preference, is the load-bearing choice: an
initial value taken from an asynchronous query has to be re-synchronised when the query resolves
*and* re-applied on every message change, and the per-message reset effect would have to learn
about the preference. The derived form leaves that effect untouched — `imagesShown` keeps meaning
"the user clicked Show images on *this* message" and nothing else.

`showImages` replaces `imagesShown` at its two readers: the body reveal, and the banner
condition. The banner and its button therefore disappear together, with no new branch — the
existing `!imagesShown` guard already expresses it.

A flash of the banner before the preferences land is not reachable in practice: opening a message
means the message list rendered, and the list waits on the preferences before rendering at all.

## The setting

A `ToggleRow` in `GeneralPage`, placed under "Preview in the message list" — both say how mail is
displayed, and they belong ahead of the notification block, which is about something else.

- Label: **Always show remote images**
- Toast on: `Remote images will always load`
- Toast off: `Remote images stay blocked until you ask`

While it is on, a `.settings-note` under the row carries the warning the banner used to:
*Loading them tells the sender you opened the message.* That warning has to survive somewhere,
and the moment of choosing is where it is useful — repeating it on every message is what the
setting exists to stop.

## Tests

- `UserPreferencesTests` — the key is present in `Effective` with its default, `true`/`false` are
  valid, anything else is refused.
- `usePreferences.test.tsx` — `alwaysShowImagesOf`: absent → false, `'true'` → true, a stray
  value → false.
- `MessageReader.test.tsx` — with the preference on and `blockedImageCount > 0`: no banner, no
  "Show images" button, and the `srcDoc` carries restored `src` attributes. With it off, today's
  behaviour intact. The file's `api.js` mock gains `getPreferences`, which it does not have yet.
- `GeneralPage.test.tsx` — the toggle saves the key, the toast matches the direction, and the
  note appears only while it is on.
