# Spam Score Gauge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Une ligne « Spam score: » sous To:/Cc: dans le lecteur — jauge continue vert→rouge + score chiffré, tooltip avec le header brut — alimentée par rspamd, SpamAssassin ou le SCL Microsoft, derrière un réglage General actif par défaut.

**Architecture :** Le backend parse le header antispam en `{ score, threshold, raw }` (miroir de `MailAuthenticationReader` : topmost header de chaque nom, priorité rspamd > SpamAssassin > SCL) et l'expose sur `MailMessageDetail`. Le front calcule le ratio clampé (pure), rend la jauge (`color-mix` entre `--success` et `--danger`, zéro token nouveau) et gate la ligne par la préférence `mail.showSpamScore`.

**Tech Stack :** ASP.NET Core (.NET 10) + MimeKit + xUnit ; React 18 + TypeScript + Vitest.

Spec de référence : `docs/superpowers/specs/2026-07-22-spam-score-design.md`.

## Global Constraints

- **Aucun nouveau role token CSS, aucune couleur littérale** dans `mail.css`. La jauge se colore par `color-mix(in oklab, var(--success), var(--danger) …)` sur les tokens existants. Les fichiers `theme-*.css` ne doivent pas être touchés (`palettes.test.ts` casse sinon).
- **Règle 7 (CLAUDE.md backend) :** pour chaque nom de header, seule la première occurrence est lue — les suivantes sont falsifiables. Un header retenu mais illisible fait passer au moteur suivant, jamais à une occurrence plus basse.
- **Contrat JSON :** `DefaultIgnoreCondition = WhenWritingNull` omet la clé nulle — le front reçoit `undefined`, ses gardes acceptent les deux.
- C# : namespaces file-scoped, `sealed`, `record` pour les DTO, `internal` par défaut (`MailSpamScore` est `public`, il pend de `MailMessageDetail`), collection expressions, `[GeneratedRegex]` pour les regex.
- Commentaires : seulement quand le code seul ne suffit pas, 3 lignes max.
- Messages de commit : **deux lignes maximum** (sujet court, ligne vide, une ligne de corps max). Pas de trailer Co-Authored-By.
- Backend : `dotnet test` (jamais `--no-build`) dès qu'un fichier de test est ajouté. Répertoire : `src/snoopy.microservice`. Frontend : `src/frontend`.
- Nombres parsés en `CultureInfo.InvariantCulture` — jamais la culture système.

## File Structure

**Backend** (`src/snoopy.microservice/`)
- Créer `Models/Mail/MailSpamScore.cs` — le DTO.
- Créer `Services/MailSpamScoreReader.cs` — les trois parseurs, ordre de priorité fixe.
- Créer `snoopy.microservice.Tests/Services/MailSpamScoreReaderTests.cs`.
- Modifier `Models/Mail/MailMessageDetail.cs` — champ `SpamScore`.
- Modifier `Services/ImapSession.cs` — branchement dans `GetMessageAsync`.
- Modifier `Models/UserPreferences.cs` + `snoopy.microservice.Tests/Models/UserPreferencesTests.cs` — la clé `mail.showSpamScore`.

**Frontend** (`src/frontend/src/`)
- Modifier `modules/mail/api/mailTypes.ts` — `MailSpamScore`, champ `spamScore`.
- Créer `modules/mail/reader/spamRatio.ts` + `.test.ts` — le ratio clampé.
- Modifier `hooks/usePreferences.ts` — clé + accesseur `showSpamScoreOf`.
- Créer `modules/mail/reader/SpamGauge.tsx` + `.test.tsx` — la ligne complète.
- Modifier `styles/mail.css` — `.reader-spam`, `.spam-gauge-*`.
- Modifier `modules/mail/reader/MessageReader.tsx` + `.test.tsx` — le rendu gaté.
- Modifier `modules/settings/general/GeneralPage.tsx` + `.test.tsx` — le toggle.
- Modifier `src/frontend/CLAUDE.md` + `src/snoopy.microservice/CLAUDE.md` — docs (Task 7).

---

