-- Rattrapage 4c-ii : donne un nom de ressource et un rang aux fiches existantes.
--
-- À passer IMMÉDIATEMENT APRÈS le déploiement du backend, et l'ordre n'est pas une commodité :
-- entre le déploiement et ce script, les fiches existantes n'ont ni dav_name ni rang. Un client
-- CardDAV qui se connecterait dans cette fenêtre verrait un carnet vide et effacerait ses propres
-- copies en les croyant supprimées côté serveur. Aucune route /dav n'existe avant la tranche
-- 4c-ii-c ; ce fichier est écrit maintenant parce que le DDL qu'il complète l'est aussi.
--
-- ET APRÈS LE RATTRAPAGE DE CARTES DE LA TRANCHE 4a, ACHEVÉ — POST /api/Contacts/Backfill rejoué
-- jusqu'à remaining = 0, voir docs/superpowers/contacts-4a-backfill.md. Cet ordre-là est
-- OBLIGATOIRE et l'enfreindre ne se rattrape pas : ce script pose sync_sequence = 1 sur TOUTES les
-- fiches, y compris celles qui n'ont pas encore de carte. Le rattrapage 4a leur en donnerait une
-- ensuite sans prendre de rang — c'est un balayage d'exploitation, pas une porte d'écriture — et
-- elles se mettraient à satisfaire la clause de visibilité à un rang DÉJÀ PUBLIÉ. Tout client
-- détenant un jeton >= 1 ne les recevrait alors jamais, sans erreur nulle part, et ce script ne
-- pourrait plus les réparer : sa clause WHERE sync_sequence = 0 ne les trouve plus. Voir le
-- contrôle sans_carte en fin de fichier.
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

-- CONTRÔLE — doit rendre 0, et un chiffre non nul ici n'est pas un avertissement : c'est un dégât
-- déjà fait. Une fiche que le rattrapage de 4a n'a pas atteinte n'a ni carte ni condensat, donc
-- elle est invisible du protocole par la clause de visibilité — correct tant qu'elle le reste. Mais
-- elle vient de recevoir le rang 1, et le jour où le rattrapage 4a lui donne sa carte elle devient
-- visible à un rang déjà publié, sans prendre de rang neuf : tout client détenant un jeton >= 1 ne
-- la recevra JAMAIS, sans erreur nulle part, ni côté serveur ni côté téléphone. Ce script ne peut
-- plus la réparer, sa clause WHERE sync_sequence = 0 ne la trouvant plus. Le seul remède est une
-- rotation d'epoch — assets/contacts-sync-epoch-rotate.sql, forme mono-utilisateur si le dégât
-- tient à quelques comptes, forme base entière sinon — qui force une resynchronisation complète de
-- chaque appareil concerné. D'où l'ordre imposé en tête de fichier : 4a d'abord, achevé.
SELECT COUNT(*) AS `sans_carte`
FROM `contacts`
WHERE `vcard_raw` IS NULL OR `card_hash` = '';
