# Webmail weesky — Tranche 2a.5 : Dossiers systèmes

**Date :** 2026-07-19
**Statut :** design validé, prêt pour la planification d'implémentation
**Dépend de :** tranche 2a (dossiers & lecture), branche `webmail`
**Précède :** tranche 2b — dont les actions écrivent en se fondant sur ces rôles

---

## 1. Pourquoi maintenant

Aujourd'hui, l'affectation d'un rôle à un dossier (`sent`, `drafts`, `trash`, `junk`,
`archive`) est **devinée**. La tranche 2a résout `SPECIAL-USE` quand le serveur l'annonce, et
retombe sinon sur une correspondance par nom.

Le sondage du serveur maison a montré que cette devinette est fragile :

- `Archive` ne porte **aucun** flag `SPECIAL-USE` et n'est reconnu que par son nom.
- La boîte porte **deux jeux** de dossiers spéciaux, anglais et français : `Drafts` *et*
  `Brouillons`, `Junk E-mail` *et* `Courrier indésirable`. Aucun nom français n'est flaggé — ils
  ont été créés par un client, pas provisionnés par le serveur.
- `ResolveSpecialUses` attribue donc chaque rôle au premier dossier rencontré dans l'ordre de
  tri, ce qui donne `Drafts` plutôt que `Brouillons` — **par hasard, pas par choix**.

En 2a, tout est en lecture seule : un rôle faux ne coûte qu'un tri discutable. **Dès 2b, un
rôle faux devient un problème de données** — supprimer envoie le message dans le mauvais
dossier, archiver aussi ; en 2c les messages envoyés atterrissent au mauvais endroit.

C'est pourquoi cette tranche précède 2b : 2b est la première à écrire en se fondant sur les
rôles, et elle ne doit pas être construite sur une devinette.

Deuxième motif, indépendant : **le site sera multilingue à terme.** Le rôle est la clé
canonique indépendante de la langue ; sans lui, un utilisateur en interface française verrait
« Deleted Items », une chaîne anglaise qu'il n'a même pas choisie.

---

## 2. Périmètre

**Dans le périmètre**

- Stockage persistant des surcharges utilisateur, par compte, dans une base à nous
- Chaîne de résolution ordonnée à trois maillons (§ 4.1)
- Détection de péremption d'une surcharge (renommage, suppression, réutilisation de chemin)
- Maintien des surcharges lors de nos propres renommages et suppressions de dossiers
- Endpoints de lecture et d'écriture des surcharges
- Page Settings de configuration, portée par le compte actif
- Affichage du libellé de rôle dans l'arborescence, avec la couture d'internationalisation

**Hors périmètre**

- **Défaut de domaine posé par un administrateur** — évalué et écarté, voir § 2.1
- Internationalisation elle-même : la couture est posée, le catalogue viendra avec l'i18n
- Toute action de message fondée sur ces rôles (supprimer, archiver, déplacer) — 2b
- Comptes liés externes — 2d, mais le schéma est déjà taillé pour eux (§ 4.2)

### 2.1 Pourquoi le défaut de domaine est écarté

Un niveau intermédiaire — un administrateur fixant un défaut pour tous les comptes d'un domaine
— a été évalué et rejeté.

**Il ne peut pas être présenté correctement.** Pour choisir un dossier il faut une liste de
dossiers ; l'administrateur n'a de session IMAP sur aucune boîte du domaine, et le master user a
été écarté en 2a. Il devrait donc saisir un chemin en texte libre, à l'aveugle.

**Il aide la mauvaise population.** Un compte fraîchement provisionné reçoit des dossiers
flaggés `SPECIAL-USE` ; les niveaux 3 et 4 lui donnent déjà cinq bonnes réponses sur cinq. Les
utilisateurs qui ont besoin d'une correction sont ceux dont la boîte porte un héritage
désordonné — et ce désordre est individuel, donc hors de portée d'un défaut de domaine.

