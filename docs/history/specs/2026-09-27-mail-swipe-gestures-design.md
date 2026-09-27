# Glisser une ligne de la liste de mails

Sur un écran tactile, un mail de la liste se glisse vers la droite ou vers la gauche pour agir
sans l'ouvrir. L'action de chaque sens se choisit dans les réglages. Aujourd'hui, au doigt, une
ligne n'offre que l'étoile et l'appui long : `@media (hover: none)` masque déjà le groupe de
boutons de ligne (`mail.css`), si bien que lire, archiver ou supprimer un mail demande de l'ouvrir
ou de passer en mode sélection.

Maquettes validées le 2026-09-27 : https://claude.ai/artifact/Jmmx3pFtzqsp2ZfEtLvwTZ — retenus, le
réglage **A** (deux listes de choix) et la liste **A** (bande grise, puis pleine au seuil),
augmentée de la barre de décompte de la notification **B**.

## Décisions

| # | Question | Décision |
|---|---|---|
| 1 | Annulation d'un glissement destructeur | Action **différée de 5 s** : la ligne part aussitôt, le serveur n'est appelé qu'à l'échéance. Undo = rien n'a été envoyé. Pas de changement serveur. |
| 2 | Actions proposées | `none`, `seen` (Read / Unread), `flag` (Flag / Unflag), `archive`, `delete`. **Junk exclu** : action trop rare pour un geste. **Move to… exclu** : un sélecteur après le geste en casse la vitesse, et l'appui long y mène déjà. |
| 3 | Déclenchement | **Glisser et relâcher** (Gmail), pas « glisser pour révéler un bouton » : un seul geste, et aucun état « ligne ouverte » à gérer dans la liste. |
| 4 | Écrans concernés | **Tout pointeur tactile ou stylet**, téléphone et tablette ; jamais la souris. |
| 5 | Activer / désactiver | **Pas d'interrupteur séparé** : `None` des deux côtés désactive le glissement. |
| 6 | Défauts | Droite → `seen`, gauche → `delete` : la convention des clients mail courants. |
| 7 | Plusieurs gestes à la suite | **Un seul Undo à la fois** : un nouveau geste différé envoie aussitôt le précédent et prend sa place. |

## Le réglage

Une section **Swipe gestures** dans `GeneralPage`, après *Layout*, visible sur tous les appareils —
on la règle aussi bien depuis un ordinateur — avec :

- une phrase d'introduction : *Swipe a message in the list to act on it without opening it. Touch
  screens only.* ;
- deux rangées `.field-h.is-setting`, **Swipe right** et **Swipe left**, chacune un `MenuSelect`
  dont chaque option porte l'icône de l'action dans sa couleur (`SelectOption.icon` existe déjà) ;
- une note permanente : *Choose None on both sides to turn swiping off.*

Chaque changement passe par `save(key, value, toastText)` comme les autres réglages de la page.

### Préférences

| Clé | Valeurs | Défaut |
|---|---|---|
| `mail.swipeRight` | `none` \| `seen` \| `flag` \| `archive` \| `delete` | `seen` |
| `mail.swipeLeft` | idem | `delete` |

- **Serveur** : deux `PreferenceDefinition` dans `Models/UserPreferences.cs`, valeurs énumérées
  (`IsSet: false`). Aucun script SQL : `user_preferences` est une table clé/valeur.
- **Client** : les deux clés dans `PREFERENCE_KEYS` (`hooks/usePreferences.ts`) et un accesseur
  `swipeActionOf(prefs, side)` qui retombe sur le défaut quand la clé est absente — un serveur
  antérieur à la fonctionnalité ne l'envoie pas — ou porte une valeur inconnue.

## Le geste

### Démarrage

- Seul un pointeur `touch` ou `pen` démarre un glissement ; la souris jamais. Un portable tactile
  glisse donc au doigt.
- La ligne porte `touch-action: pan-y` : le navigateur garde le défilement vertical et le « tirer
  pour rafraîchir » (`usePullToRefresh`), et livre les mouvements horizontaux au geste.
- La ligne ne suit le doigt qu'une fois le mouvement **nettement horizontal** : au-delà de
  `GESTURE_TRAVEL_PX` (10 px, `hooks/gestureThresholds.ts`) et avec |dx| > |dy|. Un défilement en
  diagonale reste un défilement.
- Ce même déplacement annule déjà l'appui long (`useLongPress`) : les deux gestes ne se
  chevauchent pas.
- Un sens réglé sur `None` ne bouge pas : la ligne reste en place, le défilement continue.

### Seuil

