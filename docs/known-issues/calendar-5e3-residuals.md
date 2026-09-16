# Agenda 5e3 — résidus

Les mineurs relevés par les relectures de paquet et laissés ouverts, triés « restent différés » par la revue finale du 15 septembre 2026. Chaque ligne dit d'abord ce qui se verrait, puis où cela se trouve. À reprendre quand le code voisin bouge.

| Résidu |
|---|
| Avec deux instances du service, l'autre ne voit une clé régénérée qu'au redémarrage : le fournisseur est un cache par processus (même limite que le compte d'envoi) |
| Rien ne dit à l'administrateur qu'un serveur mail appelle avec une mauvaise clé après un premier succès : pas de date du dernier refus (décision 8, écarté) |
| Une réponse enfermée dans un mail transféré n'est appliquée par aucun chemin (comme à l'ouverture) |
| Un utilisateur qui se connecte au webmail par un alias n'a jamais ses réponses appliquées à la livraison (`UnknownMailbox`) : pas de résolution d'alias côté livraison |
| Le ruling 22 (réponse forgée) s'applique désormais sans qu'un utilisateur voie la carte : consenti par l'administrateur à l'activation (décision 4) |
| Rien à l'écran : `ReplaceAsync` du store rend un `bool` là où `SetEnabledAsync` et `DeleteAsync` rendent un `DeliveryKeyWrite`, seule incohérence de la petite API (`IDeliveryKeyStore.cs`) |
| Rien à l'écran, un fichier de plus à ouvrir pour trouver un type : `IDeliveryKeyStore.cs` et `IDeliveryKeyProvider.cs` portent chacun deux types, `DeliveryMailReader.cs` trois — la cohésion l'a emporté sur « un type par fichier » |
| Rien à l'écran, une ligne morte : la garde `if (request is null)` de `SetDeliveryReplies` n'est jamais atteinte, le binder MVC répondant 400 avant elle sur un corps absent |
| Rien à l'écran, un appel pourtant accepté sans que « Dernier appel reçu le … » n'avance : `RecordCallAsync` abandonne silencieusement après trois défaites contre une écriture concurrente de l'écran (décision 8 : pas de journal par tentative) |
| Rien à l'écran : `MaxParts` (256) ne borne que la marche dans les parties une fois le mail entièrement parsé, c'est la limite de requête à 5 Mo qui borne réellement la mémoire (`DeliveryMailReader.cs`) |
| Rien à l'écran dans l'usage normal, un caractère mal formé possible dans le journal : `LogText.Safe` peut couper une paire de substituts UTF-16 à la troncature |
| Rien à l'écran, un chemin non testé : aucun test ne pose une partie calendrier dans un jeu de caractères autre qu'UTF-8 à travers la porte |
| Cosmétique dans le journal : un caractère de formatage Unicode (U+200B…) dans `X-Delivery-Mailbox` survit à `DeliveryMailbox` et s'y retrouve tel quel |
| Rien à l'écran, un code dupliqué : le gestionnaire de la touche Échap est recopié dans `DeliveryKeyDialog` comme dans les autres fenêtres plutôt que de vivre dans `useDialogFocusTrap` |
| Rien à l'écran, une couverture manquante : aucun test DOM ne couvre les états `loading`/`loadFailed` de la section |
| Rien à l'écran, un test manquant : aucun test de bout en bout ne vérifie qu'un corps de plus de 5 Mo répond 413, l'attribut `[RequestSizeLimit]` étant pris pour acquis |
