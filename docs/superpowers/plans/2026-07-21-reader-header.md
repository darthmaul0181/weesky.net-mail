# Reader Header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructurer l'en-tête du lecteur de message en `Titre / Sender [✓] (date) / To: / Cc:`, avec un libellé d'expéditeur porteur d'un tooltip, un badge d'authentification SPF+DKIM, et des destinataires nommés.

**Architecture :** Le backend lit le header `Authentication-Results` du `MimeMessage` déjà chargé et l'expose sous forme parsée, et cesse de jeter le display name des destinataires. Le frontend extrait de `HelpTooltip` un composant `Tooltip` générique, puis rend l'en-tête à partir de deux nouveaux composants (`AddressLabel`, `AuthBadge`) et d'une fonction de verdict pure.

**Tech Stack :** ASP.NET Core (.NET 10) + MailKit/MimeKit + xUnit côté backend ; React 18 + TypeScript + Vitest + `@testing-library/react` côté frontend.

Spec de référence : `docs/superpowers/specs/2026-07-21-reader-header-design.md`.

## Global Constraints

- **Aucun nouveau role token CSS.** Le badge utilise `--success`, `--danger` et `--badge-count-fg`, la bulle utilise `--surface`, `--border`, `--text`, `--radius-sm` — tous déjà déclarés dans les six palettes. Les fichiers `src/frontend/src/styles/theme-*.css` ne doivent pas être modifiés, sous peine de faire échouer `src/frontend/src/styles/palettes.test.ts`.
- **Aucune couleur littérale dans `mail.css` ni dans `tooltip.css`.** Un composant ne code jamais une couleur en dur ; il consomme un role token.
- **Commentaires :** uniquement quand le code seul ne suffit pas, 3 lignes maximum.
- **C# :** namespaces file-scoped, `sealed`, `record` pour les DTO immuables, `internal` par défaut. Les DTO référencés depuis `MailMessageDetail` (qui est `public`) doivent être `public`.
- **Messages de commit :** deux lignes maximum, concis.
- **Backend :** lancer `dotnet test` (jamais `--no-build`) dès qu'un fichier de test est ajouté.
- Répertoire de travail backend : `src/snoopy.microservice`. Frontend : `src/frontend`.

## File Structure

**Backend** (`src/snoopy.microservice/`)
- Créer `Models/Mail/MailAuthentication.cs` — le DTO du verdict d'authentification.
- Créer `Models/Mail/MailAddressInfo.cs` — nom + adresse d'un destinataire.
- Créer `Services/AuthenticationResults.cs` — le parseur pur du header `Authentication-Results`.
- Modifier `Models/Mail/MailMessageDetail.cs` — `To`/`Cc` typés, champ `Authentication`.
- Modifier `Services/ImapSession.cs` — projection des destinataires et appel du parseur.
- Créer `snoopy.microservice.Tests/Services/AuthenticationResultsTests.cs`.

**Frontend** (`src/frontend/src/`)
- Créer `components/Tooltip.tsx` + `components/Tooltip.test.tsx` — la bulle générique.
- Créer `styles/tooltip.css` ; l'importer dans `main.tsx`.
- Modifier `components/HelpTooltip.jsx` — réécrit par-dessus `Tooltip`.
- Modifier `index.css` — retrait des règles de bulle devenues doublons.
- Modifier `modules/mail/api/mailTypes.ts` — `MailAddressInfo`, `MailAuthentication`, champs de `MailMessageDetail`.
- Créer `modules/mail/reader/authVerdict.ts` + `.test.ts` — la règle vert/rouge/rien.
- Créer `modules/mail/reader/AddressLabel.tsx` + `.test.tsx` — libellé + tooltip, sender ou destinataire.
- Créer `modules/mail/reader/AuthBadge.tsx` — la pastille.
- Modifier `modules/mail/reader/MessageReader.tsx` + `.test.tsx` — la nouvelle mise en page.
- Modifier `styles/mail.css` — `.reader-meta` en colonne, `.reader-from`, `.address-label`, `.auth-badge`.

---

### Task 1: Parseur du header Authentication-Results

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailAuthentication.cs`
- Create: `src/snoopy.microservice/Services/AuthenticationResults.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/AuthenticationResultsTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `public sealed record MailAuthentication(string? Spf, string? Dkim, string Raw)` dans `weesky.Snoopy.Microservice.Models.Mail` ; `internal static class AuthenticationResults` dans `weesky.Snoopy.Microservice.Services` exposant `public static MailAuthentication? Parse(MimeKit.HeaderList headers)`. Le projet déclare `InternalsVisibleTo("snoopy.microservice.Tests")`, donc la classe interne est directement testable.

- [ ] **Step 1: Écrire le DTO**