- **35 % de la largeur de la ligne.**
- Avant le seuil : bande `--surface-sunken`, icône `--text-muted`.
- Au franchissement : la bande prend la couleur pleine de l'action, l'icône et le libellé passent
  en blanc, et `navigator.vibrate(10)` quand il existe (Android ; iOS l'ignore).
- En repassant sous le seuil, la bande redevient grise.
- Relâcher avant le seuil ramène la ligne à sa place.

### Au relâcher, par action

| Action | Effet |
|---|---|
| `seen`, `flag` | Appliquée aussitôt par `on.setFlag`, comme le bouton de ligne. La ligne revient à sa place avec son nouvel état. Pas de notification. |
| `archive`, `delete` hors corbeille | La ligne se replie (`useRowExit`, 300 ms) ; notification *Moved to Archive* / *Moved to Trash* avec **Undo** et une barre de décompte de **5 s** ; appel serveur différé (voir plus bas). |
| `delete` dans la corbeille | La confirmation de suppression définitive s'ouvre (`removeRow` → `setExpunging`), comme aujourd'hui. La ligne revient à sa place. Pas d'Undo. |

### Action impossible

- Cas couverts : `archive` sans dossier au rôle archive, ou dans l'archive elle-même ; `delete`
  sans corbeille. Ce sont les mêmes drapeaux que les boutons de ligne (`archiveOff`, `trashOff`).
- La ligne glisse, mais la bande reste grise avec l'icône barrée, même au-delà du seuil.
- Au relâcher, la ligne revient à sa place et rien n'est déclenché.

### Où le geste est coupé

- En mode sélection (`selecting`) : l'appui long y a mené, cocher et agir ne se mélangent pas.
- Sur les lignes d'une recherche tous dossiers (`crossFolder`) : elles n'ont déjà aucune action.
- Pendant le repli d'une ligne (`leaving`).

### Conversations

Une ligne de conversation agit sur `rowUids`, tous ses membres, exactement comme ses boutons.

### Mouvement réduit

Sous `prefers-reduced-motion: reduce`, le retour et le repli sont instantanés. `useRowExit` honore
déjà cette préférence.

## L'action différée

### Au geste

1. Les caches sont patchés **tout de suite** : la ligne sort de la page et du bloc de flux, et les
   compteurs du dossier bougent. La copie d'avant (snapshot) est gardée.
2. Aucun appel serveur n'est fait à ce moment.
3. Une notification s'affiche : durée 5 000 ms, barre de décompte, bouton **Undo**.

### Envoi

L'appel serveur (`api.moveMessages`) part au premier de ces événements :

- l'échéance des 5 s ;
- un nouveau geste différé ;
- un changement de dossier, de page ou de compte, ou le démontage de la liste ;
- `visibilitychange` vers `hidden`, ou `pagehide` (onglet masqué ou fermé).

Sur `pagehide`, la requête part avec `keepalive: true`, sans quoi le navigateur peut l'abandonner
avec la page ; `request()` doit donc accepter cette option.

En cas d'échec, la copie est restaurée et une notification d'erreur s'affiche : le comportement
actuel de `useMoveMessages`.

### Undo

Undo restaure la copie. La ligne réapparaît à sa place, les compteurs reviennent, et rien n'a
quitté le navigateur.

### Protection contre le rafraîchissement

Le rafraîchissement de 60 s peut fusionner un bloc 0 frais (`refreshFirstBlock`) qui contient
encore le mail, puisque le serveur n'a rien reçu. La liste **filtre donc à l'affichage les uid en
attente** tant que l'action n'est pas envoyée ou annulée.

### Découpage du code

`useMoveMessages` (`modules/mail/messages.ts`) est scindé en deux parties, partagées par la
mutation actuelle et par le nouveau hook, sans aucun doublon :

- **patch + snapshot** : ce que fait aujourd'hui son `onMutate` ;
- **envoi + rollback** : l'appel serveur, et la restauration si celui-ci échoue.

Le nouveau hook `useDeferredMove` (`modules/mail/list/`) tient une seule action en attente, son
minuteur et son snapshot.

### Limite acceptée

Sur tablette, glisser le mail ouvert dans le lecteur fait avancer le lecteur (`reportDeparted`),
comme le bouton. Undo remet la ligne mais ne rouvre pas ce mail.

## La notification

`useToasts` gagne deux options facultatives, sans effet sur les notifications existantes :

- `durationMs`, qui remplace les 3 000 / 8 000 ms par défaut ;
- `countdown`, qui dessine sous le texte une barre qui se vide sur cette durée.

La barre suit la pause au survol qui existe déjà, et se fige sous mouvement réduit. Sa couleur est
le `--status-tone` du toast.

## La ligne

- **La bande** (couleur, icône, libellé) est dessinée derrière la `Row`, dans
  `.message-row-slot`. Elle n'est pas un `gridcell` : la ligne garde ses **quatre cellules**, et la
  navigation au clavier ne change pas. La bande est `aria-hidden`.
- **Le déplacement** est un `transform: translateX` écrit directement sur l'élément pendant le
  mouvement, sans rendu React par événement de pointeur. React ne rend qu'aux changements d'état :
  seuil franchi ou non, relâché.
- **La couleur par action** : `seen` → `--action-primary`, `flag` → `--warning`, `archive` →
  `--success`, `delete` → `--danger`. Texte et icône en `--status-fg` sur la bande pleine.
- **Lisibilité** : un test calcule le contraste du blanc sur chaque couleur de bande, pour les
  **16 combinaisons** palette × mode, à partir des fichiers de palette et non dans un navigateur
  (voir `frontend-theming.md`). Le libellé fait 14 px, donc le seuil est **4,5:1**.
  - Constaté à la conception : blanc sur `#16a34a` ≈ 3,3:1, blanc sur le `--danger` sombre
    `#f87171` ≈ 2,8:1. Tous deux échouent.
  - Toute couleur qui échoue reçoit un rôle dédié (`--swipe-archive`, …) déclaré dans les 16 blocs,
    et assombri juste assez pour passer.
- **Glisser-déposer** : la ligne est `draggable` pour le glisser-déposer vers un dossier. À
  vérifier sur Chrome Android que ce glisser-déposer natif ne démarre pas sous le doigt. S'il
  démarre, `draggable` est retiré pendant un geste tactile.

## Accessibilité

Le glissement n'est qu'un **raccourci**. Chaque action reste atteignable sans lui :

- au clavier et au lecteur d'écran, par la grille ;
- au doigt, par l'appui long puis la barre de sélection, ou par le lecteur de mail.

## Fichiers

| Fichier | Changement |
|---|---|
| `src/scotty.microservice/Models/UserPreferences.cs` | deux définitions |
| `src/frontend/src/hooks/usePreferences.ts` | clés, type `SwipeAction`, `swipeActionOf` |
| `src/frontend/src/hooks/useSwipe.ts` | **nouveau** : le geste, sans rien savoir du mail |
| `src/frontend/src/modules/mail/list/useDeferredMove.ts` | **nouveau** : attente, envoi, Undo |
| `src/frontend/src/modules/mail/messages.ts` | `useMoveMessages` scindé en patch / envoi |
| `src/frontend/src/modules/mail/list/MessageRow.tsx` | bande + branchement de `useSwipe` |
| `src/frontend/src/modules/mail/list/MessageList.tsx` | actions de geste, filtre des uid en attente |
| `src/frontend/src/hooks/useToasts.ts`, `components/Toasts.tsx` | `durationMs`, `countdown` |
| `src/frontend/src/modules/settings/general/GeneralPage.tsx` | section Swipe gestures |
| `src/frontend/src/styles/mail.css` (+ palettes si besoin) | bande, `touch-action` |
| `src/frontend/src/locales/{en,fr}/*.json` | libellés |
| `src/frontend/probes/mobile-layout.html` | cas « swipe » |

## Tests

- **`useSwipe`** :
  - le seuil, dans les deux sens ;
  - un geste vertical ignoré ;
  - la souris ignorée ;
  - un sens à `none` qui ne bouge pas ;
  - l'annulation par `pointercancel`.
- **`useDeferredMove`**, avec des minuteurs simulés :
  - l'envoi à 5 s ;
  - Undo, qui restaure et n'envoie rien ;
  - l'envoi anticipé par un nouveau geste, par le démontage et par `visibilitychange` ;
  - l'échec serveur, qui restaure et affiche l'erreur.
- **`MessageRow` / `MessageList`** :
  - la bonne action appelée pour chaque réglage ;
  - l'action impossible, qui ne déclenche rien ;
  - le mode sélection, qui coupe le geste ;
  - `delete` dans la corbeille, qui ouvre la confirmation ;
  - l'uid en attente, filtré malgré un bloc 0 frais.
- **`useToasts`** : durée personnalisée, barre de décompte, pause.
- **`GeneralPage`** : les deux réglages enregistrés, les défauts affichés.
- **Contraste** : blanc sur chaque couleur de bande, pour les 16 combinaisons.
- **Serveur** : `UserPreferencesTests` couvre les deux clés, leurs défauts et le refus d'une valeur
  inconnue.
- **Géométrie** : la sonde `probes/mobile-layout.html` est lue sous **émulation tactile** à 360 et
  390 : la bande, la ligne décalée et la notification au-dessus de la barre d'onglets. jsdom ne
  calcule aucune mise en page.

## Hors périmètre

- Choisir le seuil ou la vibration dans les réglages.
- Un geste « lancé » : un mouvement rapide mais court qui déclencherait l'action.
- Un Undo pour Read / Unread ou Flag : l'action inverse est à un geste.
