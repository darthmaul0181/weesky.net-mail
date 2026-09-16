# Webmail — Tranche 2c1 : rédaction & envoi d'un mail neuf

**Date :** 2026-07-23
**Statut :** design validé, prêt pour la planification d'implémentation
**Amont :** tranche 2c du sous-projet Mail (spec shell § 11), découpée en trois sous-tranches :

| Sous-tranche | Contenu | Dépend de |
|---|---|---|
| **2c1** | ce document — envoi SMTP, rédaction d'un mail neuf, identité par défaut, pièces jointes sortantes | 2a |
| **2c2** | réponse / répondre à tous / transfert (quoting + threading), choix d'identité (alias) | 2c1 |
| **2c3** | brouillons (sauvegarde / reprise), signatures | 2c1 |

Reporté hors 2c : images en ligne dans le corps (machinerie `cid:`) — amélioration ultérieure.

---

## 1. Décisions validées

| Sujet | Décision |
|---|---|
| Éditeur | **WYSIWYG HTML** via **Squire** (`squire-rte`, ~30 KB, zéro dépendance, moteur de FastMail/Snappymail). Parité Rainloop ; pas d'UI d'édition de tableaux (comme Rainloop) — un tableau collé est conservé tel quel |
| Conteneur | **Zone contenu du module Mail**, routé **`/mail/compose`** : remplace (liste + lecteur), conserve rail + arbre de dossiers. Deep-linkable, bouton retour naturel |
| Pièces jointes | **Upload staged immédiat** (modèle Gmail/Outlook/Rainloop) : chaque fichier monte dès l'ajout vers un store temporaire serveur, l'envoi référence des ids. Aucun retravail en 2c3 |
| Limite de taille | Configurable — `Mail:MaxMessageSizeMb`, défaut **25** — vérifiée par le backend, qui la nomme dans son 400 ; le client relaie l'erreur sans dupliquer la constante |
| Format d'envoi | `multipart/alternative` (HTML assaini + repli text/plain généré) ; PJ en `multipart/mixed` |
| Identité 2c1 | **Adresse principale seule** (login + `FullName`) ; le choix d'alias arrive en 2c2 |
| Copie Sent | `APPEND` IMAP dans le dossier au rôle **sent** (chaîne de résolution existante), drapeau `\Seen` |
| Validation d'envoi | ≥ 1 destinataire valide ; sujet et corps vides autorisés (pas de garde-fou en 2c1) |

---

## 2. Backend

Aucun changement de modèle d'authentification : comme pour IMAP, SMTP s'authentifie avec
**le mot de passe de l'utilisateur** repris du cookie chiffré (`IMailCredentialStore.Retrieve`).
Les deux modes d'échec du contrôleur Mail restent la règle : cookie absent/illisible → **401**
`credentials_unavailable` ; refus du serveur de mail (SMTP ou IMAP) → **502**.

### 2.1 `SmtpConnectionFactory`

Calqué trait pour trait sur `ImapConnectionFactory` : un `MailKit.Net.Smtp.SmtpClient` par
requête, `ConnectAsync` + `AuthenticateAsync` sous timeout lié, callback de certificat partagé
(`AllowInvalidCertificate` loggé en warning), message générique au client et détail loggé,
`AuthenticationException` jamais réémise verbatim. `MailOptions` porte déjà
`SmtpHost`/`SmtpPort`/`SmtpSecurity` ; s'ajoutent :

```csharp
/// <summary>Maximum outgoing message size — sum of raw attachment bytes, in megabytes.</summary>
public int MaxMessageSizeMb { get; set; } = 25;

/// <summary>How long a staged attachment survives without being sent, in hours.</summary>
public int StagedAttachmentTtlHours { get; set; } = 12;

/// <summary>True when enough is configured to attempt an SMTP connection.</summary>
public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(SmtpHost);
```

**La limite s'applique à la somme des octets bruts des pièces jointes.** L'encodage base64
ajoute ~35 % : l'exploitant règle `MaxMessageSizeMb` en dessous du `message_size_limit`
de Postfix en tenant compte de cette marge. C'est le compromis de tous les clients
(le « 25 MB » de Gmail est brut, pas encodé).

### 2.2 Store de pièces jointes staged

`IStagedAttachmentStore` (service singleton) :