`src/snoopy.microservice/Models/Mail/MailAuthentication.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>SPF and DKIM verdicts as the receiving server reported them, plus the header they came from.</summary>
public sealed record MailAuthentication(string? Spf, string? Dkim, string Raw);
```

- [ ] **Step 2: Écrire les tests qui échouent**

`src/snoopy.microservice/snoopy.microservice.Tests/Services/AuthenticationResultsTests.cs` :

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class AuthenticationResultsTests
{
    private static HeaderList Headers(params string[] values)
    {
        var headers = new HeaderList();
        foreach (var value in values) headers.Add(new Header("Authentication-Results", value));
        return headers;
    }

    [Fact]
    public void Parse_ReadsBothVerdictsFromARealHeader()
    {
        var headers = Headers(
            "mx.google.com; dkim=pass header.i=@claude.com header.s=s1; " +
            "spf=pass (google.com: domain of no-reply@claude.com designates 1.2.3.4 as permitted sender) " +
            "smtp.mailfrom=no-reply@claude.com; dmarc=pass header.from=claude.com");

        var result = AuthenticationResults.Parse(headers);

        Assert.NotNull(result);
        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
        Assert.Contains("dmarc=pass", result.Raw);
    }

    [Fact]
    public void Parse_ReturnsNullWithoutTheHeader()
    {
        var headers = new HeaderList { new Header("Subject", "hello") };

        Assert.Null(AuthenticationResults.Parse(headers));
    }

    // Each relay prepends its own header, so the topmost one is the receiving server's verdict.
    [Fact]
    public void Parse_LetsTheMostRecentHeaderWin()
    {
        var headers = Headers("mx.weesky.net; spf=fail; dkim=fail", "relay.upstream.net; spf=pass; dkim=pass");

        var result = AuthenticationResults.Parse(headers);

        Assert.Equal("fail", result!.Spf);
        Assert.Equal("fail", result.Dkim);
    }

    [Fact]
    public void Parse_FillsAMethodMissingFromTheFirstHeaderFromTheNext()
    {
        var headers = Headers("mx.weesky.net; spf=pass", "relay.upstream.net; dkim=pass");

        var result = AuthenticationResults.Parse(headers);

        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
    }

    [Fact]
    public void Parse_LeavesAMissingMethodNull()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; spf=softfail"));

        Assert.Equal("softfail", result!.Spf);
        Assert.Null(result.Dkim);
    }

    [Fact]
    public void Parse_MatchesTheMethodAndNormalisesTheResultRegardlessOfCase()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; SPF=Pass; DKIM=PASS"));

        Assert.Equal("pass", result!.Spf);
        Assert.Equal("pass", result.Dkim);
    }

    // A header mentioning neither method still proves the server ran checks; the verdicts are
    // simply unknown, which the reader renders as no badge at all.
    [Fact]
    public void Parse_KeepsAHeaderCarryingNeitherMethod()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; dmarc=pass header.from=claude.com"));

        Assert.NotNull(result);
        Assert.Null(result!.Spf);
        Assert.Null(result.Dkim);
        Assert.Contains("dmarc=pass", result.Raw);
    }

    // "smtp.mailfrom=x" must not be mistaken for the spf method, nor "header.i=" for dkim.
    [Fact]
    public void Parse_IgnoresPropertiesThatMerelyContainTheMethodName()
    {
        var result = AuthenticationResults.Parse(Headers("mx.weesky.net; none; smtp.mailfrom=spf@x.be"));

        Assert.Null(result!.Spf);
        Assert.Null(result.Dkim);
    }
}
```

- [ ] **Step 3: Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~AuthenticationResultsTests`
Expected: échec de compilation — `The name 'AuthenticationResults' does not exist in the current context`.

- [ ] **Step 4: Écrire le parseur**

`src/snoopy.microservice/Services/AuthenticationResults.cs` :

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the SPF and DKIM verdicts out of the Authentication-Results headers (RFC 8601).</summary>
internal static class AuthenticationResults
{
    private const string HeaderName = "Authentication-Results";

    public static MailAuthentication? Parse(HeaderList headers)
    {
        string? spf = null, dkim = null, first = null, verdicts = null;

        foreach (var header in headers)
        {
            if (!string.Equals(header.Field, HeaderName, StringComparison.OrdinalIgnoreCase)) continue;

            first ??= header.Value;

            var headerSpf = MethodResult(header.Value, "spf");
            var headerDkim = MethodResult(header.Value, "dkim");
            if (headerSpf is null && headerDkim is null) continue;

            spf ??= headerSpf;
            dkim ??= headerDkim;
            verdicts ??= header.Value;
        }

        return first is null ? null : new MailAuthentication(spf, dkim, verdicts ?? first);
    }

