# Vérification de 4c-ii-a sur l'environnement de développement

À jouer une fois, après le premier déploiement de la branche `cardav` sur `snoopy_webmail_dev`, et
**avant que le plan 4c-ii-b n'ouvre la moindre route `/dav`**.

Ce fichier existe pour la même raison que `assets/contacts-sync-epoch-rotate.sql` : une procédure
qu'il faut reconstituer au moment de s'en servir est une procédure qu'on ne joue pas. La tranche
4c-ii-a n'expose aucune route — rien ne lit encore la séquence, les tombes ni les révisions — donc
la seule surface observable est la base, les journaux, et un écran. C'est suffisant : ce que les
plans b et c iront lire, c'est exactement ce qui s'écrit ici.

## L'ordre de déploiement, qui est lui-même un contrôle

1. **Le DDL** — la section « Tranche 4c-ii » de `webmail-carddav-tables.md`, joué **avant** le
   déploiement du backend : les requêtes EF du nouveau code nomment `dav_name` et `sync_sequence`,
   et un backend déployé sur un schéma qui ne les a pas ne démarre pas.
2. **Déployer le backend.**
3. **Le contrôle `sans_carte` du rattrapage, joué seul d'abord.** S'il rend autre chose que 0, le
   rattrapage de la tranche 4a n'est pas terminé sur cette base, et il faut le finir :
   `POST /api/Contacts/Backfill?batchSize=1000` — route d'administrateur, idempotente — rappelée
   jusqu'à ce que sa réponse porte `remaining: 0`.
4. **Seulement ensuite** `assets/contacts-dav-backfill.sql`.
5. Ses deux contrôles rendent 0.

**Inverser 3 et 4 est le défaut que ce plan a fermé en dernier.** Le rattrapage DAV pose
`sync_sequence = 1` sur toutes les fiches, y compris celles sans carte ; le balayage 4a les garnira
ensuite sans prendre de rang, puisqu'il n'est pas une porte de synchronisation. La fiche se met
alors à satisfaire la clause de visibilité **à un rang déjà publié**, et le rattrapage DAV ne peut
plus la reprendre — sa clause `WHERE sync_sequence = 0` ne correspond plus. Au plan c, aucun client
dont le jeton vaut 1 ou plus ne recevrait jamais ces cartes, sans erreur nulle part.

Dev est l'endroit où prouver que cet ordre marche, pas celui où découvrir qu'il compte.

## Le contrôle qui bloque le plan c

**La procédure à deux sessions**, écrite dans `webmail-carddav-tables.md` sous `## Vérification`.
C'est la seule propriété de correction de toute la tranche qui n'a aucun test automatique :
l'incrément est du SQL MySQL que le fournisseur InMemory ne sait pas exécuter.

Cinq observations, plus une. Les cinq premières sont dans le document ; la sixième est celle qui
attrape un liage de `Guid` défaillant vers une colonne `CHAR(36)` :

```sql
SELECT COUNT(*) AS `lignes_d_etat` FROM `contact_sync_state` WHERE `user_id` = @u;
-- attendu : 1, jamais 2. Deux lignes veulent dire que le paramètre GUID ne s'écrit pas
-- au format de la colonne, et que chaque incrément en crée une nouvelle.
```

**Si la session B ne bloque pas, s'arrêter là et le signaler.** Rien de ce qui suit n'a de sens si
deux transactions concurrentes peuvent prendre le même rang.

## Le tableau de bord

Toutes les vérifications de la matrice suivante se lisent sur ces quelques nombres. Poser d'abord
l'utilisateur d'essai, puis relire ce bloc **avant et après** chaque geste :

```sql
SET @u = (SELECT `id` FROM `users` WHERE `email` = 'essai@weesky.be');

SELECT
  (SELECT `seq`          FROM `contact_sync_state` WHERE `user_id` = @u) AS `seq`,
  (SELECT `pruned_below` FROM `contact_sync_state` WHERE `user_id` = @u) AS `filigrane`,
  (SELECT COUNT(*) FROM `contacts`           WHERE `user_id` = @u)       AS `fiches`,
  (SELECT COUNT(*) FROM `contact_tombstones` WHERE `user_id` = @u)       AS `tombes`,
  (SELECT COUNT(*) FROM `contact_revisions`  WHERE `user_id` = @u)       AS `revisions`;
```

