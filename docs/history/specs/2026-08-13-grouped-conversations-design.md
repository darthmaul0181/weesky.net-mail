# Regroupement des conversations — design

Un réglage de compte, **désactivé par défaut**, qui regroupe la liste de messages en fils de
discussion : une ligne par conversation, dépliable en sous-lignes, **par dossier**. Le lecteur ne
change pas — un fil se déplie dans la liste, jamais dans le lecteur.

## Les décisions

| Décision | Retenu | Pourquoi |
|---|---|---|
| Portée d'un fil | Le dossier courant seul | Le modèle Snappymail/Outlook. Le style Gmail (fusion Inbox + Sent) rend chaque page multi-dossiers et chaque action (déplacer, supprimer) ambiguë. |
| UX du fil | Ligne dépliable dans la liste | Le lecteur actuel reste inchangé ; un lecteur-conversation aurait touché le marquage lu, les actions par message et le chargement des corps. |
| Valeur par défaut | `false` | Aucun changement de comportement au déploiement ; chacun l'active dans Settings → General. |
| Calcul des fils | IMAP `THREAD=REFERENCES`, côté serveur | Le seul découpage correct (par `Message-Id`/`References`, pas par sujet). Le regroupement client par sujet fabrique de faux fils ; le regroupement par page coupe un fil à cheval sur deux pages en deux lignes visibles. |
| Pagination | Une page = N **fils** | Compter des messages ferait déborder ou tronquer des fils aux frontières de page. |
| Recherche et filtre étoilé | Résultats **à plat** | Une recherche cherche des messages, pas des fils — le comportement Snappymail. Le chemin `POST /Search` ne change pas. |
| Actions sur la ligne repliée | Le fil entier | Checkbox, étoile et cluster d'actions envoient le batch des UIDs membres ; les endpoints batch existent. Les sous-lignes portent leurs contrôles individuels. |
| Serveur sans `THREAD` | Liste plate, sans erreur | Le pattern du repli `SORT` : la capability manque, la réponse reste plate, le client rend ce qu'il reçoit. |

## Le réglage

- **Backend** — une entrée de plus dans le registre `UserPreferences.cs` :
  `mail.groupConversations`, booléen, défaut `"false"`. Rien d'autre : la table
  `user_preferences` et `GET/PUT /api/Preferences` existent déjà, et le registre est le seul
  endroit où un réglage se déclare.
- **Frontend** — `groupConversationsOf` dans `usePreferences.ts`, vrai sur strictement `'true'`
  comme les autres booléens. Toggle dans **Settings → General → Layout** (`.setting-label` +
  `.setting-hint`), libellé anglais « Group conversations », traduit dans les deux catalogues.
  Changer une préférence invalide déjà tout le cache `['mail']` : la liste se recharge dans la
  bonne forme sans code neuf.

## Backend — `GET /api/Mail/Messages` prend `grouped=true`

Le client passe le paramètre quand la préférence est active — le même modèle que `pageSize` : la
préférence reste côté client, le backend reste sans état sur ce point.

Dans `ImapMessageCommands.ListMessagesAsync`, quand `grouped` **et** que la session annonce
`THREAD=REFERENCES` (lu après authentification, comme `SORT`) :

1. `SORT (REVERSE DATE)` comme aujourd'hui → l'ordre des messages.
2. `THREAD REFERENCES` sur le dossier entier → l'arbre des fils, aplati : un fil = l'ensemble des
   UIDs de son sous-arbre.
3. **La position d'un fil = la position de son membre le plus récent dans le résultat du SORT** ;
   les fils sont triés là-dessus. Deux commandes IMAP, aucun fetch supplémentaire pour trier.
4. Une page = N fils — `page`/`pageSize` gardent leurs noms et leur mécanique, mais l'unité
   comptée devient le fil. Un seul
   `FetchAsync` ramène les résumés de **tous** les membres des fils de la page, dans le même
   aller-retour que les `SummaryHeaders` actuels — le dépliage client est instantané, sans nouvel
   endpoint.

**Réponse.** `MailFolderPage` gagne deux champs optionnels :

