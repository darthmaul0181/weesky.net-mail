# Birthdays calendar — schema migration

```sql
ALTER TABLE calendars
  ADD COLUMN kind ENUM('regular','birthdays') NOT NULL DEFAULT 'regular' AFTER user_id,
  ADD COLUMN birthday_reminder ENUM('none','same_day','day_before','week_before') NULL,
  ADD COLUMN birthday_language CHAR(2) NULL,
  ADD COLUMN birthdays_owner CHAR(36) AS (IF(kind = 'birthdays', user_id, NULL)) PERSISTENT,
  ADD UNIQUE KEY ux_calendars_birthdays_owner (birthdays_owner);
```

This is replayed by hand on each database — prod and dev — before deploying the code that reads
these columns.

Check afterwards:

```sql
SHOW CREATE TABLE calendars;
```

## Vérification

À rejouer sur dev, dans deux clients SQL ouverts sur `scotty_webmail` (session A et session B), avec un
compte de test dont le calendrier « Anniversaires » est activé. Le but : montrer qu'une modification de
contact qui croise « Désactiver » ne produit plus d'erreur 500.

Dans **les deux** sessions, remplacer l'identifiant par celui du compte de test :

```sql
SET @user = '00000000-0000-0000-0000-000000000000';
SET @cal = (SELECT id FROM calendars WHERE user_id = @user AND kind = 'birthdays');
SELECT @@tx_isolation;   -- attendu : REPEATABLE-READ
```

**Session A** — l'enregistrement d'un contact, jusqu'à sa première lecture (qui fige la photo de la base) :

```sql
START TRANSACTION;
SELECT id FROM calendars WHERE user_id = @user AND kind = 'birthdays';   -- 1 ligne
SELECT seq FROM calendar_sync_state WHERE calendar_id = @cal;            -- note la valeur, ex. 12
```

**Session B** — « Désactiver », jusqu'au bout :

```sql
START TRANSACTION;
INSERT INTO calendar_sync_state (calendar_id, epoch, seq, pruned_below)
VALUES (@cal, UUID(), 1, 0) ON DUPLICATE KEY UPDATE seq = seq + 1;
DELETE FROM calendar_events WHERE calendar_id = @cal;
DELETE FROM calendar_tombstones WHERE calendar_id = @cal;
DELETE FROM calendar_sync_state WHERE calendar_id = @cal;
DELETE FROM calendars WHERE id = @cal;
COMMIT;
```

**Session A** — la prise de rang, telle que le code la fait désormais :

```sql
INSERT INTO calendar_sync_state (calendar_id, epoch, seq, pruned_below)
SELECT id, UUID(), 1, 0 FROM calendars WHERE id = @cal
ON DUPLICATE KEY UPDATE calendar_sync_state.seq = calendar_sync_state.seq + 1;
-- attendu : « 0 rows affected » → le code répond « pas de calendrier » et n'écrit aucun événement
SELECT seq FROM calendar_sync_state WHERE calendar_id = @cal;              -- renvoie encore 12 : la vieille photo
SELECT seq FROM calendar_sync_state WHERE calendar_id = @cal FOR UPDATE;   -- attendu : aucune ligne
ROLLBACK;
```

La lecture simple renvoie encore l'ancienne valeur : c'est exactement ce qui faisait échouer l'ancien
code (il repartait avec ce rang et l'insertion de l'événement tombait sur l'erreur 1452, donc un 500).
Le code actuel s'arrête au « 0 rows affected » et, sinon, relit avec `FOR UPDATE`.

Remettre ensuite le calendrier « Anniversaires » en service depuis Réglages › Général.

Limite connue, non corrigée : si A lance sa prise de rang pendant que B est **entre** son `INSERT … ON
DUPLICATE KEY` et son `DELETE FROM calendars` (pour le voir : dans B, seulement
l'`INSERT … ON DUPLICATE KEY` ; puis la prise de rang de A, qui se met en attente ; puis les `DELETE` de B),
A attend B, puis B attend A : MariaDB tue l'une des deux avec l'erreur 1213 (deadlock). L'utilisateur voit
alors une erreur et n'a qu'à recommencer son geste.