- **Stockage** : fichiers sous un répertoire temporaire du service, un sous-répertoire par
  utilisateur (hash de l'email canonique), nom de fichier = id aléatoire (GUID). Les
  métadonnées (nom d'origine, taille, content-type, horodatage) vivent en mémoire process —
  un restart perd les uploads en cours, c'est accepté (le client re-téléverse).
- **Scellement** : un id n'est résoluble que par l'utilisateur qui l'a créé. Un id inconnu
  ou appartenant à autrui répond 404, jamais le fichier.
- **Plafonds** : fichier unique ≤ `MaxMessageSizeMb` ; total staged par utilisateur ≤
  4 × `MaxMessageSizeMb` (borne anti-abus, laisse la place à un compose abandonné).
- **GC** : un `IHostedService` balaie toutes les heures et supprime ce qui dépasse
  `StagedAttachmentTtlHours` ; l'envoi et le retrait explicite purgent immédiatement.

### 2.3 Endpoints

Sur `MailController` (mêmes conventions : 400/401/502, `ResultEnveloppe`) :

| Endpoint | Rôle | Réponses |
|---|---|---|
| `POST /api/Mail/Attachments` | multipart, **un fichier** ; valide taille + plafond utilisateur ; répond `{ id, fileName, size, contentType }` | 200 / 400 (taille, plafond, fichier absent) / 401 |
| `DELETE /api/Mail/Attachments/{id}` | retire un staged (id = GUID, sûr en segment de route) ; idempotent | 204 / 401 / 404 |
| `POST /api/Mail/Send` | JSON ci-dessous ; construit, envoie, copie dans Sent, purge les staged | 200 / 400 / 401 / 502 |

L'upload porte un `[RequestSizeLimit]` dimensionné sur `MaxMessageSizeMb` (le défaut Kestrel,
~30 MB, serait dépassé dès que la limite configurée monte).

```jsonc
// POST /api/Mail/Send — SendMessageRequest
{
  "to":  ["alice@example.com"],          // ≥ 1 adresse valide exigée
  "cc":  [],                             // optionnel
  "bcc": [],                             // optionnel
  "subject": "…",                        // vide autorisé
  "htmlBody": "<div>…</div>",            // vide autorisé
  "attachmentIds": ["…guid…"]            // staged ids, tous scellés à l'appelant
}
// 200 — SendMessageResult
{ "appendedToSent": true }
```

Les adresses sont validées par `MailboxAddress.TryParse` (MimeKit) — une seule invalide
suffit au 400, qui la nomme. Un `attachmentId` inconnu est un 400 (le client a perdu la
synchro, il doit re-téléverser), jamais un envoi partiel.

### 2.4 Construction et envoi du message

Un service d'orchestration `IMailSender` (le contrôleur reste mince) :

1. **From** = identité principale : adresse d'authentification + `FullName` comme nom
   d'affichage.
2. **Corps** : le HTML passe l'assainisseur **sortant** (§ 2.5), puis un repli text/plain en
   est extrait (parse AngleSharp — déjà dans l'arbre de dépendances via Ganss — texte des
   blocs avec sauts de ligne). `BodyBuilder` (MimeKit) assemble `multipart/alternative`
   + pièces jointes staged (`multipart/mixed`). MimeKit encode les en-têtes — l'injection
   d'en-têtes par le sujet est neutralisée par construction.
3. **Bcc** : dans l'enveloppe SMTP (`MailKit` la dérive du `MimeMessage.Bcc`), et MailKit
   retire l'en-tête `Bcc` à la transmission — les autres destinataires ne le voient jamais.
   La copie Sent **conserve** le Bcc : c'est le suivi de l'expéditeur.
4. **Envoi** SMTP via `SmtpConnectionFactory`. Échec → 502, staged **conservés** (l'utilisateur
   réessaie sans re-téléverser).
5. **Copie Sent** : résolution du rôle `sent` par la chaîne existante
   (`GetTreeAsync` + `FolderRoleResolver`), puis `APPEND` avec `\Seen` — nouvelle méthode
   `AppendAsync(user, password, folderPath, message, seen)` sur `IMailMessageRepository` /
   `ImapSession`. **Un échec d'APPEND ne défait pas l'envoi** : le mail est parti ; réponse
   200 avec `appendedToSent: false`, le client affiche un avertissement doux. Même chose
   quand aucun dossier ne tient le rôle sent.
6. **Purge** des staged référencés — après l'envoi réussi, quel que soit le sort de l'APPEND.

### 2.5 Assainisseur sortant

**Politique distincte de l'assainisseur d'affichage entrant** — celui-ci bloque les images
distantes et cull `url()`, deux règles pensées pour la *lecture* et absurdes à l'*envoi*.
`OutgoingMailSanitizer` (Ganss, instance et allowlists propres) :

- **Retire** : `script`, `iframe`, `object`, `embed`, `form`, tous les handlers `on*`,
  les schémas autres que `http`/`https`/`mailto` sur les liens.
- **Conserve** : les styles inline que la barre d'outils produit (couleurs, fonds, polices,
  tailles, alignement), les `<img src="http(s)://…">` (référence distante légitime en
  sortant), les tableaux collés.
