# Palette perso — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** une 9e carte « My palette » dans l'onglet Apparence : l'utilisateur règle trois curseurs, le webmail génère une palette lisible en clair et en sombre, gardée sur le compte.

**Architecture:** la définition (`"265,muted,35"`) est une préférence serveur validée par le registre `UserPreferences`. Un générateur TypeScript pur en tire les 43/37 rôles CSS. Un `<style id="custom-palette">` les déclare sous `[data-palette='custom']`. Une copie locale du CSS permet au script de pré-affichage de peindre la bonne palette avant React. Le choix de la palette active reste en `localStorage`, par appareil.

**Tech Stack:** ASP.NET Core (xUnit) · React 19 + TypeScript, TanStack Query, react-i18next, Vitest + Testing Library.

**Spec:** `docs/history/specs/2026-09-28-webmail-custom-palette-design.md`

## Global Constraints

- Préférence serveur : clé `ui.customPalette`, défaut `""`, sinon exactement `^(0|[1-9]\d{0,2}),(neutral|muted|vivid),(0|[1-9]\d{0,2})$` avec les deux teintes `< 360`.
- Clés locales : `appearance_palette` (existe, accepte en plus `custom`), `appearance_custom_palette` (définition), `appearance_custom_palette_css` (CSS généré). `<style id="custom-palette">`.
- Le générateur couvre **exactement** les rôles de `theme-night.css` : 43 dans le bloc clair, 37 dans le bloc sombre.
- `--action-primary-fg` vaut `#ffffff` dans les deux modes ; l'accent sombre qui porte les boutons est ≥ 2,8:1 sous le blanc.
- Sélecteurs du CSS généré **non ancrés** : `[data-palette='custom']` et `[data-palette='custom'][data-theme='dark']`.
- Textes UI en anglais (`en`) et en français (`fr`, vouvoiement, espace insécable U+00A0 avant `:` `;` `?` `!`). L'outil Edit remplace U+00A0 par une espace ordinaire : vérifier et corriger en PowerShell, jamais avec `"$nb?"`.
- Le dépôt est en CRLF (`autocrlf=true`) : Edit/Write écrivent du LF, c'est accepté par Git ; ne pas « corriger » les fins de ligne.
- `dotnet test` (jamais `--no-build` quand un fichier de test est ajouté) ; avant chaque commit backend, révertir `src/scotty.microservice/ApiDocumentation.xml` s'il a dérivé (`git checkout -- src/scotty.microservice/ApiDocumentation.xml`).
- Commits : message de deux lignes maximum, jamais de `@` en début ou fin, terminé par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Utiliser `git commit -F -` avec un heredoc. Ne pas pousser.
- Pas de commentaire pour du code évident ; un commentaire ajouté fait au plus 3 lignes.

## Review Focus

1. **Un autre compte sans palette perso se connecte** sur un appareil réglé sur `custom` → l'affichage repasse sur `night`, le `<style>` et la copie locale disparaissent (Task 4, test `CustomPaletteSync`).
2. **Copie locale incomplète** (`appearance_palette=custom` mais pas de CSS) → le pré-affichage et `ThemeContext` retombent sur `night` (Task 4, tests du script exécuté et de `ThemeContext`).
3. **Échec de l'enregistrement** (400 ou réseau) → la fenêtre reste ouverte avec le message, la palette active ne change pas, le cache optimiste est restauré (Task 5).
4. **Ligne serveur devenue invalide** (`"400,muted,10"`, `"065,muted,10"`) → `Effective` rend `""`, le client la traite comme « pas de palette » (Task 2 et Task 3, `parseCustomPalette`).
5. **Brouillon et palette enregistrée coexistent** → l'aperçu de l'éditeur montre le brouillon, la vignette de la grille montre la palette enregistrée (Task 5).

---

### Task 1: Retirer Bordeaux, Mocha et Graphite

**Files:**
- Revert: commit `f854a595` (theme-bordeaux/mocha/graphite.css, ThemeContext, main.tsx, index.html, AppearancePage, swipeColours.test, webAppManifest, calendar.css, `.claude/rules/frontend-theming.md`)

- [ ] **Step 1: Revert sans commit**

Run: `git revert --no-commit f854a595`
Expected: les 11 fichiers reviennent à l'état d'avant, sans conflit.

- [ ] **Step 2: Lancer la suite frontend**

Run (dans `src/frontend`) : `npx vitest run src/styles src/modules/settings/appearance src/contexts`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git commit -q -F - <<'EOF'
Remove the Bordeaux, Mocha and Graphite palettes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: La préférence `ui.customPalette` côté serveur

**Files:**
- Modify: `src/scotty.microservice/Models/UserPreferences.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: `UserPreferences.UiCustomPalette = "ui.customPalette"` ; `PreferenceDefinition(..., Func<string, bool>? Validator = null)` ; `UserPreferences.IsValid` consulte `Validator` quand il est présent.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `UserPreferencesTests` :

```csharp
[Theory]
[InlineData("", true)]
[InlineData("0,neutral,0", true)]
[InlineData("359,vivid,359", true)]
[InlineData("265,muted,35", true)]
[InlineData("360,muted,35", false)]
[InlineData("265,muted,360", false)]
[InlineData("065,muted,35", false)]   // one spelling per value, like every other key
[InlineData("265,Muted,35", false)]
[InlineData("265,loud,35", false)]
[InlineData("265,muted", false)]
[InlineData("265,muted,35,", false)]
[InlineData(" 265,muted,35", false)]
[InlineData("-1,muted,35", false)]
public void IsValid_CustomPalette_AcceptsEmptyOrThreeFields(string value, bool expected)
{
    Assert.Equal(expected, UserPreferences.IsValid(UserPreferences.UiCustomPalette, value));
}

[Fact]
public void Effective_HasNoCustomPaletteByDefault()
{
    Assert.Equal("", UserPreferences.Effective([])[UserPreferences.UiCustomPalette]);
}

