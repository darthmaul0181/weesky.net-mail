# Agenda 5e2 — résidus

Les mineurs relevés par les relectures de tâche et laissés ouverts, triés « restent différés » par la revue finale du 14 septembre 2026. Chaque ligne dit d'abord ce qui se verrait, puis où cela se trouve. À reprendre quand le code voisin bouge.

| Tâche | Résidu |
|---|---|
| 1 | Rien à l'écran, un fichier plus long à parcourir : cinq types vivent dans `SchedulingDecider.cs` (six relevés en tâche 1, voulus par le plan ; `WriteOrigin` en est sorti depuis) |
| 1 | Rien à l'écran, mais un test qui ne protège rien : `CalendarEntitiesTests` compare les attributs de colonne à eux-mêmes, la base en mémoire ignorant `[Column]` |
| 1 | Rien aujourd'hui ; si une révision de contact portait un jour la cause « scheduling » (10 caractères), la base la refuserait : `ContactRevision.Cause` reste limité à 8 caractères, les contacts n'écrivant jamais cette cause |
| 2 | Un invité délégué à plusieurs personnes peut voir sa délégation mal relue par un autre agenda : Ical.Net écrit le paramètre multi-valeur `DELEGATED-TO` en une seule chaîne entre guillemets (comportement de la bibliothèque, antérieur) |
| 3 | La date de dernière modification d'un événement avance quand le serveur envoie ses invitations, sans que le fichier ait changé : `SetSchedulingAsync` fait bouger `updated_at`, donc `Last-Modified`, mais pas l'ETag, sur lequel les clients CalDAV se règlent |
| 4 | Rien à l'écran, un test plus lent que nécessaire : `TryEnqueue_RefusesWhenTheQueueIsFull` remplit les 1 000 places de la capacité par défaut alors que le constructeur accepte une capacité plus petite |
| 4 | Un utilisateur authentifié malveillant pourrait faire grossir la mémoire du service jusqu'à environ 2 Go au pire : la file du compte de service est bornée en nombre de mails, pas en octets |
| 4 | Pour mémoire, corrigé : les lignes de plus de 75 octets des CANCEL et des REPLY, relevées en tâche 4, sont désormais pliées (`ItipCalendar.Fold`) |
| 5 | Enregistrer un événement avec invités quand la requête nomme un compte connecté fait partir les invitations par le compte de service, sans copie dans les Envoyés : la session n'est ouverte que pour le compte principal, faute d'un `ResolvePrimaryAsync` |
| 5 | Une écriture avec invités coûte plus de temps serveur que nécessaire : le même fichier passe quatre fois dans Ical.Net (décideur, empreinte, lecture, composition) |
| 5 | Idem en plus petit : `InvitationMailer.Compose` analyse de nouveau chaque destinataire que le planificateur a déjà filtré |
| 6 | Trois réponses d'un même invité ouvertes dans le désordre (A, puis C, puis B) peuvent laisser la plus ancienne affichée quand la ligne de l'agenda porte déjà la même réponse sans tampon, écrite par un client CalDAV : rien ne date alors la réponse stockée |
| 6 | Une réponse d'invité sans `DTSTAMP` en UTC est appliquée sans dater la ligne : l'ancien tampon `X-WEESKY-REPLY-STAMP` reste, et c'est à lui qu'une réponse ouverte ensuite est comparée |
| 6 | Une alarme qui nomme un destinataire (`ATTENDEE` dans un `VALARM`) voit sa ligne prendre la réponse de l'invité de même adresse : la réécriture textuelle du `PARTSTAT` ne distingue pas les composants (antérieur) |
| 7 | Une salle (`urn:uuid:`) disparaît de l'événement au premier enregistrement depuis le webmail, et un participant `sip:` devient `mailto:sip:` : Ical.Net ne relit pas ces lignes et les perd à toute réécriture (antérieur, ticket à ouvrir) |
| 7 | Le texte des puces d'invités pourrait être rogné sur un autre système que Windows : la hauteur de ligne 1,25 n'a été mesurée qu'avec Segoe UI |
| 7 | Cas théorique : deux invités dont l'adresse décodée de l'un est l'orthographe encodée de l'autre se confondraient à l'enregistrement, par collision de clés dans `IcsComposer.PlaceAttendees` |
| 8 | Rien à l'écran : le séparateur « · » de l'encart est écrit dans le code plutôt que dans les traductions |
| 8 | Une relecture de l'agenda pour rien après une réponse d'invité non appliquée : le cache de l'agenda est invalidé même quand `applied` vaut `false` |
| Ruling 22 | Un invité qui connaît l'UID d'un rendez-vous et l'adresse d'un autre invité peut forger sa réponse : la réponse forgée est écrite dans l'événement stocké et synchronisée sur les appareils de l'utilisateur, sans qu'aucun mail ne parte ; une réponse authentique reçue ensuite la remplace, sauf si la réponse forgée porte une date plus récente (le tampon `X-WEESKY-REPLY-STAMP` compare les horodatages). **Décidé le 14 septembre 2026, option A : pas de changement.** `ApplyReply` ne compare pas l'expéditeur du mail à l'`ATTENDEE` de la réponse, comme Google Agenda et Outlook, qui ne protègent que la réception des invitations ; le RFC 6047 demande d'ignorer une réponse forgée mais n'accepte comme preuve qu'une signature S/MIME, absente des agendas courants, et vérifier l'expéditeur rejetterait des réponses légitimes (Gmail répond toujours depuis l'adresse principale, un délégué Outlook répond « de la part de ») |

