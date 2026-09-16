# Webmail — Tranche 2c3a : brouillons

**Date :** 2026-07-25
**Statut :** design validé, prêt pour la planification d'implémentation
**Amont :** 2c1 (rédaction & envoi), 2c2a (identités), 2c2b (réponse / transfert — `ComposeSeed`,
`QuotePreparer`, re-staging serveur). La tranche 2c3 annoncée en 2c2a (« brouillons, signatures »)
se scinde : **2c3a = brouillons (ce document)**, 2c3b = signatures.

---

## 1. Le problème

Le composeur ne connaît que deux issues : envoyer ou tout perdre. Un message interrompu — un
onglet fermé, une réponse à finir plus tard — n'a nulle part où aller, alors que le dossier au
rôle `drafts` existe déjà dans l'arbre et que les autres clients IMAP du même compte y déposent
leurs brouillons. La tranche apporte : sauvegarder un brouillon dans ce dossier, le reprendre
d'un clic, l'envoyer, et croiser sans friction avec Snappymail/Thunderbird.

## 2. Décisions d'UX (validées)

- **Sauvegarde explicite** : un bouton **Save draft** dans le composeur + une proposition à la
  fermeture quand le contenu a changé. Pas d'autosave périodique.
- **Fermeture d'un composeur modifié** : le blocker de navigation existant devient un dialogue à
  trois choix — **Save draft / Discard / Keep editing**. « Discard » d'un brouillon existant
  jette les modifications, pas la version déjà sauvée dans Drafts.
- **Reprise** : dans un dossier au rôle `drafts`, cliquer une ligne ouvre directement le
  composeur pré-rempli — pas le lecteur.
- **Liste du dossier Drafts** : la ligne affiche un marqueur **Draft** discret et
  **« To: destinataire(s) »** (ou « (no recipient) ») à la place de l'expéditeur — s'afficher
  soi-même sur chaque ligne n'aide personne.
- Chaque sauvegarde **remplace** la version précédente : une seule ligne par brouillon.

## 3. Architecture (approche retenue)

Le brouillon est un **message MIME standard dans le dossier IMAP au rôle `drafts`**, flags
`\Draft \Seen`. Les pièces jointes vivent **dans le message** ; le store staged reste un plan de
travail transitoire (TTL 12 h inchangé) :

1. pendant la rédaction, une pièce ajoutée part dans le store staged, comme pour un envoi ;
2. au « Save draft », le backend construit le message complet — pièces incluses — et l'APPEND
   dans Drafts ; la copie durable est le mail ;
3. à la reprise, les pièces du message sont **re-stagées** côté serveur (mécanique du transfert
   2c2b) pour redevenir éditables ;
4. un balayage TTL du staged ne fait perdre à un brouillon sauvé strictement rien.

Aucune construction MIME n'est dupliquée : la fabrication du message sortant est **extraite de
`MailSender.SendAsync` en une méthode partagée** (sanitizer sortant, réécriture staged→cid,
`multipart/related`, pièces jointes, en-têtes de threading sûrs, identité résolue côté serveur)
que Send et Drafts appellent tous deux. La reprise réutilise la machinerie `QuotePreparer` du
mode `editAsNew` (corps citable assaini + re-staging), assemblée avec l'extraction d'enveloppe.

Interopérabilité gratuite : un brouillon sauvé ici se rouvre dans Snappymail/Thunderbird et
réciproquement — MIME standard, flag standard, dossier standard.

## 4. Backend

### 4.1 `POST /api/Mail/Drafts` — sauvegarder

Corps : les champs de `SendMessageRequest` (`to`, `cc`, `bcc`, `subject`, `htmlBody`,
`attachmentIds`, `fromAddress?`, `inReplyTo?`, `references`) + **`replaceUid?: uint`** —
l'ancienne version à remplacer.

- Construit le message par la méthode partagée, l'APPEND (`\Draft \Seen`) dans le dossier au
  rôle `drafts`, résolu par le même mécanisme que la copie Sent de l'envoi — mais ici l'absence
  de dossier est un **502 explicite**, pas un best-effort : l'APPEND est l'objet même de la
  requête.
- Puis supprime `replaceUid` quand il est fourni. Un échec de cette suppression est loggé et
  **non fatal** : la réponse porte le nouvel uid, la version orpheline partira à la prochaine
  sauvegarde ou à la main.
- Réponse **200 `{ uid, folderPath }`** (UIDPLUS) — la sauvegarde suivante enverra cet uid en
  `replaceUid`.
- Validation : un brouillon **vide, sans destinataire ou sans objet est valide** (contrairement
  à Send). En revanche un `fromAddress` non possédé (règle `IdentityResolver`, identique à
  Send) et un id staged inconnu restent des **400**. Adresse destinataire mal formée : 400,
  même règle que Send — on ne stocke pas ce qu'on ne saura pas envoyer.