    private static string? MethodResult(string value, string method)
    {
        foreach (var token in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = token.IndexOf('=');
            if (equals < 0 || !string.Equals(token[..equals], method, StringComparison.OrdinalIgnoreCase)) continue;

            var result = token[(equals + 1)..].TrimStart();
            var end = result.IndexOfAny([' ', '\t', '(']);
            return (end < 0 ? result : result[..end]).ToLowerInvariant();
        }

        return null;
    }
}
```

Le `token[..equals]` compare le nom de méthode entier, pas une sous-chaîne : c'est ce qui empêche `smtp.mailfrom=` d'être lu comme un résultat SPF. La coupure sur `' '`, `'\t'` ou `'('` détache le verdict des propriétés et du commentaire qui le suivent dans le même token.

- [ ] **Step 5: Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~AuthenticationResultsTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailAuthentication.cs src/snoopy.microservice/Services/AuthenticationResults.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/AuthenticationResultsTests.cs
git commit -m "Parse the SPF and DKIM verdicts out of Authentication-Results

Topmost header wins per method: each relay prepends its own, so the first one is the receiving server's."
```

---

### Task 2: Exposer l'authentification et les destinataires nommés

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailAddressInfo.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs:15-17`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs:389-403`

**Interfaces:**
- Consumes: `MailAuthentication`, `AuthenticationResults.Parse` (Task 1).
- Produces: `public sealed record MailAddressInfo(string Name, string Address)` ; `MailMessageDetail.To` et `.Cc` de type `List<MailAddressInfo>` ; `MailMessageDetail.Authentication` de type `MailAuthentication?`. En JSON ces champs sortent en `to: [{ name, address }]`, `cc`, `authentication: { spf, dkim, raw } | null` — c'est ce que la Task 4 déclare côté TypeScript.

- [ ] **Step 1: Écrire le DTO destinataire**

`src/snoopy.microservice/Models/Mail/MailAddressInfo.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One recipient. Name is empty when the message carried no display name.</summary>
public sealed record MailAddressInfo(string Name, string Address);
```

Le suffixe `Info` suit `MailAttachmentInfo`, le DTO voisin, et écarte l'ambiguïté avec `System.Net.Mail.MailAddress`.

- [ ] **Step 2: Étendre MailMessageDetail**

Dans `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs`, remplacer les deux lignes `To`/`Cc` :

```csharp
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
```

par :

```csharp
    public List<MailAddressInfo> To { get; set; } = new();
    public List<MailAddressInfo> Cc { get; set; } = new();
```

et ajouter, juste après la propriété `Date` :

```csharp
    /// <summary>SPF/DKIM verdicts from the receiving server. Null when the message carries no Authentication-Results.</summary>
    public MailAuthentication? Authentication { get; set; }
```

- [ ] **Step 3: Câbler ImapSession**

Dans `src/snoopy.microservice/Services/ImapSession.cs`, méthode `GetMessageAsync`, remplacer les deux projections de destinataires :

```csharp
                To = message.To?.Mailboxes?.Select(m => m.Address).ToList() ?? new List<string>(),
                Cc = message.Cc?.Mailboxes?.Select(m => m.Address).ToList() ?? new List<string>(),
```

par :

```csharp
                To = ToAddressInfos(message.To),
                Cc = ToAddressInfos(message.Cc),
```

et ajouter, dans le même objet, juste après `BlockedImageCount = sanitized.BlockedImageCount` :

```csharp
                Authentication = AuthenticationResults.Parse(message.Headers)
```

(attention à la virgule sur la ligne `BlockedImageCount` qui devient non finale).

Ajouter enfin la méthode privée, à côté de `ToSummary` :

```csharp
    private static List<MailAddressInfo> ToAddressInfos(InternetAddressList? addresses) =>
        addresses?.Mailboxes?.Select(m => new MailAddressInfo(m.Name ?? string.Empty, m.Address)).ToList() ?? [];
```

`InternetAddressList` vient de `MimeKit`, déjà importé par le fichier.

- [ ] **Step 4: Compiler et lancer toute la suite backend**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS. `MailControllerTests` construit ses `MailMessageDetail` sans `To`/`Cc` (`new MailMessageDetail { Uid = 42, Subject = "Re: facture" }`), donc le changement de type ne casse aucun test existant. Si la compilation échoue ailleurs, corriger l'appelant plutôt que de revenir au type `string`.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail src/snoopy.microservice/Services/ImapSession.cs
git commit -m "Expose the auth verdicts and keep the recipients' display names

