# Colonne `security_stamp` — DDL manuel

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`, comme les autres scripts de cette
série. Aucun `GRANT` à rejouer.

> **Ordre imposé.** Ce script doit être appliqué **avant** le déploiement du code qui lit la
> colonne. Dans l'autre sens, chaque requête authentifiée lèverait sur une colonne absente et le
> service serait entièrement indisponible.

## Script

```sql
ALTER TABLE `users`
  ADD COLUMN `security_stamp` CHAR(36) NOT NULL DEFAULT ''
  COMMENT 'Tourne à chaque révocation ; un JWT qui ne le porte plus est refusé'
  AFTER `email`;

-- Les comptes existants reçoivent un stamp : sans lui leur colonne resterait vide et
-- aucune session ne pourrait plus être validée.
UPDATE `users` SET `security_stamp` = UUID() WHERE `security_stamp` = '';

-- Le défaut n'existait que pour peupler les lignes en place ; l'application écrit toujours
-- une valeur, et une ligne sans stamp doit être une erreur, pas une chaîne vide.
ALTER TABLE `users` ALTER COLUMN `security_stamp` DROP DEFAULT;
```

## Vérification

```sql
SELECT id, email, security_stamp FROM users;
```

Chaque ligne doit porter un GUID distinct. La table collationne en `utf8mb4_bin`, donc la
comparaison du stamp est exacte — c'est voulu.

## Ce que le déploiement provoque, une fois

Le code refuse un jeton qui ne porte pas de claim `webmail_stamp`. Tous les jetons émis avant le
déploiement en sont dépourvus : **toutes les sessions ouvertes sont coupées au déploiement**, une
seule fois. C'est la contrepartie assumée du fait qu'un jeton sans stamp doit être rejeté et non
accepté — l'accepter rendrait le contrôle contournable par omission.
