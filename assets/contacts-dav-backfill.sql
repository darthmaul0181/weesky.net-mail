-- Rattrapage 4c-ii : donne un nom de ressource et un rang aux fiches existantes.
--
-- À passer IMMÉDIATEMENT APRÈS le déploiement du backend, et l'ordre n'est pas une commodité :
-- entre le déploiement et ce script, les fiches existantes n'ont ni dav_name ni rang. Un client
-- CardDAV qui se connecterait dans cette fenêtre verrait un carnet vide et effacerait ses propres
-- copies en les croyant supprimées côté serveur. Aucune route /dav n'existe avant la tranche
-- 4c-ii-c ; ce fichier est écrit maintenant parce que le DDL qu'il complète l'est aussi.
--
-- IDEMPOTENT : chaque instruction ne touche que les lignes encore à NULL ou à 0. Un opérateur qui
-- le rejoue ne réattribue aucun nom et ne remet aucun compteur en arrière.
--
-- Rang 1 pour toutes les fiches d'un même utilisateur, et non un rang par fiche : elles arrivent
-- ensemble dans la première synchronisation, et aucun client n'existe encore pour distinguer leurs
-- rangs. Un rang par fiche coûterait un balayage ordonné pour un ordre que personne ne lit.

START TRANSACTION;

-- 1. La ligne d'état, une par utilisateur qui a au moins une fiche, avec son epoch.
INSERT INTO `contact_sync_state` (`user_id`, `epoch`, `seq`, `pruned_below`)
SELECT c.`user_id`, UUID(), 1, 0
FROM `contacts` c
GROUP BY c.`user_id`
ON DUPLICATE KEY UPDATE `seq` = GREATEST(`contact_sync_state`.`seq`, 1);

-- 2. Le nom de ressource. La convention {id}.vcf est celle des fiches nées dans le webmail ; les
--    clients l'affichent dans leurs journaux et il n'y a aucune raison de les dérouter.
UPDATE `contacts`
SET `dav_name` = CONCAT(`id`, '.vcf')
WHERE `dav_name` IS NULL;

-- 3. Le rang. La clause = 0 est ce qui rend le script rejouable : une fiche déjà rattrapée, ou
--    écrite depuis par le store, porte un rang > 0 et n'est pas touchée.
UPDATE `contacts`
SET `sync_sequence` = 1
WHERE `sync_sequence` = 0;

COMMIT;

-- CONTRÔLE — doit rendre 0. Tant qu'il ne rend pas 0, le carnet DAV est incomplet et ne doit pas
-- être ouvert aux clients.
SELECT COUNT(*) AS `restantes`
FROM `contacts`
WHERE `dav_name` IS NULL OR `sync_sequence` = 0;

-- CONTRÔLE — doit rendre 0. Une fiche que le rattrapage de 4a n'a pas atteinte n'a ni carte ni
-- condensat : elle est invisible du protocole par la clause de visibilité, ce qui est correct,
-- mais l'opérateur doit savoir qu'elles existent avant d'ouvrir le carnet.
SELECT COUNT(*) AS `sans_carte`
FROM `contacts`
WHERE `vcard_raw` IS NULL OR `card_hash` = '';
