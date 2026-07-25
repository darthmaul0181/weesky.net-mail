# En-tête du lecteur de message — design

Date : 2026-07-21

## Objectif

Restructurer l'en-tête d'un message ouvert (`MessageReader`) pour qu'il se lise ainsi :

```
Titre
Sender  [✓]  (date)
To:  …
Cc:  …
```

Le sender est le libellé de l'expéditeur quand il en existe un, avec son adresse complète en
tooltip. Le badge entre le sender et la date signale que le message a passé SPF et DKIM.
`To:` et `Cc:` ne s'affichent que si l'information existe.

## État actuel

`src/frontend/src/modules/mail/reader/MessageReader.tsx` rend une seule ligne `.reader-meta`
en `flex-wrap`, où sender, date, To et Cc sont quatre `<span>` côte à côte. Le sender y est
une chaîne concaténée `Nom <adresse>`.

Côté API, `MailMessageDetail` expose `FromName`/`FromAddress` mais `To`/`Cc` sont des
`List<string>` d'adresses nues : `ImapSession` projette `m.Address` et jette `m.Name`.
Aucun header d'authentification n'est lu ni exposé.

## 1. Backend — résultat d'authentification

### Source

Le header `Authentication-Results` (RFC 8601), posé par le serveur de réception, seul. Il
porte déjà `spf=…` et `dkim=…` sous une forme normalisée. `Received-SPF` et la présence
d'une `DKIM-Signature` ne sont pas consultés : une signature présente n'est pas une
signature vérifiée, et l'afficher comme telle serait un faux signal de sécurité.

Le `MimeMessage` est déjà chargé par `GetMessageAsync` (`folder.GetMessageAsync`), donc
lire ses headers ne coûte aucun aller-retour IMAP supplémentaire.

### Parseur

Nouveau `Services/MailAuthenticationReader.cs`, fonction pure statique sur
`HeaderList` → `MailAuthentication?`.

**Le parsing est délégué à `MimeKit.Cryptography.AuthenticationResults`**, livré par MailKit
que le projet référence déjà. Un découpage maison sur `;` a été écrit puis retiré : il se
fait piéger par un commentaire RFC 5322 contenant un `;` — `dkim=fail (also; a note)` — et
laissait fuiter une parenthèse dans le verdict. La grammaire CFWS n'est pas quelque chose
qu'on redécoupe à la main quand une implémentation conforme est déjà dans le sac.

**Seul le premier header `Authentication-Results` est lu, jamais fusionné avec les
suivants.** Un message en porte parfois plusieurs, un par relais traversé, empilés du plus
récent au plus ancien : celui du dessus est celui que notre propre serveur de réception a
écrit, et **tous ceux d'en dessous ont été écrits par quelqu'un d'autre**. N'importe qui peut
mettre `Authentication-Results: spf=pass` dans un message qu'il envoie. Aller chercher un
verdict DKIM manquant chez un relais amont — ce que cette spec décrivait d'abord — est un
vecteur d'usurpation, pas une tolérance. Le corollaire tombe tout seul : `Raw` étant la
valeur de cet unique header, il justifie toujours les deux verdicts affichés.

Une méthode qui apparaît plusieurs fois dans ce header : **un `pass` l'emporte sur tout le
reste**, sinon la première occurrence gagne. Un message à deux signatures DKIM dont une liste
de diffusion a cassé la première est authentifié par la seconde ; c'est aussi la règle
d'alignement DMARC.

La comparaison des noms de méthode et des valeurs est insensible à la casse ; la valeur
retenue est normalisée en minuscules.

Aucun header `Authentication-Results` → le parseur répond `null`. Un premier header qui ne
mentionne ni `spf` ni `dkim`, ou que le parseur refuse, donne deux verdicts `null` et ce
header en `Raw` : le serveur a bien tourné, ses verdicts nous sont simplement inconnus.

### DTO

```csharp
public sealed record MailAuthentication(string? Spf, string? Dkim, string Raw);
```

`Raw` est la valeur du header retenu, telle quelle : c'est ce que le tooltip du badge
affiche. `MailMessageDetail.Authentication` est nullable.

Le backend rapporte, il ne juge pas : vert/rouge est une règle d'affichage, elle vit côté
front (voir §5).

## 2. Backend — display names des destinataires

Nouveau `Models/Mail/MailAddressInfo.cs` :

```csharp
public sealed record MailAddressInfo(string Name, string Address);
```

