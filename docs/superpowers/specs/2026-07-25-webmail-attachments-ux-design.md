# Webmail — Attachments UX : drag & drop et viewer d'images

**Date :** 2026-07-25
**Statut :** design validé, prêt pour la planification d'implémentation
**Amont :** 2c1 (composeur, staging XHR), 2c2b (reader inline — le mur cookie Lax / cross-origin),
2c3a (brouillons — le tray et son cycle staged actuels). Tranche frontend pure : aucun changement
backend, les octets viennent des endpoints existants.

---

## 1. Le problème

Deux frictions autour des pièces jointes :

- **Composeur** : l'ajout passe uniquement par le bouton « Attach files ». Le geste universel des
  webmails — glisser les fichiers sur le message — n'existe pas.
- **Lecteur** : une pièce jointe image ne se regarde qu'en la téléchargeant. Pour vérifier une
  photo ou une capture, un aller-retour par le gestionnaire de fichiers est disproportionné.

## 2. Décisions d'UX (validées)

- **Zone de drop : toute la surface du composeur.** Un overlay « Drop files to attach » couvre le
  composeur dès qu'un drag porteur de fichiers le survole ; tout fichier lâché part dans le tray.
  Pas d'insertion inline au point de chute (écarté en design).
- **Chevron sur les images seulement.** Dans le lecteur, seules les pièces `image/*` gagnent le
  contrôle scindé chip + chevron ↑ → menu **Download** / **View**. Les autres pièces gardent la
  chip actuelle (clic = téléchargement), sans menu à entrée unique.
- **Viewer dans le lecteur seulement.** Le tray du composeur ne change pas — on vient de choisir
  le fichier, on sait à quoi il ressemble.
- Le clic principal sur une chip — image ou non — reste le téléchargement, inchangé.

## 3. Composeur — drag & drop

- La racine de `ComposeView` écoute `dragenter` / `dragleave` / `dragover` / `drop` avec un
  **compteur d'entrées** : `dragleave` se déclenche à chaque frontière d'enfant, l'overlay ne
  s'éteint que quand le compteur retombe à zéro (anti-scintillement classique).
- Le drag n'active l'overlay que s'il **porte des fichiers** (`dataTransfer.types` contient
  `Files`) — un drag de texte ou d'URL est ignoré de bout en bout.
- Le drop appelle le **`addFiles` existant** : même staging XHR avec progression, mêmes erreurs
  par fichier (taille / quota du compte), même `markDirty`. Aucune nouvelle mécanique d'upload.
- Le `preventDefault` de l'overlay (dragover + drop) empêche à la fois la navigation du
  navigateur vers le fichier et toute insertion sauvage dans Squire.
- Le bouton « Attach files » et l'input picker restent tels quels.

## 4. Lecteur — contrôle scindé et viewer

### 4.1 Le chip image

- Détection : `contentType` commençant par `image/`, sur les pièces non-inline déjà filtrées
  aujourd'hui (`!isInline`).
- La chip devient un **contrôle scindé** : la partie principale garde le comportement actuel
  (clic = `download()`), le chevron ↑ accolé ouvre un `DropdownMenu` **vers le haut** — la rangée
  des pièces vit en bas de colonne — avec deux entrées : **Download** (le même `download()`) et
  **View** (ouvre le viewer). Le chevron porte un `aria-label` nommant le fichier
  (« More actions for {fileName} »).
- Pièce non-image : chip simple actuelle, aucun chevron.

### 4.2 `AttachmentViewerModal`

- Forme des dialogues du site : ✕ seule sortie, titre = nom du fichier, la taille à côté.
- Chargement par **`requestBlob(mailAttachmentUrl(...))` → object URL** — obligatoire, pas un
  choix : le cookie est Lax et l'API cross-origin, un `<img src>` direct partirait sans cookie
  (le même mur qui a mené aux data URIs du reader inline en 2c2b). Pas de conversion data URI :
  un object URL fait mieux pour une photo lourde.
- L'object URL est **révoqué à la fermeture** (et au remplacement).
- États : chargement (spinner/texte), erreur (message dans la modal, pas un toast).
- L'image se cale en `max-width` / `max-height` dans la popup, ratio préservé. Pas de zoom, pas
  de navigation entre les images du message (non demandés).
- Un bouton **Download** en pied de modal réutilise `download()` — on vient de vérifier l'image,
  pas besoin de rouvrir le menu.

## 5. Erreurs & sécurité

- Le viewer n'introduit aucun nouveau chemin de données : mêmes endpoints authentifiés, même
  `requestBlob`. Un échec de fetch s'affiche dans la modal ; le `downloadError` existant du
  lecteur reste le canal des téléchargements.
- Le drop ne contourne rien : chaque fichier passe par le staging existant et ses caps ; un
  fichier refusé s'affiche en erreur dans le tray comme aujourd'hui.
- L'object URL vit le temps de la modal — révoqué à la fermeture, pas de fuite mémoire sur une
  session de lecture longue.

## 6. Tests

- **Drag & drop** : enter/leave imbriqués (l'overlay ne clignote pas et s'éteint à zéro) ; drop
  → `addFiles` + dirty ; drag sans fichiers ignoré (pas d'overlay) ; `preventDefault` posé.
- **Chip/chevron** : chevron présent sur `image/*` seulement ; menu Download/View ; le clic
  principal télécharge toujours ; pin de régression sur la chip non-image.
- **Viewer** : View → modal, `requestBlob` mocké → l'image porte l'object URL ; révocation à la
  fermeture ; chemin d'erreur affiché dans la modal ; Download du pied de modal appelle
  `download()`.
- Suite complète, `typecheck`, `lint`, `build` propres.

## 7. Vérification manuelle (dev)

Glisser deux fichiers sur le composeur (l'overlay apparaît, s'éteint, les fichiers montent) ;
glisser du texte (rien) ; ouvrir un message avec une photo et un PDF — la photo a le chevron,
View l'affiche, Download télécharge, le PDF garde sa chip simple.

## 8. Hors périmètre

- Insertion inline au point de chute dans le corps (écarté) ; viewer dans le tray du composeur ;
  View sur les PDF ; zoom / navigation multi-images dans le viewer ; drag & drop vers les
  dossiers (existe déjà pour les messages, sans rapport).
