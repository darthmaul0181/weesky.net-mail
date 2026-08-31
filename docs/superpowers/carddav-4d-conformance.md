# CardDAV 4d — rapport de conformité

Rapport de la tranche [4d](specs/2026-08-31-webmail-contacts-4d-conformance-design.md). Les
chiffres viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère,
chaque passage est recopié une fois et daté.

## 1. Passage initial — avant tout correctif

Date : 2026-08-31 · commit serveur déployé : `6743b06b` (origin/cardav, avant la décision 3) ·
fichier : `results/20260831-183718.txt` · 148 tests en 67 s · toutes les lignes `Authorization` épurées (67/67)

| Suite | Tests | OK | Échecs | Ignorés | Fichier sauté |
|---|---|---|---|---|---|
| propfind.xml | 16 | 13 | 3 | 0 | non |
| proppatch.xml | 7 | 0 | 7 | 0 | non |
| put.xml | — | — | — | — | **oui** — start : `DELETEALL` du home → `DELETE default/` répond `405` |
| get.xml | 3 | 1 | 2 | 2 suites (directory gateway) | non |
| reports.xml | 39 | 33 | 6 | 2 suites (directory gateway) | non |
| sync-report.xml | — | — | — | — | **oui** — start : `DELETE` de la collection répond `405` |
| errors.xml | 10 | 4 | 6 | 0 | non |
| errorcondition.xml | 12 | 4 | 8 | 1 suite | non |
| limits.xml | — | — | — | fichier entier | non (ignoré : require-feature non satisfaite) |
| nonascii.xml | 7 | 3 | 4 | 0 | non |
| well-known.xml | 10 | 0 | 10 | 0 | non |
| current-user-principal.xml | 3 | 0 | 3 | 1 suite | non |
| mkcol.xml | 4 | 2 | 1 | 1 test | non |
| copymove.xml | 3 | 0 | 3 | 1 suite | non |
| aclreports.xml | 21 | 1 | 18 | 2 tests | non |
| ab-client.xml | 0 | 0 | 0 | 0 | non (aucun test retenu) |

Attendu avant mesure : `put.xml` et `sync-report.xml` sautent au `<start>` (`DELETE` de la
collection encore en 405) ; `mkcol.xml`, `copymove.xml` et `aclreports.xml` échouent (divergences
nommées).

Constaté : **exactement les deux sauts prédits**, pour la raison prédite (le `405` du `DELETE` de
collection, décision 3 non déployée). Total de l'outil : `ok=64, failed=71, ignored=11, errors=2`.
Les échecs de `mkcol`/`copymove`/`aclreports` (22) sont les divergences nommées ; `proppatch` 0/7
est la divergence PROPPATCH-403 ; restent à trier notamment `well-known` 0/10 et
`current-user-principal` 0/3 (section 3).

## 2. Second passage — après le `DELETE` du carnet (décision 3)

Date : 2026-08-31 · commit : `e4c45012` · fichier : `results/20260831-195140.txt` ·
201 tests en 88 s · `ok=89, failed=90, ignored=22, errors=0` · épuration vérifiée (0 ligne en clair)

Seules les lignes qui ont bougé — la décision 3 a fait tourner les deux fichiers sautés,
tout le reste est au chiffre près identique au passage initial :

| Suite | Tests | OK | Échecs | Ignorés | Fichier sauté |
|---|---|---|---|---|---|
| put.xml | 18 | 14 | 4 | 0 | non — le `<start>` (DELETEALL) passe |
| sync-report.xml | 34 | 11 | 15 | 8 tests + 3 suites | non — le `<start>` (`DELETE` de la collection) passe |
| ab-client.xml | 3 | 3 | 0 | 0 | non (3 tests exécutés cette fois) |

## 3. Triage

Un verdict par échec (décision 4) : **défaut serveur** (corrigé, test cité), **divergence nommée**
(décision 4c citée), **défaut de l'outil** (RFC cité).

Triage du passage `20260831-195140` — les 90 échecs se réduisent à 29 lignes, DS = défaut
serveur, DN = divergence nommée, DO = défaut de l'outil, H = harnais :

