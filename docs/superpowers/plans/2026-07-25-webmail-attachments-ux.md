# Attachments UX (drag & drop + viewer d'images) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Glisser-déposer des fichiers sur le composeur, et visualiser les pièces jointes image du lecteur dans une popup (contrôle scindé chip + chevron ↑ → Download / View).

**Architecture:** Tranche frontend pure (spec `docs/superpowers/specs/2026-07-25-webmail-attachments-ux-design.md`). Le drop appelle le `addFiles` existant du composeur (staging XHR inchangé). Le viewer charge par `requestBlob` → object URL (cookie Lax + API cross-origin : un `<img src>` direct partirait sans cookie), révoqué à la fermeture. Le menu réutilise `DropdownMenu` avec un nouveau mode d'ouverture vers le haut.

**Tech Stack:** React 18/TS, Vitest + Testing Library, CSS tokens (aucune couleur littérale — `mail.css`/`shell.css`).

## Global Constraints

- Tout le code, les commentaires et les libellés UI en anglais. Libellés épinglés par le spec : overlay **"Drop files to attach"** ; entrées de menu **"Download"** / **"View"** ; aria-label du chevron **"More actions for {fileName}"**.
- Chevron et viewer sur les pièces `image/*` non-inline du lecteur SEULEMENT ; chip non-image inchangée (pin de régression exigé).
- Le clic principal d'une chip reste `download()` — comportement inchangé.
- Aucune couleur littérale en CSS : tokens de rôle existants (au besoin `color-mix` sur tokens, motif `SpamGauge`). Pas de nouveau token (sinon il faudrait toucher les 6 palettes + le test de parité — hors périmètre).
- Vérification par tâche depuis `src/frontend` : tests ciblés, puis `npm test`, `npm run typecheck`, `npm run lint`, `npm run build` (3 warnings lint pré-existants connus dans les onglets admin ; 1 flake MailLayout pré-classé — vert en isolation).
- Git : staging explicite ; jamais `.claude/settings.local.json`, `src/frontend/src/App.test.tsx`, `src/snoopy.microservice/ApiDocumentation.xml` ; message sujet + ≤2 lignes de corps, jamais commencer/finir par `@`, via heredoc POSIX `git commit -F -` ; jamais de push.

---

### Task 1: Composeur — drag & drop plein-surface

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Modify: `src/frontend/src/styles/mail.css` (`.compose-view` + nouvel overlay)
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`

**Interfaces:**
- Consumes: `addFiles` (callback existant de ComposeView — markDirty + staging XHR).
- Produces: rien pour les autres tâches (feature autonome).

- [ ] **Step 1: Write the failing tests**

Dans `ComposeView.test.tsx` (suivre les helpers de rendu existants du fichier ; `renderCompose()` ou équivalent local). Un helper de données de drag :

```tsx
function fileDragData(files: File[] = []) {
  return { dataTransfer: { types: ['Files'], files, items: files.map(f => ({ kind: 'file' })) } }
}
```

```tsx
describe('dropping files on the composer', () => {
  it('shows the overlay while a file drag hovers and hides it when the drag leaves', () => {
    render(...)
    const view = screen.getByTestId('compose-view')
    fireEvent.dragEnter(view, fileDragData())
    expect(screen.getByText('Drop files to attach')).toBeInTheDocument()
    // Nested enter/leave (child boundaries) must not blink the overlay off.
    fireEvent.dragEnter(view, fileDragData())
    fireEvent.dragLeave(view, fileDragData())
    expect(screen.getByText('Drop files to attach')).toBeInTheDocument()
    fireEvent.dragLeave(view, fileDragData())
    expect(screen.queryByText('Drop files to attach')).not.toBeInTheDocument()
  })

  it('ignores a drag that carries no files', () => {
    render(...)
    fireEvent.dragEnter(screen.getByTestId('compose-view'),
      { dataTransfer: { types: ['text/plain'], files: [], items: [] } })
    expect(screen.queryByText('Drop files to attach')).not.toBeInTheDocument()
  })

  it('stages the dropped files and dirties the composer', async () => {
    // Reuse the file-upload mocking already used by the "Attach files" tests in this file
    // (XHR/staging mocks). Assert after the drop:
    //  - the tray shows the dropped file's name (same assertion style as the picker test)
    //  - the overlay is gone
    //  - navigating away now raises the leave dialog (dirty) — same pattern as the
    //    existing attachment-dirty test.
    const file = new File(['x'], 'photo.png', { type: 'image/png' })
    fireEvent.drop(screen.getByTestId('compose-view'), fileDragData([file]))
    ...
  })
})
```

- [ ] **Step 2: Run to verify RED** — `npx vitest run src/modules/mail/compose/ComposeView.test.tsx` : les 3 nouveaux tests échouent (pas d'overlay, pas de handler).

- [ ] **Step 3: Implement**

`ComposeView.tsx` — état + handlers (près des autres callbacks) :

```tsx
// Counter, not a boolean: dragleave fires at every child boundary, so the overlay only
// goes away when as many leaves as enters have fired (or on drop).
const [dropTarget, setDropTarget] = useState(false)
const dragDepth = useRef(0)