[Fact]
public void Effective_DropsACustomPaletteOutOfRange()
{
    var effective = UserPreferences.Effective([Row(UserPreferences.UiCustomPalette, "400,muted,10")]);

    Assert.Equal("", effective[UserPreferences.UiCustomPalette]);
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~UserPreferencesTests"`
Expected: FAIL à la compilation (`UiCustomPalette` inconnu).

- [ ] **Step 3: Implémenter**

Dans `UserPreferences.cs` :

```csharp
public sealed record PreferenceDefinition(
    string Key, string Default, IReadOnlyList<string> Allowed, bool IsSet = false,
    Func<string, bool>? Validator = null);
```

Ajouter la clé, près de `UiLanguage` :

```csharp
// ui., like the language: the palette is the account's, whichever device picks it.
public const string UiCustomPalette = "ui.customPalette";
```

Dans `All` :

```csharp
new(UiCustomPalette, "", [], Validator: IsCustomPalette),
```

`IsValid` devient :

```csharp
var definition = All.FirstOrDefault(p => p.Key == key);
if (definition is null)
    return false;
if (definition.Validator is not null)
    return definition.Validator(value);

return definition.IsSet ? IsValidSubset(definition, value) : definition.Allowed.Contains(value);
```

Et le validateur (usings `System.Globalization`, `System.Text.RegularExpressions`) :

```csharp
private static readonly Regex CustomPalettePattern = new(
    @"^(0|[1-9]\d{0,2}),(neutral|muted|vivid),(0|[1-9]\d{0,2})$",
    RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

/// <summary>Empty is "no palette yet"; otherwise structure hue, intensity, accent hue.</summary>
private static bool IsCustomPalette(string value)
{
    if (value.Length == 0)
        return true;

    var match = CustomPalettePattern.Match(value);

    return match.Success
        && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) < 360
        && int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) < 360;
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~UserPreferencesTests"`
Expected: PASS, `Default_IsItselfAValueTheRegistryAccepts` compris pour la nouvelle clé.

- [ ] **Step 5: Suite backend complète, puis commit**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests` → PASS. Puis `git checkout -- src/scotty.microservice/ApiDocumentation.xml` si modifié.

```bash
git add src/scotty.microservice/Models/UserPreferences.cs src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs
git commit -q -F - <<'EOF'
Accept a ui.customPalette preference

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Le générateur de palette

**Files:**
- Create: `src/frontend/src/lib/customPalette.ts`
- Test: `src/frontend/src/lib/customPalette.test.ts`

**Interfaces:**
- Consumes: `type Palette` de `contexts/ThemeContext.tsx` (import de type uniquement), qui vaut à ce stade les 8 ids prédéfinis.
- Produces :

```ts
export type Intensity = 'neutral' | 'muted' | 'vivid'
export const INTENSITIES: readonly Intensity[]
export interface CustomPaletteDef { structure: number; intensity: Intensity; accent: number }
export type TokenSet = Record<`--${string}`, string>
export interface CustomPaletteTokens { light: TokenSet; dark: TokenSet }
export function parseCustomPalette(value: string | null | undefined): CustomPaletteDef | null
export function formatCustomPalette(def: CustomPaletteDef): string
export function isNearDanger(accent: number): boolean
export function hueGradient(lightness: number, chroma: number): string
export function generateCustomPalette(def: CustomPaletteDef): CustomPaletteTokens
export function customPaletteCss(tokens: CustomPaletteTokens): string
export const PALETTE_SEEDS: Record<BuiltinPalette, CustomPaletteDef>   // BuiltinPalette: Task 4 renames; here use the Palette type
export function contrast(a: string, b: string): number
```

- [ ] **Step 1: Écrire les tests qui échouent**

`src/frontend/src/lib/customPalette.test.ts` :

```ts
import { describe, it, expect } from 'vitest'
import {
  contrast, customPaletteCss, formatCustomPalette, generateCustomPalette, INTENSITIES,
  isNearDanger, parseCustomPalette, PALETTE_SEEDS, type CustomPaletteDef, type TokenSet,
} from './customPalette'
import nightCss from '../styles/theme-night.css?raw'

function rolesIn(selector: string): string[] {
  const at = nightCss.indexOf(`${selector} {`)
  const body = nightCss.slice(at, nightCss.indexOf('}', at)).replace(/\/\*[\s\S]*?\*\//g, '')
  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map(m => m[1]!).sort()
}

describe('parseCustomPalette / formatCustomPalette', () => {
  it.each([
    ['265,muted,35', { structure: 265, intensity: 'muted', accent: 35 }],
    ['0,neutral,359', { structure: 0, intensity: 'neutral', accent: 359 }],
  ])('reads %s', (value, expected) => {
    expect(parseCustomPalette(value)).toEqual(expected)
    expect(formatCustomPalette(expected as CustomPaletteDef)).toBe(value)
  })

  it.each(['', null, undefined, '360,muted,35', '065,muted,35', '265,loud,35', '265,muted', ' 265,muted,35'])(
    'refuses %s', value => expect(parseCustomPalette(value)).toBeNull(),
  )
})

describe('generateCustomPalette', () => {
  const sample = generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })

  // A role missing here falls back to whatever the cascade holds, silently.
  it('declares exactly the roles night declares, in both modes', () => {
    expect(Object.keys(sample.light).sort()).toEqual(rolesIn("[data-palette='night']"))
    expect(Object.keys(sample.dark).sort()).toEqual(rolesIn("[data-palette='night'][data-theme='dark']"))
  })

  it('only writes six-digit hex colours, rgba for the scrim', () => {
    for (const set of [sample.light, sample.dark])
      for (const [role, value] of Object.entries(set))
        expect(value, role).toMatch(role === '--scrim' ? /^rgba\(/ : /^#[0-9a-f]{6}$/)
  })

  it('is deterministic', () => {
    expect(generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })).toEqual(sample)
  })

  // Every combination the sliders can reach, at 5° steps: the guarantee is the product.
  it('keeps every pair legible for every combination', () => {
    const failures: string[] = []
    const need = (ok: boolean, label: string) => { if (!ok) failures.push(label) }
    for (const intensity of INTENSITIES)
      for (let structure = 0; structure < 360; structure += 5)
        for (let accent = 0; accent < 360; accent += 5) {
          const { light, dark } = generateCustomPalette({ structure, intensity, accent })
          const id = `${structure},${intensity},${accent}`
          for (const [mode, t] of [['light', light], ['dark', dark]] as [string, TokenSet][]) {
            const at = (a: `--${string}`, b: `--${string}`, min: number) =>
              need(contrast(t[a]!, t[b]!) >= min, `${id} ${mode} ${a}/${b}`)
            at('--text', '--surface', 4.5)
            at('--text', '--bg', 4.5)
            for (const ground of ['--surface', '--folders-bg', '--folders-item-hover', '--surface-sunken'] as const)
              at('--text-muted', ground, 4.5)
            at('--quote-text', '--surface', 4.5)
            at('--topbar-fg', '--topbar-bg', mode === 'light' ? 7 : 4.5)
            at('--rail-fg', '--rail-bg', 4.5)
            at('--rail-item-active-fg', '--rail-item-active', 4.5)
            at('--badge-count-fg', '--badge-count-bg', 4.5)
            at('--list-row-selected-fg', '--list-row-selected-bg', 4.5)
            at('--pane-item-active-fg', '--pane-item-active-bg', 4.5)
            at('--accent-unread', '--surface', 3)
            at('--action-primary-fg', '--action-primary', mode === 'light' ? 4.5 : 2.8)
            if (mode === 'dark')
              for (const fill of ['--danger', '--danger-hover', '--success'] as const) at('--status-fg', fill, 4.5)
          }
        }
    expect(failures.slice(0, 10)).toEqual([])
  }, 60_000)

  it('keeps white on the primary action in both modes', () => {
    expect(sample.light['--action-primary-fg']).toBe('#ffffff')
    expect(sample.dark['--action-primary-fg']).toBe('#ffffff')
  })
})

describe('isNearDanger', () => {
  it.each([[27, true], [20, true], [34, true], [35, false], [19, false], [200, false]])(
    'accent %i → %s', (accent, expected) => expect(isNearDanger(accent)).toBe(expected),
  )
})

describe('customPaletteCss', () => {
  it('writes two unanchored blocks', () => {
    const css = customPaletteCss(generateCustomPalette({ structure: 10, intensity: 'vivid', accent: 200 }))
    expect(css).toMatch(/^\[data-palette='custom'\] \{/)
    expect(css).toContain("[data-palette='custom'][data-theme='dark'] {")
    expect(css).not.toContain('html[')
  })
})

describe('PALETTE_SEEDS', () => {
  it('seeds the editor from every built-in palette', () => {
    expect(Object.keys(PALETTE_SEEDS).sort())
      .toEqual(['azure', 'classic', 'forest', 'indigo', 'ink', 'night', 'plum', 'slate'])
  })
})
```

- [ ] **Step 2: Vérifier l'échec**

Run (dans `src/frontend`) : `npx vitest run src/lib/customPalette.test.ts`
Expected: FAIL (module introuvable).

- [ ] **Step 3: Implémenter `customPalette.ts`**

```ts
import type { Palette } from '../contexts/ThemeContext'

export type Intensity = 'neutral' | 'muted' | 'vivid'
export const INTENSITIES: readonly Intensity[] = ['neutral', 'muted', 'vivid']
export interface CustomPaletteDef { structure: number; intensity: Intensity; accent: number }
export type TokenSet = Record<`--${string}`, string>
export interface CustomPaletteTokens { light: TokenSet; dark: TokenSet }

const PATTERN = /^(0|[1-9]\d{0,2}),(neutral|muted|vivid),(0|[1-9]\d{0,2})$/
const WHITE = '#ffffff'
const DANGER_HUE = 27

export function parseCustomPalette(value: string | null | undefined): CustomPaletteDef | null {
  const match = PATTERN.exec(value ?? '')
  if (!match) return null
  const structure = Number(match[1]), accent = Number(match[3])
  if (structure >= 360 || accent >= 360) return null
  return { structure, intensity: match[2] as Intensity, accent }
}

export function formatCustomPalette({ structure, intensity, accent }: CustomPaletteDef): string {
  return `${structure},${intensity},${accent}`
}

function hueGap(a: number, b: number): number {
  const d = Math.abs(a - b) % 360
  return d > 180 ? 360 - d : d
}

export function isNearDanger(accent: number): boolean {
  return hueGap(accent, DANGER_HUE) < 8
}

const lin = (v: number) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4
const gam = (v: number) => v <= 0.0031308 ? 12.92 * v : 1.055 * v ** (1 / 2.4) - 0.055

function oklchToRgb(L: number, C: number, h: number): number[] {
  const a = C * Math.cos(h * Math.PI / 180), b = C * Math.sin(h * Math.PI / 180)
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ]
}

/** Chroma gives way until the colour exists in sRGB, so the asked hue and lightness are kept. */
function oklch(L: number, C: number, h: number): string {
  let rgb = oklchToRgb(L, C, h)
  while (rgb.some(v => v < -0.0005 || v > 1.0005) && C > 0) {
    C = Math.max(0, C - 0.004)
    rgb = oklchToRgb(L, C, h)
  }
  return '#' + rgb.map(v => Math.round(gam(Math.min(1, Math.max(0, v))) * 255).toString(16).padStart(2, '0')).join('')
}

const channels = (hex: string) => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
const luminance = (hex: string) => {
  const [r, g, b] = channels(hex).map(lin) as [number, number, number]
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

export function contrast(a: string, b: string): number {
  const x = luminance(a), y = luminance(b)
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)
}

function mix(a: string, b: string, t: number): string {
  const A = channels(a), B = channels(b)
  return '#' + A.map((v, i) => Math.round((v + (B[i]! - v) * t) * 255).toString(16).padStart(2, '0')).join('')
}

/** Walks the lightness by `step` until `ok` holds: each threshold is imposed, not hoped for. */
function solve(L: number, C: number, h: number, step: number, ok: (c: string) => boolean): string {
  let colour = oklch(L, C, h)
  for (let i = 0; i < 80 && !ok(colour); i++) colour = oklch(L += step, C, h)
  return colour
}

const STRUCTURE: Record<Intensity, { L: number; C: number; tint: number }> = {
  neutral: { L: 0.27, C: 0.012, tint: 0.004 },
  muted: { L: 0.30, C: 0.06, tint: 0.008 },
  vivid: { L: 0.40, C: 0.14, tint: 0.012 },
}

function inkOn(fill: string, hue: number): string {
  const dark = oklch(0.2, 0.03, hue)
  return contrast(dark, fill) >= contrast(WHITE, fill) ? dark : WHITE
}

// Same values as night: status colours and swipe bands do not depend on the palette.
const SEMANTIC_LIGHT = {
  '--icon-hover-danger': '#f87171', '--danger': '#dc2626', '--danger-hover': '#b91c1c',
  '--success': '#16a34a', '--warning': '#b45309', '--status-fg': '#ffffff', '--scrim': 'rgba(0, 0, 0, 0.35)',
  '--swipe-seen': '#2563eb', '--swipe-flag': '#b45309', '--swipe-archive': '#15803d', '--swipe-delete': '#dc2626',
}
const SEMANTIC_DARK = {
  '--danger': '#f87171', '--danger-hover': '#ef4444', '--success': '#4ade80', '--warning': '#d97706',
  '--scrim': 'rgba(0, 0, 0, 0.35)',
}

export function generateCustomPalette({ structure: hs, intensity, accent: ha }: CustomPaletteDef): CustomPaletteTokens {
  const s = STRUCTURE[intensity], t = s.tint, dt = t * 1.4
  const accentLight = solve(0.55, 0.16, ha, -0.01, c => contrast(WHITE, c) >= 4.6)
  const accentDark = solve(0.74, 0.14, ha, -0.01, c => contrast(WHITE, c) >= 2.8)
  const railActiveLight = oklch(0.8, 0.14, ha)

  const surface = oklch(0.99, t / 2, hs), foldersHover = oklch(0.895, t * 2.6, hs)
  const topbar = solve(s.L, s.C, hs, -0.01, c => contrast(WHITE, c) >= 7)
  const selected = mix(surface, accentLight, 0.11), separator = oklch(0.915, t, hs)
  const light: TokenSet = {
    '--bg': oklch(0.958, t, hs), '--folders-bg': oklch(0.925, t * 2.2, hs), '--folders-item-hover': foldersHover,
    '--surface': surface, '--surface-raised': surface, '--surface-sunken': oklch(0.948, t, hs),
    '--border': oklch(0.885, t * 1.2, hs), '--text': oklch(0.2, t * 1.5, hs),
    '--text-muted': solve(0.5, t * 2, hs, -0.01, c => contrast(c, foldersHover) >= 4.6),
    '--topbar-bg': topbar, '--topbar-fg': WHITE, '--rail-bg': topbar,
    '--rail-fg': mix(topbar, WHITE, 0.74), '--rail-item': mix(topbar, WHITE, 0.11),
    '--rail-item-active': railActiveLight, '--rail-item-active-fg': inkOn(railActiveLight, ha),
    '--pane-item-hover': oklch(0.935, t, hs), '--pane-item-active-bg': selected, '--pane-item-active-fg': topbar,
    '--accent-unread': accentLight,
    '--list-row-hover': oklch(0.948, t, hs), '--list-row-selected-bg': selected, '--list-row-selected-fg': topbar,
    '--list-separator': separator, '--badge-count-bg': accentLight, '--badge-count-fg': WHITE,
    '--reader-header-border': separator,
    '--quote-text': solve(0.56, t * 1.5, hs, -0.01, c => contrast(c, surface) >= 4.6),
    '--action-primary': topbar, '--action-primary-hover': mix(topbar, WHITE, 0.12), '--action-primary-fg': WHITE,
    '--icon-hover-accent': accentDark,
    ...SEMANTIC_LIGHT,
  }

  const bg = oklch(0.19, dt, hs), darkSurface = oklch(0.222, dt, hs), raised = oklch(0.245, dt, hs)
  const border = oklch(0.29, dt, hs), text = oklch(0.925, 0.006, hs)
  const darkTop = oklch(0.16, Math.min(s.C * 0.5, 0.04), hs)
  const darkInk = inkOn(accentDark, ha), darkSelected = mix(darkSurface, accentDark, 0.16)
  const darkSelectedFg = oklch(0.93, 0.03, ha)
  const dark: TokenSet = {
    '--bg': bg, '--folders-bg': bg, '--folders-item-hover': raised,
    '--surface': darkSurface, '--surface-raised': raised, '--surface-sunken': oklch(0.205, dt, hs),
    '--border': border, '--text': text,
    '--text-muted': solve(0.7, 0.01, hs, 0.01, c => contrast(c, raised) >= 4.6),
    '--topbar-bg': darkTop, '--topbar-fg': text, '--rail-bg': darkTop,
    '--rail-fg': oklch(0.76, 0.02, hs), '--rail-item': mix(darkTop, WHITE, 0.07),
    '--rail-item-active': accentDark, '--rail-item-active-fg': darkInk,
    '--pane-item-hover': raised, '--pane-item-active-bg': darkSelected, '--pane-item-active-fg': darkSelectedFg,
    '--accent-unread': accentDark,
    '--list-row-hover': raised, '--list-row-selected-bg': darkSelected, '--list-row-selected-fg': darkSelectedFg,
    '--list-separator': border, '--badge-count-bg': accentDark, '--badge-count-fg': darkInk,
    '--reader-header-border': border,
    '--quote-text': solve(0.64, 0.01, hs, 0.01, c => contrast(c, darkSurface) >= 4.6),
    '--action-primary': accentDark, '--action-primary-hover': mix(accentDark, WHITE, 0.12), '--action-primary-fg': WHITE,
    '--status-fg': bg,
    ...SEMANTIC_DARK,
  }

  return { light, dark }
}

export function hueGradient(lightness: number, chroma: number): string {
  const stops = Array.from({ length: 19 }, (_, i) => oklch(lightness, chroma, (i * 20) % 360))
  return `linear-gradient(to right, ${stops.join(', ')})`
}

function block(selector: string, set: TokenSet): string {
  return `${selector} {\n${Object.entries(set).map(([role, value]) => `  ${role}: ${value};`).join('\n')}\n}`
}

export function customPaletteCss({ light, dark }: CustomPaletteTokens): string {
  return `${block("[data-palette='custom']", light)}\n${block("[data-palette='custom'][data-theme='dark']", dark)}\n`
}

/** Where the editor starts: the hues of the palette in use, measured from its stylesheet. */
export const PALETTE_SEEDS: Record<Exclude<Palette, 'custom'>, CustomPaletteDef> = {
  night: { structure: 265, intensity: 'muted', accent: 35 },
  classic: { structure: 267, intensity: 'vivid', accent: 267 },
  forest: { structure: 159, intensity: 'muted', accent: 71 },
  slate: { structure: 228, intensity: 'muted', accent: 190 },
  plum: { structure: 320, intensity: 'muted', accent: 81 },
  ink: { structure: 286, intensity: 'neutral', accent: 263 },
  azure: { structure: 242, intensity: 'muted', accent: 244 },
  indigo: { structure: 288, intensity: 'vivid', accent: 288 },
}
```

(`Exclude<Palette, 'custom'>` compile aussi avant Task 4, quand `Palette` ne contient pas encore `custom`.)

- [ ] **Step 4: Vérifier le succès**

Run: `npx vitest run src/lib/customPalette.test.ts`
Expected: PASS. Si le balayage signale un seuil, corriger la **recherche** (`solve`) du rôle concerné, jamais le seuil du test.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/lib/customPalette.ts src/frontend/src/lib/customPalette.test.ts
git commit -q -F - <<'EOF'
Generate a legible palette from two hues and an intensity

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Appliquer la palette `custom` (style, copie locale, pré-affichage, synchronisation)

**Files:**
- Create: `src/frontend/src/lib/customPaletteStyle.ts`, `src/frontend/src/components/CustomPaletteSync.tsx`
- Modify: `src/frontend/src/contexts/ThemeContext.tsx`, `src/frontend/index.html`, `src/frontend/src/App.tsx`, `src/frontend/src/hooks/usePreferences.ts`
- Test: `src/frontend/src/lib/customPaletteStyle.test.ts`, `src/frontend/src/components/CustomPaletteSync.test.tsx`, `src/frontend/src/contexts/ThemeContext.test.tsx`, `src/frontend/src/styles/palettes.test.ts`

**Interfaces:**
- Consumes: `parseCustomPalette`, `generateCustomPalette`, `customPaletteCss`, `CustomPaletteDef` (Task 3).
- Produces :
  - `ThemeContext` : `PALETTE_IDS` inchangé (8 ids) ; `export type BuiltinPalette = typeof PALETTE_IDS[number]` ; `export type Palette = BuiltinPalette | 'custom'`.
  - `customPaletteStyle.ts` : `CUSTOM_PALETTE_MIRROR_KEY = 'appearance_custom_palette'`, `CUSTOM_PALETTE_CSS_KEY = 'appearance_custom_palette_css'`, `CUSTOM_PALETTE_STYLE_ID = 'custom-palette'`, `applyCustomPalette(def: CustomPaletteDef | null): void`, `hasCustomPaletteMirror(): boolean`.
  - `usePreferences.ts` : `PREFERENCE_KEYS.customPalette = 'ui.customPalette'`, `customPaletteOf(preferences: Preferences): CustomPaletteDef | null`.

- [ ] **Step 1: Écrire les tests qui échouent**

`src/frontend/src/lib/customPaletteStyle.test.ts` :

```ts
import { describe, it, expect, beforeEach } from 'vitest'
import html from '../../index.html?raw'
import {
  applyCustomPalette, CUSTOM_PALETTE_CSS_KEY, CUSTOM_PALETTE_MIRROR_KEY, CUSTOM_PALETTE_STYLE_ID,
  hasCustomPaletteMirror,
} from './customPaletteStyle'

const styleEl = () => document.getElementById(CUSTOM_PALETTE_STYLE_ID)

describe('applyCustomPalette', () => {
  beforeEach(() => { localStorage.clear(); styleEl()?.remove() })

  it('writes one style element and the mirror', () => {
    applyCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })
    applyCustomPalette({ structure: 100, intensity: 'vivid', accent: 200 })

    expect(document.querySelectorAll(`#${CUSTOM_PALETTE_STYLE_ID}`)).toHaveLength(1)
    expect(styleEl()!.textContent).toContain("[data-palette='custom']")
    expect(localStorage.getItem(CUSTOM_PALETTE_MIRROR_KEY)).toBe('100,vivid,200')
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBe(styleEl()!.textContent)
    expect(hasCustomPaletteMirror()).toBe(true)
  })

  it('removes both on null', () => {
    applyCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })
    applyCustomPalette(null)

    expect(styleEl()).toBeNull()
    expect(localStorage.getItem(CUSTOM_PALETTE_MIRROR_KEY)).toBeNull()
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBeNull()
    expect(hasCustomPaletteMirror()).toBe(false)
  })
})