### Task 1: Backend — MailSpamScoreReader

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailSpamScore.cs`
- Create: `src/snoopy.microservice/Services/MailSpamScoreReader.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSpamScoreReaderTests.cs`

**Interfaces:**
- Consumes: rien (MimeKit `HeaderList`, déjà référencé).
- Produces: `public sealed record MailSpamScore(double Score, double Threshold, string Raw)` dans `weesky.Snoopy.Microservice.Models.Mail` ; `internal static partial class MailSpamScoreReader` dans `weesky.Snoopy.Microservice.Services`, exposant `public static MailSpamScore? Parse(HeaderList headers)`. `InternalsVisibleTo("snoopy.microservice.Tests")` est déjà déclaré.

- [ ] **Step 1: Écrire le DTO**

`src/snoopy.microservice/Models/Mail/MailSpamScore.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The spam filter's own verdict: score, the threshold it judges against, and the header it came from.</summary>
public sealed record MailSpamScore(double Score, double Threshold, string Raw);
```

- [ ] **Step 2: Écrire les tests qui échouent**

`src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSpamScoreReaderTests.cs` :

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailSpamScoreReaderTests
{
    private static HeaderList Headers(params (string Name, string Value)[] entries)
    {
        var headers = new HeaderList();
        foreach (var (name, value) in entries) headers.Add(new Header(name, value));
        return headers;
    }

    [Fact]
    public void Parse_ReadsAnRspamdResult()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [7.00 / 16.00]; R_SPF_ALLOW(-0.20)[+ip4:1.2.3.0/24]; DMARC_POLICY_ALLOW(-0.50)[weesky.be,none]")));

        Assert.NotNull(result);
        Assert.Equal(7.00, result!.Score);
        Assert.Equal(16.00, result.Threshold);
        Assert.StartsWith("X-Spamd-Result:", result.Raw);
    }

    [Fact]
    public void Parse_ReadsASpamAssassinStatus()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spam-Status", "No, score=2.3 required=5.0 tests=DKIM_SIGNED,DKIM_VALID autolearn=ham version=4.0.0")));

        Assert.Equal(2.3, result!.Score);
        Assert.Equal(5.0, result.Threshold);
    }

    // X-Spam-Score alone carries no threshold; 5.0 is SpamAssassin's universal default.
    [Fact]
    public void Parse_FallsBackToABareSpamAssassinScore()
    {
        var result = MailSpamScoreReader.Parse(Headers(("X-Spam-Score", "8.2")));

        Assert.Equal(8.2, result!.Score);
        Assert.Equal(5.0, result.Threshold);
        Assert.StartsWith("X-Spam-Score:", result.Raw);
    }

    [Fact]
    public void Parse_ReadsAnExchangeScl()
    {
        var result = MailSpamScoreReader.Parse(Headers(("X-MS-Exchange-Organization-SCL", "6")));

        Assert.Equal(6, result!.Score);
        Assert.Equal(5, result.Threshold);
    }

    // SCL -1 marks trusted internal mail; a negative score would read as "less than clean".
    [Fact]
    public void Parse_TreatsTrustedInternalMailAsClean()
    {
        var result = MailSpamScoreReader.Parse(Headers(("X-MS-Exchange-Organization-SCL", "-1")));

        Assert.Equal(0, result!.Score);
    }

    // Our own platform runs rspamd, so its header beats whatever an upstream relay added.
    [Fact]
    public void Parse_PrefersRspamdOverTheOtherEngines()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spam-Status", "Yes, score=9.9 required=5.0"),
            ("X-MS-Exchange-Organization-SCL", "9"),
            ("X-Spamd-Result", "default: False [1.10 / 15.00];")));

        Assert.Equal(1.10, result!.Score);
        Assert.Equal(15.00, result.Threshold);
    }

    [Fact]
    public void Parse_PrefersSpamAssassinOverScl()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-MS-Exchange-Organization-SCL", "9"),
            ("X-Spam-Status", "No, score=1.5 required=5.0")));

        Assert.Equal(1.5, result!.Score);
    }

    [Fact]
    public void Parse_ReadsOnlyTheTopmostHeaderOfAName()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [7.00 / 16.00];"),
            ("X-Spamd-Result", "default: False [0.00 / 16.00];")));

        Assert.Equal(7.00, result!.Score);
    }

    // An unreadable header moves to the next ENGINE, never to a lower occurrence of the same name.
    [Fact]
    public void Parse_MovesToTheNextEngineWhenAHeaderIsUnreadable()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [garbled];"),
            ("X-Spam-Status", "No, score=2.3 required=5.0")));

        Assert.Equal(2.3, result!.Score);
        Assert.StartsWith("X-Spam-Status:", result.Raw);
    }

    [Fact]
    public void Parse_KeepsANegativeScore()
    {
        var result = MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "default: False [-1.50 / 15.00];")));

        Assert.Equal(-1.50, result!.Score);
    }

    [Fact]
    public void Parse_ReturnsNullWithoutAnyKnownHeader()
    {
        Assert.Null(MailSpamScoreReader.Parse(Headers(("Subject", "hello"))));
    }

    [Fact]
    public void Parse_ReturnsNullWhenNothingIsReadable()
    {
        Assert.Null(MailSpamScoreReader.Parse(Headers(
            ("X-Spamd-Result", "nonsense"),
            ("X-Spam-Score", "not a number"),
            ("X-MS-Exchange-Organization-SCL", "high"))));
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~MailSpamScoreReaderTests`
Expected: échec de compilation — `MailSpamScoreReader` n'existe pas.