To/Cc were projected to bare addresses, dropping every display name the message carried."
```

---

### Task 3: Le composant Tooltip générique

**Files:**
- Create: `src/frontend/src/components/Tooltip.tsx`
- Create: `src/frontend/src/components/Tooltip.test.tsx`
- Create: `src/frontend/src/styles/tooltip.css`
- Modify: `src/frontend/src/main.tsx:10-12`
- Modify: `src/frontend/src/components/HelpTooltip.jsx`
- Modify: `src/frontend/src/index.css:1456-1502`

**Interfaces:**
- Consumes: rien.
- Produces: `export default function Tooltip({ content, placement, children })` — `content: ReactNode`, `placement?: 'top-right' | 'bottom-left'` (défaut `'top-right'`), `children: ReactNode`. Rend `<span class="tooltip-wrap">{children}<span class="tooltip-bubble is-{placement}" role="tooltip">{content}</span></span>`. Les Tasks 5 et 6 l'utilisent en `placement="bottom-left"`.

- [ ] **Step 1: Écrire le test qui échoue**

`src/frontend/src/components/Tooltip.test.tsx` :

```tsx
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import Tooltip from './Tooltip'

describe('Tooltip', () => {
  it('renders its trigger and its bubble', () => {
    render(<Tooltip content="the detail"><span>trigger</span></Tooltip>)

    expect(screen.getByText('trigger')).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('the detail')
  })

  // The bubble is revealed by CSS, so the placement modifier is the only thing a test can
  // hold on to — and getting it wrong puts the bubble outside the mail column's overflow.
  it('places the bubble above and to the right by default', () => {
    render(<Tooltip content="x"><span>trigger</span></Tooltip>)

    expect(screen.getByRole('tooltip')).toHaveClass('is-top-right')
  })

  it('places the bubble below and to the left on request', () => {
    render(<Tooltip content="x" placement="bottom-left"><span>trigger</span></Tooltip>)

    expect(screen.getByRole('tooltip')).toHaveClass('is-bottom-left')
  })
})
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm run test -- src/components/Tooltip.test.tsx`
Expected: FAIL — `Failed to resolve import "./Tooltip"`.

- [ ] **Step 3: Écrire le composant**

`src/frontend/src/components/Tooltip.tsx` :

```tsx
import type { ReactNode } from 'react'

interface Props {
  content: ReactNode
  placement?: 'top-right' | 'bottom-left'
  children: ReactNode
}

export default function Tooltip({ content, placement = 'top-right', children }: Props) {
  return (
    <span className="tooltip-wrap">
      {children}
      <span className={`tooltip-bubble is-${placement}`} role="tooltip">{content}</span>
    </span>
  )
}
```

- [ ] **Step 4: Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npm run test -- src/components/Tooltip.test.tsx`
Expected: PASS, 3 tests.

- [ ] **Step 5: Écrire la feuille de style**

`src/frontend/src/styles/tooltip.css` :

```css
.tooltip-wrap {
  position: relative;
  display: inline-flex;
}

.tooltip-bubble {
  display: none;
  position: absolute;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 10px 12px;
  font-size: 12px;
  font-weight: 400;
  line-height: 1.55;
  color: var(--text);
  white-space: pre-line;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.12);
  z-index: 10;
  pointer-events: none;
}

.tooltip-bubble.is-top-right {
  bottom: calc(100% + 8px);
  right: 0;
  width: 280px;
}

/* Anchored bottom-left because the mail column is overflow: hidden — a bubble hanging left
   of the header's leftmost element is the one placement that cannot be clipped. */
.tooltip-bubble.is-bottom-left {
  top: calc(100% + 8px);
  left: 0;
  width: max-content;
  max-width: 320px;
}

.tooltip-wrap:hover .tooltip-bubble,
.tooltip-wrap:focus-within .tooltip-bubble {
  display: block;
}
```

`white-space: pre-line` porte le tooltip multi-ligne du badge (Task 6). `is-top-right` garde la largeur fixe de 280px de l'ancienne bulle de `HelpTooltip`, pour que son rendu ne bouge pas.

- [ ] **Step 6: Importer la feuille de style**

Dans `src/frontend/src/main.tsx`, ajouter après `import './styles/shell.css'` :

```ts
import './styles/tooltip.css'
```

- [ ] **Step 7: Réécrire HelpTooltip par-dessus Tooltip**

`src/frontend/src/components/HelpTooltip.jsx` en entier :

```jsx
import Tooltip from './Tooltip'

export function HelpTooltip({ text }) {
  return (
    <Tooltip content={text}>
      <div className="help-tooltip-icon">?</div>
    </Tooltip>
  )
}
export default HelpTooltip
```

- [ ] **Step 8: Retirer les règles devenues doublons**