/** Runs the real pre-paint script, since it cannot import this module and repeats its keys. */
describe('the pre-paint script and the custom palette', () => {
  const script = html.match(/<script>\s*\(function\(\)\{[\s\S]*?\}\)\(\);\s*<\/script>/)![0]
    .replace(/^<script>/, '').replace(/<\/script>$/, '')
  const run = () => new Function(script)()

  // 'light' keeps the script off window.matchMedia, which jsdom does not implement.
  beforeEach(() => { localStorage.clear(); localStorage.setItem('appearance_theme', 'light'); styleEl()?.remove() })

  it('paints the mirrored palette before React', () => {
    localStorage.setItem('appearance_palette', 'custom')
    localStorage.setItem(CUSTOM_PALETTE_CSS_KEY, "[data-palette='custom'] { --bg: #123456; }")
    run()

    expect(document.documentElement.getAttribute('data-palette')).toBe('custom')
    expect(styleEl()!.textContent).toContain('#123456')
  })

  it('falls back to night without a mirror', () => {
    localStorage.setItem('appearance_palette', 'custom')
    run()

    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
    expect(styleEl()).toBeNull()
  })
})
```

`src/frontend/src/components/CustomPaletteSync.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from '../contexts/ThemeContext'
import CustomPaletteSync from './CustomPaletteSync'
import { createTestQueryClient } from '../test-utils'
import { CUSTOM_PALETTE_CSS_KEY, CUSTOM_PALETTE_STYLE_ID } from '../lib/customPaletteStyle'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))
vi.mock('../contexts/AuthContext', () => ({ useAuth: () => ({ isLoggedIn: true }) }))

