# Tranche 2b4 — Recherche IMAP (webmail) — design

Date : 2026-07-23. 2b1 (drapeaux), 2b2 (actions), 2b3 (multi-sélection, vider un dossier)
et le drag & drop sont livrés. Ce document couvre **2b4 : la recherche** — rapide dans le
dossier courant, avancée via une popup, avec extension optionnelle à toute la boîte.
Maquette validée (variante 2, loupe dans la toolbar) : artifact `search-placement-mockup`.

## 1. Décisions validées

| Sujet | Décision |
|---|---|
| Portée | **Dossier courant par défaut** ; « tous les dossiers » disponible **uniquement dans la popup avancée** |
| Recherche rapide | Une textbox : `objet OU expéditeur contient <texte>` |
| Recherche avancée | Popup : De, À, Objet, Texte, Date, cases Non lu / Suivi / Pièce jointe, sélecteur de portée |
| Point d'entrée | **Loupe dans la `SelectionToolbar`** qui déplie/replie la barre de recherche (variante 2) |
| Déclenchement | **Entrée** (ou le bouton de la popup) lance la recherche — jamais au fil de la frappe : chaque requête est un SEARCH IMAP |
| Résultats | La liste **devient** les résultats ; bandeau épinglé « N results for “…” » + **Clear** |
| URL | La recherche **ne vit pas dans l'URL** — pas un deep-link ; `?folder`/`?uid` inchangés |
| Tri | `SORT (REVERSE DATE)` comme la liste ; fusion par date interne en portée « tous » |
| Pagination | Numérotée classique (réutilise `Pagination`) ; **pas de stream** sur les résultats |

## 2. Modèle d'interaction

```
┌──────────────────────────────────────────────┐
│ ☐  Inbox        🗄 ⚠ 🗑   🔍   ⋮              │  ← SelectionToolbar + loupe
├──────────────────────────────────────────────┤
│ [🔍  Search in Inbox              ⌄ ]        │  ← barre dépliée (au clic loupe)
├──────────────────────────────────────────────┤
│ 🔍 3 results for “factures”        Clear ✕   │  ← bandeau résultats (recherche active)
├──────────────────────────────────────────────┤
│  Krys      Facture n° 4471…                  │  ← les résultats scrollent dessous
```

- **Loupe** dans la `SelectionToolbar`, entre le cluster d'actions et le kebab. Un clic
  déplie la barre (focus dans le champ) ; un second la replie. Replier **efface la
  recherche active** — un seul état, pas de recherche fantôme cachée derrière une barre fermée.
- **Barre** : bande `flex: none` sous la toolbar — champ (placeholder « Search in {folder} »,
  le libellé de rôle via `roleLabel`) + **chevron ⌄** ouvrant la popup avancée. **Entrée**
  lance la recherche rapide. **Escape dans le champ** replie la barre (et efface) ;
  l'Escape de la liste (vider la sélection, 2b3) n'est pas concerné — le champ a le focus.
- **Bandeau résultats** : même famille que `EmptyFolderBanner` (bande épinglée hors zone
  scrollable), « {N} results for “{texte}” » — ou « Results for … » si la requête avancée n'a
  pas de texte libre — et **Clear ✕** qui revient au dossier tel quel.
- Recherche en cours : « Searching… » dans la zone liste ; zéro résultat : « No results. ».

## 3. Recherche avancée — la popup

Construite sur les mêmes briques que les dialogues existants (`.field-h`, `htmlFor`/`id`,
un `.btn-primary` dans un `<form>` pour qu'Entrée soumette, ✕ pour sortir — patron
`AddEditUserModal`). Pré-remplie avec le texte déjà saisi dans la barre rapide (dans Objet).

| Champ | Critère IMAP (MailKit) |
|---|---|
| From | `SearchQuery.FromContains` |
| To | `SearchQuery.ToContains` |
| Subject | `SearchQuery.SubjectContains` |
| Text | `SearchQuery.BodyContains` (corps seul — TEXT inclurait les en-têtes) |
| Date (select) | All time / Last 7 days / Last 30 days / Last 6 months / This year → `DeliveredAfter` (SINCE, date interne — celle qui ordonne la liste) |
| ☐ Unread | `SearchQuery.NotSeen` |
| ☐ Starred | `SearchQuery.Flagged` (la métaphore étoile de 2b1) |
| ☐ Has attachment | **Pas de critère IMAP standard** — post-filtre sur `BODYSTRUCTURE` (§5) |
| Scope (select) | This folder ({folder}) / All folders |