- **Retire `<img src="data:…">`** : les images en ligne sont hors périmètre 2c1 ; un
  data-URI collé partirait dans le HTML sans partie MIME et s'afficherait mal chez la
  plupart des destinataires (Gmail/Outlook les bloquent). Les retirer est le comportement
  honnête tant que `cid:` n'existe pas.

Les trois règles porteuses de l'assainisseur entrant (shorthands, unwrap, cull `url()`)
appartiennent à *sa* politique et ne contraignent pas celle-ci.

---

## 3. Frontend

### 3.1 Route et intégration au module

`routes.tsx` ajoute `{ path: 'mail/compose' }` rendu par le même `MailLayout` lazy.
`MailLayout` détecte la correspondance (`useMatch('/mail/compose')`) et, en mode compose,
remplace tout le bloc (liste + splitter + lecteur) par `<ComposeView/>` — l'arbre de
dossiers et le rail restent en place et interactifs. Pas de paramètre `folder` sur cette
route ; quitter le compose ramène à `/mail?folder=…` (le dossier d'où l'on venait, ou
l'inbox par résolution existante).

**Garde de sortie.** Sans brouillons (2c3), quitter = perdre le message. Dès que le compose
est « sale » (corps, sujet, destinataire ou PJ), toute sortie — clic sur un dossier, ✕,
bouton retour — passe par un confirm « Discard this message? » ; `beforeunload` couvre la
fermeture de l'onglet. Un abandon confirmé supprime les staged (`DELETE` par id, best-effort).
Pas de raccourci Escape : trop de texte à perdre pour une touche.

### 3.2 Point d'entrée

Un bouton **New message** (icône crayon) dans la bande d'en-tête de la liste
(`SelectionToolbar`, groupe de droite, à côté de la loupe), tooltip « New message » —
l'emplacement validé en design. Il navigue vers `/mail/compose`.

### 3.3 `ComposeView`

```
┌ New message ──────────────────────────── ✕ ┐
│ From:    Mick <mick@weesky.be>      (fixe) │
│ To:      [alice@…] [bob@…] _____   Cc Bcc  │
│ Cc:      …             (replié par défaut) │
│ Subject: ________________________________  │
│ [↶ ↷|B I U S|A▾ ▩▾|Font▾ Size▾|≡▾|• 1.|⇥ ⇤|❝|🔗|⌫fmt] │
│                                            │
│  (Squire — canvas clair dans tous thèmes)  │
│                                            │
│ 📎 rapport.pdf (1.2 MB) ✕   [▓▓▓░ 60%] ✕   │
│ ┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈  │
│ [Send]  [Discard]        📎 Attach files   │
└────────────────────────────────────────────┘
```

Fichiers sous `src/modules/mail/compose/` :

- **`ComposeView.tsx`** — l'orchestrateur : état des champs, garde de sortie, mutation
  d'envoi, navigation retour + toast « Message sent » (et avertissement doux si
  `appendedToSent === false`).
- **`RecipientsField.tsx`** — champ à jetons réutilisé pour To/Cc/Bcc : Entrée, virgule,
  point-virgule ou blur committent un jeton ; un collage se découpe sur `,`/`;` ; un jeton
  invalide (regex adresse simple) s'affiche en rouge et **bloque l'envoi**. Cc et Bcc sont
  repliés derrière deux liens tant qu'ils sont vides. Pas d'autocomplétion : le carnet
  d'adresses est le sous-projet 4.
- **`SquireEditor.tsx`** — wrapper React fin autour de `squire-rte` : monte Squire sur un
  `<div>`, expose `getHTML()/setHTML()` et les commandes à la barre, propage `onChange`
  (pour le dirty). **Le canvas d'édition est clair dans les deux thèmes** — même règle que
  l'iframe du lecteur : on montre ce que le destinataire verra, `color-scheme: light`,
  fond blanc.