function renderSync(value: string) {
  mocks.getPreferences.mockResolvedValue({ 'ui.customPalette': value })
  return render(
    <QueryClientProvider client={createTestQueryClient()}>
      <ThemeProvider><CustomPaletteSync /></ThemeProvider>
    </QueryClientProvider>,
  )
}

describe('CustomPaletteSync', () => {
  beforeEach(() => { localStorage.clear(); document.getElementById(CUSTOM_PALETTE_STYLE_ID)?.remove() })

  it("writes the account's palette", async () => {
    renderSync('265,muted,35')
    await waitFor(() => expect(document.getElementById(CUSTOM_PALETTE_STYLE_ID)).not.toBeNull())
    expect(localStorage.getItem('appearance_custom_palette')).toBe('265,muted,35')
  })

  // Another account on the same device: its choice of "custom" named someone else's palette.
  it('falls back to night when the account has none', async () => {
    localStorage.setItem('appearance_palette', 'custom')
    localStorage.setItem('appearance_custom_palette', '100,vivid,200')
    localStorage.setItem(CUSTOM_PALETTE_CSS_KEY, "[data-palette='custom'] {}")
    renderSync('')

    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('night'))
    expect(localStorage.getItem('appearance_palette')).toBe('night')
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBeNull()
  })
})
```

Ajouter à `src/frontend/src/contexts/ThemeContext.test.tsx` (suivre la forme de ses tests existants de palette) :

```tsx
it('keeps custom when its mirror exists', () => {
  localStorage.setItem('appearance_palette', 'custom')
  localStorage.setItem('appearance_custom_palette', '265,muted,35')
  localStorage.setItem('appearance_custom_palette_css', "[data-palette='custom'] {}")
  renderProvider()
  expect(document.documentElement.getAttribute('data-palette')).toBe('custom')
})

