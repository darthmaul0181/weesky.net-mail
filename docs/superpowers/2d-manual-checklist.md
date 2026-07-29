# Tranche 2d — comptes connectés : recette manuelle

Cette liste couvre tout ce que la suite automatisée **ne peut pas** prouver. Les 1617 tests backend et
les 2022 tests frontend passent, mais aucun d'eux n'ouvre une vraie session IMAP, ne parle à un vrai
serveur ManageSieve, et surtout **aucun ne calcule une mise en page** : jsdom n'a pas de moteur de
rendu, donc pas une seule des vérifications d'apparence ci-dessous n'a jamais été mesurée.

## 0. Prérequis — à faire avant tout le reste

- [ ] **La migration DDL n'a été jouée nulle part.** Appliquer
      `docs/superpowers/webmail-connected-accounts-tables.md` sur la base `snoopy_webmail` de
      l'environnement de test avant d'ouvrir le webmail. Sans elle, le service démarre mais toute
      requête touchant aux comptes connectés échoue.

- [ ] **🔴 RISQUE PRINCIPAL — Dovecot autorise-t-il ManageSieve avec les identifiants du compte
      lui-même ?** Un compte connecté s'authentifie désormais à ManageSieve **avec son propre mot de
      passe**, y compris pour une boîte partagée hébergée sur notre propre serveur : il ne passe
      **plus** par l'utilisateur master. C'est une déviation du plan validée en tâche 10 (le chemin
      master gardait un accès Sieve en écriture vivant après que le mot de passe de la boîte partagée
      avait changé, alors qu'IMAP échouait correctement).

      Si notre Dovecot n'autorise l'accès Sieve que via l'utilisateur master, **l'onglet Rules d'une
      boîte partagée locale renverra une 502**, et aucun test unitaire ne peut le détecter.

      Vérification en ligne de commande, avant même d'ouvrir le webmail :

      ```
      openssl s_client -connect <serveur>:4190 -starttls sieve
      ```

      puis une authentification SASL PLAIN avec **authzid vide**, l'adresse du compte partagé en
      authcid, et son propre mot de passe. Si l'authentification est refusée alors que la même boîte
      s'ouvre bien en IMAP, l'onglet Rules des comptes connectés est cassé et il faut soit rouvrir
      l'accès Sieve direct côté Dovecot, soit revenir au chemin master pour les seules boîtes locales.

      **Faire cette vérification en premier** : elle conditionne toute la section 6.

## 1. Connexion d'un compte

- [ ] `/settings/accounts` : connecter une **boîte partagée locale** (pas de domaine externe
      sélectionné). Adresse + mot de passe, le panneau annonce le succès et la tuile apparaît.
- [ ] Connecter un **vrai compte externe** sur un domaine préalablement créé dans
      Administration → External domains. La tuile apparaît avec le nom du domaine.
- [ ] Mot de passe volontairement faux : le message doit parler d'identifiants refusés, **pas**
      afficher un code brut ni renvoyer vers la page de connexion.
- [ ] Adresse inconnue / domaine injoignable : message lisible, aucune tuile créée.
- [ ] Répéter cinq échecs de connexion d'affilée : vérifier qu'un éventuel 429 s'affiche comme une
      limite de débit et non comme un mauvais mot de passe. *(Voir aussi la note de déploiement en
      fin de document.)*

## 2. Bascule entre comptes

- [ ] Menu identité (bas de la colonne des dossiers) → choisir le compte connecté. Attendu :
      **cache vide** (aucun message de la boîte précédente ne subsiste une seule frame), **INBOX
      sélectionné**, et le **bandeau du menu mis à jour** (deuxième ligne nommant la boîte, pastille
      `is-connected`).
- [ ] Rebasculer vers le compte principal : mêmes trois points, en sens inverse.
- [ ] Enchaîner A → B → A rapidement : aucun message de B ne doit apparaître sous A.
- [ ] Recharger la page (F5) sur un compte connecté : la session reste sur ce compte.
- [ ] Basculer alors qu'un brouillon est ouvert dans le compositeur : le garde-fou de sortie
      (Enregistrer / Abandonner / Continuer) doit s'afficher **avant** que le compte change.

## 3. Envoi et réception depuis un compte connecté

- [ ] Réception : envoyer un mail vers la boîte connectée depuis l'extérieur, il arrive dans son
      INBOX et **pas** dans celle du compte principal.
- [ ] Composer depuis le compte connecté : le sélecteur **From est restreint aux identités de ce
      compte**. L'adresse du compte principal ne doit **jamais** y figurer.
- [ ] Envoyer : le message part bien depuis le serveur de ce compte.
- [ ] **La copie Sent est classée sur le serveur du compte connecté**, pas dans le dossier Sent du
      compte principal. Le vérifier dans le dossier, pas seulement au retour de l'API.
- [ ] Pièce jointe : ajouter un fichier, envoyer, vérifier qu'il arrive. Puis basculer de compte et
      confirmer que les pièces jointes en attente de l'autre compte ne sont pas visibles.
- [ ] Répondre à tous depuis le compte connecté : **l'adresse du compte connecté est retirée** des
      destinataires, et **l'adresse principale de l'utilisateur reste** dans la liste (elle n'est pas
      la sienne dans ce contexte).

## 4. Identités libres

- [ ] `/settings/identities` sous un compte connecté : ajouter une **identité libre** (une adresse
      qui n'est pas un alias du compte). Elle doit être acceptée.
- [ ] Composer et envoyer avec cette identité : le message part avec cette adresse en From.
      ⚠️ Si le serveur distant refuse l'expéditeur, le message d'erreur doit **nommer l'adresse**.
- [ ] Supprimer l'identité libre, vérifier qu'elle disparaît du sélecteur du compositeur.

## 5. Mot de passe principal

- [ ] **Changement depuis l'application** (`/settings/account` → changer le mot de passe) :
      après le changement, **tous les comptes connectés continuent de fonctionner** sans ressaisie.
      Ouvrir chacun d'eux et lire un message pour le confirmer réellement.
- [ ] **Changement hors application** (directement en base, ou via un autre outil) : au retour dans
      le webmail, chaque compte connecté affiche **`Password needed`** — dans le menu identité, et
      en pleine page si on tente d'ouvrir ce compte.
- [ ] Ressaisir le mot de passe du compte connecté depuis `/settings/accounts` : il redevient
      utilisable immédiatement, sans reconnexion.
- [ ] Pendant l'état `Password needed`, vérifier que l'utilisateur n'est **jamais déconnecté du
      webmail** (le backend répond 409, pas 401 — c'est précisément ce qui est en jeu).

## 6. Règles Sieve

*(Conditionné par le prérequis 0.)*

- [ ] Compte connecté sur un **domaine externe où Sieve est configuré** (hôte + port renseignés) :
      l'onglet **Rules** est présent, la liste des règles se charge, une règle créée est bien écrite
      dans le script du **compte connecté** et non dans celui du principal.
- [ ] Le sélecteur de dossier de l'assistant de règles liste les dossiers **du compte connecté**.
- [ ] Compte connecté sur un domaine externe **sans Sieve** : l'onglet **Rules est absent** de la
      navigation Settings.
- [ ] Boîte partagée locale : onglet Rules présent et fonctionnel — **c'est le point que le
      prérequis 0 conditionne**. Une 502 ici confirme le risque master user.

## 7. Administration — domaines externes

- [ ] Administration → **External domains** : créer un domaine (nom + IMAP hôte/port/sécurité +
      SMTP hôte/port/sécurité + la paire Sieve optionnelle). Il apparaît dans la liste de choix du
      panneau de connexion.
- [ ] Modifier un domaine, vérifier que la modification est prise en compte.
- [ ] **Supprimer un domaine sur lequel des comptes sont encore connectés : ce doit être refusé**,
      avec un message qui l'explique.
- [ ] Supprimer un domaine sans compte rattaché : accepté.

---

# Apparence — à regarder dans un vrai navigateur

**Rien de tout ce qui suit n'a jamais été mesuré** : jsdom ne calcule aucune géométrie, donc aucun
test du dépôt ne peut voir un débordement, une troncature manquante ou un contraste insuffisant.

Là où la couleur intervient, regarder les **quatre combinaisons thème × palette** : clair et sombre,
croisés avec deux palettes prises aux extrêmes de la gamme (`night`, la palette par défaut, et
`classic`). Les huit palettes n'ont pas besoin d'y passer, mais les deux thèmes, si.

## 8. Menu identité (colonne des dossiers, largeur 180px)

- [ ] Un **nom d'affichage long** à côté de la puce `Password needed` **et** de la coche du compte
      actif, sur la même ligne : rien ne doit déborder ni chevaucher.
- [ ] Sur un compte connecté, la **deuxième ligne du bandeau** (nom de la boîte) : troncature propre,
      pas de débordement hors de la colonne.
- [ ] Le menu **s'ouvre vers le haut sans être rogné** par `.context-pane` — vérifier avec une liste
      de 4-5 comptes, et sur une fenêtre courte en hauteur.
- [ ] **Contraste de la pastille `is-connected`** : lisible dans les quatre combinaisons.

## 9. État `Password needed` pleine page (module mail)

- [ ] Ouvrir un compte dont les identifiants ne déchiffrent plus. Vérifier la hauteur du panneau
      dans `.app-content`, la mesure de texte (~46 caractères par ligne), et que le bouton d'action
      garde une **largeur automatique** — il ne doit pas s'étirer sur toute la largeur.

## 10. Page Connected accounts (`/settings/accounts`)

- [ ] **Troncature de la ligne secondaire de la tuile** (adresse + nom de domaine) : ellipse propre
      avec une adresse longue et un nom de domaine long.
- [ ] Le **panneau de connexion en pleine largeur de page** : sur un écran large, il doit rester
      dans son plafond de largeur et ne pas s'étaler.
- [ ] Une liste vide, une liste à un compte, une liste à cinq comptes.

## 11. Dialogue External domains

- [ ] **Le formulaire le plus dense du produit** (480px : nom + deux triplets hôte/port/sécurité +
      la paire Sieve optionnelle). Vérifier l'alignement de la colonne de libellés, que rien ne
      déborde, et le rendu avec des messages d'erreur affichés sous plusieurs champs à la fois.

## 12. `/settings/identities` sous un compte connecté

- [ ] **Alignement des tuiles sans la colonne étoile** (un compte connecté n'a pas de défaut à
      choisir) : les tuiles ne doivent pas se décaler par rapport au compte principal.
- [ ] **L'indication d'adresse libre sous la colonne de libellés** (110px) : elle tient sur deux
      lignes, sans passer sous le contrôle.
- [ ] Le tag **`Account address`** en capitales, `flex-shrink: 0`, dans un volet Settings étroit :
      il ne doit ni écraser le nom à côté de lui ni sortir de la tuile.

---

## Note de déploiement (hors recette, à traiter séparément)

La limite de débit `login` partitionne sur `RemoteIpAddress` et **aucun middleware de forwarded
headers n'est enregistré**. Derrière le reverse proxy, la partition est donc de fait globale :
5 tentatives de connexion d'un compte peuvent renvoyer 429 sur `POST /api/login` **pour tout le
monde**. Le problème préexiste à cette tranche mais elle l'aggrave, puisqu'elle ajoute un second
chemin qui consomme le même quota.
