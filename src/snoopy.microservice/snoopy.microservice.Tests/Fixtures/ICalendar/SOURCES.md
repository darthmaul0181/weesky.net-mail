# Corpus iCalendar — origine des fichiers

Fichiers réels écrits par de vrais clients, repris tels quels (à la fin de ligne près, que git
normalise) des suites de tests de projets open source. Chaque ligne donne le producteur d'après
son `PRODID`, ce que le fichier exerce, et sa provenance avec la licence du dépôt d'origine.

| Fichier | Producteur | Exerce | Provenance |
|---|---|---|---|
| `apple-icloud.ics` | iCloud (`caldav.icloud.com`) | 8 événements, `RRULE` avec `UNTIL` en UTC, `RDATE`, journées entières, `X-APPLE-CALENDAR-COLOR`, `VTIMEZONE` complet | allenporter/ical, `tests/examples/testdata/apple_ical.ics` — Apache-2.0 |
| `google.ics` | Google Agenda | `RRULE`, `X-WR-TIMEZONE`, `VTIMEZONE` Berlin | ical-org/ical.net, `Ical.Net.Tests/Calendars/Serialization/Google1.ics` — MIT |
| `google-alarm.ics` | Google Agenda | `VALARM` avec `ACKNOWLEDGED`, `ATTENDEE`, `RRULE` | collective/icalendar, `src/icalendar/tests/calendars/alarm_google_acknowledged.ics` — BSD-2 |
| `thunderbird.ics` | Thunderbird (Mozilla Calendar V1.1, 2024) | `VALARM` reporté (`X-MOZ-SNOOZE-TIME`, `X-MOZ-LASTACK`), `RDATE`, `X-TZINFO`, `VTIMEZONE` Londres | collective/icalendar, `…/alarm_thunderbird_snoozed_until_1457.ics` — BSD-2 |
| `outlook-exchange.ics` | Exchange Server 2010 | `TZID=Eastern Standard Time` (nom Windows, palier 1 de la décision 4) | collective/icalendar, `…/issue_836_do_not_quote_tzid.ics` — BSD-2 |
| `outlook-2003.ics` | Outlook 11 | `TZID` localisé « Canberra, Melbourne, Sydney » avec son `VTIMEZONE` maison (palier 2), `X-MICROSOFT-CDO-*`, `VALARM` | ical4j/ical4j, `src/test/resources/samples/valid/Standup.ics` — BSD-3 |
| `nextcloud.ics` | Nextcloud Calendar 5.3.2 | journée entière, `RRULE`, `VALARM` | ical4j/ical4j, `…/maritz.ics` — BSD-3 |
| `etar-android.ics` | Etar / iCal Import-Export (Android) | `VALARM`, `RDATE`, `RRULE` | collective/icalendar, `…/alarm_etar_notification.ics` — BSD-2 |

**Ce que le corpus ne couvre pas encore** : un `VEVENT` à `RECURRENCE-ID` écrit par un client
(« cette occurrence seulement »), et un export iPhone récent. Les tests du composeur en fabriquent ;
un vrai fichier — export d'un iPhone ou de DAVx⁵ après une occurrence déplacée — se dépose ici,
sous son nom de producteur, et les tests corpus le prennent sans changer de code. Remplacer titres,
lieux et adresses avant de le déposer ; garder toutes les autres lignes.
