# Drag & drop des mails vers les dossiers — note de cadrage (earmark)

> **Statut : planifié, non conçu en détail.** Cette note fige l'intention et les décisions déjà acquises. Ce n'est pas une spec finalisée : le brainstorm complet → spec → plan se fera quand la multi-sélection existera.

**Séquencement décidé avec l'utilisateur (2026-07-23) : après 2b3 (multi-sélection).** Le DnD glisse la *sélection courante* (un ou plusieurs mails), donc l'usage naturel dépend de la multi-sélection. Le concevoir maintenant reviendrait à concevoir contre une sélection qui n'existe pas encore ; on le conçoit une seule fois, pour son usage complet, une fois 2b3 en place.

## Ce qui est déjà ancré (fondation 2b2)

Le déplacement de mails est entièrement construit ; le DnD est une **couche d'interaction** par-dessus, pas une nouvelle plomberie.

- **Réutilise `useMoveMessages`** (`src/modules/mail/queries.ts`) — même retrait optimiste des lignes à la source, suppression des caches cible (`removeQueries`), patch des deux compteurs, rollback, et avance de sélection via `onDeparted`. La garde du poll (`mailKeys.writes`) couvre déjà ces écritures.
- **Cibles de drop valides = les règles de `MoveMessagesModal`** : un dossier sélectionnable qui n'est pas le dossier courant (cible ≠ source, imposé aussi côté backend) et qui n'est pas un conteneur non sélectionnable (`\NoSelect`). Une cible invalide n'accepte simplement pas le drop.
- **Source = la sélection de la liste** ; **zone de drop = l'arbre de dossiers de la colonne de gauche** (`FolderTree.tsx`).
- **Le DnD est un bonus, jamais l'unique chemin.** La modale/menu « Déplacer vers… » offre déjà une voie clavier accessible ; le DnD (notoirement difficile à rendre accessible) n'a donc pas à porter seul le déplacement de mails.

## Questions ouvertes à trancher au moment de construire (post-2b3)

1. **Move seul, ou move + copy ?** Glisser = déplacer par défaut ; touche (Ctrl/Alt) pour copier, ou la copie reste réservée au menu/modale ?
2. **Ligne hors sélection qu'on glisse** : elle seule, ou toute la sélection courante ? Convention usuelle : une ligne non sélectionnée se glisse seule ; une ligne sélectionnée entraîne toute la sélection.
3. **Fantôme de drag** : badge de compte (« 3 messages »), sujet du mail, ou enveloppe générique ?
4. **Feedback de drop** : surbrillance de la ligne dossier survolée ; auto-déploiement d'un parent replié au survol prolongé ?
5. **Technique** : DnD HTML5 natif vs pointer-based ; tactile (appui long pour saisir) dans le périmètre ou desktop-only ?
6. **Depuis le lecteur** : peut-on glisser le mail ouvert depuis le lecteur, ou seulement les lignes de la liste ?

## Suite

Quand 2b3 (multi-sélection) est livré : brainstorm complet de cette fonctionnalité (résoudre les six questions ci-dessus), puis spec finalisée et plan d'implémentation selon le flux habituel.