`MailMessageDetail.To` et `.Cc` passent de `List<string>` à `List<MailAddressInfo>` ;
`ImapSession.GetMessageAsync` projette `new MailAddressInfo(m.Name ?? "", m.Address)`.

Le suffixe `Info` suit `MailAttachmentInfo`, le DTO voisin, et évite du même coup l'ambiguïté
avec `System.Net.Mail.MailAddress`.

`From` reste sur les champs plats `FromName`/`FromAddress` : ils sont aussi portés par
`MailMessageSummary`, que la liste de messages consomme, et les uniformiser ici ne sert
aucun besoin de cette tranche.

## 3. Frontend — le composant Tooltip

`HelpTooltip` (`src/components/HelpTooltip.jsx`) porte déjà exactement la bulle voulue —
`--surface`, `--border`, `--text`, révélée au `:hover` — mais soudée à son icône `?`, donc
inattachable à un texte existant. Plutôt que d'écrire une seconde bulle qui divergerait de
la première, la bulle est extraite.

**`src/components/Tooltip.tsx`** — enveloppe ses enfants :

```tsx
<Tooltip content={…} placement="bottom-left">{trigger}</Tooltip>
```

rend `<span class="tooltip-wrap">{children}<span class="tooltip-bubble">{content}</span></span>`.

**`src/styles/tooltip.css`**, importé dans `main.tsx` après `index.css` — la bulle est
partagée par le module mail et les settings, elle n'appartient donc ni à `mail.css` ni à
`index.css`, déjà à ~2200 lignes. Les règles :

- `.tooltip-wrap` — `position: relative; display: inline-flex`
- `.tooltip-bubble` — `position: absolute`, `max-width: 320px`, `white-space: pre-line`
  (le tooltip du badge est multi-ligne), `pointer-events: none`, `z-index: 10`, sur les
  tokens existants `--surface` / `--border` / `--text` / `--radius-sm`. Aucun nouveau role
  token, donc aucun des six fichiers de palette n'est touché.
- Deux modificateurs de placement : `.is-top-right` (au-dessus, aligné à droite — le
  placement actuel de `HelpTooltip`) et `.is-bottom-left` (en dessous, aligné à gauche — le
  placement du lecteur, où le déclencheur est en haut à gauche de la colonne).
- Révélée par `:hover` **et `:focus-within`** : le sender sera un élément focusable, et une
  info visible à la souris seule est une info perdue au clavier.

Le placement `bottom-left` est le bon choix dans le lecteur précisément parce que la colonne
mail est en `overflow: hidden` : une bulle ancrée à gauche sous un déclencheur situé en haut
à gauche ne peut être rognée ni en haut ni à droite.

**`HelpTooltip.jsx`** est réécrit par-dessus `Tooltip` (`placement="top-right"`), en gardant
son icône et sa classe `.help-tooltip-icon`. Les règles `.help-tooltip-wrap` et
`.help-tooltip-bubble` disparaissent d'`index.css` ; `.help-tooltip-icon` et son survol y
restent. Comportement et rendu inchangés pour ses consommateurs actuels.

## 4. Frontend — AddressLabel

**`src/modules/mail/reader/AddressLabel.tsx`** — un seul composant pour l'expéditeur et pour
chaque destinataire.

- Affiche `name || address`.
- Si `name` existe **et diffère de l'adresse**, l'enveloppe dans un `Tooltip` dont le contenu
  est `"Nom" <adresse>`. Sans nom, pas de tooltip : une bulle qui répète mot pour mot le
  texte survolé est du bruit.
- Prop `sender` : ajoute la classe `.is-sender` (poids, couleur `--text`, curseur pointeur au
  survol) et rend le libellé dans un `<button type="button">` sans handler pour l'instant.
  Le bouton plutôt qu'un `<span>` cliquable : c'est ce qui le rend focusable et donc son
  tooltip atteignable au clavier, et le jour où le composer arrive il n'y a qu'un `onClick` à
  brancher.

`FromName` valant déjà l'adresse quand le message ne porte pas de nom (le backend applique ce
repli), la condition « name diffère de address » couvre les deux cas d'un coup.

## 5. Frontend — verdict et badge

**`src/modules/mail/reader/authVerdict.ts`** — pure :