## La matrice de comportement

Chaque section est un geste dans le webmail et ce qui doit être vrai en base juste après.

### 1. Créer une fiche

```sql
SELECT `id`, `dav_name`, `sync_sequence` FROM `contacts`
 WHERE `user_id` = @u ORDER BY `updated_at` DESC LIMIT 1;
```

`dav_name` vaut `{id}.vcf` — le même GUID que la colonne `id` —, `sync_sequence` est strictement
positif, et `seq` a avancé de **exactement 1**. Aucune révision de plus : rien n'a été remplacé.

### 2. Modifier un champ de cette fiche

`seq` avance de 1, le `sync_sequence` de la fiche prend cette nouvelle valeur, et **une** révision
apparaît :

```sql
SELECT `cause`, `dav_name`, `contact_id`, LEFT(`vcard_raw`, 200) AS `debut_de_carte`
  FROM `contact_revisions` WHERE `user_id` = @u ORDER BY `id` DESC LIMIT 1;
```

`cause` vaut `webmail`, `contact_id` désigne la fiche, et `vcard_raw` porte **les octets d'avant** —
c'est tout le point : la révision archive ce qui a été remplacé, pas ce qui l'a remplacé. Le
vérifier en lisant le nom dans le début de carte, qui doit être l'ancien.

### 3. Rouvrir la fiche et l'enregistrer sans rien changer

**`seq` ne bouge pas. Aucune révision.** C'est la garde qui empêchera, au plan c, de réveiller tous
les téléphones pour une écriture qui n'a rien changé.

### 4. Basculer l'étoile

**`seq` ne bouge pas.** Le favori n'est pas dans la carte, donc il n'est pas visible du protocole.
C'est le cas piège de la décision 6, et celui que personne ne pense à vérifier.

### 5. Supprimer la fiche

```sql
SELECT `dav_name`, `sync_sequence`, `deleted_at` FROM `contact_tombstones`
 WHERE `user_id` = @u ORDER BY `sync_sequence` DESC LIMIT 1;

SELECT `cause`, `contact_id`, `dav_name` FROM `contact_revisions`
 WHERE `user_id` = @u ORDER BY `id` DESC LIMIT 1;
```

Une tombe portant le `dav_name` de la fiche disparue, au rang que `seq` vient d'atteindre. Une
révision en `cause = 'delete'` dont **`contact_id` est NULL** : une révision de suppression survit
à la fiche qu'elle décrit, et la clé étrangère refuserait un identifiant sur le point de
disparaître.

**Sans cette tombe, le client garderait la fiche pour toujours et la rendrait à l'utilisateur qui
vient de l'effacer, sans erreur nulle part.** C'est le mode de défaillance silencieux que la
tranche existe pour fermer, et il arrive par la porte la plus fréquentée du produit.

### 6. Recréer un contact identique après l'avoir supprimé

Le webmail engendre un nouvel identifiant, donc **un `dav_name` différent** : la tombe de la fiche
supprimée reste en place et ne gêne pas. Le cas « même nom enterré deux fois » n'est pas atteignable
depuis cet écran — il le deviendra au plan c, quand un client choisira lui-même le nom de la
ressource.

### 7. Supprimer trois fiches par la barre d'actions groupées

**Un seul rang pour les trois**, trois tombes, trois révisions. `seq` avance de 1 et non de 3 : une
transaction, un rang — tout devient visible au même `COMMIT`, et incrémenter davantage ne
distinguerait rien.

### 8. Importer un fichier de plus de cent lignes

`seq` avance de **2** : cent fiches par transaction, deux lots. Toutes les lignes créées portent un
`dav_name` en `{id}.vcf` et un `sync_sequence` non nul :

```sql
SELECT COUNT(*) AS `sans_nom_ou_sans_rang` FROM `contacts`
 WHERE `user_id` = @u AND (`dav_name` IS NULL OR `sync_sequence` = 0);
-- attendu : 0
```

### 9. Réimporter exactement le même fichier

**Aucun `sync_sequence` de fiche ne change, et aucune révision n'apparaît.** Le compteur, lui,
avance d'un rang par lot — c'est assumé : décider si quelque chose a changé exigerait de lire les
fiches avant de prendre le verrou d'état, ce qui est précisément l'interblocage que l'ordre de
verrou interdit. Le coût est un `sync-collection` vide par téléphone, une fois.