it('reads custom without a mirror as night', () => {
  localStorage.setItem('appearance_palette', 'custom')
  renderProvider()
  expect(document.documentElement.getAttribute('data-palette')).toBe('night')
})
```

(`renderProvider` : le helper de rendu déjà présent dans ce fichier ; le lire avant et réutiliser le sien.)

- [ ] **Step 2: Vérifier l'échec**

Run: `npx vitest run src/lib/customPaletteStyle.test.ts src/components/CustomPaletteSync.test.tsx src/contexts/ThemeContext.test.tsx`
Expected: FAIL (modules introuvables, `custom` ramené à `night`).

- [ ] **Step 3: Implémenter**

`src/frontend/src/lib/customPaletteStyle.ts` :

```ts
import { readStored, removeStored, writeStored } from './safeStorage'
import {
  customPaletteCss, formatCustomPalette, generateCustomPalette, parseCustomPalette, type CustomPaletteDef,
} from './customPalette'

// Repeated by the pre-paint script in index.html, which cannot import them.
export const CUSTOM_PALETTE_MIRROR_KEY = 'appearance_custom_palette'
export const CUSTOM_PALETTE_CSS_KEY = 'appearance_custom_palette_css'
export const CUSTOM_PALETTE_STYLE_ID = 'custom-palette'

export function hasCustomPaletteMirror(): boolean {
  return parseCustomPalette(readStored(CUSTOM_PALETTE_MIRROR_KEY)) !== null && readStored(CUSTOM_PALETTE_CSS_KEY) !== null
}

export function applyCustomPalette(def: CustomPaletteDef | null): void {
  let style = document.getElementById(CUSTOM_PALETTE_STYLE_ID)
  if (!def) {
    style?.remove()
    removeStored(CUSTOM_PALETTE_MIRROR_KEY)
    removeStored(CUSTOM_PALETTE_CSS_KEY)
    return
  }
  const css = customPaletteCss(generateCustomPalette(def))
  if (!style) {
    style = document.createElement('style')
    style.id = CUSTOM_PALETTE_STYLE_ID
    document.head.appendChild(style)
  }
  style.textContent = css
  writeStored(CUSTOM_PALETTE_MIRROR_KEY, formatCustomPalette(def))
  writeStored(CUSTOM_PALETTE_CSS_KEY, css)
}
```

`ThemeContext.tsx` :

```ts
export const PALETTE_IDS = [
  'night', 'classic', 'forest', 'slate', 'plum', 'ink', 'azure', 'indigo',
] as const
export type BuiltinPalette = typeof PALETTE_IDS[number]
export type Palette = BuiltinPalette | 'custom'
```

et `readPalette` :

```ts
function readPalette(): Palette {
  const stored = readStored(PALETTE_KEY)
  if (stored === 'custom') return hasCustomPaletteMirror() ? 'custom' : 'night'
  return PALETTE_IDS.includes(stored as BuiltinPalette) ? stored as BuiltinPalette : 'night'
}
```

`usePreferences.ts` : ajouter `customPalette: 'ui.customPalette',` à `PREFERENCE_KEYS`, et près de `languageOf` :

```ts
export function customPaletteOf(preferences: Preferences): CustomPaletteDef | null {
  return parseCustomPalette(preferences[PREFERENCE_KEYS.customPalette])
}
```

`src/frontend/src/components/CustomPaletteSync.tsx` :

```tsx
import { useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useTheme } from '../contexts/ThemeContext'
import { customPaletteOf, usePreferences } from '../hooks/usePreferences'
import { formatCustomPalette } from '../lib/customPalette'
import { applyCustomPalette } from '../lib/customPaletteStyle'