Dans `src/frontend/src/index.css`, supprimer les trois blocs `.help-tooltip-wrap`, `.help-tooltip-bubble` et `.help-tooltip-wrap:hover .help-tooltip-bubble` (lignes 1456-1459 et 1482-1502).

Remplacer aussi le sélecteur de survol de l'icône, qui référençait le wrap supprimé :

```css
.help-tooltip-wrap:hover .help-tooltip-icon {
```

par :

```css
.tooltip-wrap:hover .help-tooltip-icon {
```

`.help-tooltip-icon` et son bloc de règles restent tels quels.

- [ ] **Step 9: Lancer la suite frontend complète**

Run: `cd src/frontend && npm run test && npm run typecheck`
Expected: PASS. Aucun test ne cible `HelpTooltip` aujourd'hui ; s'il en existe un, ses assertions doivent passer sans modification — le refactor est à rendu constant.

- [ ] **Step 10: Commit**

```bash
git add src/frontend/src/components/Tooltip.tsx src/frontend/src/components/Tooltip.test.tsx src/frontend/src/components/HelpTooltip.jsx src/frontend/src/styles/tooltip.css src/frontend/src/main.tsx src/frontend/src/index.css
git commit -m "Extract the tooltip bubble out of HelpTooltip

HelpTooltip's bubble was soldered to its ? icon; the reader needs the same bubble on plain text."
```

---