function carriesFiles(event: React.DragEvent) {
  return Array.from(event.dataTransfer.types).includes('Files')
}
function onDragEnter(event: React.DragEvent) {
  if (!carriesFiles(event)) return
  event.preventDefault()
  dragDepth.current += 1
  setDropTarget(true)
}
function onDragOver(event: React.DragEvent) {
  if (carriesFiles(event)) event.preventDefault()
}
function onDragLeave(event: React.DragEvent) {
  if (!carriesFiles(event)) return
  dragDepth.current = Math.max(0, dragDepth.current - 1)
  if (dragDepth.current === 0) setDropTarget(false)
}
function onDrop(event: React.DragEvent) {
  if (!carriesFiles(event)) return
  event.preventDefault()
  dragDepth.current = 0
  setDropTarget(false)
  const files = Array.from(event.dataTransfer.files)
  if (files.length > 0) addFiles(files)
}
```

Racine (le `data-testid` existe déjà) :

```tsx
<div className="compose-view" data-testid="compose-view"
  onDragEnter={onDragEnter} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}>
```

Overlay, dernier enfant de la racine (au-dessus de Squire pendant le drag, donc le drop ne peut
pas atteindre le contenteditable ; PAS de `pointer-events: none` — l'overlay doit intercepter et
laisser remonter au handler racine) :

```tsx
{dropTarget && (
  <div className="compose-drop-overlay">Drop files to attach</div>
)}
```

`mail.css` — ajouter `position: relative;` au bloc `.compose-view` existant (~ligne 1348), puis :

```css
.compose-drop-overlay {
  position: absolute;
  inset: 0;
  z-index: 30;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 17px;
  font-weight: 600;
  color: var(--text);
  border: 2px dashed var(--action-primary);
  background: color-mix(in srgb, var(--surface) 82%, transparent);
}
```

- [ ] **Step 4: Run to verify GREEN** — les 3 tests passent ; puis la suite du fichier entier.

- [ ] **Step 5: Full verification & commit** — depuis `src/frontend` : `npm test`, `npm run typecheck`, `npm run lint`, `npm run build`. Commit (`ComposeView.tsx`, `mail.css`, `ComposeView.test.tsx`) : `Webmail: drop files anywhere on the composer to attach them`.

---

### Task 2: Lecteur — `AttachmentViewerModal`

**Files:**
- Create: `src/frontend/src/modules/mail/reader/AttachmentViewerModal.tsx`
- Test: `src/frontend/src/modules/mail/reader/AttachmentViewerModal.test.tsx`
- Modify: `src/frontend/src/styles/mail.css` (styles du viewer)

**Interfaces:**
- Consumes: `requestBlob` (`api.js` — `import { requestBlob } from '../../../api.js'`).
- Produces (Task 3 s'y branche) :

```tsx
interface Props {
  /** Authenticated API URL of the part (mailAttachmentUrl output — the caller builds it). */
  src: string
  fileName: string
  size: number
  onDownload: () => void
  onClose: () => void
}
export default function AttachmentViewerModal(props: Props)
```

- [ ] **Step 1: Write the failing tests**

`AttachmentViewerModal.test.tsx` — mocker `api.js` (`requestBlob`) et
`URL.createObjectURL`/`revokeObjectURL` (jsdom ne les implémente pas) :

```tsx
vi.mock('../../../api.js', () => ({ requestBlob: vi.fn() }))
// beforeEach: URL.createObjectURL = vi.fn(() => 'blob:mock-url'); URL.revokeObjectURL = vi.fn()

it('shows the image once the blob arrives', async () => {
  vi.mocked(requestBlob).mockResolvedValue({ blob: new Blob(['x']), fileName: 'photo.png' })
  render(<AttachmentViewerModal src="/u" fileName="photo.png" size={12345}
    onDownload={() => {}} onClose={() => {}} />)
  expect(screen.getByText('Loading…')).toBeInTheDocument()
  const img = await screen.findByRole('img', { name: 'photo.png' })
  expect(img).toHaveAttribute('src', 'blob:mock-url')
  expect(screen.getByText('photo.png')).toBeInTheDocument()
})