**Le différer coûte presque rien.** La chaîne de résolution étant écrite comme une séquence
ordonnée explicite (§ 4.1), l'insérer plus tard consiste à ajouter un maillon : aucune donnée à
migrer, aucune signature à changer. Si une migration de masse le réclame un jour, il sera ajouté
avec le cas d'usage réel sous les yeux plutôt qu'imaginé.

Le niveau 2 reste **numéroté et vacant** dans la chaîne, pour que sa place soit évidente.

---

## 3. Décisions de conception validées

| Sujet | Décision |
|---|---|
| Granularité | **Par compte**, jamais par utilisateur ni par serveur — les dossiers sont une propriété de la boîte |
| Stockage | **Base MySQL séparée**, sur le même serveur que `dovecot` |
| Identifiant stocké | **Le chemin, toujours** ; `OBJECTID` seulement en appoint facultatif |
| Défaut de domaine | Écarté (§ 2.1) ; sa place reste réservée dans la chaîne |
| `METADATA` IMAP | Écarté — un serveur externe qui ne la supporte pas nous laisserait sans stockage |
| Configuration serveur | **Aucune.** Rien dans `appsettings.json` : un rôle n'est pas une propriété du serveur |
| Emplacement UI | **Settings**, pas le module mail — la visibilité est de l'entretien, le rôle est de la configuration |
| Libellé | Le nom du rôle **remplace** le nom du dossier dans l'arbre ; le nom réel reste en `title` |
| Repli d'une surcharge périmée | **Signalé** dans Settings, jamais par une interruption pendant la lecture |

### 3.1 Ce que `appsettings.json` ne recevra pas

La configuration des dossiers systèmes du serveur maison dans `appsettings.json` a été
envisagée puis écartée. La raison est concrète, pas doctrinale : **les rôles sont une propriété
de la boîte, pas du serveur.**

La boîte de l'utilisateur principal porte `Drafts` et `Brouillons` ; le compte
`webmail-test@weesky.be`, créé récemment, ne porte que ce que le serveur provisionne. Deux
boîtes, le même serveur, deux jeux de dossiers. Une configuration au niveau serveur serait juste
pour l'une et fausse pour l'autre, et le jour où un troisième utilisateur crée `Papierkorb` il
faudrait éditer un fichier de déploiement et redémarrer.

Accessoirement, elle n'apporterait rien : le serveur annonce déjà `SPECIAL-USE` pour Sent,
Drafts, Junk, Trash et Inbox, et le repli par nom récupère `Archive`. Et elle créerait une
troisième source de vérité, avec une règle d'arbitrage à écrire et à documenter.

---

## 4. Architecture backend

### 4.1 La chaîne de résolution

Une **séquence ordonnée explicite**, chaque maillon ne comblant que ce que le précédent n'a pas
fourni :

| Niveau | Source | Portée |
|---|---|---|
| **1** | Surcharge utilisateur | par compte |
| *2* | *(vacant — défaut de domaine, § 2.1)* | *par domaine* |
| **3** | Flags `SPECIAL-USE` annoncés par le serveur | par boîte |
| **4** | Correspondance par nom, multilingue | par boîte |

La forme importe autant que le contenu : une séquence de sources interrogées en boucle, **pas
une pile de `if` imbriqués**. C'est ce qui rend l'insertion du niveau 2 gratuite, et ce qui rend
la précédence testable maillon par maillon.

`ResolveSpecialUses` (2a) devient l'implémentation des niveaux 3 et 4 et garde sa contrainte
d'unicité : un rôle n'est attribué qu'à un seul dossier, les flags serveur réclamant avant les
devinettes par nom.

**Un rôle rempli par un maillon n'est plus disponible pour les suivants.** Si l'utilisateur
affecte `drafts` à `Brouillons`, alors `Drafts` — qui porte pourtant le flag serveur — réclame
`drafts` au niveau 3, le trouve pris, et **reste sans rôle**. C'est le résultat voulu : il
s'affichera sous son propre nom, comme un dossier ordinaire. Cette conséquence tombe de la forme
en séquence ; elle est notée ici parce qu'une implémentation en `if` imbriqués la manquerait.

