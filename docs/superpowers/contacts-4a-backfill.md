# Rattrapage des contacts — tranche 4a (vCard)

Opération d'exploitation, à jouer **une fois** par déploiement. Elle donne une carte vCard, un
`card_hash` et une projection à chaque contact enregistré **avant** la tranche 4a. Sans elle, ces
fiches restent avec `card_hash = ''` : aucun écran ne casse, mais elles n'ont pas de carte à
synchroniser et le modèle de la tranche est incomplet.

Elle est **idempotente** : la rejouer ne casse rien et ne refait rien.

---

## 1. Quand

Dans cet ordre, sans en sauter une étape :

1. Les tables et colonnes de la tranche 4a sont créées
   (`docs/superpowers/webmail-contacts-tables.md` et ses avenants).
2. Le backend qui les connaît est **déployé et démarré**.
3. Alors seulement, le rattrapage.

Un backend qui projette avant que les tables n'existent tombe à la première écriture. Inversement,
lancer le rattrapage contre l'ancien backend ne fait rien du tout : la route n'existe pas encore.

À jouer sur chaque environnement qui porte des contacts antérieurs à 4a — donc `prod` **et** `dev`
si les deux en ont.

---

## 2. Comment

La route est `POST /api/Contacts/Backfill`, réservée aux **administrateurs**
(policy `Admin`, la même que `PUT /api/AppSettings`). Un compte non-admin reçoit un `403`.

Elle travaille **par lots** et balaye **tous les utilisateurs** : c'est un geste d'exploitant sur
toute la table, pas sur un carnet. Chaque appel répond :

```json
{ "processed": 200, "remaining": 1340 }
```

- `processed` — fiches converties par **cet** appel ;
- `remaining` — fiches restant à convertir **après** cet appel.

Taille du lot : `?batchSize=N`, ramenée dans `1..1000` si la valeur sort des bornes. **Passer
`batchSize=500`** : le défaut est 200, prudent, mais la sélection balaye la table à chaque appel,
donc moins d'appels coûte moins cher. Les exemples ci-dessous le passent explicitement.

### Ouvrir une session

```bash
BASE=https://mail.weesky.net          # ou l'URL de l'environnement visé
JAR=$(mktemp)

curl -s -c "$JAR" -X POST "$BASE/api/Login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@weesky.be","password":"…"}'
```

### La boucle

Relancer tant que `remaining > 0` :

```bash
while :; do
  OUT=$(curl -s -b "$JAR" -X POST "$BASE/api/Contacts/Backfill?batchSize=500")
  echo "$OUT"
  PROCESSED=$(echo "$OUT" | grep -o '"processed":[0-9]*' | cut -d: -f2)
  REMAINING=$(echo "$OUT" | grep -o '"remaining":[0-9]*'  | cut -d: -f2)
  [ "$REMAINING" = "0" ] && { echo "Terminé."; break; }
  [ "$PROCESSED" = "0" ] && { echo "Bloqué : voir § 4."; break; }
done
```

La deuxième garde compte. `remaining > 0` avec `processed = 0` veut dire que la tête de la file
est refusée en boucle ; sans cette garde la boucle tourne indéfiniment.

Un dernier appel après la fin répond `{ "processed": 0, "remaining": 0 }` — c'est le contrôle que
tout est passé.

---

## 3. Journalisation

Chaque appel écrit une ligne dans le log applicatif (Serilog, niveau `Information`) :

```
Contacts backfill: 500 processed, 1340 remaining
```

Un `processed` inférieur au `batchSize` demandé avec un `remaining` non nul signale des fiches
refusées (voir § 4). Une opération silencieuse dont personne ne sait si elle a fini est une
opération qu'on rejoue dans le doute : cette ligne existe pour ne pas avoir à le faire.

---

## 4. Une fiche refusée

Le seul refus possible est une carte dépassant le plafond de **1 Mo** (typiquement une `PHOTO`
énorme sur une carte importée avant la tranche). La fiche est laissée **intacte** — `vcard_raw`
octet pour octet, `card_hash` toujours vide — donc elle revient dans le lot suivant,
indéfiniment. C'est ce que la garde `processed == 0` de la boucle du § 2 arrête.

### La décision, d'abord

**Une fiche refusée peut être laissée telle quelle.** C'est l'état exact dans lequel toutes les
fiches étaient avant ce rattrapage : aucun écran ne casse, la fiche s'affiche, se lit, s'édite et
s'exporte comme avant. Elle n'aura simplement pas de carte à synchroniser le jour où CardDAV
arrivera. Deux ou trois fiches dans ce cas ne justifient **pas** de rester debout à 3 h du matin :
noter leurs `id`, arrêter la boucle, et traiter ça un jour ouvré.

Les repérer :

