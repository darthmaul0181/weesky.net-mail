# Agenda 5d — ce que la tranche laisse derrière elle

Le tri de fin de tranche, sur le modèle de `calendar-5c-residuals.md` (et de `calendar-5a-` /
`calendar-5b-residuals.md` avant lui) : ce que la campagne de conformité a mesuré et n'a pas corrigé,
ce que le triage de l'outil a trouvé et classé plutôt que corrigé, ce que les tests n'ont pas pu
couvrir, et ce dont 5e hérite. Il existe pour que la tranche suivante n'ait pas à redécouvrir à ses
frais ce qui a déjà coûté une mesure ou un arbitrage.

Rappel de périmètre : 5d touche au serveur en deux temps, et mesure le reste. La **tâche zéro**
(spec § 3, points 2, 6 et 7) est déployée **avant** la première mesure et change quatre réponses :
un `supported-calendar-component-set` vide refuse désormais la création, le `Cache-Control:
no-cache` est posé sur les refus de `MKCALENDAR`/`MKCOL` et plus seulement sur le `201`, et les
refus de `resourcetype` et de `calendar-timezone` d'un `MKCOL` étendu sortent en
`DAV:mkcol-response` à `propstat` plutôt qu'en `403` + `<D:error>` nu. Vient ensuite la vague de
correctifs du rapport [`calendar-5d-conformance.md`](calendar-5d-conformance.md). Tout le reste est
mesure, contre l'outil et contre des clients réels, de ce que 5a à 5c ont construit.

## Ce qu'un client peut rencontrer, et qui n'est pas corrigé

| Point | Où | Ce que ça donne, et pourquoi c'est resté |
|---|---|---|
| **`COPY` / `MOVE` en `405`** | `DavControllerBase` / `CalDavController` | RFC 4918 § 9.8 et § 9.9 en font un MUST sous le jeton `calendar-access` annoncé par `DavHeaders.ComplianceClasses`. Coûte rien aux trois clients visés (DAVx⁵, Agenda Samsung, Thunderbird n'écrivent ni l'un ni l'autre), tout à un outil WebDAV générique. Différée à 5e. |
| **`limit-recurrence-set` / `limit-freebusy-set` non lus** | rapports CalDAV (`CalendarQueryReport`, `FreeBusyQueryReport`) | RFC 4791 § 9.6.6 et § 9.6.7. Aucun client visé ne les envoie. Différée à 5e. |
| **`time-range` d'un `VALARM` fermé à cinq ans, `free-busy-query` qui refuse une borne absente, fenêtre à deux bornes de plus de cinq ans refusée** | `OccurrenceExpander`, `FreeBusyQueryReport` | RFC 4791 § 9.9 et § 7.10. Aucun client visé ne l'exerce. Résidu propre à 5d. |
| **L'asymétrie du `remove` de `CalendarPropertyUpdate.Judge`** | `Services/CalDav/CalendarPropertyUpdate.cs` | Seule `calendar-description` accepte l'effacement (`DAV:remove` → `200`) ; les quatre autres propriétés inscriptibles (`displayname`, `calendar-color`, `calendar-order`, `calendar-timezone`) répondent `403`, comme pour toute propriété non écrivable. Un `remove` d'une `calendar-description` jamais renseignée répond aussi `200` — `Judge` ne consulte pas l'état stocké, il accepte l'effacement de la description sans condition. Aucun test de l'outil n'atteint cette branche (`proppatch.xml` ne pose jamais deux `remove` de suite sur la même propriété, et aucun client visé n'envoie de `DAV:remove` sur un agenda). |
| **Un `TZID` que seul le fichier définit dé-juge la ressource entière** (`CheckOverrides`, `IcsGuards.cs`) | `Services/Calendar/IcsGuards.cs` | La garde des `RECURRENCE-ID` ne juge que ce qu'elle peut marcher : une ressource portant un fuseau du **troisième palier** — un `TZID` qu'aucune base ne résout et que seul le `VTIMEZONE` du fichier définit — est marchée en heure flottante, donc dans un repère que ses instants ne partagent plus avec une surcharge en forme `Z`. Elle n'est donc pas jugée du tout. **Le coût est plus large que sa cause** : `IcsTimeZones.Expandable` inspecte `DTSTART`, `DTEND`, `RECURRENCE-ID`, `RDATE` et `EXDATE` de **tous** les composants, si bien qu'un tel `TZID` posé sur le seul `DTEND` de la surcharge — un champ que cette garde ne lit jamais — suffit à dé-juger la maîtresse avec. Cas déclencheur : une série hebdomadaire ordinaire en UTC, un `RECURRENCE-ID` fautif, et un `DTEND;TZID="Canberra, Melbourne, Sydney"` sur la surcharge : acceptée. C'est une sur-acceptation assumée, jamais un faux refus — la règle de la tâche est « ne refuser que ce qu'un MUST nomme, et sur le doute, accepter ». |
| **Le plafond de densité tronque la fenêtre de la garde des surcharges** | `Services/Calendar/IcsGuards.cs`, `InstanceInstants` | La marche s'arrête à `MaxInstancesPerYear` (10 000) instances. Si elle n'a pas dépassé la dernière surcharge avant ce plafond, l'ensemble est incomplet et **rien n'est jugé** : un identifiant que la fenêtre n'a jamais atteint n'est pas un identifiant que cette garde peut déclarer absent. Cas déclencheur : une règle quotidienne (10 000 instances ≈ 27 ans) et une surcharge au-delà — `RECURRENCE-ID` fautif accepté. Élargir la fenêtre coûterait la marche que le plafond existe pour borner ; l'alternative — refuser dans le doute — est exactement ce que la règle interdit. |
| **Un `RECURRENCE-ID` d'un autre type de valeur que le `DTSTART` de sa maîtresse n'est pas jugé** | `Services/Calendar/IcsGuards.cs`, `CheckOverrides` | Reprise de `IcsComposer.IsAt` : une date et un horodatage ne se comparent pas, et la garde passe son tour (`at.HasTime != master.DtStart.HasTime`). Cas déclencheurs : une maîtresse tout-journée (`DTSTART;VALUE=DATE`) surchargée par un `RECURRENCE-ID:20260914T090000Z`, et le miroir, une maîtresse horaire surchargée par un `RECURRENCE-ID;VALUE=DATE:20260914`. Les deux sont acceptés sans examen. |

## Dette de forme

À compléter à la clôture, sur la base du triage de l'outil (section 2 du rapport) et de la revue des
correctifs de la vague.

## Ce que les tests n'ont pas couvert

À compléter à la clôture, sur la base du triage de l'outil et des scénarios clients réels non
joués ou non applicables.

## Ce dont 5e hérite

À compléter à la clôture.