### Task 4: Types et règle de verdict

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts:56-72`
- Create: `src/frontend/src/modules/mail/reader/authVerdict.ts`
- Create: `src/frontend/src/modules/mail/reader/authVerdict.test.ts`

**Interfaces:**
- Consumes: la forme JSON produite par la Task 2.
- Produces: `export interface MailAddressInfo { name: string; address: string }` et `export interface MailAuthentication { spf: string | null; dkim: string | null; raw: string }` dans `mailTypes.ts` ; `MailMessageDetail.to: MailAddressInfo[]`, `.cc: MailAddressInfo[]`, `.authentication: MailAuthentication | null`. Et `export type AuthVerdict = 'pass' | 'fail' | null` + `export function authVerdict(auth: MailAuthentication | null | undefined): AuthVerdict`, consommés par la Task 6.

- [ ] **Step 1: Déclarer les types**

Dans `src/frontend/src/modules/mail/api/mailTypes.ts`, ajouter au-dessus de `export interface MailMessageDetail {` :

```ts
export interface MailAddressInfo {
  name: string
  address: string
}

/** SPF/DKIM as the receiving server reported them, plus the raw header behind them. */
export interface MailAuthentication {
  spf: string | null
  dkim: string | null
  raw: string
}
```

Dans `MailMessageDetail`, remplacer :

```ts
  to: string[]
  cc: string[]
```

par :

```ts
  to: MailAddressInfo[]
  cc: MailAddressInfo[]
```

et ajouter, après la ligne `date: string` :

```ts
  authentication: MailAuthentication | null
```

- [ ] **Step 2: Écrire le test qui échoue**

`src/frontend/src/modules/mail/reader/authVerdict.test.ts` :

```ts
import { describe, it, expect } from 'vitest'
import { authVerdict } from './authVerdict'

const auth = (spf: string | null, dkim: string | null) => ({ spf, dkim, raw: 'mx.weesky.net; …' })

describe('authVerdict', () => {
  it('passes only when both methods passed', () => {
    expect(authVerdict(auth('pass', 'pass'))).toBe('pass')
  })

  it('fails when either method failed explicitly', () => {
    expect(authVerdict(auth('fail', 'pass'))).toBe('fail')
    expect(authVerdict(auth('pass', 'fail'))).toBe('fail')
    expect(authVerdict(auth('fail', 'fail'))).toBe('fail')
  })

  // A softfail or a neutral is not a failure, and painting either signal onto an ambiguous
  // result is worse than painting none: the reader learns to ignore the badge.
  it('says nothing about a result that is neither a pass nor a failure', () => {
    expect(authVerdict(auth('softfail', 'pass'))).toBeNull()
    expect(authVerdict(auth('neutral', 'neutral'))).toBeNull()
    expect(authVerdict(auth('temperror', 'permerror'))).toBeNull()
  })

  it('says nothing when a method is missing', () => {
    expect(authVerdict(auth('pass', null))).toBeNull()
    expect(authVerdict(auth(null, null))).toBeNull()
  })

  it('says nothing when the message carries no authentication at all', () => {
    expect(authVerdict(null)).toBeNull()
    expect(authVerdict(undefined)).toBeNull()
  })
})
```

- [ ] **Step 3: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/authVerdict.test.ts`
Expected: FAIL — `Failed to resolve import "./authVerdict"`.

- [ ] **Step 4: Écrire la règle**

`src/frontend/src/modules/mail/reader/authVerdict.ts` :

```ts
import type { MailAuthentication } from '../api/mailTypes'

export type AuthVerdict = 'pass' | 'fail' | null

export function authVerdict(auth: MailAuthentication | null | undefined): AuthVerdict {
  if (!auth) return null
  if (auth.spf === 'pass' && auth.dkim === 'pass') return 'pass'
  if (auth.spf === 'fail' || auth.dkim === 'fail') return 'fail'

  return null
}
```

- [ ] **Step 5: Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/authVerdict.test.ts`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/mail/api/mailTypes.ts src/frontend/src/modules/mail/reader/authVerdict.ts src/frontend/src/modules/mail/reader/authVerdict.test.ts
git commit -m "Type the auth verdicts and named recipients, and rule on the badge

Only a double pass is green and only an explicit fail is red; everything else shows nothing."
```

Note : `npm run typecheck` échouera tant que `MessageReader` lit `data.to.join(', ')` sur un tableau d'objets — c'est la Task 6 qui le répare. Ne pas contourner en retypant `to` en `string[]`.

---

### Task 5: AddressLabel

**Files:**
- Create: `src/frontend/src/modules/mail/reader/AddressLabel.tsx`
- Create: `src/frontend/src/modules/mail/reader/AddressLabel.test.tsx`

**Interfaces:**
- Consumes: `Tooltip` (Task 3), `MailAddressInfo` (Task 4).
- Produces: `export default function AddressLabel({ name, address, sender })` — `name: string`, `address: string`, `sender?: boolean` ; et `export function AddressList({ addresses }: { addresses: MailAddressInfo[] })`. Tous deux consommés par la Task 6.

- [ ] **Step 1: Écrire le test qui échoue**

`src/frontend/src/modules/mail/reader/AddressLabel.test.tsx` :

```tsx
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import AddressLabel, { AddressList } from './AddressLabel'

describe('AddressLabel', () => {
  it('shows the display name and keeps the full address in a tooltip', () => {
    render(<AddressLabel name="Claude Team" address="no-reply@email.claude.com" />)

    expect(screen.getByText('Claude Team')).toBeInTheDocument()
    expect(screen.getByRole('tooltip'))
      .toHaveTextContent('"Claude Team" <no-reply@email.claude.com>')
  })

  it('falls back to the address when the message carried no name', () => {
    render(<AddressLabel name="" address="bob@x.be" />)

    expect(screen.getByText('bob@x.be')).toBeInTheDocument()
  })

  // The backend already falls back FromName to the address, so a label equal to the address
  // is the no-name case — a bubble repeating the text under the cursor is noise.
  it('offers no tooltip when the label is already the address', () => {
    render(<AddressLabel name="bob@x.be" address="bob@x.be" />)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('renders the sender as a focusable control, ready to be wired to a composer', () => {
    render(<AddressLabel sender name="Claude Team" address="no-reply@email.claude.com" />)

    expect(screen.getByRole('button', { name: 'Claude Team' })).toBeInTheDocument()
  })

  // Recipients are plain text, so without this they would be unreachable by keyboard and
  // their tooltip — the only place the address is written — invisible to anyone not using a mouse.
  it('makes a recipient carrying a tooltip focusable', () => {
    render(<AddressLabel name="Bob" address="bob@x.be" />)

    expect(screen.getByText('Bob')).toHaveAttribute('tabindex', '0')
  })

  it('leaves a recipient with nothing to reveal out of the tab order', () => {
    render(<AddressLabel name="" address="bob@x.be" />)

    expect(screen.getByText('bob@x.be')).not.toHaveAttribute('tabindex')
  })
})

describe('AddressList', () => {
  // Asserted on nameless recipients: a named one renders its tooltip inside the wrapper, so
  // its textContent is "Bob" plus the whole bubble, and a separator assertion would not hold.
  it('separates the addresses with a comma', () => {
    const { container } = render(
      <AddressList addresses={[{ name: '', address: 'bob@x.be' }, { name: '', address: 'eve@x.be' }]} />)

    expect(container.textContent).toBe('bob@x.be, eve@x.be')
  })

  it('renders nothing for an empty list', () => {
    const { container } = render(<AddressList addresses={[]} />)

    expect(container.textContent).toBe('')
  })
})
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/AddressLabel.test.tsx`
Expected: FAIL — `Failed to resolve import "./AddressLabel"`.

- [ ] **Step 3: Écrire le composant**

`src/frontend/src/modules/mail/reader/AddressLabel.tsx` :

```tsx
import Tooltip from '../../../components/Tooltip'
import type { MailAddressInfo } from '../api/mailTypes'

interface Props {
  name: string
  address: string
  sender?: boolean
}

export default function AddressLabel({ name, address, sender = false }: Props) {
  const label = name || address
  const detail = label === address ? null : `"${name}" <${address}>`
  const className = sender ? 'address-label is-sender' : 'address-label'

  const trigger = sender
    ? <button type="button" className={className}>{label}</button>
    : <span className={className} tabIndex={detail ? 0 : undefined}>{label}</span>

  if (!detail) return trigger

  return <Tooltip content={detail} placement="bottom-left">{trigger}</Tooltip>
}

export function AddressList({ addresses }: { addresses: MailAddressInfo[] }) {
  return (
    <>
      {addresses.map((recipient, index) => (
        <span key={`${recipient.address}-${index}`}>
          {index > 0 && ', '}
          <AddressLabel name={recipient.name} address={recipient.address} />
        </span>
      ))}
    </>
  )
}
```

La clé combine l'adresse et l'index parce qu'un message peut légitimement adresser deux fois la même adresse.

- [ ] **Step 4: Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/AddressLabel.test.tsx`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/AddressLabel.tsx src/frontend/src/modules/mail/reader/AddressLabel.test.tsx
git commit -m "Add AddressLabel: display name up front, full address in a tooltip

One component for the sender and the recipients, so the two can never drift apart."
```

---

### Task 6: Badge et nouvelle mise en page de l'en-tête

**Files:**
- Create: `src/frontend/src/modules/mail/reader/AuthBadge.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx:1-10,61-71`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx:33-41,62-70`
- Modify: `src/frontend/src/styles/mail.css:443-444`

**Interfaces:**
- Consumes: `Tooltip` (Task 3), `authVerdict` / `MailAuthentication` (Task 4), `AddressLabel` et `AddressList` (Task 5).
- Produces: l'en-tête final. Rien en aval.

- [ ] **Step 1: Écrire le badge**

`src/frontend/src/modules/mail/reader/AuthBadge.tsx` :

```tsx
import Tooltip from '../../../components/Tooltip'
import type { MailAuthentication } from '../api/mailTypes'
import { authVerdict } from './authVerdict'

export default function AuthBadge({ authentication }: { authentication: MailAuthentication | null }) {
  const verdict = authVerdict(authentication)
  if (!verdict || !authentication) return null

  // The raw header is what lets a suspicious reader check for themselves; the summary line is
  // what serves everyone else.
  const detail = `SPF: ${authentication.spf ?? 'none'} · DKIM: ${authentication.dkim ?? 'none'}\n${authentication.raw}`
  const label = verdict === 'pass' ? 'Passed SPF and DKIM' : 'Failed SPF or DKIM'

  return (
    <Tooltip content={detail} placement="bottom-left">
      <span className={`auth-badge is-${verdict}`} tabIndex={0} role="img" aria-label={label}>
        {verdict === 'pass' ? '✓' : '!'}
      </span>
    </Tooltip>
  )
}
```

- [ ] **Step 2: Mettre à jour les fixtures du test du lecteur**

Dans `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`, remplacer dans la constante `detail` :

```ts
  to: ['mick@weesky.be'], cc: [], date: '2026-07-18T09:00:00Z',
```

par :

```ts
  to: [{ name: 'Mick', address: 'mick@weesky.be' }], cc: [],
  date: '2026-07-18T09:00:00Z', authentication: null,
```

- [ ] **Step 3: Écrire les tests qui échouent**

Toujours dans `MessageReader.test.tsx`, remplacer le test `renders the headers` (lignes 62-70) par le bloc suivant :

```tsx
  it('renders the headers', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('Re: facture')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Alice Martin' })).toBeInTheDocument()
    expect(screen.getByText('Mick')).toBeInTheDocument()
  })

  it('keeps the sender address one hover away', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    // getAllByRole, not getByRole: the named recipient carries a bubble of its own.
    const bubbles = screen.getAllByRole('tooltip').map(bubble => bubble.textContent)
    expect(bubbles).toContain('"Alice Martin" <alice@x.be>')
  })

  it('hides To and Cc when the message carries neither', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...detail, to: [], cc: [] })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByText(/^To:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/^Cc:/)).not.toBeInTheDocument()
  })

  it('lists the Cc recipients when there are any', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      cc: [{ name: 'Bob', address: 'bob@x.be' }, { name: '', address: 'eve@x.be' }],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.getByText('Bob')).toBeInTheDocument()
    expect(screen.getByText('eve@x.be')).toBeInTheDocument()
  })

  describe('the authentication badge', () => {
    const authenticated = (spf: string | null, dkim: string | null) => ({
      ...detail,
      authentication: { spf, dkim, raw: 'mx.weesky.net; spf=x; dkim=y' },
    })

    it('vouches for a message that passed both checks', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('pass', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByRole('img', { name: /passed spf and dkim/i })).toBeInTheDocument()
    })

    it('shows the headers behind its claim', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('pass', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      const bubbles = screen.getAllByRole('tooltip').map(bubble => bubble.textContent)
      expect(bubbles.some(text => text?.includes('SPF: pass · DKIM: pass'))).toBe(true)
      expect(bubbles.some(text => text?.includes('mx.weesky.net; spf=x; dkim=y'))).toBe(true)
    })

    it('warns about a message that failed one', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('fail', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByRole('img', { name: /failed spf or dkim/i })).toBeInTheDocument()
    })

    // Nothing at all rather than a reassuring or an alarming badge: the checks did not run.
    it('says nothing when the message carries no authentication headers', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('img', { name: /spf/i })).not.toBeInTheDocument()
    })

    it('says nothing about a softfail', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('softfail', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('img', { name: /spf/i })).not.toBeInTheDocument()
    })
  })
