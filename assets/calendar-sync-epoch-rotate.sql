-- À JOUER APRÈS TOUTE RESTAURATION D'UNE SAUVEGARDE de la base du webmail.
--
-- Une restauration rembobine calendar_sync_state.seq. Le refus du jeton postérieur à la séquence
-- courante n'attrape que les clients les plus en avance : un jeton resté sous la séquence
-- restaurée passe, et couvre des rangs dont le contenu a changé — divergence silencieuse et
-- permanente, sur des téléphones qui continuent de synchroniser sans rien signaler.
--
-- Cette ligne rend étrangers à l'agenda tous les jetons émis par la base d'avant, et le ctag change
-- avec eux. Elle reste juste si elle est jouée deux fois.
--
-- POURQUOI L'EPOCH ET NON pruned_below = seq : les deux invalident les jetons, mais le second le
-- fait en déplaçant un filigrane dont le sens est « ces tombes-là n'existent plus », c'est-à-dire
-- en mentant sur autre chose pour obtenir l'effet voulu. L'epoch ne dit qu'une chose et la dit
-- entièrement : cet agenda n'est plus celui qui a émis vos jetons.
--
-- DEUX INVOCATIONS, ET ELLES NE SE SUBSTITUENT PAS L'UNE À L'AUTRE. Le prix d'une rotation est une
-- resynchronisation complète de chaque appareil concerné, et un ré-appairage à la main sur
-- Thunderbird (voir plus bas) : l'étendue de la rotation doit donc être exactement celle du dégât.

-- FORME « BASE ENTIÈRE » — après une restauration. C'est le geste que décrit
-- docs/superpowers/carddav-restore-prerequisite.md, et c'est celui que joue un `mariadb < ce
-- fichier` : une restauration ne rembobine pas un agenda, elle rembobine la base, et rien ne dit
-- lesquels de ses agendas ont divergé.

UPDATE `calendar_sync_state` SET `epoch` = UUID();

-- FORME « UN SEUL AGENDA » — après le contrôle de cohérence au démarrage (SyncStateConsistencyCheck,
-- passe agenda). Cet incident-là nomme son calendar_id : lui seul a un agenda à invalider, et jouer
-- la forme « base entière » pour lui infligerait le ré-appairage manuel à TOUS les agendas
-- Thunderbird du déploiement — une panne pour tout le monde à cause d'un seul agenda.
--
-- À copier, décommenter, et remplacer le littéral par le calendar_id que le journal a nommé.
-- Laissée en commentaire pour que le fichier reste jouable tel quel dans sa forme « base entière ».
--
-- UPDATE `calendar_sync_state` SET `epoch` = UUID() WHERE `calendar_id` = '00000000-0000-0000-0000-000000000000';

-- LA REPRISE N'EST PAS UNIFORME CÔTÉ CLIENT, et il faut le savoir avant de prévenir les
-- utilisateurs :
--   * DAVx5 et iOS lisent le 403 valid-sync-token et repartent d'une synchronisation complète tout
--     seuls.
--   * Thunderbird ne retombe en synchronisation complète que sur un 400 : son code rejoue un jeton
--     refusé en 403 à chaque cycle, indéfiniment. Après cette rotation, un agenda Thunderbird est à
--     RÉ-APPAIRER À LA MAIN — supprimer l'agenda et le recréer.
