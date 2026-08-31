-- Vide `contacts.display_name` partout où il ne porte que le FN calculé.
--
-- Le FN est obligatoire en vCard 3.0 comme en 4.0, et `VCardComposer.FallbackDisplayName` en
-- fabrique un dès qu'une écriture n'en porte pas : noms joints, sinon surnom, sinon première
-- adresse. `VCardProjector` le projetait tel quel, si bien que `display_name` était rempli sur
-- tous les contacts créés par le store — et l'éditeur, qui préremplissait sa boîte avec, le
-- renvoyait à chaque enregistrement. Le FN gelait donc à la forme qu'avait le nom le jour de la
-- création, et aucun renommage ultérieur ne l'atteignait.
--
-- Le projecteur ne garde désormais qu'un FN qui diverge des composantes. Ce script applique la
-- même règle aux lignes déjà en base : sans lui, un contact renommé avant le déploiement
-- continuerait d'afficher son ancien nom sur la fiche et dans la liste, qui lisent maintenant
-- `display_name` en premier.
--
-- La carte n'est pas touchée : son FN reste écrit, comme la norme l'exige. Seule la colonne se
-- vide, et `ReplaceProjectionAsync` la recalculera par la même règle au prochain enregistrement —
-- le script est donc idempotent et rejouable.
--
-- Un FN réellement choisi (`FN:Dr. John Smith Jr.` sur une carte importée) ne correspond à aucune
-- des deux requêtes et survit.
--
-- À passer sur dev d'abord : le pipeline de déploiement n'exécute aucun SQL.
--   mariadb -u <user> -p <base> < assets/contacts-display-name-backfill.sql

-- 1. Le FN vaut les composantes du nom, ou à défaut le surnom.
--    CONCAT_WS ignore les NULL ; le NULLIF extérieur ramène « aucun nom » à NULL pour que
--    COALESCE passe au surnom.
UPDATE contacts
SET display_name = NULL
WHERE display_name IS NOT NULL
  AND display_name = COALESCE(
        NULLIF(CONCAT_WS(' ',
          NULLIF(first_name, ''), NULLIF(middle_name, ''), NULLIF(last_name, '')), ''),
        NULLIF(nickname, ''));

-- 2. Carte sans nom ni surnom : le FN vaut sa première adresse. Comparaison insensible à la casse
--    parce que la projection canonicalise l'adresse là où le FN garde ce que la carte portait, et
--    que la table collationne en binaire.
UPDATE contacts c
JOIN contact_emails e
  ON e.contact_id = c.id
 AND e.position = (SELECT MIN(position) FROM contact_emails WHERE contact_id = c.id)
SET c.display_name = NULL
WHERE c.display_name IS NOT NULL
  AND CONCAT_WS(' ',
        NULLIF(c.first_name, ''), NULLIF(c.middle_name, ''), NULLIF(c.last_name, '')) = ''
  AND (c.nickname IS NULL OR c.nickname = '')
  AND LOWER(c.display_name) = LOWER(e.address);