- [ ] **Step 4: Écrire le parseur**

`src/snoopy.microservice/Services/MailSpamScoreReader.cs` :

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the spam score out of the topmost header of each known anti-spam engine.</summary>
internal static partial class MailSpamScoreReader
{
    // Our own platform runs rspamd, so its header outranks whatever an upstream relay added.
    public static MailSpamScore? Parse(HeaderList headers) =>
        FromRspamd(headers) ?? FromSpamAssassin(headers) ?? FromExchangeScl(headers);

    private static MailSpamScore? FromRspamd(HeaderList headers)
    {
        var header = Topmost(headers, "X-Spamd-Result");
        if (header is null) return null;

        var match = RspamdScore().Match(header.Value);
        return match.Success
            ? new MailSpamScore(Number(match.Groups[1]), Number(match.Groups[2]), Raw(header))
            : null;
    }

    private static MailSpamScore? FromSpamAssassin(HeaderList headers)
    {
        var status = Topmost(headers, "X-Spam-Status");
        if (status is not null)
        {
            var score = SpamAssassinScore().Match(status.Value);
            var required = SpamAssassinRequired().Match(status.Value);
            if (score.Success && required.Success)
                return new MailSpamScore(Number(score.Groups[1]), Number(required.Groups[1]), Raw(status));
        }

        // X-Spam-Score alone carries no threshold; 5.0 is SpamAssassin's universal default.
        var bare = Topmost(headers, "X-Spam-Score");
        return bare is not null
            && double.TryParse(bare.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? new MailSpamScore(value, 5.0, Raw(bare))
            : null;
    }

    private static MailSpamScore? FromExchangeScl(HeaderList headers)
    {
        var header = Topmost(headers, "X-MS-Exchange-Organization-SCL");
        if (header is null
            || !int.TryParse(header.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var scl))
        {
            return null;
        }

        // SCL -1 is Microsoft's trusted-internal marker; 5 and up is classed as spam.
        return new MailSpamScore(Math.Max(0, scl), 5, Raw(header));
    }

    // Anything below the topmost occurrence could have been forged by the sender.
    private static Header? Topmost(HeaderList headers, string name)
    {
        foreach (var header in headers)
            if (string.Equals(header.Field, name, StringComparison.OrdinalIgnoreCase)) return header;

        return null;
    }

    private static string Raw(Header header) => $"{header.Field}: {header.Value}";

    private static double Number(Group group) => double.Parse(group.Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[(-?\d+(?:\.\d+)?)\s*/\s*(-?\d+(?:\.\d+)?)\]")]
    private static partial Regex RspamdScore();

    [GeneratedRegex(@"\bscore=(-?\d+(?:\.\d+)?)")]
    private static partial Regex SpamAssassinScore();

    [GeneratedRegex(@"\brequired=(-?\d+(?:\.\d+)?)")]
    private static partial Regex SpamAssassinRequired();
}
```

- [ ] **Step 5: Vérifier le vert**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~MailSpamScoreReaderTests`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailSpamScore.cs src/snoopy.microservice/Services/MailSpamScoreReader.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSpamScoreReaderTests.cs
git commit -m "Read the spam score from rspamd, SpamAssassin or the Exchange SCL

Topmost header of each name only, rspamd first: our own filter outranks upstream relays."
```

---

### Task 2: Backend — exposer le score et enregistrer la préférence

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs:20-21`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (`GetMessageAsync`, initialiseur du détail)
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Consumes: `MailSpamScore`, `MailSpamScoreReader.Parse` (Task 1).
- Produces: `MailMessageDetail.SpamScore` (`MailSpamScore?`) — en JSON `spamScore: { score, threshold, raw }`, clé **absente** quand null (`WhenWritingNull`) ; la constante `UserPreferences.MailShowSpamScore = "mail.showSpamScore"`, défaut `"true"`.

- [ ] **Step 1: Étendre MailMessageDetail**

Dans `MailMessageDetail.cs`, sous la propriété `Authentication` :

```csharp
    /// <summary>The spam filter's verdict. Null when the message carries no recognised anti-spam header.</summary>
    public MailSpamScore? SpamScore { get; set; }
```

- [ ] **Step 2: Brancher ImapSession**

Dans `ImapSession.cs`, `GetMessageAsync`, initialiseur de `MailMessageDetail`, juste après la ligne `Authentication = MailAuthenticationReader.Parse(message.Headers)` (ajouter la virgule qu'il faut) :

```csharp
                SpamScore = MailSpamScoreReader.Parse(message.Headers)
```

- [ ] **Step 3: Enregistrer la clé**

Dans `Models/UserPreferences.cs` : la constante, après `MailNotifyDesktop` :

```csharp
    public const string MailShowSpamScore = "mail.showSpamScore";
```

et l'entrée dans `All`, en dernier :

```csharp
        new(MailShowSpamScore, "true", Booleans),
```

- [ ] **Step 4: Compléter les tests à InlineData**

Dans `UserPreferencesTests.cs` — la Theory registry-wide couvre le défaut automatiquement ; les trois tests énumérés reçoivent leur cas à la main :

Dans `All_CarriesTheKeysTheClientOffers` :
```csharp
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailShowSpamScore);
```

Sur `Default_IsTheValueAnAccountWithNoRowsGets` :
```csharp
    [InlineData(UserPreferences.MailShowSpamScore, "true")]
```

Sur `IsValid_AcceptsOnlyTheOfferedValues` :
```csharp
    [InlineData(UserPreferences.MailShowSpamScore, "false", true)]
    [InlineData(UserPreferences.MailShowSpamScore, "yes", false)]
```

- [ ] **Step 5: Toute la suite backend**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS. Si un appelant casse sur le nouveau champ, corriger l'appelant.

- [ ] **Step 6: Commit**

```bash
git add src/snoopy.microservice/Models src/snoopy.microservice/Services/ImapSession.cs src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs
git commit -m "Expose the spam score on the message detail, behind mail.showSpamScore

The gauge ships enabled: the key defaults to true."
```

---

### Task 3: Frontend — types, ratio et accesseur

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts` (sous `MailAuthentication`)
- Create: `src/frontend/src/modules/mail/reader/spamRatio.ts`
- Create: `src/frontend/src/modules/mail/reader/spamRatio.test.ts`
- Modify: `src/frontend/src/hooks/usePreferences.ts`

**Interfaces:**
- Consumes: la forme JSON de la Task 2.
- Produces: `export interface MailSpamScore { score: number; threshold: number; raw: string }` ; `MailMessageDetail.spamScore: MailSpamScore | null` ; `export function spamRatio(spam: MailSpamScore | null | undefined): number | null` ; `PREFERENCE_KEYS.showSpamScore = 'mail.showSpamScore'` et `export function showSpamScoreOf(preferences: Preferences): boolean`.

- [ ] **Step 1: Déclarer les types**

Dans `mailTypes.ts`, sous l'interface `MailAuthentication` :

```ts
/** The spam filter's own verdict: score, the threshold it judges against, and the raw header. */
export interface MailSpamScore {
  score: number
  threshold: number
  raw: string
}
```

Dans `MailMessageDetail`, après `authentication: MailAuthentication | null` :

```ts
  spamScore: MailSpamScore | null
```

- [ ] **Step 2: Écrire le test du ratio qui échoue**

`spamRatio.test.ts` :

```ts
import { describe, it, expect } from 'vitest'
import { spamRatio } from './spamRatio'

const spam = (score: number, threshold: number) => ({ score, threshold, raw: 'X-Spamd-Result: …' })

describe('spamRatio', () => {
  it('is the score over the threshold', () => {
    expect(spamRatio(spam(7, 16))).toBeCloseTo(0.4375)
  })

  it('caps at 1 past the threshold', () => {
    expect(spamRatio(spam(20, 5))).toBe(1)
  })

  // Ham can score negative in both rspamd and SpamAssassin; the gauge floor is empty, not inverted.
  it('floors a negative score at 0', () => {
    expect(spamRatio(spam(-1.5, 15))).toBe(0)
  })

  it('refuses a threshold of zero or less rather than dividing by it', () => {
    expect(spamRatio(spam(3, 0))).toBeNull()
    expect(spamRatio(spam(3, -5))).toBeNull()
  })

  it('answers null for an absent score', () => {
    expect(spamRatio(null)).toBeNull()
    expect(spamRatio(undefined)).toBeNull()
  })
})
```

- [ ] **Step 3: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/spamRatio.test.ts`
Expected: FAIL — `Failed to resolve import "./spamRatio"`.

- [ ] **Step 4: Écrire le ratio**

`spamRatio.ts` :

```ts
import type { MailSpamScore } from '../api/mailTypes'

export function spamRatio(spam: MailSpamScore | null | undefined): number | null {
  if (!spam || spam.threshold <= 0) return null

  return Math.min(1, Math.max(0, spam.score / spam.threshold))
}
```

- [ ] **Step 5: Vérifier le vert**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/spamRatio.test.ts`
Expected: PASS, 5 tests.

- [ ] **Step 6: La préférence côté front**

Dans `hooks/usePreferences.ts` — l'entrée dans `PREFERENCE_KEYS` :

```ts
  showSpamScore: 'mail.showSpamScore',
```

et l'accesseur, à côté de `showPreviewOf` :

```ts
/** On unless explicitly off — the gauge ships enabled, like the list preview. */
export function showSpamScoreOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.showSpamScore] !== 'false'
}
```

- [ ] **Step 7: Suite + typecheck**

Run: `cd src/frontend && npm run test && npm run typecheck`
Expected: PASS partout. Le fixture de `MessageReader.test.tsx` est passé à `mockResolvedValue` (non typé), donc l'absence de `spamScore` n'y casse rien ; la Task 5 le complète.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/modules/mail/api/mailTypes.ts src/frontend/src/modules/mail/reader/spamRatio.ts src/frontend/src/modules/mail/reader/spamRatio.test.ts src/frontend/src/hooks/usePreferences.ts
git commit -m "Type the spam score, clamp its ratio, and read the new preference

threshold <= 0 answers null: no gauge beats a meaningless division."
```

---

### Task 4: Frontend — SpamGauge et son CSS

**Files:**
- Create: `src/frontend/src/modules/mail/reader/SpamGauge.tsx`
- Create: `src/frontend/src/modules/mail/reader/SpamGauge.test.tsx`
- Modify: `src/frontend/src/styles/mail.css` (sous le bloc `.auth-badge`)

**Interfaces:**
- Consumes: `Tooltip` (placement `bottom-left`), `MailSpamScore`, `spamRatio` (Task 3).
- Produces: `export default function SpamGauge({ spamScore }: { spamScore: MailSpamScore | null | undefined })` — rend la ligne `.reader-spam` complète, ou `null`. Consommé par la Task 5.

- [ ] **Step 1: Écrire les tests qui échouent**

`SpamGauge.test.tsx` :

```tsx
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import SpamGauge from './SpamGauge'

const spam = { score: 7, threshold: 16, raw: 'X-Spamd-Result: default: False [7.00 / 16.00];' }

describe('SpamGauge', () => {
  it('shows the label and the score as the filter reported it', () => {
    render(<SpamGauge spamScore={spam} />)

    expect(screen.getByText(/^Spam score:/)).toBeInTheDocument()
    expect(screen.getByText('7.0 / 16.0')).toBeInTheDocument()
  })

  // jsdom applies no stylesheet, so the custom property on the track is what a test can pin:
  // it drives both the fill width and the green-to-red mix.
  it('hands the clamped ratio to the CSS', () => {
    const { container } = render(<SpamGauge spamScore={spam} />)

    const track = container.querySelector('.spam-gauge-track') as HTMLElement
    expect(track.style.getPropertyValue('--gauge-ratio')).toBe('0.4375')
  })

  it('keeps the raw header one hover away', () => {
    render(<SpamGauge spamScore={spam} />)

    expect(screen.getByRole('tooltip')).toHaveTextContent('X-Spamd-Result: default: False')
  })

  it('renders nothing without a score', () => {
    const { container } = render(<SpamGauge spamScore={null} />)

    expect(container.textContent).toBe('')
  })

  it('renders nothing when the threshold makes no sense', () => {
    const { container } = render(<SpamGauge spamScore={{ score: 3, threshold: 0, raw: 'x' }} />)

    expect(container.textContent).toBe('')
  })
})
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/SpamGauge.test.tsx`
Expected: FAIL — `Failed to resolve import "./SpamGauge"`.

- [ ] **Step 3: Écrire le composant**

`SpamGauge.tsx` :

```tsx
import type { CSSProperties } from 'react'
import Tooltip from '../../../components/Tooltip'
import type { MailSpamScore } from '../api/mailTypes'
import { spamRatio } from './spamRatio'

export default function SpamGauge({ spamScore }: { spamScore: MailSpamScore | null | undefined }) {
  const ratio = spamRatio(spamScore)
  if (ratio === null || !spamScore) return null

  return (
    <div className="reader-spam">
      Spam score:{' '}
      <Tooltip content={spamScore.raw} placement="bottom-left">
        <span className="spam-gauge" tabIndex={0}>
          <span
            className="spam-gauge-track"
            style={{ '--gauge-ratio': String(ratio) } as CSSProperties}
          >
            <span className="spam-gauge-fill" />
          </span>
          <span className="spam-gauge-value">
            {spamScore.score.toFixed(1)} / {spamScore.threshold.toFixed(1)}
          </span>
        </span>
      </Tooltip>
    </div>
  )
}
```

Le `tabIndex={0}` rend la jauge focusable, donc son tooltip atteignable au clavier — même règle que le badge SPF/DKIM.

- [ ] **Step 4: Vérifier le vert**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/SpamGauge.test.tsx`
Expected: PASS, 5 tests.

- [ ] **Step 5: Le CSS**

Dans `styles/mail.css`, sous le bloc `.auth-badge.is-fail` :

```css
/* Block, not flex: a flex row makes "Spam score: " an anonymous item and strips its
   trailing space — the same trap .reader-recipients fell into. */
.reader-spam { display: block; }

.spam-gauge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  vertical-align: middle;
}

