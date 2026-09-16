# Webmail — Tranche 2c2b : réponse, répondre à tous, transfert

**Date :** 2026-07-25
**Statut :** design validé, prêt pour la planification d'implémentation
**Amont :** tranche 2c2a (identités d'envoi). Reprend et fige les décisions esquissées au §7 de
`2026-07-24-webmail-identities-2c2a-design.md`.

| Sous-tranche | Contenu | Dépend de |
|---|---|---|
| 2c2a | identités d'envoi : table, endpoints, écran Settings, sélecteur From | 2c1 |
| **2c2b** | ce document — réponse / répondre à tous / transfert / éditer comme nouveau (citation, threading, pièces jointes) | 2c2a |
| 2c3 | brouillons, signatures | 2c1 |

---

## 1. Le problème

2c1 sait rédiger un message neuf ; 2c2a l'ouvre aux identités d'envoi. Reste le geste le plus
courant d'un client mail : **repartir d'un message reçu**. Trois actions, une même mécanique de
fond — reprendre l'original, en citer le corps, recalculer les destinataires, et rester dans la
bonne conversation :

| Action | Destinataires | Sujet | Corps | Fil |
|---|---|---|---|---|
| **Reply** | l'expéditeur (`Reply-To` sinon `From`) | `Re: …` | citation de l'original | même fil |
| **Reply all** | + tous les `To`/`Cc`, **moins mes adresses** | `Re: …` | citation de l'original | même fil |
| **Forward** | vide (l'utilisateur saisit) | `Fwd: …` | bloc transféré + original | même fil |
| **Edit as new** | ceux de l'original, tels quels | inchangé | l'original, nu et éditable | nouveau fil |

*Edit as new* (le « Éditer comme nouveau » de Rainloop) rouvre un message existant comme brouillon :
rien n'est cité, rien n'est préfixé, rien n'est threadé — on repart du message lui-même. Il partage
toute la mécanique serveur du transfert (corps citable + re-staging des pièces), d'où sa place dans
cette tranche.

Deux exigences dominent le design :

1. **Une seule source de vérité par règle.** Le libellé du `From` est déjà résolu serveur (2c2a) ;
   de même, le corps citable est **assaini une fois, côté serveur, par la politique sortante**, et
   les en-têtes de threading sont **transcrits par le backend** depuis le message d'origine. Le
   frontend, lui, décide de ce qui est *présentation* : destinataires, préfixe de sujet, ligne
   d'attribution, assemblage de la citation. Fonctions pures, testables sans réseau.
2. **Fidélité de la citation.** Les clients grand public préservent les **images inline** (`cid:`)
   d'un message cité ou transféré. On fait de même : elles sont re-stagées côté serveur, affichées
   dans le composeur, puis ré-emballées en `multipart/related` à l'envoi. C'est la seule vraie
   nouveauté d'architecture de la tranche.

---

## 2. Décisions validées

| Sujet | Décision |
|---|---|
| Architecture | **Hybride** : le backend transcrit les en-têtes et sert le corps citable ; le frontend calcule destinataires / sujet / attribution / threading en fonctions pures |
| Corps citable | **Endpoint dédié paresseux** (`POST /api/Mail/Messages/PrepareQuote`), appelé au clic seulement — l'ouverture normale d'un message ne paie rien |
| Assainissement | Le corps citable passe par la **politique sortante** (`IOutgoingMailSanitizer`), jamais par la politique d'affichage |
| Images inline | **Préservées** : parties `cid:` re-stagées comme pièces *inline*, `src` réécrit vers l'URL stagée pour l'affichage, ré-emballées `multipart/related` + `cid:` à l'envoi |
| Levier d'assainissement | La politique **sortante autorise désormais `src="cid:…"`** sur les `<img>` (une référence à une partie embarquée, sûre — ni fichier local ni pisteur). C'est ce qui rend l'aller-retour symétrique et sans astuce de placeholder |
| Pièces jointes (transfert) | Re-stagées **côté serveur** depuis l'IMAP, jamais rapatriées par le navigateur |
| Threading | Reply, Reply all **et** Forward posent `In-Reply-To` et `References` → tout reste dans le fil de l'original |
| Message-Id | Posé par le serveur à l'envoi ; c'est lui qui alimente le `References` des réponses suivantes |
| Présélection d'identité | Première de mes adresses trouvée dans `To` puis `Cc`, sinon l'identité par défaut. Même règle pour les trois actions |
| Dédup « moi » | Contre l'ensemble {principale} ∪ {alias vivants} (`GET /api/aliases`), l'autorité d'appartenance — pas la table d'identités |
| Composeur | **Réutilisé**, seedé. Pas de vue de réponse séparée. Un brouillon prérempli est non-vide, donc la garde de sortie 2c1 s'applique sans changement |
| Boutons | Trois boutons icônes **Reply / Reply all / Forward** dans l'en-tête du lecteur ; **Edit as new** dans le menu kebab (action occasionnelle, comme chez Rainloop) |
| Edit as new | Graine = l'original tel quel : `To`/`Cc`/`Bcc`, sujet sans préfixe, corps nu (pas de citation), toutes les pièces, aucun threading. Identité présélectionnée = le `From` de l'original s'il est à moi, sinon le défaut |
| Bcc sur le détail | `MailMessageDetail` gagne `Bcc` (la copie Sent conserve cet en-tête) — sans lui, rééditer un envoi perdrait silencieusement ses destinataires Bcc. Exposé pour la graine ; son affichage dans le lecteur reste un choix d'UI séparé, hors tranche |

---

## 3. Backend

### 3.1 En-têtes transcrits sur `MailMessageDetail`

`Models/Mail/MailMessageDetail.cs` gagne cinq champs, alimentés dans
`Services/ImapSession.GetMessageAsync` (initialiseur lignes 743-764, à partir du `MimeMessage`
déjà chargé ligne 738) :

- `string? MessageId` — `message.MessageId` ;
- `IReadOnlyList<string> References` — `message.References` (le `MessageIdList`), liste vide si absent ;
- `string? InReplyTo` — `message.InReplyTo` ;
- `IReadOnlyList<MailAddressInfo> ReplyTo` — `message.ReplyTo`, mappé comme `To`/`Cc` déjà existants ;
- `IReadOnlyList<MailAddressInfo> Bcc` — `message.Bcc`, même mapping. Vide sur un message reçu ;
  porteur sur une copie Sent (l'en-tête y est conservé). Sert la graine d'*Edit as new* — son
  affichage dans le lecteur est un choix d'UI séparé, hors tranche.

Aucune session IMAP supplémentaire : ces valeurs sont déjà dans le `MimeMessage` que le détail
construit, elles étaient simplement ignorées (cf. `SendMessageRequest.cs:3-6`). Le miroir TS est
ajouté à `mailTypes.ts` (`MailMessageDetail`, ~89-112).

### 3.2 `POST /api/Mail/Messages/PrepareQuote`

Body `{ "folder": string, "uid": number, "purpose": "reply" | "forward" | "editAsNew" }` → `200`.
`editAsNew` est servi **exactement comme `forward`** (corps citable + toutes les pièces re-stagées) —
la valeur existe pour que l'API dise l'intention, pas pour un troisième comportement.

```jsonc
{
  "quotableHtml": "…",                 // assaini sortant, images cid réécrites vers URL stagée
  "attachments": [                     // vide pour un reply sans image inline
    { "id": "…guid…", "fileName": "logo.png", "size": 8123, "contentType": "image/png",
      "contentId": "part1@mail" },     // contentId non nul ⇒ pièce inline
    { "id": "…guid…", "fileName": "rapport.pdf", "size": 91234, "contentType": "application/pdf",
      "contentId": null }              // contentId nul ⇒ pièce jointe classique (forward)
  ]
}
```

Le contrôleur re-fetch le `MimeMessage` brut (mêmes credentials/session que le détail), puis :

1. **Corps citable.** Le HTML brut de l'original passe par `IOutgoingMailSanitizer.Prepare`. La
   politique sortante conservant désormais les `<img src="cid:X">` (§ 3.6), on assainit d'abord, puis
   on réécrit chaque `cid:X` restant vers l'URL de contenu stagé de sa partie — l'ordre est fixé, sans
   placeholder. **Un original sans corps HTML** (texte seul, cas courant) est cité depuis son
   `TextBody` : texte échappé, retours à la ligne rendus (`<br>`), servi comme `quotableHtml` — le
   composeur ne connaît qu'un seul format d'entrée.
2. **Parties inline.** Chaque partie référencée par un `cid:` présent dans le corps est stagée via
   `IStagedAttachmentStore` avec son `Content-ID` d'origine. Le `src` de l'`<img>` pointe alors vers
   `GET /api/Mail/Attachments/{id}/content` (§ 3.3).
3. **Pièces jointes.** Si `purpose` = `forward` ou `editAsNew`, chaque pièce jointe réelle de
   l'original est re-stagée (stream IMAP → `IStagedAttachmentStore.SaveAsync`, `contentId = null`).
   Pour un `reply`, aucune pièce jointe n'est reprise — seules les images inline le sont.

Le store stagé existant fournit tout : sous-dossier par compte, cap de taille, quota de réservation
(4× la limite), et **TTL** (`MailOptions.StagedAttachmentTtlHours`, balayage des expirés + orphelins).
Aucun nouveau mécanisme de rétention.

`401` sans authentification ; `502` si l'IMAP refuse la lecture ; `400` si le re-staging dépasse le
cap de taille ou le quota du compte (le même refus que l'upload existant — un transfert de pièces
énormes échoue proprement, toast côté client, composeur non ouvert).

### 3.3 `GET /api/Mail/Attachments/{id:guid}/content`

Sert le contenu d'une pièce stagée à son **propriétaire authentifié** (le store est déjà cloisonné
par compte), pour que le composeur affiche les images inline de la citation. `200` avec le
`Content-Type` stocké, `404` si l'id est inconnu du compte. Lecture seule ; ni upload ni
suppression (ceux-ci restent `POST` / `DELETE /api/Mail/Attachments`, lignes 673-704).

### 3.4 `StagedAttachmentInfo` gagne `ContentId`

`Models/Mail/StagedAttachmentInfo.cs` : `record StagedAttachmentInfo(Guid Id, string FileName,
long Size, string ContentType, string? ContentId)`. `null` = pièce jointe classique (comportement
2c1/2c2a inchangé) ; non nul = ressource inline à ré-emballer à l'envoi. `SaveAsync` accepte un
`contentId` optionnel (défaut `null`), de sorte que l'upload multipart existant reste identique.

### 3.5 `POST /api/Mail/Send` — threading et parties inline

`Models/Mail/SendMessageRequest.cs` gagne :

- `string? InReplyTo` — l'`In-Reply-To` calculé par le client ;
- `IReadOnlyList<string> References` — la chaîne `References` calculée par le client (vide par défaut).

`Services/MailSender.BuildMessageAsync` (110-140) :

1. **En-têtes de threading.** Pose `message.MessageId` (généré serveur), `In-Reply-To` = `InReplyTo`
   du request, `References` = `References` du request. Absents ⇒ message neuf, comportement 2c1
   inchangé.
2. **Parties.** Pour chaque pièce stagée ouverte (`_staged.Open`, 78-81) :
   - `ContentId` **non nul** → dans le `HtmlBody` reçu du client, remplace l'URL stagée de cette pièce
     par `cid:{ContentId}`, puis `builder.LinkedResources.Add(...)` avec ce `Content-ID` ; MimeKit
     construit alors le `multipart/related`. **Si l'URL stagée n'apparaît pas dans le corps**
     (l'utilisateur a supprimé l'image dans l'éditeur), la pièce n'est **pas** emballée — pas de
     `LinkedResource` orpheline — mais reste supprimée avec les autres après l'envoi.
   - `ContentId` **nul** → `builder.Attachments.AddAsync(...)` comme aujourd'hui (129-136).
3. Le corps ainsi réécrit passe par `OutgoingMailSanitizer`, qui conserve les `cid:` (§ 3.6).
4. Le reste est inchangé : identité/`From` résolus par `IdentityResolver` (2c2a), suppression des
   pièces stagées après envoi réussi (105), APPEND best-effort dans Sent.

Ordre fixé : réécriture des URL stagées → `cid:`, **puis** assainissement sortant, **puis**
construction du corps. Invariant : le message émis ne contient **aucune** URL stagée, et chaque
`<img>` inline pointe vers un `cid:` présent dans les `LinkedResources`.

### 3.6 `IOutgoingMailSanitizer` autorise `src="cid:…"`

Le `cid:` est aujourd'hui éliminé par **deux portes successives**, et les deux doivent s'ouvrir —
n'en corriger qu'une laisse les images disparaître silencieusement :

1. **L'allowlist de schémas de Ganss** (`OutgoingMailSanitizer.cs:25-28`) retire l'attribut `src`
   lui-même (schéma inconnu) avant que la boucle images ne s'exécute. `cid` doit y être ajouté.
2. **La coupe `IsRemote`** (lignes 40-44, 53-55) supprime ensuite toute `<img>` dont le `src`
   restant n'est pas `http(s)`. Elle doit reconnaître `cid:` comme source légitime.

Les autres schémas restent retirés. Un `cid:` référence une partie embarquée : ni accès fichier, ni
pisteur. C'est le seul changement à la politique partagée ; il est couvert par des tests dédiés
(un `<img cid:>` conservé **avec son `src` intact**, un `<img file:>` retiré) pour verrouiller les
deux portes à la fois.

---

## 4. Frontend

### 4.1 Fonctions pures colocalisées sous `compose/`

Chacune avec son `.test.ts`, sur le patron des modules purs du module mail (`reader/authVerdict.ts`,
`reader/spamRatio.ts`, `list/searchCriteria.ts`). Elles ne dépendent que du `MailMessageDetail` (déjà
en cache react-query) et de la liste d'alias/identités — aucun réseau.

- **`compose/replyModel.ts`**
  - `replyRecipients(detail, mine)` : `Reply-To` si présent, sinon `From`. **Répondre à son propre
    message** (expéditeur ∈ `mine`, cas du dossier Sent) reprend les `To` d'origine à la place —
    le geste attendu est « relancer le fil », pas s'écrire à soi-même.
  - `replyAllRecipients(detail, mine)` : le partage To/Cc de l'original est **préservé** (comme
    Gmail/Thunderbird) — To = expéditeur + `To` d'origine moins `mine` ; Cc = `Cc` d'origine moins
    `mine` (dédup casse-insensible). Si tout est à moi, dégénère en reply.
  - `subjectFor(purpose, subject)` : préfixe `Re:` (reply) ou `Fwd:` (forward), **sans empiler** un préfixe déjà présent (comparaison casse-insensible sur `Re:` / `Fwd:` / `Fw:`).
  - `preselectIdentity(detail, identities, mine)` : première de `mine` trouvée dans `To` puis `Cc`, sinon l'identité `isDefault`. Écarte les identités `stale`.
  - `editAsNewSeed(detail, identities, mine)` : `To`/`Cc`/`Bcc` de l'original tels quels (pas de
    dédup — on réédite, on ne répond pas), sujet inchangé, aucun threading. Identité : le `From` de
    l'original s'il ∈ `mine` (le cas typique — rééditer son propre envoi), sinon le défaut.
  - `mine` = {adresse principale} ∪ {alias vivants}, dérivé de `useAliases()`.
- **`compose/quote.ts`** — assemble le corps initial à partir du `quotableHtml` servi :
  - *reply* : ligne d'attribution `On {date}, {name} <{address}> wrote:` au-dessus d'un
    `<blockquote>` contenant `quotableHtml`, avec un paragraphe vide **au-dessus** (le curseur y ira).
  - *forward* : paragraphe vide, puis bloc
    `---------- Forwarded message ----------` + `From:` / `Date:` / `Subject:` / `To:`, puis
    `quotableHtml`.
  - Reçoit les dates/adresses **déjà formatées** (chaînes) pour rester pure ; le formatage de date
    réutilise `reader/formatReaderDate.ts`.
- **`compose/threadingHeaders.ts`** — `{ inReplyTo, references }` : `inReplyTo = detail.messageId`,
  `references = [...detail.references, detail.messageId]` (dédupliqué, `messageId` absent ⇒ chaîne
  inchangée). Même calcul pour les trois actions.

### 4.2 En-tête du lecteur

`reader/MessageReader.tsx` (en-tête 185-252) reçoit trois boutons icônes **Reply / Reply all /
Forward** (nouvelles icônes), dans le langage visuel des boutons icônes du mail (recolorisation du
glyphe, cf. `website-design.md`). Au clic :

1. lit le `MailMessageDetail` en cache ;
2. calcule destinataires / sujet / identité / threading via les fonctions pures ;
3. appelle `PrepareQuote` (`purpose` selon le bouton) pour obtenir `quotableHtml` + pièces stagées ;
4. assemble le corps via `quote.ts` ;
5. navigue vers `/mail/compose` en **seedant** le composeur (§ 4.3).

**Edit as new** est une entrée du menu kebab (`ReaderActions`, avec Archive / Move to…) — action
occasionnelle, pas un quatrième bouton icône. Même séquence, à deux différences près : le corps de
la graine est `quotableHtml` **nu** (`quote.ts` n'intervient pas) et la graine vient de
`editAsNewSeed` (§ 4.1) — `Bcc` restauré, `showBcc` ouvert s'il est non vide.

Un échec de `PrepareQuote` lève le toast d'erreur habituel et n'ouvre pas le composeur.

### 4.3 `ComposeView` seedé

`ComposeView.tsx` accepte une **graine** optionnelle (via l'état de navigation, comme `openCompose`
passe déjà `state: { from }` dans `MailLayout.tsx:92-94`) :

```ts
type ComposeSeed = {
  to: string[]; cc: string[]; bcc: string[]; subject: string;
  html: string;                 // corps prérempli (citation / bloc transfert / original nu)
  fromAddress: string | null;   // identité présélectionnée
  attachmentIds: string[];      // pièces stagées (inline + réelles) renvoyées par PrepareQuote
  inReplyTo: string | null; references: string[];
};
```

- L'état local (`useState`, 36-44) s'initialise depuis la graine ; `showCc` / `showBcc` s'ouvrent si
  leur liste est non vide.
- `SquireEditor` gagne l'**injection d'un HTML initial** (aujourd'hui `EditorHandle` n'expose ni
  `setHTML` ni insertion, 13-24) ; le contenu passe par la même sanitisation d'entrée
  (`sanitizePolicy`) que tout `setHTML`. Le curseur se place dans le paragraphe vide de tête.
- Les pièces stagées de la graine peuplent la barre de pièces jointes (`useStagedAttachments`) ;
  l'utilisateur peut les retirer. Les images inline restent invisibles dans la barre (ce sont des
  ressources du corps, pas des pièces), mais s'affichent dans la citation via leur URL stagée.
- La garde de sortie 2c1 est inchangée : un brouillon prérempli est non-vide, donc `dirty` (57-61)
  est vrai d'emblée ; fermer propose l'abandon habituel.
- `submit()` (94-108) ajoute `inReplyTo` et `references` au payload.

### 4.4 API & queries

- `src/api.js` : `prepareQuote(folder, uid, purpose)` → `POST /api/Mail/Messages/PrepareQuote` ;
  `stagedAttachmentUrl(id)` → `GET /api/Mail/Attachments/{id}/content`.
- `src/modules/mail/queries.ts` : `useSendMessage` (641-658) et `SendMessageArgs` (624-635) gagnent
  `inReplyTo` / `references`. `PrepareQuote` est une **mutation** (effet de bord : staging), pas une
  query cachée.
- `mailTypes.ts` : nouveaux champs de `MailMessageDetail`, type `PreparedQuote`
  (`quotableHtml` + `StagedAttachmentInfo[]`), `contentId` sur l'info de pièce stagée.

---

## 5. Tests

**Backend (xUnit).**
- `PrepareQuote` : `cid:` réécrit vers l'URL stagée dans `quotableHtml` ; partie inline stagée avec
  son `Content-ID` ; `purpose=forward` re-stage les pièces jointes réelles (`contentId=null`) ;
  `purpose=reply` ne reprend aucune pièce jointe ; le corps citable passe bien par la politique
  **sortante** (un `<img>` distant conservé, un script retiré) ; un original **texte seul** produit
  un `quotableHtml` échappé avec ses retours à la ligne ; un re-staging au-delà du cap ⇒ `400`.
- `GET …/{id}/content` : sert la pièce du compte, `404` pour un id étranger, `401` sans auth.
- `OutgoingMailSanitizer` : un `<img src="cid:…">` est conservé **avec son `src` intact** (les deux
  portes de § 3.6), un `<img src="file:…">` (et autres schémas non `http(s)`) retiré.
- `MailSender` : `Message-Id`/`In-Reply-To`/`References` posés depuis le request ; une pièce inline
  (`ContentId` non nul) devient une `LinkedResource` et le `src` stagé correspondant est réécrit en
  `cid:{ContentId}` ; une pièce inline **non référencée dans le corps** n'est pas emballée ; une
  pièce `ContentId=null` reste une pièce jointe classique ; message émis sans aucune URL stagée
  résiduelle ; `InReplyTo`/`References` absents ⇒ message neuf (2c1 inchangé).
- Mapping du détail : `MessageId` / `References` / `InReplyTo` / `ReplyTo` / `Bcc` transcrits depuis
  un `MimeMessage` de test.

**Frontend (Vitest + RTL).**
- `replyModel` : reply (Reply-To prioritaire sur From) ; réponse à son propre message ⇒ `To`
  d'origine ; reply-all (partage To/Cc préservé, mes adresses exclues, dédup casse) ; dégénérescence
  en reply quand tout est à moi ; `subjectFor` sans empilement de `Re:`/`Fwd:` ; `preselectIdentity`
  (première des miennes dans To puis Cc, sinon défaut, jamais une `stale`).
- `quote` : reply produit attribution + `<blockquote>` + paragraphe curseur en tête ; forward produit
  le bloc `Forwarded message` avec les en-têtes.
- `threadingHeaders` : `references = references original + messageId` ; `messageId` absent ⇒ chaîne
  inchangée.
- `editAsNewSeed` : `To`/`Cc`/`Bcc` repris tels quels, sujet sans préfixe, aucun threading, identité
  = le `From` de l'original quand il est à moi, sinon le défaut.
- `ComposeView` seedé : préremplissage des destinataires/sujet/corps/identité, ouverture de `Cc` /
  `Bcc` si non vides, pièces stagées présentes, envoi transmettant `inReplyTo`/`references`, garde de
  sortie active dès l'ouverture.
- En-tête du lecteur : les trois boutons appellent `PrepareQuote` avec le bon `purpose` et naviguent
  vers le composeur seedé ; *Edit as new* (menu kebab) appelle `purpose=editAsNew` et seede le corps
  nu ; un échec de `PrepareQuote` n'ouvre pas le composeur.

---

## 6. Ce que 2c2b prépare sans l'implémenter

- **2c3 (brouillons, signatures)** : le composeur seedé est le même point d'entrée qu'un brouillon
  rouvert ; la graine `ComposeSeed` est la forme qu'un brouillon persisté prendra. Une signature par
  identité s'insérera au même endroit que la citation. Aucune colonne ni champ ajouté en avance.

---

## 7. Vérification

- Suites complètes vertes des deux côtés, `build` et `eslint` propres.
- Vérification manuelle sur `dev` : répondre à un message (destinataires et `Re:` corrects, citation
  visible, fil conservé côté client mail tiers) ; répondre à tous (mes adresses exclues) ; transférer
  un message **avec pièce jointe et image inline** — contrôle que le destinataire reçoit la pièce, que
  l'image inline s'affiche (donc `multipart/related` + `cid:` corrects), et que le `From` porte
  l'identité présélectionnée.
- Un `reply` sur un message sans image inline n'entraîne aucune pièce stagée superflue.
- *Edit as new* sur une copie Sent : destinataires (`Bcc` compris) et pièces restaurés, sujet sans
  `Re:`, message émis hors de tout fil.