### 4.2 Séparation des responsabilités

Trois unités, chacune testable seule :

| Unité | Responsabilité | Ce qu'elle ignore |
|---|---|---|
| `ImapSession` | Découverte seule — niveaux 3 et 4 | la base de données |
| `IFolderRoleStore` | Lecture/écriture des surcharges | IMAP |
| `FolderRoleResolver` | Applique la chaîne, détient les règles de péremption | le transport HTTP |

`ImapSession` **ne doit pas** acquérir de connaissance de la base : elle reste testable sans
elle, et c'est ce qui garde la découverte pure. Le contrôleur appelle la session, puis le
résolveur.

Conséquence sur l'existant : `GET /api/Mail/Folders` renvoie déjà `SpecialUse` par nœud. Cette
valeur devient la sortie de la chaîne complète au lieu de la seule découverte. C'est le principal
point d'intégration.

### 4.3 Stockage

**Base MySQL séparée** sur le même serveur que `dovecot`, avec sa propre chaîne de connexion et
son propre `DbContext`.

Le schéma `dovecot` appartient à Dovecot : il peut être reconstruit par le provisionnement du
serveur mail, et nos préférences partiraient avec. Les politiques de sauvegarde diffèrent
également. `last_login`, déjà présente, est une table du plugin Dovecot — pas un précédent pour
y poser les nôtres.

```sql
CREATE TABLE folder_role_overrides (
  account_id    VARCHAR(255) NOT NULL,
  role          VARCHAR(16)  NOT NULL,
  folder_path   VARCHAR(1024) NOT NULL,
  uid_validity  BIGINT       NOT NULL,
  mailbox_id    VARCHAR(255) NULL,
  PRIMARY KEY (account_id, role)
);
```

**`account_id`, jamais `user_id`.** En 2d, un utilisateur aura N comptes liés, chacun avec ses
propres dossiers ; une table indexée par utilisateur devrait être migrée. C'est la même décision
que celle prise pour les clés TanStack Query en 2a, pour la même raison. Aujourd'hui la valeur
est l'adresse de la boîte (`user@domain`), ce qui suit la convention de `last_login` ; 2d y
mettra des identifiants générés pour les comptes externes, et la colonne est assez large.

**`role` est un enum stable** : `trash`, jamais `corbeille`. Le libellé est une affaire
d'affichage (§ 5.2).

**Absence de ligne = repli sur la découverte.** La surcharge est une couche de correction, pas
un remplacement. Un compte neuf aux flags propres n'a besoin d'aucune ligne.

**Il n'y a pas de migrations EF dans ce projet** — `ApplicationDbContext` mappe un schéma
existant, géré hors EF. La création de la base et de la table est donc un **prérequis serveur
manuel** (§ 7), au même titre que `StateDirectory=`.

### 4.4 Identité d'un dossier et péremption

Un chemin IMAP est mutable. Cinq façons de casser le lien :

| # | Cas | Détection |
|---|---|---|
| 1 | Renommage par notre UI | prévenu : on met à jour (§ 4.5) |
| 2 | Renommage par un autre client | chemin absent de l'arbre |
| 3 | Renommage d'un **parent** | chemin absent — le sous-arbre entier a bougé |
| 4 | Suppression | chemin absent |
| 5 | **Réutilisation du chemin** | `uid_validity` différente |

Les quatre premiers dégradent ; **le cinquième ment**. Renommer `Trash` en `Old Trash` puis
créer un nouveau `Trash` fait pointer le chemin stocké vers un dossier physiquement différent,
sans erreur. C'est ce cas qui condamne le stockage du chemin seul.

**Résolution, dans l'ordre :**

1. `mailbox_id` renseigné **et** la session annonce `OBJECTID` (RFC 8474) → résoudre par
   l'identifiant. Immunisé aux renommages, internes comme externes.