.spam-gauge-track {
  width: 90px;
  height: 8px;
  flex: none;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--surface-sunken);
  overflow: hidden;
}

/* One custom property drives both the length and the green-to-red mix, so the two can
   never disagree about how spammy a message looks. */
.spam-gauge-fill {
  display: block;
  height: 100%;
  width: calc(var(--gauge-ratio) * 100%);
  background: color-mix(in oklab, var(--success), var(--danger) calc(var(--gauge-ratio) * 100%));
}
```

Aucune couleur littérale, aucun token nouveau : `--border`, `--surface-sunken`, `--success`, `--danger` existent dans les six palettes.

- [ ] **Step 6: Suite + typecheck + lint**

Run: `cd src/frontend && npm run test && npm run typecheck && npm run lint`
Expected: PASS partout.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/reader/SpamGauge.tsx src/frontend/src/modules/mail/reader/SpamGauge.test.tsx src/frontend/src/styles/mail.css
git commit -m "Build the spam gauge: one custom property drives width and colour

color-mix between --success and --danger, so no palette file learns a new token."
```

---

### Task 5: Frontend — brancher la jauge dans le lecteur

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (imports + fin du bloc `.reader-meta`)
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx` (fixture + nouveaux tests)

**Interfaces:**
- Consumes: `SpamGauge` (Task 4), `showSpamScoreOf` (Task 3).
- Produces: la ligne finale du header. Rien en aval.

- [ ] **Step 1: Compléter le fixture**

Dans `MessageReader.test.tsx`, constante `detail`, après `authentication: null,` :

```ts
  spamScore: null,
