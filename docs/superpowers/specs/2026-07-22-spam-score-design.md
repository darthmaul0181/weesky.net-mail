# Score de spam dans l'en-tête du lecteur — design

Date : 2026-07-22

## Objectif

Ajouter sous les lignes To:/Cc: de l'en-tête du lecteur une ligne « Spam score: » portant
une jauge continue vert→rouge et le score chiffré tel que l'antispam l'a rapporté :

```
Titre
Sender  [✓]  (date)
To:  …
Cc:  …
Spam score:  [████░░░░░░]  7.0 / 16.0
```

Le détail complet (le header brut) reste dans un tooltip au survol, comme pour le badge
SPF/DKIM. La ligne est absente quand le message ne porte aucun header antispam reconnu —
même règle que To:/Cc: et que le badge : pas d'info, pas de ligne. L'affichage est gouverné
par un nouveau réglage de la page General, **actif par défaut**.

## Partage des rôles

Le même que pour l'authentification : **le backend rapporte, le front juge.** Le backend
parse le header en `{ score, threshold, raw }` ; le ratio, le clamp et la couleur sont des
règles d'affichage et vivent côté front. Les deux alternatives ont été rejetées : un ratio
calculé par l'API mettrait une règle d'affichage dans le contrat, et expédier les headers
bruts au front ferait transiter des kilooctets pour trois valeurs.

## 1. Backend — `MailSpamScoreReader`

Nouveau `Services/MailSpamScoreReader.cs`, miroir de `MailAuthenticationReader` : classe
statique `internal`, fonction pure `HeaderList` → `MailSpamScore?`, branchée dans
`ImapSession.GetMessageAsync` où le `MimeMessage` est déjà chargé — zéro aller-retour IMAP
supplémentaire.

### DTO

```csharp
public sealed record MailSpamScore(double Score, double Threshold, string Raw);
```

`Raw` est `"Nom: valeur"` du header retenu — le nom dit quel moteur a parlé, et c'est ce
que le tooltip affiche. `MailMessageDetail.SpamScore` est nullable ; aucun header reconnu
→ `null`.

### Les trois parseurs, dans un ordre de priorité fixe

L'ordre est celui des **parseurs**, pas celui des headers dans le message :

1. **rspamd** — `X-Spamd-Result: default: False [7.00 / 16.00]; …`
   Extraction du couple `[score / seuil]` (les deux peuvent être négatifs ou décimaux).
2. **SpamAssassin** — `X-Spam-Status: Yes, score=8.2 required=5.0 tests=…`, extraction de
   `score=` et `required=`. À défaut, `X-Spam-Score: 8.2` seul : le seuil est alors **5.0,
   le défaut universel de SpamAssassin** — une convention documentée ici, pas une donnée
   du message.
3. **Microsoft SCL** — `X-MS-Exchange-Organization-SCL: 5`, échelle 0–9.
   score = `max(0, scl)` (SCL −1 = courrier interne de confiance → 0), seuil = 5
   (SCL ≥ 5 est classé spam par Microsoft).

rspamd d'abord parce que c'est le filtre que la plateforme weesky.net exécute elle-même :
le header de **notre** infrastructure prime sur ceux posés par des relais amont.

### Confiance — la règle 7 s'applique telle quelle

