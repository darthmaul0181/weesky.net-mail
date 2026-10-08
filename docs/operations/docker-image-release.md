# Publier une image Docker

Pour le propriétaire du dépôt. Le guide des utilisateurs est `docker/README.md`.

## Le parcours

```
branche ──(candidate rc.1 : essai sur ton serveur)──► corrections ──► fusion dans master
                                                                         │
                                       candidate rc.2 depuis master ◄────┘
                                                  │
                                       test sur ton serveur
                                                  │
                                       release 1.0.0-rc.2 ──► 1.0.0, 1.0, 1, latest
```

## 1. Décider du numéro

`docker/VERSION` porte le numéro de la prochaine image. Le faire monter sur la branche, avant la
fusion : correctif (`1.0.1`) pour un correctif ou une image de base mise à jour, mineur (`1.1.0`)
pour une nouveauté, majeur (`2.0.0`) si un `.env` existant doit changer.

## 2. Construire une candidate

GitHub › Actions › **image** › **Run workflow** : choisir la branche, mode `candidate`. Le workflow
construit les deux architectures, démarre l'image à côté d'une MariaDB jetable, et publie
`1.0.0-rc.N`. Le numéro publié est dans le résumé du run.

Une candidate construite depuis une autre branche que `master` sert à essayer ; elle ne pourra
jamais être publiée. Ses versions web et serveur se terminent par `-dev`.

Le bouton « Run workflow » n'apparaît que pour un workflow présent sur `master`. Tant qu'une
modification de `image.yml` n'y est pas fusionnée, lancer la candidate en ligne de commande :
`gh workflow run image.yml --ref <branche> -f mode=candidate`.

## 3. La tester

Sur le serveur de test, dans le `docker-compose.yml` :

```yaml
    image: ghcr.io/darthmaul0181/scotty-webmail:1.0.0-rc.2
```

puis `docker compose pull && docker compose up -d`. **Avec une base de test** : l'image applique
ses migrations à la base qu'on lui donne. Vérifier `docker compose ps` (`healthy`), la connexion,
puis Réglages › À propos (« Docker image 1.0.0 »).

Tant que le paquet est privé (voir plus bas), le serveur doit d'abord s'identifier auprès de GHCR :
`docker login ghcr.io`, avec ton nom d'utilisateur GitHub et un jeton (token) qui a le droit
`read:packages`.

## 4. Publier

Actions › **image** › **Run workflow**, mode `release`, candidate `1.0.0-rc.2` (la branche choisie
n'importe pas). Le workflow refuse, en disant pourquoi, si la candidate ne vient pas de `master`, si
`docker/VERSION` ne vaut pas `1.0.0` à son commit, ou si `image-v1.0.0` existe déjà. Sinon, il
ajoute `1.0.0`, `1.0`, `1` et `latest` à la même image, sans la reconstruire, et pose le tag Git
`image-v1.0.0`.

## Une seule fois, après la première publication

GHCR crée tout nouveau paquet en privé. GitHub › ton profil › **Packages** › `scotty-webmail` ›
**Package settings** › **Change visibility** › **Public**. Vérifier depuis une machine sans
connexion à GHCR : `docker pull ghcr.io/darthmaul0181/scotty-webmail:1.0.0`.

## Mises à jour de l'image de base

Le `Dockerfile` désigne ses images de base par leur étiquette (`aspnet:10.0-noble-chiseled-extra`),
pas par une empreinte figée : chaque nouvelle candidate prend les derniers correctifs de Microsoft.
Une image déjà publiée, elle, ne change jamais. Pour livrer ces correctifs, publier de temps en
temps une version corrective, par exemple après le correctif mensuel de .NET (le deuxième mardi du
mois) : faire monter `docker/VERSION` d'un correctif (`1.0.1`), fusionner, puis les étapes 2 à 4.