Les champs remplis se combinent en **ET** (`SearchQuery.And`). La recherche rapide compile
`Or(SubjectContains, FromContains)`. Soumission refusée si aucun critère n'est rempli.

## 4. État résultats — ce qui marche, ce qui est neutralisé

- **Ouvrir un résultat** : portée dossier courant → `?uid` comme d'habitude, la recherche
  reste active (elle n'est réinitialisée que par dossier ou Clear). Portée « tous » : chaque
  résultat porte son `folderPath` ; l'ouverture passe le couple au lecteur **sans toucher
  `?folder`** — la recherche reste affichée, l'arbre ne bouge pas. Le marquage lu à
  l'ouverture (2b1) fonctionne tel quel : la mutation est keyée dossier+uid.
- **Sélection & actions groupées** : actives en portée dossier courant (les UID sont du même
  dossier — la plomberie 2b3 s'applique sans retouche, drag & drop compris). **Neutralisées
  en portée « tous »** (cases masquées) : les hooks batch prennent un seul `folderPath`, et un
  lot multi-dossiers est hors périmètre.
- **Aucune mutation optimiste ne patche le cache résultats** : une action depuis les
  résultats (dossier courant) patche les caches de liste comme aujourd'hui ; la ligne
  résultat correspondante est retirée localement par `MessageList` (même mécanique de
  filtrage que `selectedUids`). Pas de rollback à orchestrer sur un cache de plus.
- **Le poll ne touche pas les résultats** : un résultat est un instantané. `useListRefresh`
  continue de rafraîchir les caches de liste en dessous ; les résultats ne se re-exécutent
  que par une nouvelle soumission.

## 5. Backend — `POST /api/Mail/Messages/Search`

POST et non GET : les critères forment un objet, et les chemins de dossier ne vont jamais
en segment de route. Body `SearchMessagesRequest` :

```
{ folderPath, allFolders, from?, to?, subject?, text?, sinceDays?,
  unread?, flagged?, hasAttachment?, page, pageSize }
```

- `folderPath` requis même en `allFolders` (c'est le dossier d'où l'on cherche — utile au
  libellé d'erreur et à la validation), `pageSize` plafonné à 200 comme la liste.
- **Validation** : 400 si aucun critère rempli ; `sinceDays` compilé serveur en
  `DeliveredAfter(today - N)` — le client n'envoie jamais de date littérale.
- **Un dossier** : `folder.SortAsync(query, [OrderBy.ReverseDate])`, page des UID,
  `FetchAsync(SummaryItems)`, `InOrderOf` — le chemin existant, avec un critère à la place
  de `SearchQuery.All`. **Sans la capacité SORT**, le repli n'est pas la fenêtre
  séquentielle de `ListMessagesAsync` (elle fenêtre un dossier entier, pas un sous-ensemble) :
  c'est le chemin multi-dossiers ci-dessous, appliqué à un seul dossier.
- **Tous les dossiers** : **une seule session**, itération des dossiers sélectionnables
  (mêmes exclusions que l'arbre), `SearchAsync(query)` par dossier, puis un
  `FetchAsync(UniqueId | InternalDate)` léger des correspondances pour **fusionner et trier
  par date interne côté C#**, page sur la liste fusionnée, et un `FetchAsync(SummaryItems)`
  **limité à la page** dans chaque dossier concerné. N SEARCH assumés — coût documenté.
- **Pièce jointe** : quand le critère est posé, les UID correspondants sont **raffinés avant
  pagination** par un `FetchAsync(BodyStructure)` et le prédicat `Attachments.Any()` — le
  même qui remplit `HasAttachments`. Filtrer après pagination rendrait `total` et les pages
  faux. Coût assumé, proportionnel aux correspondances, pas au dossier.
- **Réponse** : `MailSearchPage` — `Total`, `Page`, `PageSize`, `Results[]` où chaque
  résultat est un `MailMessageSummary` **plus** `FolderPath` et `UidValidity` (en portée
  « tous », chaque ligne doit dire d'où elle vient ; en dossier courant ils sont uniformes
  mais la forme reste une).
- **Statuts** : 200 ; 400 (validation) ; 401 (`credentials_unavailable`) ; 502 (serveur).
  Méthode `SearchAsync` sur `ImapSession`, repo passe-plat comme les autres.

## 6. Frontend — données

- `api.js` : `searchMessages(criteria, page, pageSize, { signal })`.
- `queries.ts` : `useSearchMessages(criteria | null, page, pageSize)` — clé
  `['mail', accountId, 'search', criteria, page, pageSize]` (les critères sérialisés dans la
  clé : deux recherches différentes sont deux caches). `enabled: criteria !== null`,
  `refetchOnWindowFocus: false` (un focus ne doit pas rejouer un SEARCH multi-dossiers),
  `placeholderData: previous` pour le changement de page sans flash.
- **État** : les critères actifs vivent dans `MailLayout` (ils pilotent la liste **et** le
  dossier du lecteur en portée « tous ») ; la barre et sa saisie vivent dans `MessageList`.
  Changement de dossier (`?folder`) → recherche effacée.

## 7. Cycle de vie & cas limites

- **Changement de dossier** pendant une recherche : recherche effacée, barre repliée —
  choisir un dossier dit « je navigue », pas « je cherche ailleurs ».
- **Clear** (bandeau) et **repli de la barre** : effacent critères et résultats ; si le
  message ouvert venait d'un autre dossier (portée « tous »), le lecteur se ferme —
  `?uid` ne veut rien dire dans le dossier affiché.
- **`uidValidity`** : un résultat ouvert après un break de validité tombe sur le 404 existant
  (`Message not found`) — pas de mécanique neuve, l'instantané assume d'être périmé.
- **Nouvelle soumission** : remplace les résultats (une recherche à la fois).
- **Mode `none`** : barre et résultats vivent dans la liste, masqués pendant la lecture,
  restaurés au retour — l'état survit, comme le scroll (liste jamais démontée).

## 8. Découpage fichiers / testabilité

**Backend**
- DTO `SearchMessagesRequest` + `MailSearchPage`/`MailSearchResult` ; validation contrôleur
  (aucun critère, plafond 200) ; `ImapSession.SearchAsync` (un dossier / tous, compile les
  critères en `SearchQuery`, raffinement pièce jointe) ; `MailController.SearchMessages` ;
  tests contrôleur + session ; `ApiDocumentation.xml` régénéré.
- La compilation critères → `SearchQuery` est une fonction **pure et testée à part**
  (chaque champ, la combinaison ET, le OR de la recherche rapide, le refus du vide).

**Frontend**
- `list/searchCriteria.ts` — type `SearchCriteria`, `isEmptyCriteria`, libellé du bandeau.
  Pur, testé à part.
- `list/SearchBar.tsx` — la bande repliable (champ, chevron, Entrée, Escape). Piloté par props.
- `list/AdvancedSearchModal.tsx` — la popup (briques dialogues existantes).
- `list/SearchResultsBanner.tsx` — le bandeau N results / Clear.
- `MessageList.tsx` — la loupe dans `SelectionToolbar`, bascule liste ↔ résultats,
  neutralisation sélection en portée « tous ».
- `MailLayout.tsx` — porte les critères actifs et le dossier du lecteur.
- `queries.ts` + `api.js` — la query et la méthode.

## 9. Contraintes globales (rappel)

- Tokens uniquement, jamais de littéral couleur ; UI en anglais.
- Jamais `invalidateQueries` sur le stream ; `settle()` avant toute assertion de silence.
- La recherche est une **lecture** : pas de `mailKeys.writes`, le poll n'a pas à se taire.
- Backend : `dotnet test` (jamais `--no-build`) avec de nouveaux fichiers de test ;
  `Assert.IsType<BadRequestObjectResult>` pour les 400 via `BadRequest(body)` ; chemins de
  dossier dans le corps, jamais en segment de route.

## 10. Hors périmètre (YAGNI)

- Recherche incrémentale (à la frappe), surlignage des termes dans les résultats.
- Recherches sauvegardées, historique, recherche dans l'URL (deep-link).
- Actions groupées sur un lot multi-dossiers ; drag & drop depuis des résultats « tous ».
- Index plein-texte côté serveur (fts Dovecot) — la tranche consomme SEARCH tel quel.
- Critères supplémentaires (taille, plage de dates libre, Cc/Bcc) — la popup est extensible.