2. Sinon → résoudre par le chemin, sous deux contrôles : il existe dans l'arbre déjà récupéré,
   et l'`uid_validity` du dossier correspond à celle stockée.
3. Échec de l'un ou l'autre → traiter la surcharge comme **absente** et descendre dans la chaîne.

Le contrôle d'`uid_validity` n'est pas infaillible : un serveur peut légitimement la changer lors
d'une maintenance, et une surcharge valide serait alors abandonnée. Le mode d'échec devient
« repli sur la découverte » au lieu de « lié au mauvais dossier » — **dégrader plutôt que
mentir**.

**`OBJECTID` est un appoint, jamais la clé.** Le chemin est toujours écrit, parce qu'il est le
seul identifiant qu'IMAP garantit sur tout serveur. Faire de `mailbox_id` la clé stockée
signifierait que la table contient deux natures de clé selon le serveur — le schéma persisté
encoderait alors un fait sur le serveur, ce que § 5.1 de la spec 2a interdit. Un serveur qui
gagne la capacité la voit renseignée au prochain réglage ; un serveur qui la perd retombe sur le
chemin qu'on avait de toute façon.

### 4.5 Maintien lors de nos propres opérations

**Renommage.** Mettre à jour le chemin exact **et tout chemin préfixé par
`ancien + séparateur`** — un renommage de parent déplace tout le sous-arbre et peut invalider
plusieurs surcharges d'un coup. En une transaction.

> **Le séparateur vient de la session**, jamais d'une constante. Il vaut `.` sur le serveur
> maison et `/` ailleurs. Écrire `oldPath + "/"` casserait silencieusement en production, et un
> test écrit contre un seul séparateur ne l'attraperait pas. Les tests doivent couvrir les deux.

**Relire l'`uid_validity`** du dossier renommé plutôt que reporter l'ancienne : certains serveurs
la modifient lors d'un renommage, et reporter l'ancienne ferait **déclencher notre propre
garde-fou par notre propre renommage**.

**Suppression.** Purger les surcharges du chemin et de son sous-arbre.

**Ordre d'écriture : IMAP d'abord, base ensuite.** Si l'écriture en base échoue après un
renommage IMAP réussi, la surcharge pointe vers un chemin disparu, la vérification à la lecture
l'attrape, et on retombe sur la découverte. Dégradé, jamais faux. IMAP est la source de vérité et
l'opération que l'utilisateur a réellement demandée ; si elle échoue, rien ne doit avoir bougé.

### 4.6 Endpoints

Sous `MailController`, le regroupement suivant le domaine et non l'emplacement de l'UI.

| Verbe | Route | Rôle |
|---|---|---|
| `GET` | `/api/Mail/FolderRoles` | rôle → chemin résolu, **avec la provenance** (niveau 1, 3 ou 4) et l'indication d'une surcharge périmée |
| `PUT` | `/api/Mail/FolderRoles` | pose une surcharge — corps `{ role, folderPath }` |
| `DELETE` | `/api/Mail/FolderRoles?role=` | efface la surcharge, retour à la découverte |

Les chemins de dossier voyagent en query string ou en corps de requête, **jamais en segment de
route** — ils peuvent contenir le séparateur (règle 2 de la spec 2a).

La provenance est nécessaire à l'UI : c'est elle qui permet d'afficher « défini par le serveur »
plutôt que de faire croire à un choix de l'utilisateur, et de signaler un repli (§ 5.3).

### 4.7 Unicité

**Un dossier ne peut porter qu'un seul rôle.** Deux rôles sur le même dossier rendraient
l'affichage indécidable — quel libellé montrer.

- Le `<select>` d'un rôle exclut les dossiers portant **une autre surcharge**, et eux seuls.
  Un dossier dont le rôle vient de la découverte n'est **pas** exclu : la découverte est
  précisément ce que l'utilisateur est en train de corriger, et l'exclure lui interdirait
  d'affecter `Drafts` à la corbeille alors que c'est un choix légitime. Le conflit se résout
  seul — le dossier prend la surcharge au niveau 1 et cesse de réclamer son rôle découvert au
  niveau 3 (§ 4.1).