/** Keeps the <style> and the local mirror on the account's palette. Renders nothing. */
export default function CustomPaletteSync() {
  const { isLoggedIn } = useAuth()
  const { data } = usePreferences({ enabled: isLoggedIn })
  const { palette, setPalette } = useTheme()
  const def = data ? customPaletteOf(data) : undefined
  const key = def === undefined ? undefined : def && formatCustomPalette(def)

  useEffect(() => {
    if (def === undefined) return
    applyCustomPalette(def)
    if (!def && palette === 'custom') setPalette('night')
    // `key` stands for `def`, which is a fresh object on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, palette])

  return null
}
```

`App.tsx` : monter `<CustomPaletteSync />` dans `<AuthProvider>`, juste avant `<LocaleProvider>`.

`index.html`, dans le script de pré-affichage, remplacer la ligne de résolution de `p` par :

```js
        var p=localStorage.getItem('appearance_palette');
        if(p==='custom'){
          var css=localStorage.getItem('appearance_custom_palette_css');
          if(css){var s=document.createElement('style');s.id='custom-palette';s.textContent=css;document.head.appendChild(s)}
          else p='night';
        }
        else if(['night','classic','forest','slate','plum','ink','azure','indigo'].indexOf(p)<0)p='night';
```

`src/frontend/src/styles/palettes.test.ts` : le test « accepts exactly the palettes the module knows » garde son regex `\[([^\]]*)\]\.indexOf\(p\)` ; il reste vert. Ajouter :

```ts
it('reads the custom palette mirror the module writes', () => {
  expect(html).toContain(`localStorage.getItem('${CUSTOM_PALETTE_CSS_KEY}')`)
  expect(html).toContain(`s.id='${CUSTOM_PALETTE_STYLE_ID}'`)
})
```

(import `CUSTOM_PALETTE_CSS_KEY, CUSTOM_PALETTE_STYLE_ID` depuis `../lib/customPaletteStyle`).

- [ ] **Step 4: Vérifier le succès**

Run: `npx vitest run src/lib src/components/CustomPaletteSync.test.tsx src/contexts src/styles`
Expected: PASS. Puis `npx tsc -b` sans erreur (les usages de `Palette` dans `AppearancePage` compilent : `PalettePreview value` accepte `Palette`).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/lib/customPaletteStyle.ts src/frontend/src/lib/customPaletteStyle.test.ts src/frontend/src/components/CustomPaletteSync.tsx src/frontend/src/components/CustomPaletteSync.test.tsx src/frontend/src/contexts/ThemeContext.tsx src/frontend/src/contexts/ThemeContext.test.tsx src/frontend/src/hooks/usePreferences.ts src/frontend/src/App.tsx src/frontend/index.html src/frontend/src/styles/palettes.test.ts
git commit -q -F - <<'EOF'
Apply the account's custom palette, before paint and after login

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: La carte « My palette » et l'éditeur

**Files:**
- Create: `src/frontend/src/modules/settings/appearance/CustomPaletteEditor.tsx`
- Modify: `src/frontend/src/modules/settings/appearance/AppearancePage.tsx`, `src/frontend/src/styles/shell.css`, `src/frontend/src/locales/en/settings.json`, `src/frontend/src/locales/fr/settings.json`
- Test: `src/frontend/src/modules/settings/appearance/AppearancePage.test.tsx`

**Interfaces:**
- Consumes: `PALETTE_SEEDS`, `generateCustomPalette`, `formatCustomPalette`, `hueGradient`, `isNearDanger`, `INTENSITIES`, `CustomPaletteDef`, `Intensity` (Task 3) ; `customPaletteOf`, `PREFERENCE_KEYS.customPalette`, `useSetPreference`, `usePreferences` ; `Palette`, `BuiltinPalette` (Task 4) ; `apiErrorMessage` (`lib/apiErrorMessage`) ; `Modal`, `DropletIcon`, `PencilIcon`, `PlusIcon`, `SearchIcon`.
- Produces: `CustomPaletteEditor({ initial, onClose, onSaved }: { initial: CustomPaletteDef; onClose: () => void; onSaved: () => void })`.

- [ ] **Step 1: Les textes**

`en/settings.json`, dans `appearance` :

```json
"custom": {
  "name": "My palette",
  "create": "Create my palette",
  "edit": "Edit my palette",
  "structure": "Structure",
  "intensity": "Intensity",
  "neutral": "Neutral",
  "muted": "Subtle",
  "vivid": "Vivid",
  "accent": "Accent",
  "nearDanger": "This accent is close to the red used for errors: unread counts could read as alerts.",
  "cancel": "Cancel",
  "save": "Save",
  "saveFailed": "Could not save your palette."
}
```

`fr/settings.json` (U+00A0 avant `:`, apostrophe typographique `’` comme le reste du fichier) :

```json
"custom": {
  "name": "Ma palette",
  "create": "Créer ma palette",
  "edit": "Modifier ma palette",
  "structure": "Structure",
  "intensity": "Intensité",
  "neutral": "Neutre",
  "muted": "Sobre",
  "vivid": "Vive",
  "accent": "Accent",
  "nearDanger": "Cet accent est proche du rouge des erreurs : les compteurs de non-lus pourraient ressembler à une alerte.",
  "cancel": "Annuler",
  "save": "Enregistrer",
  "saveFailed": "Impossible d’enregistrer votre palette."
}
```

Vérifier l'insécable après écriture : `Select-String -Path src/frontend/src/locales/fr/settings.json -Pattern "erreurs :"` doit trouver la ligne ; sinon la corriger en PowerShell.

- [ ] **Step 2: Écrire les tests qui échouent**

Le rendu d'`AppearancePage` a désormais besoin d'un `QueryClient` et de l'API. En tête d'`AppearancePage.test.tsx`, remplacer `renderPage` par :

```tsx
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient } from '../../../test-utils'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function renderPage(customPalette = '') {
  mocks.getPreferences.mockResolvedValue({ 'ui.customPalette': customPalette })
  mocks.setPreference.mockResolvedValue(undefined)
  return render(
    <QueryClientProvider client={createTestQueryClient()}>
      <ThemeProvider><AppearancePage /></ThemeProvider>
    </QueryClientProvider>,
  )
}
```

(les tests existants restent synchrones : ils ne dépendent pas des préférences ; `beforeEach` ajoute `vi.clearAllMocks()` et retire `#custom-palette`.)