```

- [ ] **Step 4: Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/MessageReader.test.tsx`
Expected: FAIL — `Unable to find an accessible element with the role "button" and name "Alice Martin"`, l'en-tête rendant encore la chaîne concaténée.

- [ ] **Step 5: Réécrire l'en-tête**

Dans `src/frontend/src/modules/mail/reader/MessageReader.tsx`, ajouter aux imports, après `import { formatReaderDate } from './formatReaderDate'` :

```tsx
import AddressLabel, { AddressList } from './AddressLabel'
import AuthBadge from './AuthBadge'
```

Remplacer le bloc `<header>` (lignes 63-71) par :

```tsx
      <header className="reader-header">
        <h1 className="reader-subject">{data.subject || '(no subject)'}</h1>
        <div className="reader-meta">
          <div className="reader-from">
            <AddressLabel sender name={data.fromName} address={data.fromAddress} />
            <AuthBadge authentication={data.authentication} />
            <span className="reader-date">({formatReaderDate(data.date)})</span>
          </div>
          {data.to.length > 0 && (
            <div className="reader-recipients">To: <AddressList addresses={data.to} /></div>
          )}
          {data.cc.length > 0 && (
            <div className="reader-recipients">Cc: <AddressList addresses={data.cc} /></div>
          )}
        </div>
      </header>
```

