### The API client

**API client** — `src/api.js`: all backend calls go through `request()` and are exported as named methods on `api`. `BASE` is `import.meta.env.VITE_API_BASE || 'https://api.mail.weesky.net'`.

Failures throw **`ApiError`**, which extends `Error` and carries `.status` and `.code`. The code is the backend's `ResultEnveloppe` message when it is a stable string — `credentials_unavailable`, `Message not found` — so callers branch on a symbol rather than on prose. `request()` accepts `{ signal }` for cancellation, which the message list relies on so that switching folders quickly cannot race a stale response into the UI. `requestBlob()` handles binary responses (attachments) and reads the file name from `Content-Disposition`. `mailAttachmentUrl()` builds the download URL so its encoding lives in one place. **Folder paths are always `encodeURIComponent`-encoded** — they may contain `/`, `&` or `#`.