| condition | verdict |
|---|---|
| `spf === 'pass'` **et** `dkim === 'pass'` | `'pass'` |
| `spf === 'fail'` **ou** `dkim === 'fail'` | `'fail'` |
| tout le reste (absent, `none`, `neutral`, `softfail`, `temperror`, `permerror`) | `null` |

`null` ne rend aucun badge. Un `softfail` ou un `neutral` ne sont pas des échecs, et peindre
un signal — vert comme rouge — sur une information manquante ou ambiguë est pire que ne rien
peindre : le lecteur apprendrait à ignorer le badge.

**`src/modules/mail/reader/AuthBadge.tsx`** — une pastille ronde, `✓` sur `--success` pour
`'pass'`, `!` sur `--danger` pour `'fail'`, rien pour `null`. Tokens existants dans les six
palettes : rien à ajouter côté thème.

Le badge est enveloppé dans un `Tooltip` dont le contenu est :

```
SPF: pass · DKIM: pass
<valeur brute du header Authentication-Results>
```

La ligne brute est ce qui permet à un utilisateur averti de vérifier lui-même ; la ligne
résumée est ce qui sert à tous les autres. Le badge est un `<span tabindex="0">` — non
interactif mais focusable, pour la même raison que le sender.

## 6. Frontend — la mise en page

`MessageReader` rend :

```tsx
<header className="reader-header">
  <h1 className="reader-subject">{data.subject || '(no subject)'}</h1>
  <div className="reader-meta">
    <div className="reader-from">
      <AddressLabel sender name={data.fromName} address={data.fromAddress} />
      <AuthBadge authentication={data.authentication} />
      <span className="reader-date">({formatReaderDate(data.date)})</span>
    </div>
    {data.to.length > 0 && <div className="reader-recipients">To: …</div>}
    {data.cc.length > 0 && <div className="reader-recipients">Cc: …</div>}
  </div>
</header>
```

To et Cc rendent un `AddressLabel` par destinataire, séparés par `, `, chacun avec son
tooltip — c'est la raison d'être du changement de DTO en §2.

`mail.css` : `.reader-meta` passe de la ligne `flex-wrap` à une colonne (`flex-direction:
column; gap: 4px`). `.reader-from` devient la ligne horizontale (`align-items: center; gap:
8px`). `.reader-date` et `.reader-recipients` gardent `--text-muted` et 12px ; le sender
passe à `--text` — c'est la seule chose de cette zone qu'on lit vraiment.

## 7. Types frontend

`src/frontend/src/modules/mail/api/mailTypes.ts` :

```ts
export interface MailAddressInfo { name: string; address: string }
export interface MailAuthentication { spf: string | null; dkim: string | null; raw: string }
```

Dans `MailMessageDetail` : `to: MailAddressInfo[]`, `cc: MailAddressInfo[]`,
`authentication: MailAuthentication | null`.

## 8. Tests

**Backend** — `AuthenticationResultsTests` :
header Gmail réel (`spf=pass … dkim=pass`), header absent → `null`, headers multiples (le
plus récent gagne), `spf` sans `dkim`, casse mixte (`SPF=Pass`), header présent mais ne
mentionnant ni spf ni dkim. Plus la couverture existante de `GetMessageAsync` étendue aux
`MailAddressInfo` de To/Cc.

**Frontend** :
- `authVerdict.test.ts` — les trois verdicts et les valeurs frontières (`softfail`,
  `neutral`, `null`/`null`).
- `Tooltip.test.tsx` — bulle rendue, contenu présent, les deux placements.
- `AddressLabel.test.tsx` — repli sur l'adresse sans nom, absence de tooltip quand
  `name === address`, contenu `"Nom" <adresse>` sinon.
- `MessageReader.test.tsx` — l'ordre des blocs, badge vert / rouge / absent, To et Cc
  masqués quand vides, destinataires nommés rendus avec leur libellé.
- La couverture existante de `HelpTooltip`, s'il en a une, doit continuer à passer sans
  modification d'assertion — le refactor du §3 est à rendu constant.

## Hors périmètre

- Le clic sur le sender n'ouvre rien : pas de composer dans l'application aujourd'hui. Le
  style et la focusabilité sont en place, il manquera un `onClick`.
- DMARC n'est ni parsé ni affiché. SPF et DKIM sont ce que la demande couvre, et un
  troisième état dans le verdict compliquerait la règle sans rien ajouter au badge.
- La liste de messages (`MessageList`) n'est pas touchée : elle ne montre ni destinataires
  ni badge.
