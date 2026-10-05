# Migrations de schéma automatiques — conception

Date : 2026-10-05 · Branche : `schema-migrations`

## Le but

Deuxième des trois lots qui mènent à une image Docker publique du webmail (plateforme `generic`) :

1. configuration lue au démarrage (livré, #107) ;
2. **migrations de schéma automatiques** — ce document ;
3. image, `docker-compose.yml` et workflow GHCR.

Plus personne n'exécute de SQL à la main pour faire évoluer le schéma de `scotty_webmail` : une base
vide reçoit tout le schéma, une base existante ce qui lui manque. Le mécanisme sert partout — image
Docker, installation classique et serveur du propriétaire —, pour qu'une évolution de schéma
n'existe qu'une fois.

Critères de réussite :

- une base vide atteint le schéma complet sans autre script que la création de la base et des
  comptes ;
- l'API refuse de démarrer sur une base en retard, en nommant les migrations manquantes, au lieu
  d'échouer plus tard sur une colonne inconnue ;
- le compte utilisé pour servir les requêtes garde `SELECT, INSERT, UPDATE, DELETE` et rien d'autre ;
- le serveur du propriétaire ne détient jamais le mot de passe du compte qui modifie le schéma :
  `deploy.yml` le tient d'un secret GitHub, le temps d'une commande.

Hors champ : la base `dovecot` (plateforme weesky), dont le schéma n'est jamais modifié par ce
projet ; l'image Docker et son compose (lot 3).

## L'état de départ

- `install/install.sql` crée la base, le compte du service et les 27 tables (son en-tête en annonce
  26 : `app_logo` est arrivée depuis). Il ne vaut que pour une installation neuve : relancé sur une
  base existante, il s'arrête sur « Multiple primary key defined ».
- Chaque évolution postérieure est une fiche `docs/operations/…` à rejouer à la main sur chaque base
  avant de déployer : `birthdays-calendar-migration.md` (4 colonnes de `calendars`),
  `app-logo-migration.md` (table `app_logo`).
- Aucune trace dans la base des évolutions déjà appliquées.
- Le compte `scotty_webmail` n'a que `SELECT, INSERT, UPDATE, DELETE` : « the service never migrates
  its own schema » (`install.sql`, section 2).
- La suite backend tourne sur EF Core InMemory : aucun test n'exécute de SQL.
- `deploy.yml` (job `deploy`, environnements GitHub `prod` et `dev`) dépose la publication de l'API
  par SSH puis lance `systemctl restart`.

## Les décisions

### 1. DbUp et des scripts SQL numérotés

Les migrations sont des fichiers `.sql` écrits à la main, intégrés comme ressources dans
`scotty.microservice.core` (dossier `Migrations/`), nommés `NNNN_nom.sql` et appliqués dans l'ordre de
leur nom par DbUp (`dbup-mysql`, MIT, sur MySqlConnector — le pilote que Pomelo utilise déjà).

- Journal : la table `schema_migrations` de la base désignée par la chaîne de connexion, au format
  que DbUp crée. Aucun script ne nomme la base (`USE`, `scotty_webmail.`) : c'est la chaîne qui la
  choisit.
- `0001_initial.sql` reprend les 27 tables d'`install.sql` telles quelles — section 3 et ses
  `ALTER TABLE` de clés —, sans la base, le compte ni la vérification.
- La substitution de variables de DbUp (`$nom$`) est désactivée : un script SQL peut contenir `$`.
- Pas de retour arrière : défaire une migration, c'est en écrire une nouvelle.

### 2. Deux comptes, deux chaînes de connexion

| Compte | Droits | Chaîne | Sert à |
|---|---|---|---|
| `scotty_webmail` | `SELECT, INSERT, UPDATE, DELETE` | `ConnectionStrings__WebmailPreferencesDatabase` | servir les requêtes, comme aujourd'hui |
| `scotty_webmail_schema` | les mêmes, plus `CREATE, ALTER, DROP, INDEX, REFERENCES` | `ConnectionStrings__WebmailSchema` | appliquer les migrations, puis rien d'autre |

`install.sql` ne crée plus que la base et ces deux comptes ; les tables viennent de `0001`. Le schéma
n'est plus écrit qu'à un endroit.

### 3. Deux façons d'appliquer, un seul code

**Au démarrage de l'API**, avant l'enregistrement des services qui lisent la base :

| `ConnectionStrings__WebmailSchema` | Base à jour | Base en retard |
|---|---|---|
| renseignée | démarre | applique les migrations manquantes, ferme cette connexion, démarre |
| absente ou vide | démarre | refuse de démarrer : « Schema is behind: 0002_x.sql, 0003_y.sql not applied. Run `scotty.microservice migrate`, or set ConnectionStrings__WebmailSchema. » |

La vérification « à jour » lit `schema_migrations` avec le compte du service (`SELECT`). Une base
sans `schema_migrations` est une base où rien n'a été appliqué : toutes les migrations manquent.

**`scotty.microservice migrate`** applique les migrations manquantes et s'arrête, sans démarrer le
service. Il ne lit que la chaîne de schéma : la variable `ConnectionStrings__WebmailSchema`, ou,
quand elle est absente, la première ligne de son entrée standard — `sudo` vide l'environnement, et
une chaîne sur la ligne de commande se lirait dans `ps`. Ni `Platform`, ni CORS, ni clé, ni le reste
des réglages. Code de sortie 0 si la base est à jour à la
fin, 1 sinon, l'erreur sur la sortie d'erreur.

Les deux chemins appellent le même code ; seuls l'entrée et ce qui suit diffèrent.

### 4. Robustesse

- **Deux migrations simultanées** (deux instances démarrant ensemble) : le moteur prend
  `GET_LOCK('scotty_webmail_schema', 60)` avant de lire le journal et le relâche après. La seconde
  attend, puis ne trouve plus rien à faire. Le verrou disparaît avec la connexion si le processus
  meurt. Délai dépassé : refus de démarrer, en le disant.
- **Une migration qui échoue** : MySQL et MariaDB valident chaque `CREATE`/`ALTER` à part, rien ne
  s'annule. DbUp n'inscrit pas le script ; l'API (ou `migrate`) s'arrête en citant le fichier et
  l'erreur SQL. D'où la règle d'écriture : **une modification de structure par fichier**, pour qu'un
  échec laisse la base intacte ou complète, jamais à moitié. `ADD COLUMN IF NOT EXISTS` n'est pas une
  issue : MySQL 8, déclaré compatible, ne le connaît pas.
- **Une migration livrée n'est jamais modifiée** : DbUp ne relit pas un script inscrit, une
  correction n'atteindrait aucune base existante. Un test fige la forme des noms (`NNNN_nom.sql`,
  numérotation sans trou ni doublon).