it('revokes the object URL on unmount', async () => { ... unmount(); expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url') })

it('shows the error inside the modal when the fetch fails', async () => {
  vi.mocked(requestBlob).mockRejectedValue(new Error('boom'))
  ... await screen.findByText('boom') // and no img
})

it('wires Download and the close button', async () => {
  // Download button → onDownload called; ✕ (aria-label "Close") → onClose called.
})
```

- [ ] **Step 2: Run to verify RED** — le module n'existe pas : échec d'import.

- [ ] **Step 3: Implement**

```tsx
import { useEffect, useState } from 'react'
import { requestBlob } from '../../../api.js'
import { formatSize } from './formatSize'

interface Props {
  src: string
  fileName: string
  size: number
  onDownload: () => void
  onClose: () => void
}

/**
 * Image attachment preview. Fetches through requestBlob because the API cookie is Lax and
 * cross-origin: a plain <img src> at the API would go out without it. The object URL lives
 * as long as the modal and is revoked on close.
 */
export default function AttachmentViewerModal({ src, fileName, size, onDownload, onClose }: Props) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let url: string | null = null
    let cancelled = false
    requestBlob(src)
      .then((result: { blob: Blob }) => {
        if (cancelled) return
        url = URL.createObjectURL(result.blob)
        setObjectUrl(url)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load the image')
      })
    return () => {
      cancelled = true
      if (url) URL.revokeObjectURL(url)
    }
  }, [src])

  return (
    <div className="modal-overlay">
      <div className="modal attachment-viewer">
        <div className="modal-header">
          <span className="modal-title">{fileName}</span>
          <span className="attachment-viewer-size">{formatSize(size)}</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <div className="attachment-viewer-body">
          {error
            ? <span className="attachment-viewer-error" role="alert">{error}</span>
            : objectUrl
              ? <img src={objectUrl} alt={fileName} className="attachment-viewer-img" />
              : <span>Loading…</span>}
        </div>
        <div className="folder-pick-actions">
          <button type="button" className="btn btn-ghost" onClick={onDownload}>Download</button>
        </div>
      </div>
    </div>
  )
}
```

(Vérifier la structure exacte des modales voisines — `MoveMessagesModal` est la référence dans le
même module : mêmes classes d'enveloppe, ✕ seule sortie. S'y conformer si elle diffère du squelette
ci-dessus.)

`mail.css` :

```css
.attachment-viewer { max-width: min(900px, 92vw); }
.attachment-viewer-size { color: var(--text-muted); font-size: 13px; margin-right: 8px; flex: none; }
.attachment-viewer-body {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 120px;
  overflow: hidden;
}
.attachment-viewer-img { max-width: 100%; max-height: 70vh; object-fit: contain; }
.attachment-viewer-error { color: var(--danger); }
```

- [ ] **Step 4: Run to verify GREEN** — `npx vitest run src/modules/mail/reader/AttachmentViewerModal.test.tsx`.

- [ ] **Step 5: Full verification & commit** — suites/typecheck/lint/build ; commit (`AttachmentViewerModal.tsx`, son test, `mail.css`) : `Webmail: image attachment viewer modal`.

---

### Task 3: Lecteur — contrôle scindé chip + chevron, câblage, docs

**Files:**
- Create: `src/frontend/src/icons/ChevronUpIcon.tsx`
- Modify: `src/frontend/src/components/DropdownMenu.tsx` (prop `direction`)
- Modify: `src/frontend/src/styles/shell.css` (menu vers le haut)
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (chips + état viewer)
- Modify: `src/frontend/src/styles/mail.css` (contrôle scindé)
- Modify: `src/frontend/CLAUDE.md` (une phrase dans le paragraphe mail du Project)
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `AttachmentViewerModal` (Task 2, props ci-dessus) ; `DropdownMenu` (`{ ariaLabel, trigger, items, className }`) ; `mailAttachmentUrl(folder, uid, part)` et `download(part, fileName)` existants dans `MessageReader`.
- Produces: `DropdownMenu` gagne `direction?: 'down' | 'up'` (défaut `'down'` — aucun consommateur existant ne change).

- [ ] **Step 1: Write the failing tests**

Dans `MessageReader.test.tsx` (fixtures `detail` existantes ; les entrées d'attachment portent
déjà `contentType`/`isInline`/`part`/`fileName`/`size`) :

```tsx
it('gives an image attachment the split control with Download and View', async () => {
  // detail fixture: one image/png attachment (not inline). After render:
  // - the chip still downloads on main click (requestBlob called — existing download test pattern)
  // - a button "More actions for photo.png" exists; clicking it shows menuitems Download and View
})

it('opens the viewer from the menu and closes it with the cross', async () => {
  // click View → findByRole('img', { name: 'photo.png' }) (requestBlob mocked)
  // click Close → the img is gone
})