| Suite / test | Constat | Verdict | Référence | Suite donnée |
|---|---|---|---|---|
| propfind / prop errors 1-2 | corps `propfind` invalide accepté (`allprop`+`propname` ; enfant inconnu) → 207 | **DS** | RFC 4918 § 14.20 (DTD) | vague : `400` |
| propfind / regular 4 | `getcontentlength` attendu à `0` sur la collection, servi en 404 | DO | RFC 4918 § 15.4 (due seulement si GET sert Content-Length) | — |
| proppatch (7) | propriétés mortes refusées `403` | DN | 4c décision 16 | — |
| put / PUT groups 1-4 | verbatim (ETag présent, pas de réécriture), type groupe/personne non contrôlé, + cascade `no-uid-conflict` | DN | 4a verbatim ; groupes hors produit (4c « ne fait pas ») | — |
| get / 2-3 | listing HTML d'une collection, chemin `$principaluri1:` | DO | fonctions CalendarServer, tests non gardés par feature | — |
| reports / basic query 19, 20, 21, 23, 26 | sous `nresults`, notre sous-ensemble ≠ celui de CalendarServer | DO | RFC 6352 § 8.6 : la troncature est libre | — |
| reports / basic query 28 | filtre sur propriété à préfixe de groupe (`item1.`) refusé `supported-filter` | DN | 4c décision 11 (évalué ou refusé, jamais ignoré) | — |
| sync-report / support-report-set 1 | `PROPFIND /dav/addressbooks/` → `404` | à trancher | — | question S4 |
| sync-report / support-report-set 3 | jeton attendu préfixé `data:,` | DO | RFC 6578 § 6.1 : jeton opaque | — |
| sync-report / suites « no props » | réponses `<D:response>` sans `<D:status>` quand aucune propriété n'est demandée | **DS** | RFC 4918 § 14.24 (`href+status` ou `propstat`) | vague : `status 200` |
| sync-report / « …/default » attendu partout | `$calendar_sync_extra_items:` resté à `[-]` (= inclure la collection, un CalendarServer-isme) | H | src/utils.py du tester | vague : `[]` dans le gabarit |
| sync-report / diff 5 et props 5 | un PUT à octets identiques n'avance pas la séquence, le delta est vide | DN | 4c décision 6 (avance ssi `card_hash` change) | — |
| errors / 2 et 5 | UID changé sous le même nom accepté (2), et 5 est sa cascade | DN | revue 2, P4 (sabre fait pareil ; DAVx⁵ boucle sinon) | — |
| errors / 4 ; errorcondition / PUT 2 | cartes « invalides » de l'outil parfaitement lisibles, acceptées | DO | aucun MUST identifiable dans les fixtures | — |
| errors / 8 ; (même carte ailleurs) | **caractère de contrôle (BEL) accepté dans NOTE** | **DS** | RFC 2426 ABNF (CTL exclus des valeurs) | vague : `403 valid-address-data` |
| errors / 25-26 | `PUT` sur home/collection → notre `405`, attendu `403` | DN | 4c décision 16 (`Allow` honnête) | — |
| errorcondition / PUT 1 | `Content-Type: text/xml` sur un PUT ignoré, le corps seul juge | DN | 4c (doc du writer : le corps est le seul juge) | — |
| errorcondition / PUT 3 | **ligne sans deux-points (carte structurellement cassée) acceptée** | **DS** | ABNF `contentline` (RFC 2426/6350) | vague : `403 valid-address-data` |
| errorcondition / PUT 5 | **corps portant deux VCARD pris pour un seul**, refusé `no-uid-conflict` au lieu d'invalide | **DS** | RFC 6352 § 5.1 (une ressource = un vCard) | vague : `403 valid-address-data` |
| errorcondition / PUT 4 et 7 | propriété inconnue (`METHOD`) et `PHOTO;BASE64` 2.1 tolérées ; UID changé = P4 | DO/DN | extensibilité RFC 2426 § 3 ; P4 | — |
| errorcondition / REPORT 2 et 4 | `address-data version="4.0"` servie (l'outil attend un refus) | DO | nous servons 4.0 (4c décision 11) | — |
| nonascii / non-utf-8 1-3 | la fixture du dépôt archivé contient déjà U+FFFD : le corps envoyé EST de l'UTF-8 valide | DO | octets `EF BF BD` dans `vnonascii/2.vcf` | — (notre décodeur strict est testé chez nous) |
| nonascii / high-ascii 3 | deux cartes en trop sur une requête haute-ascii | DN | collation NFC+lower (revue 2, DavCollation) | — |
| well-known (10) | `Location: /dav/` relatif (licite) vs égalité stricte avec `https://hôte/` ; `200` exigé sur `/.well-known/` nu | DO | RFC 7231 § 7.1.2 ; RFC 8615 | — |
| current-user-principal (3) | `PROPFIND /dav/principals/` → `404` — URL que `principal-collection-set` publie | à trancher | RFC 3744 § 5.8 | question S4 |
| mkcol / MKCOL with body | `404` rendu, `403` attendu | DN | 4c décision 3 (pas de MKCOL) | — |
| copymove (3), aclreports (18), mkcol (1) | verbes et rapports RFC 3744 non servis | DN | 4c décisions 13 et 16 | — |
| limits (fichier) | ignoré : `require-feature` exige `caldav` — c'est un fichier de quotas CalDAV | DO | en-tête de `limits.xml` | note dans suites.txt |

**Vague de correctifs retenue** (un test mutation-vérifié chacun) : S1 corps `propfind` invalide
→ `400` ; S2 `<D:status>HTTP/1.1 200 OK</D:status>` dans les réponses sans propriété du
`sync-collection` ; S3 le PUT refuse en `valid-address-data` les caractères de contrôle (hors
CR/LF/HTAB), un corps à plusieurs VCARD, et une ligne non pliée sans deux-points ; H1
`$calendar_sync_extra_items:` → `[]` + note `limits.xml`. S4 (servir `PROPFIND` sur
`/dav/principals/` et `/dav/addressbooks/`) : décision utilisateur, voir section.

## 4. Passage final — après la vague de correctifs

Date : — · commit : — · fichier : `results/—.txt`

(mêmes colonnes que le passage initial)

## 5. Clients réels

### Thunderbird (Windows, version —)

| # | Scénario (spec, décision 7) | Observé | Verdict |
|---|---|---|---|
| 1 | Appairage par l'adresse de l'onglet Sync | | |
| 2 | Création côté client → webmail | | |
| 3 | Création côté webmail → client | | |
| 4 | Modification dans chaque sens, photo comprise | | |
| 5 | Suppression dans chaque sens | | |
| 6 | Carte 4.0 croisée avec DAVx⁵ | | |
| 7 | Conflit : 412, le serveur gagne, l'autre version archivée | | |
| 8 | Régénération du secret | | |

### DAVx⁵ (Android, version —)

(scénarios 1 à 8, plus :)

| # | Scénario (spec, décision 7) | Observé | Verdict |
|---|---|---|---|
| 9 | « Delete collection » : le carnet se vide et réapparaît vide | | |

## 6. Divergences nommées

Reprises de la spec 4d (décision 4), confirmées ou allongées par les passages :

| Divergence | Décision 4c | Constatée par |
|---|---|---|
| `Depth` ignoré sur `sync-collection` et `addressbook-query` | 7, 14 | |
| `PROPPATCH` refuse chaque propriété en `403` | 16 | |
| RFC 3744 non servi, `access-control` retiré | 13, revue 2 (P1) | |
| `address-data` en `propstat 404` dans `sync-collection` | 14 | |
| Plafond d'un mébioctet | 15 | |
| Pas de `MKCOL`, `COPY`, `MOVE` | 3, 16 | |
| `DavPaths.Parse` ne reconnaît pas `/dav/principals/` ni `/dav/addressbooks/` : un `expand-property` sur `principal-collection-set` rend un `404` imbriqué pour une URL que le PROPFIND sert (S4) | adjudication de la revue de vague | interne, parquée — aucun client réel n'étend cette propriété, le repli PROPFIND répond |

## 7. Points de guet Apple (hors tranche)

Le mébioctet face aux photos iOS, le `me-card` en `PROPPATCH`, la lecture du 4.0 : à rejouer avec
la liste de la décision 7 le jour où un appareil Apple se présente.
