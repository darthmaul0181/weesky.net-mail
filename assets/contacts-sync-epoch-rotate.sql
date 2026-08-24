-- À JOUER APRÈS TOUTE RESTAURATION D'UNE SAUVEGARDE de la base du webmail.
--
-- Une restauration rembobine contact_sync_state.seq. Le refus du jeton postérieur à la séquence
-- courante n'attrape que les clients les plus en avance : un jeton resté sous la séquence
-- restaurée passe, et couvre des rangs dont le contenu a changé — divergence silencieuse et
-- permanente, sur des téléphones qui continuent de synchroniser sans rien signaler.
--
-- Cette ligne rend étrangers au carnet tous les jetons émis par la base d'avant, et le ctag change
-- avec eux. Elle reste juste si elle est jouée deux fois.
--
-- POURQUOI L'EPOCH ET NON pruned_below = seq : les deux invalident les jetons, mais le second le
-- fait en déplaçant un filigrane dont le sens est « ces tombes-là n'existent plus », c'est-à-dire
-- en mentant sur autre chose pour obtenir l'effet voulu. L'epoch ne dit qu'une chose et la dit
-- entièrement : ce carnet n'est plus celui qui a émis vos jetons.

UPDATE `contact_sync_state` SET `epoch` = UUID();

-- LA REPRISE N'EST PAS UNIFORME CÔTÉ CLIENT, et il faut le savoir avant de prévenir les
-- utilisateurs :
--   * DAVx5 lit le 403 valid-sync-token et repart d'une synchronisation complète tout seul.
--   * Thunderbird ne retombe en synchronisation complète que sur un 400 : son code rejoue un jeton
--     refusé en 403 à chaque cycle, indéfiniment. Après cette rotation, un carnet Thunderbird est à
--     RÉ-APPAIRER À LA MAIN — supprimer le carnet et le recréer.
