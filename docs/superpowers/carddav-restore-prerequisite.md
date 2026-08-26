# Prérequis de restauration — la rotation d'epoch CardDAV

**À jouer après toute restauration d'une sauvegarde de la base du webmail**, avant de rouvrir le
service aux clients. Sans cela, un incident de sauvegarde/restauration ne se manifeste par aucune
erreur visible pendant que la synchronisation d'un ou plusieurs téléphones diverge en silence.

Ce fichier n'est pas versionné dans une spec : le geste est livré comme un script,
`assets/contacts-sync-epoch-rotate.sql`, précisément parce qu'une consigne qu'il faut retrouver
dans un document de conception au moment d'une restauration est une consigne qui ne sera pas
jouée.

## Quoi et pourquoi

Une restauration rembobine `contact_sync_state.seq`. Le refus du jeton postérieur à la séquence
courante n'attrape que les clients les plus en avance : un jeton resté sous la séquence
restaurée passe, et couvre des rangs dont le contenu a changé — divergence silencieuse et
permanente, sur des téléphones qui continuent de synchroniser sans rien signaler.

Jouer `assets/contacts-sync-epoch-rotate.sql` rend étrangers au carnet tous les jetons émis par la
base d'avant, et le ctag change avec eux. Le script est idempotent : le rejouer une seconde
fois reste juste.

**Pourquoi l'epoch et non `pruned_below = seq`** : les deux invalident les jetons, mais le
second le fait en déplaçant un filigrane dont le sens est « ces tombes-là n'existent plus »,
c'est-à-dire en mentant sur autre chose pour obtenir l'effet voulu. L'epoch ne dit qu'une chose et
la dit entièrement : ce carnet n'est plus celui qui a émis vos jetons.

## Appliquer

```bash
mariadb -u <user> -p snoopy_webmail < assets/contacts-sync-epoch-rotate.sql
```

Répéter pour `snoopy_webmail_dev` si la restauration l'a aussi touchée.

Le fichier porte aussi, en commentaire, une forme mono-utilisateur (`WHERE user_id = …`) qui n'est
**pas** le geste d'une restauration — elle sert les incidents qui nomment un seul `user_id`, comme
le contrôle de démarrage ci-dessous. Après une restauration, c'est la forme « base entière »
ci-dessus qu'il faut jouer, et le `<` la joue déjà.

## La reprise n'est pas uniforme côté client

Il faut le savoir avant de prévenir les utilisateurs :

- **DAVx5** lit le `403 valid-sync-token` et repart d'une synchronisation complète tout seul.
- **Thunderbird** ne retombe en synchronisation complète que sur un `400` : son code rejoue
  un jeton refusé en `403` à chaque cycle, indéfiniment. Après cette rotation, un carnet
  Thunderbird est à **ré-appairer à la main** — supprimer le carnet et le recréer.

## Ce que le contrôle de démarrage ne voit pas

Le contrôle de démarrage compare `MAX(contacts.sync_sequence)` à `contact_sync_state.seq` :
il détecte une restauration qui a rembobiné l'une des deux tables sans l'autre. Une restauration
*cohérente* — les deux tables rembobinées ensemble, dans la même sauvegarde — le laisse muet,
l'inégalité restant vraie. C'est le seul endroit de cette tranche où un incident n'a aucun symptôme
côté client au moment de la restauration, et le contrôle n'en attrape que la moitié détectable.

Ne pas s'y fier pour décider si la rotation est nécessaire : elle l'est après **toute**
restauration, que le contrôle se soit tu ou non.

## Le compromis avec Radicale

C'est le seul endroit de la tranche où ce serveur demande un geste humain là où Radicale n'en
demande aucun : le ctag de Radicale est dérivé du contenu de la collection, donc une
restauration le change toute seule — auto-réparant, au prix d'un recalcul à chaque interrogation
d'état. Le nôtre est un compteur, donc `O(1)` sur le chemin qu'un téléphone emprunte toutes les
quinze minutes, mais rembobinable. C'est le bon échange pour 5000 fiches, et il ne l'est qu'à la
condition que la ligne soit jouée.