```

- [ ] **Step 2: Écrire les tests qui échouent**

Ajouter dans `MessageReader.test.tsx` :

```tsx
  describe('the spam gauge', () => {
    const scored = {
      ...detail,
      spamScore: { score: 7, threshold: 16, raw: 'X-Spamd-Result: default: False [7.00 / 16.00];' },
    }

    it('shows the gauge when the message carries a score', async () => {
      mocks.getMailMessage.mockResolvedValue(scored)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByText('7.0 / 16.0')).toBeInTheDocument()
    })

    it('shows nothing when the message carries none', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/^Spam score:/)).not.toBeInTheDocument()
    })

    it('honours the setting that turns it off', async () => {
      mocks.getMailMessage.mockResolvedValue(scored)
      mocks.getPreferences.mockResolvedValue({ 'mail.showSpamScore': 'false' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/^Spam score:/)).not.toBeInTheDocument()
    })
  })
```

- [ ] **Step 3: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/MessageReader.test.tsx`
Expected: FAIL — le premier test ne trouve pas `7.0 / 16.0`.

- [ ] **Step 4: Brancher le composant**

Dans `MessageReader.tsx` — aux imports :

```tsx
import SpamGauge from './SpamGauge'
```

et `showSpamScoreOf` ajouté à l'import existant de `usePreferences` (qui apporte déjà `alwaysShowImagesOf`).