- [ ] **Step 6: Écrire les styles**

Dans `src/frontend/src/styles/mail.css`, remplacer la ligne `.reader-meta { … }` (ligne 444) par :

```css
.reader-meta {
  color: var(--text-muted);
  font-size: 12px;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}

.reader-from { display: flex; align-items: center; gap: 8px; }
.reader-recipients { display: flex; align-items: center; flex-wrap: wrap; }

.address-label { color: inherit; font: inherit; }

.address-label.is-sender {
  border: 0;
  padding: 0;
  background: none;
  color: var(--text);
  font: inherit;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}

.address-label.is-sender:hover { color: var(--action-primary); }

/* --badge-count-fg is the readable foreground over a saturated badge fill in both modes —
   white on light, near-black on dark, which is exactly what --success and --danger need. */
.auth-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 15px;
  height: 15px;
  border-radius: 50%;
  font-size: 10px;
  font-weight: 700;
  line-height: 1;
  color: var(--badge-count-fg);
}

.auth-badge.is-pass { background: var(--success); }
.auth-badge.is-fail { background: var(--danger); }
```

- [ ] **Step 7: Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/frontend && npm run test -- src/modules/mail/reader/MessageReader.test.tsx`
Expected: PASS.

- [ ] **Step 8: Lancer toute la suite frontend, le typecheck et le lint**

Run: `cd src/frontend && npm run test && npm run typecheck && npm run lint`
Expected: PASS partout. `MessageList.test.tsx` ne touche pas à `MailMessageDetail`, mais si un autre test construit un détail de message, lui ajouter `authentication: null` et typer ses `to`/`cc`.

- [ ] **Step 9: Commit**

```bash
git add src/frontend/src/modules/mail/reader src/frontend/src/styles/mail.css
git commit -m "Rebuild the reader header: subject, sender, badge, date, then To and Cc

The four facts were one wrapping flex row; they are now a stack that reads top to bottom."
```

---

## Vérification finale

- [ ] `cd src/snoopy.microservice && dotnet test` — toute la suite backend au vert.
- [ ] `cd src/frontend && npm run test && npm run typecheck && npm run lint` — toute la suite frontend au vert.
- [ ] Vérifier à l'œil sur un message réel : le sender survolé montre `"Nom" <adresse>`, le badge survolé montre le header, la bulle n'est rognée par aucun bord de la colonne, et le badge reste lisible dans les deux modes d'au moins deux palettes.
