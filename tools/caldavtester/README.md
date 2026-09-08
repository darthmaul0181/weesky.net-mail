# Harnais de conformité CalDAV/CardDAV

Ce harnais lance `ccs-caldavtester`, l'outil de conformité de CalendarServer,
contre un déploiement dev réel. Il existe depuis 4d (CardDAV) ; 5d y ajoute
CalDAV plutôt que d'en dupliquer le clonage, le `PYTHONPATH` et l'épuration
(« pas de doublon » vaut aussi pour l'outillage). Voir
`docs/superpowers/specs/2026-08-31-webmail-contacts-4d-conformance-design.md`
et `docs/superpowers/specs/2026-09-07-webmail-calendar-5d-conformance-design.md`
pour pourquoi ce harnais existe et comment ses résultats sont lus dans un rapport.

## Prérequis

- PowerShell 7.
- Python 2.7.18 — l'outil est du Python 2 et ne tournera sur rien d'autre.
  À installer depuis `https://www.python.org/downloads/release/python-2718/`
  (installeur MSI Windows x86-64). Vérifier avec `py -2.7 -V`.
- git.

## Le compte de test dédié

Créer un utilisateur sur dev, activer l'onglet Sync, et copier les trois
valeurs qu'il affiche (GUID du compte, email, secret DAV) dans
`serverinfo.local.json`, créé en copiant `serverinfo.local.example.json`.

**Ce n'est pas un compte personnel.** Chaque passage CalDAV crée et supprime
des agendas à sa guise, et un `DELETE` sur un agenda secondaire ne le **vide
pas** — il le **supprime** (5c décision 11), contrairement à `default` que le
purge (ci-dessous) vide au lieu de supprimer. Chaque passage CardDAV vide le
carnet d'adresses. Régénérer le secret une fois la campagne de mesure close.

## Lancer

```powershell
pwsh -File tools/caldavtester/run.ps1                             # CalDAV, toutes les suites
pwsh -File tools/caldavtester/run.ps1 -Protocol CardDAV            # CardDAV seul
pwsh -File tools/caldavtester/run.ps1 -Protocol Both                # les deux, dans cet ordre
pwsh -File tools/caldavtester/run.ps1 -Suites CalDAV/propfind.xml   # une suite précise
pwsh -File tools/caldavtester/run.ps1 -SetupOnly                    # clone + config, rien d'autre
pwsh -File tools/caldavtester/run.ps1 -PrintResponses                # rejeu verbeux d'un échec
pwsh -File tools/caldavtester/run.ps1 -NoPurge                       # voir « Le purge » plus bas
```

`-Protocol` (défaut `CalDAV`) choisit `suites-caldav.txt`, `suites-carddav.txt`,
ou les deux ; il n'a d'effet que si `-Suites` n'est pas donné, auquel cas c'est
la liste explicite qui est lancée telle quelle.

## Le purge, fait par défaut

Avant de lancer les suites CalDAV, `run.ps1` nettoie l'agenda : un `PROPFIND
Depth: 1` sur le home d'agendas, un `DELETE` sur chaque agenda secondaire, puis
un `DELETE` sur `default` — qui le **vide** plutôt que de le supprimer, parce
que `default` n'est pas un agenda comme les autres (5c décision 11). C'est un
filet contre ce que le nettoyage propre à l'outil (`end-delete`) ne couvre pas :
l'agenda `movecopy`, le contenu résiduel de `default`, et tout `<end>` qu'une
trace Python interrompue aurait empêché de jouer. Il ne touche que l'agenda :
sous `-Protocol CardDAV` il ne fait rien, et sous `-Protocol Both` le carnet
reste nettoyé par les `<start>` des suites CardDAV, qui le font déjà depuis 4d.

Le purge est **obligatoire avant chaque passage complet** : c'est pour ça que
c'est le défaut, pas une case à cocher. `-NoPurge` ne se justifie que dans un
seul cas — le diagnostic d'un test isolé qu'on veut rejouer sur l'état laissé
par le précédent. Il ne se justifie **jamais** pour un passage complet, ni pour
un rejeu dont le résultat sera consigné dans un rapport : ces deux-là partent
toujours avec le purge.

## Pourquoi les entrées de `suites/` sont résolues en chemin absolu

`_normPath` (`manager.py:322` de l'outil) ne prend un chemin « tel quel » que
s'il commence par `.` ou par `/`. Un chemin Windows `D:\…`, lui, tombe dans la
branche `os.path.join("scripts/tests", f)` — et en ressort **intact**, parce
que `ntpath.join` se réinitialise dès qu'il rencontre une lettre de lecteur.
C'est ce détail, et lui seul, qui permet aux copies locales de
`tools/caldavtester/suites/CalDAV/` (voir la spec 5d, décision 10) d'être
trouvées : une entrée relative comme `suites/CalDAV/reports.xml` serait au
contraire cherchée sous `scripts/tests/` du dépôt de l'outil, où elle n'existe
pas. `Read-SuiteList`, dans `run.ps1`, résout donc en absolu toute ligne
commençant par `suites/` avant de la passer à `testcaldav.py`. **Ne pas
« corriger » cette résolution en relatif** — ce serait revenir au chemin qui
ne fonctionne pas.

## Les copies locales de `suites/CalDAV/`

`reports.xml` et `delete.xml` posent, dans leur `<start>` amont, des `VTODO`
et un `VFREEBUSY` qu'un agenda ne sert pas — et le premier échec d'un `<start>`
tue le fichier entier, zéro test joué. `suites/CalDAV/` reçoit donc une copie
de ces deux fichiers, identique à l'amont à ceci près que ces préparatifs
impossibles sont retirés ; le diff contre le commit épinglé est versionné à
côté (`reports.xml.diff`, `delete.xml.diff`), sans aucune ligne ajoutée. Ce
n'est pas un portage de l'outil, et aucun test n'est modifié : ceux qui
dépendaient de ces préparatifs échouent ensuite un par un et nommément, c'est
la mesure. Détail dans `suites/CalDAV/README.md` (spec 5d, décision 10).

## Le plafond d'authentification d'`api-dev`

`api-dev` limite les échecs d'authentification Basic à dix par fenêtre
glissante de quinze minutes, par identifiant et par IP (`AuthAttemptThrottle`),
au-delà de quoi il répond `429`. Un passage CalDAV complet en consomme **neuf**
— du bruit multi-utilisateurs nommé dans `suites-caldav.txt`, chaque requête
portant `user="$userid2:"` qui reste en texte littéral faute d'un second
compte défini. Un passage `-Protocol Both` en ajoute un dixième
(`CardDAV/current-user-principal.xml`) : il est **exactement** au plafond.

Conséquence opératoire : **un seul passage complet par quart d'heure**. Tout
rejeu, même ciblé, lancé juste après un passage complet attend la fin de la
fenêtre — sinon tout ce qui suit tombe en échec pour une raison qui n'a rien à
voir avec CalDAV ou CardDAV.

## Sortie

Chaque passage écrit `results/<horodatage>-<protocole>.txt`. Les lignes
`Authorization` sont épurées avant que le fichier ne touche le disque ; le
secret n'est jamais journalisé ni versionné (`serverinfo.xml` et
`serverinfo.local.json` sont ignorés par git). Ce qui entre dans un rapport de
conformité vient de ce fichier, jamais de la console en direct.

## Lire les résultats

Chaque test affiche `[FAILED]` ou `[OK]`. Un fichier dont le bloc `<start>`
échoue est sauté en entier (rien en dessous n'est joué). `mkcol.xml`,
`copymove.xml` et `aclreports.xml` échouent **normalement** — voir les
divergences nommées dans la spec 5d.