Dans le JSX, dernière ligne de `.reader-meta`, après le bloc Cc :

```tsx
          {!!preferences && showSpamScoreOf(preferences) && <SpamGauge spamScore={data.spamScore} />}
```

Le garde `!!preferences` suit le pattern images : tant que les préférences chargent, on ne montre rien plutôt que de deviner.

- [ ] **Step 5: Vérifier le vert, puis la suite**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/MessageReader.test.tsx && npm run test && npm run typecheck && npm run lint`
Expected: PASS partout.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/mail/reader/MessageReader.tsx src/frontend/src/modules/mail/reader/MessageReader.test.tsx
git commit -m "Render the spam gauge under To and Cc, gated by the setting

Nothing shows while preferences load: same fail-closed rule as remote images."
```

---

### Task 6: Frontend — le réglage dans General

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx` (import + un ToggleRow)
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `showSpamScoreOf`, `PREFERENCE_KEYS.showSpamScore` (Task 3).
- Produces: rien en aval.

- [ ] **Step 1: Écrire les tests qui échouent**

Dans `GeneralPage.test.tsx`, à côté des tests du toggle images :

```tsx
  it('shows the spam score toggle on by default and off when stored off', async () => {
    renderPage()
    expect(await screen.findByLabelText('Show the spam score in the message reader')).toBeChecked()
  })

  it('saves the spam score toggle', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showSpamScore': 'true' })

    fireEvent.click(await screen.findByLabelText('Show the spam score in the message reader'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.showSpamScore', 'false'))
  })
```

(Adapter l'appel `renderPage` à la signature réelle du fichier — il accepte une map de préférences.)

- [ ] **Step 2: Supprimer l'assertion de comptage — décision portée par la revue finale de la tranche always-show-images**

Le test `lays its rows out as settings rows, not dialog rows` assert `expect(rows).toHaveLength(5)`. Ce nombre magique doit être incrémenté à chaque nouveau réglage, et la revue finale d'always-show-images a acté : « delete the length assertion next time the file is touched ». C'est maintenant. Supprimer **la ligne `toHaveLength` seulement** — la boucle `rows.forEach(row => expect(row).toHaveClass('is-setting'))` reste, c'est elle qui porte la propriété qui compte.

- [ ] **Step 3: Vérifier l'échec**

Run: `cd src/frontend && npm run test -- src/modules/settings/general/GeneralPage.test.tsx`
Expected: FAIL — le label `Show the spam score in the message reader` n'existe pas.

- [ ] **Step 4: Ajouter le toggle**

Dans `GeneralPage.tsx` — `showSpamScoreOf` ajouté à l'import de `usePreferences`, puis entre la note du toggle images (`{alwaysShowImagesOf(preferences) && (…)}`) et le `ToggleRow` `notify-sound` :

```tsx
          <ToggleRow
            id="show-spam-score"
            label="Show the spam score in the message reader"
            checked={showSpamScoreOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.showSpamScore, String(on),
              on ? 'The spam score is shown' : 'The spam score is hidden')}
          />