Ajouter :

```tsx
describe('AppearancePage — my palette', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    document.getElementById('custom-palette')?.remove()
  })

  it('offers to create one when the account has none', async () => {
    renderPage()
    expect(await screen.findByRole('button', { name: 'Create my palette' })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'My palette' })).toBeNull()
  })

  it('starts the editor from the palette in use', async () => {
    localStorage.setItem('appearance_palette', 'forest')
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Create my palette' }))

    expect((screen.getByRole('slider', { name: 'Structure' }) as HTMLInputElement).value).toBe('159')
    expect((screen.getByRole('slider', { name: 'Accent' }) as HTMLInputElement).value).toBe('71')
    expect(screen.getByRole('radio', { name: 'Subtle' })).toBeChecked()
  })

  it('saves the draft and selects it', async () => {
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Create my palette' }))
    fireEvent.change(screen.getByRole('slider', { name: 'Accent' }), { target: { value: '200' } })
    await userEvent.click(screen.getByRole('radio', { name: 'Vivid' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('ui.customPalette', '265,vivid,200'))
    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('custom'))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('keeps the editor open and the palette unchanged when saving fails', async () => {
    renderPage()
    mocks.setPreference.mockRejectedValue(new Error('offline'))
    await userEvent.click(await screen.findByRole('button', { name: 'Create my palette' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Could not save your palette.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
  })

  it('reopens the editor on the saved palette', async () => {
    renderPage('100,neutral,300')
    await userEvent.click(await screen.findByRole('button', { name: 'Edit my palette' }))

    expect((screen.getByRole('slider', { name: 'Structure' }) as HTMLInputElement).value).toBe('100')
    expect(screen.getByRole('radio', { name: 'Neutral' })).toBeChecked()
  })

  // The preview shows the draft; the grid keeps showing what is saved.
  it('previews the draft, not the saved palette', async () => {
    renderPage('100,neutral,300')
    await userEvent.click(await screen.findByRole('button', { name: 'Edit my palette' }))
    fireEvent.change(screen.getByRole('slider', { name: 'Structure' }), { target: { value: '10' } })

    const dialog = screen.getByRole('dialog')
    const draft = dialog.querySelector<HTMLElement>('.palette-preview')!
    expect(draft.style.getPropertyValue('--topbar-bg')).not.toBe('')
    const card = document.querySelector<HTMLElement>('.palette-card .palette-preview[data-palette="custom"]')!
    expect(card.style.getPropertyValue('--topbar-bg')).toBe('')
  })

  it('warns when the accent sits on the error red', async () => {
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Create my palette' }))
    expect(screen.queryByText(/close to the red used for errors/)).toBeNull()

    fireEvent.change(screen.getByRole('slider', { name: 'Accent' }), { target: { value: '27' } })
    expect(screen.getByText(/close to the red used for errors/)).toBeInTheDocument()
  })

  it('selects the saved palette like any other', async () => {
    renderPage('100,neutral,300')
    await userEvent.click(await screen.findByRole('radio', { name: 'My palette' }))
    expect(localStorage.getItem('appearance_palette')).toBe('custom')
  })
})
```

(`waitFor` à importer depuis `@testing-library/react`.)

- [ ] **Step 3: Vérifier l'échec**

Run: `npx vitest run src/modules/settings/appearance`
Expected: les nouveaux tests échouent (pas de bouton « Create my palette »).

- [ ] **Step 4: Implémenter**

`PalettePreview` (dans `AppearancePage.tsx`) accepte une prop `tokens?: TokenSet` posée en style inline :

```tsx
function PalettePreview({ value, dark, large, tokens }: { value: Palette; dark: boolean; large?: boolean; tokens?: TokenSet }) {
  return (
    <span
      className={`palette-preview${large ? ' is-large' : ''}`}
      data-palette={value}
      data-theme={dark ? 'dark' : 'light'}
      style={tokens as CSSProperties | undefined}
      aria-hidden="true"
    >
```

