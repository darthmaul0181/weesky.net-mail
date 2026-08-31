# CardDAV 4d — rapport de conformité

Rapport de la tranche [4d](specs/2026-08-31-webmail-contacts-4d-conformance-design.md). Les
chiffres viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère,
chaque passage est recopié une fois et daté.

## 1. Passage initial — avant tout correctif

Date : — · commit serveur déployé : — · fichier : `results/—.txt`

| Suite | Tests | OK | Échecs | Ignorés | Fichier sauté |
|---|---|---|---|---|---|
| propfind.xml | | | | | |
| proppatch.xml | | | | | |
| put.xml | | | | | |
| get.xml | | | | | |
| reports.xml | | | | | |
| sync-report.xml | | | | | |
| errors.xml | | | | | |
| errorcondition.xml | | | | | |
| limits.xml | | | | | |
| nonascii.xml | | | | | |
| well-known.xml | | | | | |
| current-user-principal.xml | | | | | |
| mkcol.xml | | | | | |
| copymove.xml | | | | | |
| aclreports.xml | | | | | |
| ab-client.xml | | | | | |

Attendu avant mesure : `put.xml` et `sync-report.xml` sautent au `<start>` (`DELETE` de la
collection encore en 405) ; `mkcol.xml`, `copymove.xml` et `aclreports.xml` échouent (divergences
nommées).

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