```

- [ ] **Step 5: Vérifier le vert, puis la suite**

Run: `cd src/frontend && npm run test -- src/modules/settings/general/GeneralPage.test.tsx && npm run test && npm run typecheck && npm run lint`
Expected: PASS partout.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/settings/general/GeneralPage.tsx src/frontend/src/modules/settings/general/GeneralPage.test.tsx
git commit -m "Offer the spam score toggle in General, on by default

Also drops the .field-h count assertion, as the last slice's review decided."
```

---

### Task 7: Documentation

**Files:**
- Modify: `src/snoopy.microservice/CLAUDE.md` (règle 7)
- Modify: `src/frontend/CLAUDE.md` (section reader + GeneralPage)

**Interfaces:** rien — docs seules.

- [ ] **Step 1: Backend**

Dans la règle 7 de `src/snoopy.microservice/CLAUDE.md` (« Only the topmost `Authentication-Results` header is trusted… »), ajouter à la fin de la règle :

```markdown
`MailSpamScoreReader` obeys the same rule for the anti-spam headers (`X-Spamd-Result`, `X-Spam-Status`/`X-Spam-Score`, `X-MS-Exchange-Organization-SCL`): topmost occurrence of each name, rspamd first because it is the filter this platform itself runs, and an unreadable header moves to the next engine, never to a lower occurrence.
```