(le reste du composant ne change pas ; exporter `PalettePreview` pour l'éditeur.)

`CustomPaletteEditor.tsx` :

```tsx
import { useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'
import DropletIcon from '../../../icons/DropletIcon'
import { PREFERENCE_KEYS, useSetPreference } from '../../../hooks/usePreferences'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import {
  formatCustomPalette, generateCustomPalette, hueGradient, INTENSITIES, isNearDanger, type CustomPaletteDef,
} from '../../../lib/customPalette'
import { PalettePreview } from './AppearancePage'

// Structure track: the intensity's own chroma, lifted so a neutral track still reads as hues.
const TRACK_CHROMA = { neutral: 0.02, muted: 0.06, vivid: 0.14 } as const

export default function CustomPaletteEditor({ initial, onClose, onSaved }: {
  initial: CustomPaletteDef; onClose: () => void; onSaved: () => void
}) {
  const { t } = useTranslation('settings')
  const setPreference = useSetPreference()
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const tokens = useMemo(() => generateCustomPalette(draft), [draft])
  const ids = { structure: useId(), accent: useId(), intensity: useId() }

  async function save() {
    setError(null)
    try {
      await setPreference.mutateAsync({ key: PREFERENCE_KEYS.customPalette, value: formatCustomPalette(draft) })
      onSaved()
    } catch (e) {
      setError(apiErrorMessage(e, t('appearance.custom.saveFailed')))
    }
  }

  return (
    <Modal icon={<DropletIcon size={16} />} title={t('appearance.custom.name')} onClose={onClose}
      busy={setPreference.isPending} className="custom-palette-modal">
      <div className="custom-palette-editor">
        <div className="custom-palette-controls">
          <label htmlFor={ids.structure} className="custom-palette-label">{t('appearance.custom.structure')}</label>
          <input id={ids.structure} className="hue-slider" type="range" min={0} max={359} value={draft.structure}
            style={{ background: hueGradient(0.58, TRACK_CHROMA[draft.intensity]) }}
            onChange={e => setDraft({ ...draft, structure: Number(e.target.value) })} />
          <span id={ids.intensity} className="custom-palette-label">{t('appearance.custom.intensity')}</span>
          <div className="seg" role="radiogroup" aria-labelledby={ids.intensity}>
            {INTENSITIES.map(intensity => (
              <label key={intensity}>
                <input type="radio" name="custom-palette-intensity" checked={draft.intensity === intensity}
                  onChange={() => setDraft({ ...draft, intensity })} />
                {t(`appearance.custom.${intensity}`)}
              </label>
            ))}
          </div>
          <label htmlFor={ids.accent} className="custom-palette-label">{t('appearance.custom.accent')}</label>
          <input id={ids.accent} className="hue-slider" type="range" min={0} max={359} value={draft.accent}
            style={{ background: hueGradient(0.66, 0.15) }}
            onChange={e => setDraft({ ...draft, accent: Number(e.target.value) })} />
          {isNearDanger(draft.accent) && <p className="custom-palette-warning">{t('appearance.custom.nearDanger')}</p>}
        </div>
        <div className="palette-zoom-pair">
          {[false, true].map(dark => (
            <figure key={String(dark)}>
              <PalettePreview value="custom" dark={dark} large tokens={dark ? tokens.dark : tokens.light} />
              <figcaption>{t(dark ? 'appearance.theme.dark' : 'appearance.theme.light')}</figcaption>
            </figure>
          ))}
        </div>
      </div>
      {error && <p className="alert-error" role="alert">{error}</p>}
      <div className="modal-actions">
        <button type="button" className="btn btn-secondary btn-auto" onClick={onClose}>{t('appearance.custom.cancel')}</button>
        <button type="button" className="btn btn-primary btn-auto" onClick={save} disabled={setPreference.isPending}>
          {t('appearance.custom.save')}
        </button>
      </div>
    </Modal>
  )
}
```

Avant d'écrire : vérifier dans `index.css`/`modal.css` les noms réels des classes de pied de fenêtre et de bouton secondaire (`grep -n "modal-actions\|btn-secondary" src/frontend/src/index.css src/frontend/src/styles/modal.css`) et utiliser ceux qui existent. L'import circulaire `AppearancePage` ↔ `CustomPaletteEditor` se règle en déplaçant `PalettePreview` dans `PalettePreview.tsx` (même dossier) si le build le signale.

Dans `AppearancePage` :

```tsx
const { data: preferences } = usePreferences()
const saved = preferences ? customPaletteOf(preferences) : null
const [editing, setEditing] = useState<CustomPaletteDef | null>(null)
const seed = saved ?? PALETTE_SEEDS[palette === 'custom' ? 'night' : palette]
const customLabel = t('appearance.custom.name')
```

Après le `.map` des palettes, dans `.palette-grid` :

```tsx
{saved ? (
  <div className="palette-card">
    <label className="palette-pick">
      <PalettePreview value="custom" dark={isDark} />
      <span className="palette-name">
        <input type="radio" name="palette" value="custom" checked={palette === 'custom'}
          onChange={() => setPalette('custom')} />
        {customLabel}
      </span>
    </label>
    <button type="button" className="palette-zoom is-edit" aria-label={t('appearance.custom.edit')}
      onClick={() => setEditing(saved)}>
      <PencilIcon size={13} />
    </button>
    <button type="button" className="palette-zoom" aria-label={t('appearance.palette.enlarge', { name: customLabel })}
      onClick={() => setZoomed({ value: 'custom', label: customLabel })}>
      <SearchIcon size={14} />
    </button>
  </div>
) : (
  <button type="button" className="palette-card palette-create" onClick={() => setEditing(seed)}>
    <PlusIcon size={16} />
    {t('appearance.custom.create')}
  </button>
)}
```

et en bas de page :

```tsx
{editing && (
  <CustomPaletteEditor initial={editing} onClose={() => setEditing(null)}
    onSaved={() => { setPalette('custom'); setEditing(null) }} />
)}
```

`zoomed` accepte désormais `value: Palette`. Vérifier la signature réelle de `PencilIcon`/`PlusIcon` (`size`) avant usage.

`shell.css`, après les règles `.palette-zoom` :

```css
/* Top-left, clear of the loupe: their 44px touch insets would overlap side by side. */
.palette-zoom.is-edit { left: 6px; right: auto; }

.palette-create {
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 140px;
  border-style: dashed;
  background: var(--surface);
  color: var(--text);
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}
.palette-create:hover { border-color: var(--text-muted); }

.custom-palette-modal { max-width: 860px; }
.custom-palette-editor { display: flex; gap: 28px; flex-wrap: wrap; }
.custom-palette-controls { width: 280px; display: flex; flex-direction: column; gap: 10px; }
.custom-palette-label {
  margin-top: 8px;
  font-size: 13px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--text-muted);
}
.custom-palette-editor .palette-zoom-pair { flex: 1; min-width: 0; }
.custom-palette-warning { margin: 4px 0 0; font-size: 13px; color: var(--warning); }

/* The track paints the hue wheel from data, like a calendar swatch; the thumb wears role tokens. */
.hue-slider {
  appearance: none;
  width: 100%;
  height: 14px;
  margin: 6px 0;
  border-radius: 7px;
  cursor: pointer;
}
.hue-slider::-webkit-slider-thumb {
  appearance: none;
  width: 22px;
  height: 22px;
  border-radius: 50%;
  border: 2px solid var(--text);
  background: var(--surface);
}
.hue-slider::-moz-range-thumb {
  width: 18px;
  height: 18px;
  border-radius: 50%;
  border: 2px solid var(--text);
  background: var(--surface);
}
.hue-slider:focus-visible { outline: 2px solid var(--action-primary); outline-offset: 4px; }
```

Dans le bloc tactile qui contient `.palette-zoom::before` (vers la ligne 1285), ajouter son miroir :

```css
.palette-zoom.is-edit::before { left: -6px; right: -16px; }
```

- [ ] **Step 5: Vérifier le succès**

Run: `npx vitest run src/modules/settings/appearance src/a11y.test.tsx src/locales`
Expected: PASS (parité des clés de traduction incluse).

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/settings/appearance src/frontend/src/styles/shell.css src/frontend/src/locales/en/settings.json src/frontend/src/locales/fr/settings.json
git commit -q -F - <<'EOF'
Add the My palette card and its editor to the Appearance tab

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Documentation et vérification finale

**Files:**
- Modify: `.claude/rules/frontend-theming.md`, `src/frontend/docs/architecture-settings.md`

- [ ] **Step 1: Documenter**

`frontend-theming.md`, après le paragraphe des fichiers `theme-<id>.css`, ajouter un paragraphe :
la palette `custom` n'a pas de fichier ; `lib/customPalette.ts` la génère depuis `ui.customPalette`
(teinte de structure, intensité, teinte d'accent) avec les mêmes rôles que `night` ;
`CustomPaletteSync` l'injecte dans `<style id="custom-palette">` et la copie dans
`appearance_custom_palette(_css)` ; le script de pré-affichage lit cette copie et retombe sur
`night` sans elle ; le balayage de `customPalette.test.ts` garantit les seuils.

`architecture-settings.md`, section Apparence : la 9e carte, l'éditeur `CustomPaletteEditor`,
la définition sur le compte et le choix par appareil.

- [ ] **Step 2: Suites complètes**

Run: `npx vitest run` (dans `src/frontend`) → PASS ; `npx tsc -b` → sans erreur ; `npm run lint` → sans erreur ;
`dotnet test src/scotty.microservice/scotty.microservice.Tests` → PASS, puis révertir `ApiDocumentation.xml` si modifié.

- [ ] **Step 3: Vérification à l'écran**

Lancer le webmail en local (`npm run dev` dans `src/frontend`, API locale, cf. `project_dev_api_cors_no_localhost` : tout local), ouvrir Réglages › Apparence, créer une palette, l'enregistrer, recharger la page (pas de flash de Night), passer en sombre, rouvrir l'éditeur. Relever les écarts visibles.

- [ ] **Step 4: Commit**

```bash
git add .claude/rules/frontend-theming.md src/frontend/docs/architecture-settings.md
git commit -q -F - <<'EOF'
Document the custom palette

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