it('keeps the plain chip on a non-image attachment', () => {
  // application/pdf fixture: no "More actions for…" button — regression pin
})
```

- [ ] **Step 2: Run to verify RED.**

- [ ] **Step 3: Implement**

`ChevronUpIcon.tsx` (motif `ChevronRightIcon`, tourné) :

```tsx
export default function ChevronUpIcon({ size = 14 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M4.5 12.5l5.5-6 5.5 6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
```

`DropdownMenu.tsx` — prop `direction?: 'down' | 'up'` (défaut `'down'`), racine
`className={`dropdown-root${direction === 'up' ? ' is-up' : ''}`}` ; `shell.css` :

```css
.dropdown-root.is-up .dropdown-menu { top: auto; bottom: calc(100% + 4px); }
```

`MessageReader.tsx` :

```tsx
const [viewed, setViewed] = useState<MailAttachmentInfo | null>(null)
```

Reset au changement de message — ajouter `setViewed(null)` dans l'effet per-message existant (celui
qui remet le consentement images / `detailsOpen`). Rendu des chips (remplace la boucle actuelle) :

```tsx
{attachments.map(attachment => {
  const chip = (
    <button
      key={attachment.part}
      type="button"
      className="attachment-chip"
      onClick={() => download(attachment.part, attachment.fileName)}
    >
      <PaperclipIcon size={13} />
      {attachment.fileName}
      <span className="attachment-chip-size">{formatSize(attachment.size)}</span>
    </button>
  )
  if (!attachment.contentType?.toLowerCase().startsWith('image/')) return chip
  return (
    <span key={attachment.part} className="attachment-split">
      {chip}
      <DropdownMenu
        direction="up"
        ariaLabel={`More actions for ${attachment.fileName}`}
        className="attachment-split-more"
        trigger={<ChevronUpIcon size={13} />}
        items={[
          { label: 'Download', onSelect: () => download(attachment.part, attachment.fileName) },
          { label: 'View', onSelect: () => setViewed(attachment) },
        ]}
      />
    </span>
  )
})}
```

(Attention à la clé : quand le contrôle est scindé, la `key` monte sur le `<span>` et le chip
interne n'en porte plus.) Modal, à côté des autres modales du fichier :

```tsx
{viewed && (
  <AttachmentViewerModal
    src={mailAttachmentUrl(folderPath!, uid!, viewed.part)}
    fileName={viewed.fileName}
    size={viewed.size}
    onDownload={() => download(viewed.part, viewed.fileName)}
    onClose={() => setViewed(null)}
  />
)}
```

`mail.css` — le scindé colle chip et chevron :

```css
.attachment-split { display: inline-flex; align-items: stretch; }
.attachment-split .attachment-chip { border-top-right-radius: 0; border-bottom-right-radius: 0; }
.attachment-split-more {
  display: flex;
  align-items: center;
  padding: 0 6px;
  border: 1px solid var(--border);
  border-left: none;
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
  background: var(--attachment-chip-bg);
  color: var(--text);
  cursor: pointer;
}
```

(Vérifier le bloc `.attachment-chip` existant ~mail.css:1009 pour reprendre exactement son
`background`/`color` sur `.attachment-split-more` — même surface, un seul contrôle à l'œil.)

`src/frontend/CLAUDE.md` — dans le paragraphe Project (section mail/composing) : une phrase sur le
drop plein-surface du composeur, et une sur le contrôle scindé image (Download/View + viewer popup).

- [ ] **Step 4: Run to verify GREEN** — tests MessageReader ciblés, puis la suite.

- [ ] **Step 5: Full verification & commit** — suites/typecheck/lint/build ; commit (icône, DropdownMenu, shell.css, MessageReader, mail.css, CLAUDE.md, tests) : `Webmail: image attachments open in a viewer from a split chip`.

---

## Self-review notes

- **Couverture du spec** : §3 → T1 (overlay, compteur, types, addFiles, preventDefault) ; §4.1 → T3 (scindé, chevron ↑, menu, non-image intact) ; §4.2 → T2 (requestBlob→objectURL, révocation, états, Download pied de modal) ; §5 → T2/T3 (mêmes endpoints, erreurs en modal) ; §6 → répartis dans les 3 tâches ; §7 manuel (utilisateur) ; §8 respecté (pas d'inline-au-drop, pas de PDF, pas de zoom).
- **Cohérence des types** : `AttachmentViewerModal` consommé en T3 avec les props exactes produites en T2 ; `direction` par défaut `'down'` ne change aucun consommateur existant de `DropdownMenu` (kebab lecteur).
- **Pas de placeholder** : chaque étape porte son code ; les deux points laissés à la vérification de l'implémenteur (structure exacte des modales voisines, background exact du chip) sont des instructions de conformité au code en place, pas des trous.