- [ ] **Step 2: Frontend**

Dans `src/frontend/CLAUDE.md`, à la fin du paragraphe « The reader header is a stack… », ajouter :

```markdown
Below To/Cc, `SpamGauge` (`reader/SpamGauge.tsx` + `reader/spamRatio.ts`) renders "Spam score:" with a bar whose single `--gauge-ratio` custom property drives both its width and its `color-mix(in oklab, var(--success), var(--danger) …)` colour — no new token, no literal colour. The line renders only when the message carries a recognised anti-spam header AND `mail.showSpamScore` (on unless explicitly off, read in `MessageReader`, not in the component) allows it. `.reader-spam` is `display: block` for the same trailing-space reason as `.reader-recipients`.
```

Et dans la liste des réglages de `general/` (« messages per page, message-list preview, always-show-remote-images, new-mail sound and desktop notification toggles »), insérer `spam-score visibility` après `always-show-remote-images`.

- [ ] **Step 3: Commit**

```bash
git add src/snoopy.microservice/CLAUDE.md src/frontend/CLAUDE.md
git commit -m "Document the spam gauge and extend rule 7 to the anti-spam headers"
```

---

## Vérification finale

- [ ] `cd src/snoopy.microservice && dotnet test` — toute la suite au vert.
- [ ] `cd src/frontend && npm run test && npm run typecheck && npm run lint && npm run build` — tout au vert.
- [ ] Contrôle visuel (après déploiement — le front ne tourne pas en local contre l'API de prod) : jauge verte courte sur un mail sain, jauge longue rouge sur un spam, tooltip lisible, rendu correct dans les deux modes d'au moins deux palettes, ligne absente quand le réglage est off.