**Pour chaque nom de header, seule la première occurrence (la plus haute) est lue.** Un
expéditeur peut forger `X-Spamd-Result: … [0.00 / 16.00]` dans ce qu'il envoie exactement
comme il forgerait `Authentication-Results` ; les occurrences plus basses ont été écrites
par quelqu'un d'autre. Un header retenu mais illisible (le motif n'y est pas) ne fait pas
retomber sur une occurrence plus basse : le parseur passe au moteur suivant.

## 2. Backend — la préférence

`Models/UserPreferences.cs` :

- constante `MailShowSpamScore = "mail.showSpamScore"` ;
- entrée `new(MailShowSpamScore, "true", Booleans)` dans `All`.

La Theory registry-wide (`Default_IsItselfAValueTheRegistryAccepts`) couvre le défaut
automatiquement ; les tests à `InlineData` (`All_CarriesTheKeysTheClientOffers`,
`IsValid_AcceptsOnlyTheOfferedValues`, `Default_IsTheValueAnAccountWithNoRowsGets`)
reçoivent leur cas à la main.

## 3. Frontend — ratio et jauge

### `reader/spamRatio.ts`

Pure : `clamp(score / threshold, 0, 1)`, avec une garde `threshold <= 0` → `null` — pas de
jauge plutôt qu'une division dénuée de sens. Un score négatif (ham) donne 0 : jauge vide,
verte. `null` en entrée → `null`.

### `reader/SpamGauge.tsx`

La ligne entière : le libellé `Spam score:`, la barre, puis `7.0 / 16.0` (une décimale des
deux côtés). Enveloppée dans le `Tooltip` existant (`placement="bottom-left"`) dont le
contenu est le header brut (`raw`). Le composant rend `null` si `spamScore` est null ou si
le ratio est null — il ne connaît pas la préférence. C'est `MessageReader` qui la lit
(`showSpamScoreOf`, comme il lit déjà `alwaysShowImagesOf`) et décide de rendre ou non la
ligne : un seul endroit décide, le composant reste pur.

### CSS (`mail.css`)

- `.reader-spam` — la ligne, dernière de `.reader-meta`, sous Cc:/To:.
- `.spam-gauge-track` — le rail : `--surface-sunken` bordé `--border`, largeur fixe
  (~90px), hauteur ~8px, `border-radius` plein.
- `.spam-gauge-fill` — le remplissage : largeur `calc(var(--gauge-ratio) * 100%)` et
  couleur `color-mix(in oklab, var(--success), var(--danger) calc(var(--gauge-ratio) * 100%))`.
  `--gauge-ratio` est posée en style inline par le composant (0–1).

**Aucun nouveau role token, aucune couleur littérale** : le dégradé continu est un
`color-mix` entre deux tokens existants, les six palettes restent intactes, et
`palettes.test.ts` ne bouge pas.

## 4. Frontend — préférence et réglage

- `hooks/usePreferences.ts` : clé `showSpamScore: 'mail.showSpamScore'` dans
  `PREFERENCE_KEYS`, accesseur `showSpamScoreOf` sur le pattern de `showPreviewOf` —
  **on sauf refus explicite** (`!== 'false'`), l'idiome inversé déjà en place pour les
  défauts actifs.
- `GeneralPage.tsx` : un `ToggleRow` « Show spam score in the message reader », placé entre
  `always-show-images` et `notify-sound`, sauvegardé par le `save()` générique existant.

## 5. Types frontend

`modules/mail/api/mailTypes.ts` :

```ts
export interface MailSpamScore {
  score: number
  threshold: number
  raw: string
}
```

`MailMessageDetail.spamScore: MailSpamScore | null`. Attention au contrat runtime déjà
observé sur `authentication` : `DefaultIgnoreCondition = WhenWritingNull` omet la clé quand
elle est nulle, donc le front reçoit `undefined` — les gardes acceptent les deux.

## 6. Tests

**Backend** — `MailSpamScoreReaderTests` :
les trois formats réels ; priorité rspamd > SpamAssassin > SCL quand plusieurs coexistent ;
première occurrence d'un nom seulement ; `X-Spam-Score` sans `X-Spam-Status` (seuil 5.0) ;
SCL −1 → score 0 ; score négatif ; header présent mais illisible → moteur suivant ; aucun
header → null. Plus les cas `InlineData` de `UserPreferencesTests` pour la nouvelle clé.

**Frontend** :
- `spamRatio.test.ts` — clamp aux deux bornes, score négatif, seuil zéro/négatif → null.
- `SpamGauge.test.tsx` — rendu avec valeur formatée, `--gauge-ratio` posée, tooltip avec le
  header brut, null si score absent ou ratio null.
- `MessageReader.test.tsx` — ligne présente avec données + préférence, absente sans données,
  absente quand la préférence est `'false'`.
- `GeneralPage.test.tsx` — le toggle affiche l'état stocké et sauvegarde la clé.

## Hors périmètre

- Barracuda, Bogofilter, DSPAM : l'ordre des parseurs est une liste, en ajouter un est
  trivial le jour où le besoin existe.
- La liste de messages (`MessageList`) n'affiche rien de tout ceci — la jauge vit dans le
  lecteur seul.
- Aucun classement ni filtrage par score : afficher, pas trier.
