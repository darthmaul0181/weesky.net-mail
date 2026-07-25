# Reader Actions Zone Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer le bandeau de choix des couleurs par une zone d'actions en bas à droite de l'en-tête du lecteur : toggle soleil/lune (thème sombre seulement) + bouton kebab provisionnant le futur menu.

**Architecture :** Trois icônes nouvelles sur le pattern existant, un troisième placement pour `Tooltip`, un composant pur `ReaderActions` (l'état reste dans `MessageReader`), et l'en-tête qui passe en flex deux-colonnes — la zone suit le bas du contenu quelle que soit sa hauteur (spam/Cc/To absents compris).

**Tech Stack :** React 18 + TypeScript + Vitest + `@testing-library/react`. Frontend seul.

Spec de référence : `docs/superpowers/specs/2026-07-22-reader-actions-design.md`. Maquette validée : https://claude.ai/code/artifact/c6757379-f5a9-41ff-a21c-7d36b1e93997

## Global Constraints

- **Aucun nouveau role token CSS, aucune couleur littérale dans `mail.css` ni `tooltip.css`.** La zone utilise `--text-muted`, `--action-primary`, `--surface-sunken`, `--border`, `--radius-sm` — tous existants. Aucun fichier `theme-*.css` ne bouge (`palettes.test.ts` casse sinon).
- **Le test partagé `icons.test.tsx` assert `stroke="currentColor"` sur chaque icône de sa liste** : les trois icônes nouvelles sont dessinées au trait (le kebab en points de trait à bouts ronds), jamais en `fill`.
- Les tooltips reprennent **mot pour mot** les textes du bandeau : `Showing the colours the sender chose.` et `Colours are adapted to your dark theme.` (avec le point final). Les `aria-label` reprennent les libellés des anciens boutons : `Original colours` / `Match my theme`.
- Commentaires : seulement quand le code seul ne suffit pas, 3 lignes max.
- Messages de commit : **deux lignes maximum** (sujet court, ligne vide, une ligne de corps max). Pas de trailer Co-Authored-By.
- Répertoire de travail : `src/frontend`. Commandes : `npm run test -- <path>` (focalisé), `npm run test`, `npm run typecheck`, `npm run lint`.

## File Structure

- Créer `src/frontend/src/icons/SunIcon.tsx`, `MoonIcon.tsx`, `KebabIcon.tsx` ; modifier `src/frontend/src/icons/icons.test.tsx` (trois entrées dans la liste partagée).
- Modifier `src/frontend/src/components/Tooltip.tsx` + `Tooltip.test.tsx` + `src/frontend/src/styles/tooltip.css` — placement `bottom-right`.
- Créer `src/frontend/src/modules/mail/reader/ReaderActions.tsx` + `ReaderActions.test.tsx`.
- Modifier `src/frontend/src/modules/mail/reader/MessageReader.tsx` + `MessageReader.test.tsx`, `src/frontend/src/styles/mail.css`, `src/frontend/CLAUDE.md`.

---

### Task 1: Les trois icônes

**Files:**
- Create: `src/frontend/src/icons/SunIcon.tsx`
- Create: `src/frontend/src/icons/MoonIcon.tsx`
- Create: `src/frontend/src/icons/KebabIcon.tsx`
- Modify: `src/frontend/src/icons/icons.test.tsx`

**Interfaces:**
- Consumes: rien.
- Produces: trois exports par défaut `({ size = 16 }: { size?: number })` rendant un SVG `stroke="currentColor"` `aria-hidden="true"`. Consommés par la Task 3.

- [ ] **Step 1: Étendre le test partagé (il échouera à l'import)**

Dans `src/frontend/src/icons/icons.test.tsx`, ajouter aux imports :

```tsx
import SunIcon from './SunIcon'
import MoonIcon from './MoonIcon'
import KebabIcon from './KebabIcon'
```

et à la liste `icons` :

```tsx
  { name: 'SunIcon', Icon: SunIcon, defaultSize: '16' },
  { name: 'MoonIcon', Icon: MoonIcon, defaultSize: '16' },
  { name: 'KebabIcon', Icon: KebabIcon, defaultSize: '16' },
]
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/icons/icons.test.tsx`
Expected: FAIL — `Failed to resolve import "./SunIcon"`.

- [ ] **Step 3: Écrire les trois icônes**

`src/frontend/src/icons/SunIcon.tsx` :

```tsx
export default function SunIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="1.5" strokeLinecap="round" aria-hidden="true">
      <circle cx="8" cy="8" r="3.2" />
      <path d="M8 1v2M8 13v2M1 8h2M13 8h2M3.05 3.05l1.4 1.4M11.55 11.55l1.4 1.4M12.95 3.05l-1.4 1.4M4.45 11.55l-1.4 1.4" />
    </svg>
  )
}
```

`src/frontend/src/icons/MoonIcon.tsx` :

```tsx
export default function MoonIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="1.5" strokeLinejoin="round" aria-hidden="true">
      <path d="M13.5 9.5A5.8 5.8 0 0 1 6.5 2.5a5.8 5.8 0 1 0 7 7Z" />
    </svg>
  )
}
```

`src/frontend/src/icons/KebabIcon.tsx` :

```tsx
// Dots drawn as round-capped zero-length strokes, so the shared icons test's
// stroke="currentColor" assertion holds for this icon like every other.
export default function KebabIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="3" strokeLinecap="round" aria-hidden="true">
      <path d="M8 3.5h.01M8 8h.01M8 12.5h.01" />
    </svg>
  )
}
```

- [ ] **Step 4: Vérifier le vert**

Run: `cd src/frontend && npm run test -- src/icons/icons.test.tsx`
Expected: PASS (la liste entière, 3 tests × 11 icônes).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/icons
git commit -m "Add the sun, moon and kebab icons

Kebab dots are round-capped strokes, so the shared stroke assertion holds."
```

---

### Task 2: Le placement bottom-right du Tooltip

**Files:**
- Modify: `src/frontend/src/components/Tooltip.tsx:5`
- Modify: `src/frontend/src/components/Tooltip.test.tsx`
- Modify: `src/frontend/src/styles/tooltip.css`

**Interfaces:**
- Consumes: rien.
- Produces: `placement?: 'top-right' | 'bottom-left' | 'bottom-right'`. La Task 3 utilise `bottom-right`.

- [ ] **Step 1: Écrire le test qui échoue**

Dans `src/frontend/src/components/Tooltip.test.tsx`, à côté des tests de placement existants :

```tsx
  // For a trigger flush against the column's right edge: the bubble opens down-LEFT,
  // the one direction the mail column's overflow:hidden cannot clip.
  it('places the bubble below and to the right on request', () => {
    render(<Tooltip content="x" placement="bottom-right"><span>trigger</span></Tooltip>)

    expect(screen.getByRole('tooltip')).toHaveClass('is-bottom-right')
  })
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/components/Tooltip.test.tsx`
Expected: FAIL — TS refuse `"bottom-right"` (et sans typecheck, la classe manque).

- [ ] **Step 3: Élargir l'union et le CSS**

Dans `Tooltip.tsx`, la ligne du type :

```tsx
  placement?: 'top-right' | 'bottom-left' | 'bottom-right'
```

Dans `src/frontend/src/styles/tooltip.css`, sous le bloc `.is-bottom-left` :

```css
.tooltip-bubble.is-bottom-right {
  top: calc(100% + 8px);
  right: 0;
  width: max-content;
  max-width: 320px;
}
```

- [ ] **Step 4: Vérifier le vert**

Run: `cd src/frontend && npm run test -- src/components/Tooltip.test.tsx && npm run typecheck`
Expected: PASS (4 tests), typecheck propre.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/Tooltip.tsx src/frontend/src/components/Tooltip.test.tsx src/frontend/src/styles/tooltip.css
git commit -m "Give Tooltip a bottom-right placement

For a trigger flush against the column's right edge, down-left is the unclippable direction."
```

---

### Task 3: ReaderActions

**Files:**
- Create: `src/frontend/src/modules/mail/reader/ReaderActions.tsx`
- Create: `src/frontend/src/modules/mail/reader/ReaderActions.test.tsx`

**Interfaces:**
- Consumes: `Tooltip` (`bottom-right`, Task 2), `SunIcon`/`MoonIcon`/`KebabIcon` (Task 1).
- Produces: `export default function ReaderActions({ showColourToggle, originalColours, onToggleColours }: Props)` — `showColourToggle: boolean`, `originalColours: boolean`, `onToggleColours: () => void`. Consommé par la Task 4.

- [ ] **Step 1: Écrire les tests qui échouent**

`src/frontend/src/modules/mail/reader/ReaderActions.test.tsx` :

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import ReaderActions from './ReaderActions'

describe('ReaderActions', () => {
  it('offers the sender colours while the message wears the theme adaptation', () => {
    render(<ReaderActions showColourToggle originalColours={false} onToggleColours={() => {}} />)

    expect(screen.getByRole('button', { name: 'Original colours' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Showing the colours the sender chose.')
  })

  it('offers the way back while the sender colours are shown', () => {
    render(<ReaderActions showColourToggle originalColours onToggleColours={() => {}} />)

    expect(screen.getByRole('button', { name: 'Match my theme' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Colours are adapted to your dark theme.')
  })

  it('reports the click', () => {
    const onToggle = vi.fn()
    render(<ReaderActions showColourToggle originalColours={false} onToggleColours={onToggle} />)

    fireEvent.click(screen.getByRole('button', { name: 'Original colours' }))

    expect(onToggle).toHaveBeenCalledOnce()
  })

  // A rule beside a lone button reads as a rendering fault — same reason the folder tree
  // only draws its hr between two populated blocks.
  it('hides the toggle and its rule together, keeping the menu button', () => {
    const { container } = render(
      <ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}} />)

    expect(screen.queryByRole('button', { name: 'Original colours' })).not.toBeInTheDocument()
    expect(container.querySelector('.actions-rule')).toBeNull()
    expect(screen.getByRole('button', { name: 'Message actions' })).toBeInTheDocument()
  })

  it('lets the future menu button be clicked without effect', () => {
    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
  })
})
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/ReaderActions.test.tsx`
Expected: FAIL — `Failed to resolve import "./ReaderActions"`.

- [ ] **Step 3: Écrire le composant**

`src/frontend/src/modules/mail/reader/ReaderActions.tsx` :

```tsx
import Tooltip from '../../../components/Tooltip'
import KebabIcon from '../../../icons/KebabIcon'
import MoonIcon from '../../../icons/MoonIcon'
import SunIcon from '../../../icons/SunIcon'

interface Props {
  showColourToggle: boolean
  originalColours: boolean
  onToggleColours: () => void
}

/** The icon shows the action to come, theme-toggle convention: a sun while the message wears
    the dark adaptation, a moon while it shows the sender's own colours. */
export default function ReaderActions({ showColourToggle, originalColours, onToggleColours }: Props) {
  return (
    <div className="reader-actions">
      {showColourToggle && (
        <>
          <Tooltip
            placement="bottom-right"
            content={originalColours
              ? 'Colours are adapted to your dark theme.'
              : 'Showing the colours the sender chose.'}
          >
            <button
              type="button"
              className="action-btn"
              aria-label={originalColours ? 'Match my theme' : 'Original colours'}
              onClick={onToggleColours}
            >
              {originalColours ? <MoonIcon /> : <SunIcon />}
            </button>
          </Tooltip>
          <span className="actions-rule" />
        </>
      )}
      <button type="button" className="action-btn" aria-label="Message actions">
        <KebabIcon />
      </button>
    </div>
  )
}
```

- [ ] **Step 4: Vérifier le vert**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/ReaderActions.test.tsx`
Expected: PASS, 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/ReaderActions.tsx src/frontend/src/modules/mail/reader/ReaderActions.test.tsx
git commit -m "Add ReaderActions: the colour toggle and the future menu button

The icon shows the action to come; the rule renders only between two buttons."
```

---

### Task 4: Brancher la zone, retirer le bandeau

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (imports, header, suppression du bandeau)
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx` (nouveaux tests)
- Modify: `src/frontend/src/styles/mail.css` (`.reader-header` flex, `.reader-stack`, `.reader-actions`, `.action-btn`, `.actions-rule` ; suppression `.reader-colour-note`)
- Modify: `src/frontend/CLAUDE.md` (une phrase dans le paragraphe reader-header)

**Interfaces:**
- Consumes: `ReaderActions` (Task 3).
- Produces: l'en-tête final. Rien en aval.

- [ ] **Step 1: Écrire les tests qui échouent**

Dans `MessageReader.test.tsx`, un nouveau `describe` :

```tsx
  describe('the actions zone', () => {
    it('offers no colour toggle in light theme', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('button', { name: /original colours/i })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Message actions' })).toBeInTheDocument()
    })

    // Recolouring a text-only message means nothing — the same guard the banner had.
    it('offers no colour toggle for a text-only message, even in dark', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue({ ...detail, htmlBody: '', textBody: 'plain only' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('plain only')

      expect(screen.queryByRole('button', { name: /original colours/i })).not.toBeInTheDocument()
      theme.isDark = false
    })

    it('swaps the sun for a moon once the sender colours are shown', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Original colours' }))

      expect(await screen.findByRole('button', { name: 'Match my theme' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Original colours' })).not.toBeInTheDocument()
      theme.isDark = false
    })

    it('no longer shows the colour banner', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/colours are adapted to your dark theme/i)).not.toBeInTheDocument()
      theme.isDark = false
    })
  })
```

Note sur le dernier test : le texte du bandeau vit désormais uniquement dans la bulle du
tooltip, dont le contenu porte `role="tooltip"` — `queryByText` le trouverait. L'assertion
tient parce qu'en état adapté la bulle affiche l'autre texte (« Showing the colours… »),
celui du bandeau supprimé n'existe donc plus nulle part dans cet état. Ne pas « corriger »
ce test en le faisant porter sur le mauvais état.

- [ ] **Step 2: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/MessageReader.test.tsx`
Expected: FAIL — `Message actions` introuvable (la zone n'existe pas encore).

- [ ] **Step 3: Brancher le composant**

Dans `MessageReader.tsx` — aux imports :

```tsx
import ReaderActions from './ReaderActions'
```

L'en-tête devient (la pile existante est enveloppée telle quelle dans `.reader-stack`) :

```tsx
      <header className="reader-header">
        <div className="reader-stack">
          <h1 className="reader-subject">{data.subject || '(no subject)'}</h1>
          <div className="reader-meta">
            {/* … tout l'existant, inchangé … */}
          </div>
        </div>
        <ReaderActions
          showColourToggle={isDark && !!data.htmlBody}
          originalColours={originalColours}
          onToggleColours={() => setOriginalColours(v => !v)}
        />
      </header>
```

Et **supprimer** le bloc du bandeau :

```tsx
      {isDark && data.htmlBody && (
        <div className="reader-colour-note">
          …
        </div>
      )}
```

- [ ] **Step 4: Le CSS**

Dans `mail.css` :

- `.reader-header` : ajouter `display: flex;` au bloc existant (padding et bordure inchangés).
- Sous ce bloc :

```css
.reader-stack { flex: 1; min-width: 0; }

/* flex-end tracks the last line actually present — To/Cc/spam may each be absent,
   so nothing is anchored to a fixed height. */
.reader-actions { align-self: flex-end; display: flex; align-items: center; gap: 2px; }

.action-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border: 0;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  cursor: pointer;
}

.action-btn:hover { color: var(--action-primary); background: var(--surface-sunken); }

.actions-rule { width: 1px; height: 18px; background: var(--border); margin: 0 4px; }
```

- **Supprimer** les deux blocs `.reader-colour-note { … }` et `.reader-colour-note .btn { … }`.

- [ ] **Step 5: Vérifier le vert, puis tout**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/MessageReader.test.tsx && npm run test && npm run typecheck && npm run lint`
Expected: PASS partout. Les trois tests dark-mode existants passent sans modification : `restores the original colours on demand` clique désormais le bouton soleil (même accessible name `Original colours`), et `offers no way back` en clair tient toujours — le toggle n'y est pas rendu.

- [ ] **Step 6: La doc**

Dans `src/frontend/CLAUDE.md`, à la fin du paragraphe « **The reader header is a stack, not a row…** », ajouter :

```markdown
The header row is flex: the stack takes `flex: 1` and `ReaderActions` sits `align-self: flex-end` — bottom-right of whatever lines are actually present, since To, Cc and the spam gauge may each be absent. It holds the colour toggle (dark theme + HTML body only; the icon shows the action to come — sun for the sender's colours, moon for the way back, the old banner's texts as tooltips) and the inert kebab button that provisions the future message-actions menu. The colour banner is gone.
```

Dans le même fichier, la phrase du paragraphe dark-mode « `MessageReader` offers a per-message way back to the sender's colours » reste vraie telle quelle — ne pas y toucher.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/reader src/frontend/src/styles/mail.css src/frontend/CLAUDE.md
git commit -m "Replace the colour banner with the reader actions zone

Sun/moon toggle and the future menu button, flex bottom-right of the header."
```

---

## Vérification finale

- [ ] `cd src/frontend && npm run test && npm run typecheck && npm run lint && npm run build` — tout au vert.
- [ ] Contrôle visuel (après déploiement) : la zone en bas à droite dans les deux thèmes, le toggle qui bascule soleil↔lune avec le bon tooltip, le bandeau disparu, la zone qui suit quand la jauge spam est désactivée dans General.