## Compte d'envoi des invitations

Les mineurs laissés ouverts par la revue finale du compte d'envoi (Administration > Application), dans le même ordre : ce qui se verrait, puis où.

| Résidu |
|---|
| Au clavier ou avec un lecteur d'écran, la fenêtre d'un domaine externe ne se ferme pas avec Échap et n'est pas annoncée comme une boîte de dialogue : `ExternalDomainDialog` n'a ni `role="dialog"` ni `aria-modal`, alors que le crochet partagé `useDialogFocusTrap` le rend désormais facile |
| Dans la même fenêtre, « Aucune » reste proposée même quand le serveur refuse une connexion sans chiffrement, et l'enregistrement échoue alors sur un message général : seule la fenêtre du compte d'envoi lit `allowCleartext` |
| Tab, depuis un élément de la fenêtre qui a le focus sans être dans l'ordre de tabulation (`tabindex="-1"`), revient au premier élément au lieu d'aller au suivant : le piège de focus de `useDialogFocusTrap` ne connaît que les éléments tabulables |
| Les fenêtres construites sur `.field` (`AddEditDomainModal`) gardent des champs de 34 caractères sur un téléphone, plus étroits que leur fenêtre : seules les lignes `.field-h` passent à pleine largeur sous 640 px |
| Personne n'a vérifié si `.rule-wizard-input` déborde sur un téléphone : jamais mesuré |
| Avec deux instances du service, l'une continuerait d'envoyer avec l'ancien compte (ou de refuser sans compte) jusqu'à son redémarrage, et chacune ferait son propre test de connexion : le compte est mis en cache par processus et la limite d'un test à la fois est un sémaphore statique (`DESIGN.md`, § Configuration) |
| Sur une plateforme `generic`, personne ne peut configurer le compte d'envoi, et les modifications faites depuis un téléphone n'envoient aucune invitation : la politique administrateur n'y a pas de gestionnaire (conséquence voulue de « l'administration seulement ») |
| Ailleurs que sur cet écran, certaines erreurs de connexion SMTP ou IMAP changent de texte : `MailConnectionFactory` distingue maintenant l'échec de la connexion sécurisée et le délai dépassé, là où il disait « Unable to connect to the mail service » |
| Rien à l'écran, mais aucun test ne couvre un envoi réel depuis un appareil : les tests CalDAV passent par `RecordingInvitationScheduler`, jamais par la file du compte de service |
| La carte « mot de passe illisible » n'a pas de maquette validée : son aspect (bordure rouge en tirets, icône d'alerte) a été choisi à l'implémentation |
| Rien à l'écran aujourd'hui : rendre le focus au titre de la section quand la carte change d'état repose sur l'ordre dans lequel React 18 détache une référence avant de retirer le nœud (un test le garde) |
| Un administrateur peut encore sonder les ports internes les uns après les autres avec « Tester la connexion » : la limite d'un test à la fois empêche les sondages en parallèle, pas les sondages successifs (réservé aux administrateurs) |

## Avant la mise en service

**Corrigé.** Un événement de production qui a déjà des invités, dont l'utilisateur est l'organisateur, et qui a été créé avant 5e2 depuis Thunderbird ou DAVx⁵, prend désormais la main silencieusement au premier enregistrement depuis le webmail : rien n'est envoyé aux invités si rien ne change pour eux (une note, un rappel, un changement d'agenda), et seul ce qui a réellement bougé part — « Mise à jour » aux invités qui restent quand autre chose a changé, « Invitation »/« Annulation » aux seuls invités ajoutés ou retirés — comme le prévoit la table de la décision 9 de la [spec](../history/specs/2026-09-12-webmail-calendar-5e-invitations-design.md). Rien à annoncer aux utilisateurs.