- Le backend rejette un doublon de surcharge en filet de sécurité, avec un message explicite.

**« Non défini » est une valeur légitime.** Une boîte peut n'avoir aucun dossier d'archive ;
forcer un choix inventerait une donnée. Le `<select>` porte donc une option vide, qui efface la
surcharge et redonne la main à la découverte.

---

## 5. Frontend

### 5.1 Page Settings

Une page dédiée, alimentée par `activeAccount`, dans le langage `.field-h` du module
Administration (une ligne par rôle : libellé à gauche, `<select>` à droite).

Les cinq `<select>` sont peuplés depuis l'arbre du compte, déjà en cache TanStack Query. Deux
requêtes, toutes deux mises en cache : l'arbre pour les options, les rôles pour l'état courant.

**Portée par le compte dès maintenant.** En 2d la page gagne un sélecteur de compte ou se replie
sous « Linked accounts » ; la logique ne bouge pas.

Un lien depuis la popup de gestion des dossiers du module mail vers cette page : l'utilisateur
qui y voit `Brouillons` à côté de `Drafts` est précisément celui qui veut corriger le rôle.

### 5.2 Libellés et internationalisation

Le libellé du rôle **remplace** le nom du dossier dans l'arborescence : `Deleted Items` s'affiche
« Trash ». C'était la motivation d'origine, et c'est ce que fait tout webmail.

Le point faible de cette approche est qu'on perd de vue quelle boîte physique on regarde. Il est
compensé à coût nul : **le nom réel reste en `title`** au survol.

**La traduction passe par une fonction dès maintenant** — `roleLabel(role)` — qui retourne
aujourd'hui une chaîne anglaise codée en dur. Quand l'i18n arrivera, seule cette fonction change.
Ce n'est pas construire deux fois, c'est poser une couture au bon endroit.

Deux pièges à ne pas ouvrir plus tard :

- **Le repli par nom reste multilingue quelle que soit la langue de l'interface.** Un utilisateur
  en interface française peut avoir une boîte anglaise, et inversement. La liste de noms reconnus
  ne doit jamais être filtrée par la langue de session.
- **On ne traduit jamais un nom de dossier à la création.** Un dossier créé depuis une interface
  française porte le nom tapé : les noms de dossiers appartiennent à la boîte, pas à la session.

### 5.3 Signalement d'un repli

Si une surcharge devient invalide, la page Settings affiche le rôle comme non défini **avec la
raison** — dossier renommé ou supprimé hors de l'application.

Pas de notification pendant la lecture. Jeter en silence un choix explicite est malhonnête ;
interrompre quelqu'un qui lit son courrier pour le lui dire l'est aussi. L'endroit où on
répare est l'endroit où on prévient.

---

## 6. Agnosticisme

C'est la contrainte structurante héritée de 2a (§ 5.1), et cette tranche la met à l'épreuve
parce qu'elle **persiste** des données au lieu de seulement les calculer.

| Règle | Application ici |
|---|---|
| Aucun fait serveur en configuration | Rien dans `appsettings.json` (§ 3.1) |
| Aucun fait serveur dans le schéma | Le chemin est toujours stocké ; `OBJECTID` n'est qu'un appoint (§ 4.4) |
| Le séparateur vient de la session | Mise à jour du sous-arbre au renommage (§ 4.5) |
| Tout se dégrade sans capacité optionnelle | La conception tient sans `OBJECTID`, sans `METADATA` |

**Un serveur dépourvu de ces capacités doit être testé.** Un développeur qui ne voit jamais que
le serveur maison finit par écrire du code qui en dépend sans s'en rendre compte. Les tests
doivent donc couvrir explicitement : pas d'`OBJECTID`, pas de `SPECIAL-USE`, séparateur `.` et
séparateur `/`.