```sql
SELECT id, user_id, LENGTH(vcard_raw) AS octets
FROM contacts
WHERE card_hash = ''
ORDER BY octets DESC;
```

`LENGTH(vcard_raw)` classe, il ne décide pas : le plafond est mesuré sur la carte **recomposée**,
après insertion de l'`UID`, ajout du `REV` et repliement des lignes longues — quelques dizaines de
kilo-octets de plus que ce que la colonne affiche. Une fiche à 1 020 000 octets en base peut donc
être refusée. Ce que la requête donne à coup sûr, c'est l'ordre : les coupables sont en tête.

### Si l'on veut vraiment la convertir

Un `vcard_raw` de 1 Mo ne s'édite pas à la main, et surtout pas en SQL : la valeur est une carte
vCard entière, sur une seule colonne, dont la ligne `PHOTO` fait à elle seule 99 % du poids. Deux
voies, dans l'ordre de préférence :

1. **Par l'application, sans rien toucher en base.** Exporter la fiche (`GET /api/Contacts/Export`
   pour le carnet de l'utilisateur), la ré-enregistrer depuis l'éditeur de contacts du webmail
   sans sa photo : l'édition passe par le composeur, qui réécrit `vcard_raw` **et** pose le hash.
   La fiche sort de la file par la porte normale, et le rattrapage ne la reverra plus.
2. **Par un script**, si la fiche appartient à un utilisateur qu'on ne peut pas solliciter :
   retirer la propriété `PHOTO` de `vcard_raw` (elle va de `PHOTO;` jusqu'à la première ligne
   suivante ne commençant **pas** par une espace ou une tabulation — c'est une valeur repliée),
   laisser `card_hash` vide, puis relancer la boucle du § 2. Sauvegarder la ligne avant.

Ne **jamais** « débloquer » une fiche en lui posant un `card_hash` bidon : ce serait la sortir de
la file sans lui avoir donné de carte, c'est-à-dire perdre l'information qu'elle reste à faire.

---

## 5. Rejouer après un correctif du moteur

La file de travail est la colonne `card_hash` : une fiche dont le hash est vide est à traiter, une
fiche dont le hash est posé ne sera **jamais** revisitée. Pour refaire passer un périmètre, il
suffit donc de le remettre à vide, puis de rejouer la boucle du § 2.

Toujours **sauvegarder la table avant** — la reprise réécrit `vcard_raw` et reconstruit les quatre
tables filles.

```sql
-- Toute la table
UPDATE contacts SET card_hash = '';

-- Un seul utilisateur
UPDATE contacts SET card_hash = '' WHERE user_id = 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx';

-- Une seule fiche
UPDATE contacts SET card_hash = '' WHERE id = 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx';
```

Ne remettre à vide **que** ce que le correctif concerne : sur une fiche déjà éditée depuis le
rattrapage, une reprise repasse par la réconciliation et refera le bon travail, mais elle
réécrira aussi `updated_at`.

---

## 6. Ce que l'opération fait, et ce qu'elle ne fait pas

Pour chaque fiche de la file, dans cet ordre :

1. **Fiche sans carte** — une carte neuve est composée depuis les colonnes.
2. **Fiche avec carte** — la carte est **réconciliée**, pas reprojetée : les colonnes actuelles
   reposent `N`, `FN`, `NICKNAME` et le bloc `EMAIL`, **et rien d'autre**. Tous les `TEL`, `ADR`,
   `ORG`, `BDAY`, `NOTE`, `PHOTO` et propriétés hors modèle que seule la carte portait sont
   conservés tels quels. Dans le `N`, seuls le prénom et le nom sont reposés : le nom d'usage et
   l'honorifique que la carte portait en composantes 3 et 4 survivent, et le `FN` est recalculé
   **en les comptant** — `Jean Pierre Dupont` reste `Jean Pierre Dupont`, il ne devient pas
   `Jean Dupont`.
3. Un `UID` est ajouté à la carte s'il en manquait un (les cartes d'avant 4a n'en ont pas).
4. Le `card_hash` est calculé, et la carte est **projetée** dans les colonnes et les tables filles.

L'ordre est le point important. Les fiches importées **puis éditées** portent une carte dont le
nom et l'adresse sont périmés : projeter cette carte-là d'abord réécrirait les colonnes avec le
vieux nom et la vieille adresse, c'est-à-dire effacerait l'édition de l'utilisateur. C'est pour
cela que l'opération réconcilie avant de projeter, et que la réconciliation est **bornée** aux
quatre champs qui ont pu dériver.

Ce qu'elle ne fait pas : elle ne crée, ne fusionne et ne supprime **aucune** fiche, ne touche pas
`uid`, ni `source`, ni `is_favorite`. Elle écrit `updated_at` sur les fiches converties — la carte
a bel et bien changé.