- `Threads: List<MailThread>?` — chaque `MailThread` porte ses `Messages` (des
  `MailMessageSummary` existants, du plus récent au plus ancien) ; le premier est la ligne
  repliée, `Messages.Count` le compteur.
- `TotalThreads: int?` — ce que le pager pagine en mode groupé. `Total` garde son sens actuel
  (messages du dossier).

En mode plat les deux sont `null`, donc absents du JSON (`WhenWritingNull`) : **le client détecte
le mode par la présence de `threads`**, jamais par une hypothèse sur la capability. `Messages`
reste servi en mode plat exactement comme aujourd'hui — un client ancien ou un repli sans
capability lit la même forme qu'avant.

**Extraction pure.** L'ordre et la pagination des fils (l'arbre aplati + la liste triée du SORT →
les fils de la page N, ordonnés) vivent dans un helper pur `MailThreading`, testable sans IMAP —
le rôle que `MailPaging` joue pour le mode plat.

## Frontend — la ligne de fil

- Un fil de **1 message se rend exactement comme aujourd'hui** : pas de badge, pas de chevron. À
  partir de 2 : la ligne montre le dernier message (expéditeur, objet, aperçu, date) + un badge
  compteur + un chevron.
- **État agrégé** sur la ligne repliée : non-lu si un membre est non-lu, étoile si un membre est
  étoilé, trombone si un membre porte une pièce jointe.
- Le chevron déplie des **sous-lignes indentées**, une par message, plus ancien en bas. L'état de
  dépliage est local (`Set` de clés de fil), remis à zéro au changement de dossier ou de page.
- Cliquer une sous-ligne ouvre **ce** message dans le lecteur ; cliquer la ligne repliée ouvre le
  **dernier**. Le lecteur, `?uid=`, le marquage lu à l'ouverture : inchangés.
- Actions de la ligne repliée = le fil entier : la checkbox sélectionne tous les UIDs membres
  (`useSelection` reste keyé par uid), l'étoile et le cluster (lu/archiver/indésirable/supprimer)
  envoient le batch d'UIDs. Les sous-lignes portent leurs checkbox, étoile et cluster individuels.
- Les **deux skins** de ligne (étroite et `wide`) prennent le badge et le chevron.
- **La clé stable d'un fil = l'UID de son membre le plus ancien** — le plus récent change à chaque
  arrivée, le plus ancien ne change qu'à une expiration du fil lui-même.

## Stream « All », poll et cas limites

- **Streaming** : un bloc = `BLOCK_SIZE` fils ; `nextBlockIndex` s'arrête sur un bloc court,
  inchangé dans son principe. `dedupeByUid` gagne un pendant `dedupeByThread`, keyé sur l'UID
  racine — même sémantique de snapshot : un fil déjà chargé garde sa version, un doublon né du
  décalage d'offset est éliminé.
- **Poll** : `refreshFirstBlock` fusionne le bloc 0 frais avec l'ancien, dédupliqué par clé de
  fil, frais d'abord — le mécanisme existant transposé. `uidValidity` cassée → `resetQueries`
  scoped au dossier, inchangé.
- **Pager** en mode groupé : `lastPage` se calcule sur `totalThreads`.
- Recherche active ou filtre étoilé → liste plate (chemin déjà séparé) ; la sélection, le drag et
  l'empty-folder y gardent leur comportement actuel.

## Tests

- **Backend** : tests unitaires de `MailThreading` (ordre par membre le plus récent, pagination,
  fil à cheval sur la frontière, arbre dégénéré à un message, dossier vide) ; tests contrôleur sur
  la forme de réponse (`threads` présent/absent, `totalThreads`) et le repli sans capability.
- **Frontend** : rendu groupé vs plat, fil de 1 sans badge, dépliage et re-repli, sélection et
  étoile sur fil (batch d'UIDs), pager sur `totalThreads`, `dedupeByThread` et merge du bloc 0.
  Chaînes en/fr soumises aux tests de parité et de typographie française existants.
- **Géométrie** : un cas dans `probes/mobile-layout.html` pour les sous-lignes dépliées à 360px
  (jsdom ne voit aucune mise en page).