- Statuts : 200 / 400 / 401 `credentials_unavailable` / 502.

### 4.2 `POST /api/Mail/Drafts/Open` — reprendre

Corps : `{ folder, uid }`. Réponse, tout ce qu'exige le seed du composeur :

```
{ to, cc, bcc, subject, fromAddress,   // enveloppe du brouillon
  htmlBody,                            // assaini politique sortante, cid → URLs staged
  attachments: StagedAttachmentInfo[], // pièces re-stagées sous le compte appelant
  inReplyTo, references }              // relus des en-têtes — le threading survit
```

- `fromAddress` : l'adresse du `From` du message ; le composeur la confronte à la liste
  d'identités (une adresse inconnue retombe sur l'identité par défaut, règle 2c2b).
- Un brouillon de réponse rouvert garde `inReplyTo`/`references` ; envoyé, il se threade
  correctement chez le destinataire.
- Statuts : 200 / 400 / 401 / **404** (uid disparu — constantes partagées `ImapSession`) / 502.

### 4.3 Envoi d'un brouillon repris

`POST /api/Mail/Send` inchangé. Après un envoi réussi, le **frontend** supprime le brouillon via
le `DELETE /api/Mail/Messages` existant (expunge — c'est un brouillon, pas de détour par la
corbeille) et invalide les listes. Pas d'orchestration serveur : deux opérations existantes.

### 4.4 `MailMessageSummary.To`

Le résumé de liste gagne `to` : la liste courte des destinataires (nom d'affichage, repli sur
l'adresse), déjà présente dans l'enveloppe FETCH — coût nul. Sert l'affichage du dossier Drafts ;
les autres dossiers l'ignorent aujourd'hui.

## 5. Frontend

### 5.1 Composeur

- `ComposeSeed.action` gagne **`'draft'`** (titre « Draft ») ; le seed porte
  `draftRef: { folderPath, uid } | null`.
- Bouton **Save draft** (secondaire, à côté de Send) : appelle 4.1, mémorise l'uid retourné
  (remplacement au coup suivant), toast « Draft saved ».
- Dialogue de fermeture à trois choix (2. ci-dessus) branché sur le blocker existant ; il ne se
  montre que si le contenu a changé depuis l'ouverture ou la dernière sauvegarde.
- Envoi d'un brouillon repris : après le 200 de Send, suppression du brouillon (4.3).
- **Nettoyage différé de 2c2b** : le cycle de vie des ids staged inline rejoint
  `useStagedAttachments` ; `ComposeView` cesse d'appeler `api.deleteAttachment` en direct.

### 5.2 Dossier Drafts

- Le rôle est déjà stampé sur chaque nœud de l'arbre : dans un dossier `drafts`, le clic d'une
  ligne navigue vers le composeur et le seed se construit depuis `Drafts/Open`.
- La ligne affiche le marqueur **Draft** + « To: … » (2. ci-dessus) depuis le nouveau champ
  `to` du résumé.

## 6. Erreurs & sécurité

Conventions `MailController` inchangées : 401 `credentials_unavailable`, 502 refus IMAP, 404 via
les constantes partagées, 400 validation. `Drafts/Open` re-stage sous le **namespace du compte
appelant** — l'étanchéité par compte du store staged s'applique telle quelle, et le quota de
staging existant borne la reprise comme il borne le transfert. Le corps rouvert passe par le
**sanitizer sortant**, comme PrepareQuote — jamais de HTML brut du message vers le composeur.

## 7. Tests

- **Backend** : la méthode partagée exercée par ses deux appelants (les garanties
  threading/CRLF de 2c2b restent pinées) ; APPEND + remplacement ; 502 sans dossier drafts ;
  suppression de l'ancienne version en échec → 200 quand même ; brouillon vide accepté ;
  `fromAddress` non possédé → 400 ; Open : enveloppe + re-staging + threading + 404 ;
  `MailMessageSummary.To` transcrit.
- **Frontend** : seed `draft` ; Save draft (uid mémorisé → `replaceUid`) ; dialogue à trois
  choix (les trois branches) ; liste Drafts (affichage destinataire, clic → composeur) ;
  suppression du brouillon après envoi ; cycle staged inline via `useStagedAttachments`.
- Suites complètes vertes des deux côtés, `build` et `eslint` propres.

## 8. Vérification manuelle (dev)

Sauver → rouvrir → modifier → resauver (une seule ligne dans Drafts) → envoyer (le brouillon
disparaît, le message part avec pièces et threading). Croiser avec Snappymail : un brouillon
créé de chaque côté se rouvre proprement de l'autre.

## 9. Hors périmètre

- Signatures (2c3b) ; autosave périodique (écarté en design) ; édition partielle d'un message
  IMAP (impossible — chaque sauvegarde réécrit le message, modèle standard).