- **Ajouter d'abord, retirer ensuite** : sur le serveur du propriétaire, l'ancienne version tourne
  quelques secondes sur le nouveau schéma (migrer, puis redémarrer). Une migration ajoute ; une
  suppression de colonne attend la version suivante, quand plus aucun code ne la lit.
- **Journal** : chaque script appliqué est noté (« Schema migration 0002_x.sql applied ») ; le refus
  de démarrer liste tous les manquants.

### 5. Le serveur du propriétaire

- `deploy.yml`, job `deploy` : entre le dépôt des fichiers de l'API et `systemctl restart`, un second
  appel SSH lance `sudo <chemin>/scotty.microservice migrate` (le binaire est `760 root:<deploy>`,
  comme le reste du job, qui passe déjà par `sudo`). La chaîne vient du secret d'environnement
  `WEBMAIL_SCHEMA_CONNECTION` (`prod` et `dev`) et passe par l'entrée standard, jamais par la ligne
  de commande, pour qu'aucun `ps` ne la voie. Si les droits `sudo` du compte de déploiement sont
  restreints à une liste de commandes, cette commande y est ajoutée (mise en route). Un échec arrête le job avant le redémarrage : l'ancienne
  version continue de tourner.
- Le fichier de réglages du serveur ne reçoit pas `ConnectionStrings__WebmailSchema` : sur ce
  serveur, seul `deploy.yml` migre, et l'API ne fait que vérifier.
- **Mise en route, une fois, sur la prod et `dev`** (rubrique « Before deploying » de la PR) : un
  script SQL qui crée `scotty_webmail_schema` et ses droits, crée `schema_migrations` au format DbUp
  et y inscrit `0001` sous le nom exact que DbUp lui donne ; puis les deux secrets GitHub. Le
  propriétaire étant le seul utilisateur du webmail, il n'y a pas d'adoption automatique des bases
  existantes. Le script est vérifié contre une vraie base pendant l'implémentation.

## Documentation

- `install/install.sql` : la base et les deux comptes ; l'en-tête dit que les tables viennent du
  premier démarrage.
- `install/README.md` : étape 2 (deux comptes, deux mots de passe), étape 3.3
  (`ConnectionStrings__WebmailSchema`), « Updating » (plus rien à faire pour le schéma), tableau
  « When the service won't start » (base en retard, migration en échec, verrou non obtenu).
- `install/scotty.microservice.env` : la ligne `ConnectionStrings__WebmailSchema`.
- `docs/operations/birthdays-calendar-migration.md` et `app-logo-migration.md` : marquées historiques,
  remplacées par les migrations.
- `src/scotty.microservice/CLAUDE.md` : les règles d'écriture des migrations (section 4) et la fin de
  « the service never migrates its own schema ».

## Tests

Sur un vrai MariaDB lancé par Testcontainers. Sur le runner Linux de la CI, Docker est là ; sur un
poste sans Docker, ces tests sont sautés et le disent.

- `0001` sur une base vide crée les 27 tables ; un second passage n'applique rien ;
- le schéma créé correspond au modèle EF : chaque entité de `PreferencesDbContext` trouve sa table et
  ses colonnes ;
- sans chaîne de schéma, une base en retard refuse le démarrage en nommant les scripts manquants ;
  une base à jour démarre ;
- deux migrations lancées ensemble appliquent chaque script une fois ;
- un script en échec n'est pas inscrit et l'erreur cite le fichier ;
- `migrate` : code de sortie 0 sur une base à jour, 1 sans chaîne ou en échec ;
- la forme des noms de fichiers (sans base de données).

Le reste de la logique (choix entre migrer, vérifier ou refuser) est aussi testé sans base.

## Versions

`src/scotty.microservice/VERSION` monte d'une version mineure. Le front ne change pas.