- **`EditorToolbar.tsx`** — la barre validée : annuler/rétablir · gras/italique/souligné/
  barré · couleur du texte · couleur de fond · police · taille · alignement (g/c/d/justifié) ·
  listes (puces, numérotée) · retrait +/− · citation · lien (insérer/retirer) · effacer la
  mise en forme. Pas de bouton image ni tableau. Couleurs : grille fixe de nuanciers +
  « default ». Polices : jeu web-safe (Arial, Georgia, Tahoma, Times New Roman, Verdana,
  Courier New). Tailles : Small 12px / Normal 14px / Large 18px / Huge 24px. Chaque bouton appelle l'API Squire
  (`bold`, `setTextColour`, `setHighlightColour`, `setFontFace`, `setFontSize`,
  `setTextAlignment`, `makeUnorderedList`/`makeOrderedList`,
  `increaseQuoteLevel`/`decreaseQuoteLevel`, `makeLink`/`removeLink`,
  `removeAllFormatting`, `undo`/`redo`).
- **`AttachmentTray.tsx` + `useStagedAttachments.ts`** — trombone + glisser-déposer sur la
  zone compose ; chaque fichier part immédiatement (`POST /api/Mail/Attachments`) en
  **XMLHttpRequest** — `fetch` n'expose pas la progression d'upload — avec barre de
  progression par fichier et retrait (✕ → `DELETE`). La limite de taille n'est **pas
  dupliquée côté client** : le 400 du backend la nomme et le plateau affiche cette erreur
  sur le fichier refusé — un aller-retour pour un fichier trop gros vaut mieux qu'une
  constante à désynchroniser. Send est désactivé tant qu'un upload est en vol.

### 3.4 Couche de données

`api.js` gagne `sendMessage`, `uploadAttachment` (XHR, callback de progression),
`deleteAttachment`. `queries.ts` gagne la mutation `useSendMessage` (clé sous
`mailKeys.writes`) ; l'invalidation après envoi cible le dossier au rôle sent (comptes +
liste) — le poll de 60 s rattraperait de toute façon.

### 3.5 Tests

Le wrapper Squire est testé avec **Squire mocké** : jsdom n'implémente Range/Selection
qu'en partie, le moteur réel n'y tourne pas — on teste notre glue (montage, commandes
relayées, `onChange`, `getHTML`), pas Squire, qui est couvert par la vérification manuelle.
`RecipientsField`, `AttachmentTray`, la garde de sortie et `ComposeView` (envoi, erreurs,
avertissement Sent) se testent normalement en RTL. Backend : tests xUnit sur le store
staged (scellement, plafonds, TTL), le sanitiseur sortant, `MailSender` (Bcc, repli texte,
APPEND raté → 200 + warning), et le contrôleur (400/401/502, id étranger → 404).

---

## 4. Ce que 2c1 prépare sans l'implémenter

- **2c2** : `SendMessageRequest` accueillera `inReplyTo`/`references` (threading) et un
  `identityId` (choix d'alias) — champs absents aujourd'hui, pas de champ mort en attente.
- **2c3** : les brouillons réutiliseront le store staged tel quel (les ids survivent à la
  sauvegarde d'un brouillon) et l'`AppendAsync` généralisé (APPEND vers le rôle `drafts`).
- Le bouton du lecteur « sender » (déjà focusable) pourra ouvrir le compose pré-rempli —
  hors périmètre ici.

---

## 5. Vérification

1. `dotnet test` — verts, nouveaux tests inclus.
2. `npm run lint`, `npm run test`, `npm run build` — sans erreur ; couverture non régressée.
3. **Manuel** — envoyer depuis account-dev :
   - vers Gmail et Outlook : couleurs/polices/alignements survivent, repli texte présent,
     PJ ouvrables, Bcc invisible des autres destinataires ;
   - copie dans Sent (`\Seen`, PJ incluses, Bcc visible dans la copie) ;
   - PJ > limite refusée (400 nommant la limite, erreur affichée sur le fichier) ; upload en vol bloque Send ;
   - garde de sortie sur dossier/✕/fermeture d'onglet ; Discard purge les staged ;
   - compose UI correcte dans les 4 combinaisons palette × mode (canvas éditeur clair partout) ;
   - échec SMTP simulé → 502 propre, staged conservés, message non perdu.