C'est le geste le plus courant d'un utilisateur qui doute que son import ait marché.

## Les deux mécaniques de fond

### Le contrôle de cohérence au démarrage

Au démarrage, sur un carnet en phase, **il ne dit rien**. Le provoquer :

```sql
SELECT `id` INTO @fiche FROM `contacts` WHERE `user_id` = @u LIMIT 1;
UPDATE `contacts`
   SET `sync_sequence` = (SELECT `seq` FROM `contact_sync_state` WHERE `user_id` = @u) + 5
 WHERE `id` = @fiche;
```

Redémarrer le service. La ligne d'erreur doit nommer le `user_id` **et la forme par utilisateur**
de la rotation d'epoch — pas la forme globale, qui forcerait le ré-appairage manuel de tous les
carnets Thunderbird du déploiement. Remettre ensuite la fiche à son rang, et redémarrer pour
vérifier que le contrôle se tait de nouveau.

**Ce qu'il ne voit pas, et qui est écrit à côté de lui** : une restauration *cohérente*, les deux
tables rembobinées ensemble, le laisse muet — l'inégalité reste vraie. C'est le seul incident de
la tranche sans symptôme côté client, et le remède reste le `.sql`.

### Le balayeur

Sa ligne d'information paraît au démarrage, avec ses deux compteurs à zéro : c'est son battement de
cœur, zéro compris. Pour le voir élaguer, antidater une tombe et une révision — **en UTC, comme le
code les écrit** :

```sql
UPDATE `contact_tombstones` SET `deleted_at`  = UTC_TIMESTAMP() - INTERVAL 181 DAY
 WHERE `user_id` = @u LIMIT 1;
UPDATE `contact_revisions`  SET `replaced_at` = UTC_TIMESTAMP() - INTERVAL 31 DAY
 WHERE `user_id` = @u LIMIT 1;
```

Redémarrer, puis attendre jusqu'à cinq minutes : la gigue de démarrage est là pour étaler une
tempête de redémarrages. La ligne doit annoncer une tombe et une révision retirées, les deux lignes
doivent avoir disparu, et **`pruned_below` doit avoir monté au rang de la tombe élaguée** — jamais
descendre.

`UTC_TIMESTAMP()` et non `NOW()` : ces colonnes sont des `DATETIME` écrits en UTC par le code, et
c'est précisément pour cela qu'elles ne sont pas des `TIMESTAMP`.

**Un seul balayeur à la fois.** Deux instances concurrentes lisent des ensembles qui se recouvrent ;
la seconde à valider trouve ses lignes déjà parties et annule toute sa transaction, filigrane
compris. Rien n'est perdu, tout est refait pour rien.

## L'écran que personne n'a encore vu

Le contrôle de concurrence du webmail n'a jamais été regardé : l'API de développement ne porte
aucune origine `localhost`, donc une build servie localement ne franchit pas l'authentification.
Ce déploiement est la première occasion.

Ouvrir la même fiche dans deux onglets. Enregistrer dans le premier. Enregistrer dans le second.

- La boîte s'ouvre et **nomme la conséquence**, pas seulement le conflit.
- Le bouton recharge et repeuple le formulaire.
- **La saisie du second onglet est toujours là tant qu'on n'a pas rechargé.** Recharger est le choix
  de l'utilisateur, pas une conséquence du refus.
- Fermer la boîte par la croix laisse le formulaire debout ; le prochain enregistrement rouvre la
  même boîte. C'est voulu : la sortie existe pour recopier sa saisie.

Regarder aussi ce qu'aucun test ne voit : le centrage, la largeur du texte, et si la boîte masque
le formulaire encore rempli.

## Les deux formes de la rotation d'epoch

Les passer une fois chacune, sur l'utilisateur d'essai puis sur la base, et vérifier que l'`epoch`
change et que rien d'autre ne bouge — ni `seq`, ni `pruned_below`. Elles restent justes jouées deux
fois.

## Ce que dev ne dira pas

Aucun client ne peut se connecter : il n'y a pas de route. On vérifie donc que **la donnée écrite
est juste**, pas qu'un téléphone la lit correctement — cela appartient au plan c. C'est le bon
partage : si la matrice passe et que la session B bloque comme elle doit, le plan b n'a plus qu'à
servir ce qui est déjà correct.
