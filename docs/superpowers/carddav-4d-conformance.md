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

Date : — · commit : — · fichier : `results/—.txt`

(mêmes colonnes ; seules les lignes qui ont bougé sont commentées)

## 3. Triage

Un verdict par échec (décision 4) : **défaut serveur** (corrigé, test cité), **divergence nommée**
(décision 4c citée), **défaut de l'outil** (RFC cité).

| Suite / test | Constat | Verdict | Référence | Suite donnée |
|---|---|---|---|---|
| | | | | |

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

## 7. Points de guet Apple (hors tranche)

Le mébioctet face aux photos iOS, le `me-card` en `PROPPATCH`, la lecture du 4.0 : à rejouer avec
la liste de la décision 7 le jour où un appareil Apple se présente.