---

## 7. Prérequis serveur

Le projet n'utilise pas les migrations EF : le schéma est géré hors EF. La création est donc
manuelle, documentée au même titre que `StateDirectory=`.

**Le script complet — bases, table, utilisateurs, `GRANT`, vérification et désinstallation — est
dans [`docs/superpowers/mail-2a5-database-prerequisite.md`](../mail-2a5-database-prerequisite.md).**

Ce qu'il pose, en résumé :

- **Deux bases**, `snoopy_webmail` et `snoopy_webmail_dev` — le développement déploie la branche
  `webmail` en continu et ne doit jamais toucher aux préférences de production
- **Deux utilisateurs MySQL dédiés**, distincts de celui qui lit `dovecot` : si l'un des jeux
  d'identifiants fuit ou tourne, l'autre n'est pas concerné
- **`GRANT` limité aux données** — `SELECT`, `INSERT`, `UPDATE`, `DELETE`, et rien d'autre.
  L'application ne migre jamais son schéma, elle n'a donc aucune raison de pouvoir le modifier ni
  de pouvoir le détruire
- **Collation `utf8mb4_bin`** : les chemins IMAP sont sensibles à la casse et se comparent octet
  à octet ; `utf8mb4` parce que les noms de dossiers portent des accents
- **Aucune clé étrangère vers `dovecot`** — une contrainte inter-bases recréerait le couplage que
  cette base sert à éviter. En contrepartie, la suppression d'un utilisateur depuis l'écran
  Administration doit purger ses lignes ici : c'est une charge applicative, à ne pas oublier

Le service doit **refuser de démarrer** si la chaîne de connexion est absente hors Development,
sur le modèle du contrôle existant pour le key ring : un échec au démarrage avec un message
nommant le correctif vaut mieux qu'une fonctionnalité silencieusement inerte.

---

## 8. Tests

Au-delà de la couverture habituelle, ces cas doivent être écrits explicitement, chacun
correspondant à un défaut identifié pendant la conception :

**Chaîne de résolution**
- Une surcharge bat un flag `SPECIAL-USE`
- Un flag `SPECIAL-USE` bat une correspondance par nom
- Sans surcharge, la découverte de 2a est inchangée
- Un rôle sans aucune source reste nul

**Péremption**
- Chemin absent de l'arbre → repli
- `uid_validity` différente → repli *(réutilisation de chemin)*
- `mailbox_id` renseigné mais session sans `OBJECTID` → repli sur le chemin
- `mailbox_id` renseigné et session avec `OBJECTID` → résolution par identifiant, malgré un
  chemin devenu faux

**Maintien**
- Renommage d'un parent → toutes les surcharges du sous-arbre suivent
- **Avec séparateur `.` et avec séparateur `/`** — les deux, dans le même jeu de tests
- `uid_validity` relue après renommage, pas reportée
- Suppression → surcharges du sous-arbre purgées
- Échec de l'écriture en base après succès IMAP → repli, jamais de rôle faux

**Unicité**
- Poser un rôle sur un dossier déjà affecté est rejeté
- « Non défini » efface la surcharge et redonne la main à la découverte

---

## 9. Ce qui change dans l'existant

| Fichier | Changement |
|---|---|
| `ImapSession.cs` | Aucun changement de responsabilité ; `ResolveSpecialUses` devient les niveaux 3 et 4 |
| `MailController.cs` | `GET /Folders` renvoie la sortie de la chaîne ; trois routes `FolderRoles` |
| `FolderTree.tsx` | Affiche le libellé du rôle, nom réel en `title` |
| `appsettings.json` | Une chaîne de connexion supplémentaire — **et rien d'autre** |
| Documentation | `CLAUDE.md` backend et frontend, prérequis serveur |

Le repli par nom multilingue introduit le 2026-07-19 reste en place : il est le niveau 4, et 2d
en dépendra pour des serveurs qu'on ne connaîtra jamais.
