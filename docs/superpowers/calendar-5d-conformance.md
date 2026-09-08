# Agenda 5d — rapport de conformité

Rapport de la tranche [5d](specs/2026-09-07-webmail-calendar-5d-conformance-design.md). Les chiffres
viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère, chaque passage
est recopié une fois et daté. La ligne finale de l'outil porte quatre compteurs dès qu'il y a un
échec — `FAILED (ok=, ignored=, failed=, errors=)` — et deux seulement quand il n'y en a aucun.

## 0. Repère CardDAV, après la tâche zéro

Date : — · commit : — · fichier : `results/—-carddav.txt` · `suites-carddav.txt` déjà amputé de
`CardDAV/limits.xml`. Repère de l'étape 1 : le passage final de l'étape 4 se compare à **lui**, et
non au `ok=107, failed=72` de 4d, mesuré avant que la tâche zéro ne touche au socle.

## 1. Passage initial — avant tout correctif

Date : — · commit serveur déployé : — · fichier : `results/—-caldav.txt`

| Fichier | Tests | OK | Échecs | Ignorés | Erreurs | Tué au `<start>` |
|---|---|---|---|---|---|---|
| propfind.xml | | | | | | |
| proppatch.xml | | | | | | |
| put.xml | | | | | | |
| get.xml | | | | | | |
| delete.xml (copie) | | | | | | |
| reports.xml (copie) | | | | | | |
| sync-report.xml | | | | | | |
| errors.xml | | | | | | |
| mkcalendar.xml | | | | | | |
| options.xml | | | | | | |
| nonascii.xml | | | | | | |
| well-known.xml | | | | | | |
| current-user-principal.xml | | | | | | |
| expandproperty.xml | | | | | | |
| recurrenceput.xml | | | | | | |
| floating.xml | | | | | | |
| ctag.xml | | | | | | |
| encodedURIs.xml | | | | | | |
| conditional.xml | | | | | | |
| copymove.xml | | | | | | |
| aclreports.xml | | | | | | |
| timezones.xml | | | | | | |
| ical-client.xml | | | | | | |

**Le dénominateur, à côté du total** : `reports.xml` porte 115 tests et n'en joue que 68 avec ces
features ; ses deux plus grosses suites tombent à 21 sur 42 et 19 sur 38. Un fichier à 200 tests
dont 100 sont sautés ne mesure pas deux fois plus qu'un fichier à 100.

**Rejeu du `<start>` de `reports.xml`** (`-PrintResponses`, vingt `PUT` `VEVENT`) : —

## 2. Triage

Un verdict par échec, **et par fichier tué au `<start>`**, **et par divergence sortie en « ignoré »
là où la décision 5 attendait un échec** (décision 4) : **défaut du serveur** (corrigé, test cité),
**divergence nommée** (avec ce qu'elle coûte à un client réel), **défaut de l'outil** (le *quoi*
nommé), **harnais** (corrigé, et le passage final le mesure).

| Fichier / suite / test | Constat | Verdict | Référence | Suite donnée |
|---|---|---|---|---|
| | | | | |

### Divergences prédites qui ne sont pas sorties comme prévu

La table de la décision 5 annonce ce que l'outil enverra, les `<features>` décident ce qu'il enverra
vraiment, et les deux peuvent se contredire en silence. Tout écart est lui-même un constat.

| Ligne de la table | Prédit | Observé |
|---|---|---|
| | | |

## 3. Passage final — après la vague de correctifs

Date : — · commit : — · fichier : `results/—-both.txt` · `-Protocol Both`

(mêmes colonnes que le passage initial, plus les fichiers CardDAV ; le rapport dit à chaque
comparaison lequel des deux repères CardDAV il commente)

## 4. Clients réels

Le rapport **sépare** ce qui vient de DAVx⁵ (la synchronisation) de ce qui vient d'Agenda Samsung
(l'interface) : deux logiciels, deux sources d'écart. Un scénario qu'un client ne peut pas jouer
sort en « non applicable », un statut distinct des quatre verdicts.

### Thunderbird (Windows, version —)

| # | Scénario | Résultat | Écart |
|---|---|---|---|
| 1 | Appairage par la seule adresse (hôte nu, puis adresse complète) | | |
| 2 | Découverte : deux agendas, nom et couleur | | |
| 3 | Création côté client → webmail | | |
| 4 | Création côté webmail → client | | |
| 5 | Modification des deux côtés, `If-Match`, `412` | | |
| 6 | Le cinquième cas — mêmes instants, mêmes exceptions | | |
| 7 | Rappel posé côté client, conservé au retour | | |
| 8 | Couleur et nom d'agenda (`PROPPATCH`) | non applicable | |
| 9 | Création d'un agenda depuis le client | non applicable | |
| 10 | Suppression des deux côtés | | |
| 11 | Journée entière, multi-jours, autre fuseau, flottant, +5 ans | | |
| 12 | Régénération du secret → `401`, ré-appairage | | |
| 13 | Non-régression du 7 septembre | non applicable | |

### DAVx⁵ (Android, version —) + Agenda Samsung (version —)

(mêmes lignes ; 8 « non applicable » ; 9 joué **trois fois** — tout coché, Événements seuls,
Événements + Tâches — et le rapport écrit **ce que le client affiche** dans le troisième cas ;
10 joué **deux fois**, réglage par défaut et « tous les événements »)

| Écart | Colonne | Détail |
|---|---|---|
| couleur d'événement non sauvée | Samsung | |
| `VALARM` sans `ACTION` muet | Samsung | |
| `RRULE` réparée (`UNTIL` en `DATE`) | DAVx⁵ | |
| `VTIMEZONE` régénéré | DAVx⁵ | |

## 5. Apple — non branché

`ical-client.xml` : — (six `PROPFIND`, statut seul, aucune assertion de propriété).
`AppleDiscoveryReplayTests` : — . Ce que ça prouve : la séquence ne tombe pas, et une propriété que
nous ne servons pas sort en `404` propstat. Ce que ça ne prouve pas : qu'un iPhone en fasse quelque
chose. Coût connu et non mesuré : sabre nº 935 rapporte que macOS Calendar, sur un `403` au
`PROPPATCH` de `default-alarm-vevent-date`, « stops after that ».

## 6. Non-conformités connues, portées en toutes lettres

`DavHeaders.ComplianceClasses` annonce `1, 3, addressbook, calendar-access, extended-mkcol`, et
RFC 4791 § 5.1 comme RFC 4918 § 18.1 font de ces jetons la promesse de tous les MUST de leur texte.

| Non-conformité | MUST | Ce que ça coûte à un client | Suite |
|---|---|---|---|
| `COPY` / `MOVE` en `405` | RFC 4918 § 9.8, § 9.9 | rien aux trois clients visés, tout à un outil WebDAV générique | différée, 5e |
| `limit-recurrence-set` / `limit-freebusy-set` non lus | RFC 4791 § 9.6.6, § 9.6.7 | aucun client visé ne les envoie | différée, 5e |
| `time-range` d'un `VALARM` fermé à cinq ans, `free-busy-query` qui refuse une borne absente, fenêtre à deux bornes de plus de cinq ans refusée | § 9.9, § 7.10 | aucun client visé ne l'exerce | résidu 5d |

## 7. Clôture

Secret du compte de test régénéré le — . `calendar-5c-residuals.md` mis à jour ligne à ligne :
— . `calendar-5d-residuals.md` écrit : — .
