# Agenda 5e3 — les réponses d'invités appliquées à la livraison

Suite de [5e](2026-09-12-webmail-calendar-5e-invitations-design.md), livrée par les PR #74 (5e1,
recevoir) et #75 (5e2, inviter). Branche `caldav-delivery-replies`. Relue le 14 septembre 2026
par trois agents relecteurs (complétude, sécurité, Dovecot/Sieve), contre le code, une sonde
MailKit/MimeKit 4.17 et la documentation Dovecot 2.4 ; leurs constats sont intégrés.

**Livrée le 15 septembre 2026** sur la branche `caldav-delivery-replies` (plan
`docs/superpowers/plans/2026-09-14-webmail-calendar-5e3-delivery-replies.md`).

## Le problème

Depuis 5e2, quand un invité répond à une invitation envoyée par le webmail, sa réponse (un mail
portant un fichier calendrier `method=REPLY`) n'est reportée dans l'agenda de l'organisateur
**qu'au moment où il ouvre ce mail**. Tant qu'il ne l'ouvre pas, son agenda — et ses appareils
synchronisés en CalDAV — ignorent que l'invité a accepté ou refusé.

## Ce que fait la tranche

Le serveur mail applique la réponse **au moment où il livre le mail**, sans que personne ne
l'ouvre :

1. Dovecot livre le mail. Une règle Sieve globale reconnaît une réponse d'invitation et passe le
   mail entier à un petit script.
2. Le script transmet le mail au microservice, sur une porte d'entrée interne, avec l'identifiant
   de la boîte destinataire et une clé partagée.
3. Le microservice retrouve l'événement et y inscrit la réponse, par le code qu'utilise déjà
   l'ouverture du mail.
4. Le mail est livré normalement. À l'ouverture, la carte dit « déjà appliquée ».

Un réglage d'administration active le mécanisme ; **désactivé par défaut**, le webmail se comporte
comme aujourd'hui.

## Décisions

**1. Sieve, pas Postfix ni les notifications Dovecot.** Sieve connaît le propriétaire de la boîte
au moment où il tourne (pas d'alias à résoudre) et lit le mail complet. Postfix ne sait pas
encore dans quelle boîte va le mail ; les notifications de Dovecot ne transportent pas le contenu
et le microservice ne peut pas aller le lire (il n'a pas le mot de passe de l'utilisateur, et le
super-utilisateur IMAP a été écarté en 2a).

**2. Le script transmet le mail entier ; le microservice fait tout le reste.** La sélection de la
partie calendrier reste dans du code testé, **la même règle que l'ouverture du mail** (celle qui
a été corrigée pour Outlook.com le 14 septembre : une partie annoncée `method=REPLY` est
retenue). Un script qui découperait lui-même le mail aurait sa propre règle, non testée, et les
deux chemins divergeraient. La règle Sieve n'est qu'un **tri grossier** : elle peut retenir trop
(un mail transféré, une partie au-delà de ce que le microservice lit), jamais l'inverse ne
compte, c'est le microservice qui juge.

**3. L'ouverture du mail reste un secours.** Appliquer deux fois la même réponse est sans effet
(une réponse déjà inscrite n'est jamais réécrite). Couper l'ouverture quand le réglage est activé
perdrait silencieusement une réponse chaque fois que la livraison n'a pas abouti : microservice
arrêté, script hors délai, règle Sieve absente — et le webmail n'a aucun moyen de vérifier ce que
Dovecot exécute.

| Réglage | À la livraison | À l'ouverture du mail |
|---|---|---|
| désactivé (défaut) | rien | applique, comme aujourd'hui |
| activé | applique | ne fait rien si déjà inscrite ; sinon applique |

**4. Aucun filtrage de l'expéditeur dans le microservice — et ce que ça change par rapport au
ruling 22.** Un mail arrivé jusqu'à Sieve a passé rspamd : le filtrage est la responsabilité de
la pile mail en amont, pas du webmail — en faire serait implémenter un antispam. Le ruling 22 de
5e2 (pas de comparaison entre l'expéditeur du mail et l'`ATTENDEE` de la réponse) reste ; un
filtre DMARC n'y changerait rien, une réponse forgée partant d'un domaine correctement
authentifié passerait. **Mais le risque accepté par ce ruling était borné par un geste : la
réponse ne s'écrivait que quand l'organisateur ouvrait le mail, la carte sous les yeux.** Avec ce
réglage activé, une réponse forgée par qui connaît l'UID d'un rendez-vous et l'adresse d'un invité
s'écrit sans que personne ne la voie, et se propage aux appareils CalDAV ; deux résidus de 5e2
suivent (une ligne `ATTENDEE` dans un `VALARM` prend aussi la réponse ; un tampon
`X-WEESKY-REPLY-STAMP` forgé plus récent l'emporte sur la réponse authentique). Ce que la réponse
peut changer reste borné par la décision 12. **C'est un consentement éclairé de l'administrateur
qui active le réglage** : la phrase d'aide de la carte le dit.

**5. On conçoit pour deux machines distinctes**, même si Dovecot et le microservice partagent
aujourd'hui la même. Le HTTPS du microservice chiffre l'échange ; la clé partagée authentifie
l'appelant ; une restriction nginx par adresse est conseillée en plus.

**6. La clé est générée dans Administration, montrée une seule fois, stockée en empreinte.** Pas
dans l'EnvironmentFile (c'est ce que 5e2 vient d'abandonner pour le compte d'envoi : changer la
clé demanderait un accès au serveur et un redémarrage) ; pas de certificat client (trop lourd pour
une porte). La base ne garde qu'un hachage : une fuite de la base ne permet pas d'appeler la
porte. Régénérer révoque l'ancienne clé immédiatement.

**7. Le réglage activé/désactivé n'est pas dans `app_settings`.** Cette table se lit sans
connexion (`GET /api/AppSettings` est anonyme, le manifeste en a besoin) : elle révélerait à tout
Internet que la porte est ouverte. Il vit dans la table de la clé. **Pour la même raison, la
porte répond 404 à une clé fausse comme à un réglage désactivé, sans corps** : un 401 apprendrait
à qui sonde que le mécanisme est activé. Le script ignore le statut ; le journal du service garde
la distinction. Il reste un canal temporel théorique — le chemin « désactivé » répond avant de
hacher, le chemin « clé fausse » après — de l'ordre de la microseconde derrière un fournisseur en
mémoire, en deçà de ce qu'un réseau laisse mesurer ; on le nomme, on ne le traite pas.

**8. La date du dernier appel accepté est enregistrée et affichée.** C'est le seul retour que le
webmail peut donner sur une configuration Dovecot qu'il ne voit pas. Pas de compteur d'appels
refusés : n'importe qui sur Internet le ferait monter, et ce serait une écriture par tentative ;
pour la même raison, une clé fausse n'écrit pas une ligne de journal par tentative mais **au plus
une par minute** (`Warning`, avec le nombre de refus depuis la précédente). **Régénérer la clé
remet la date à zéro** : un serveur mail resté sur l'ancienne clé se voit alors à « Aucun appel
reçu depuis la création de la clé ». Pas de bouton « Tester » sur la carte, contrairement au
compte d'envoi : un test parti du microservice prouverait que la porte répond, pas que Dovecot
l'appelle, et c'est Dovecot qui peut être mal configuré. La vérification réelle est une
livraison, décrite dans la documentation serveur.

**9. Une boîte inconnue du webmail est « rien à faire ».** Un compte qui ne s'est jamais connecté
n'a ni ligne `users` ni agenda : rien à mettre à jour, et la livraison ne crée pas de compte
(`RegisterLoginAsync` poserait une date de connexion fausse).

**10. La plateforme `generic`** n'a personne pour activer le réglage aujourd'hui (pas de
gestionnaire de la politique `Admin`, donc `/api/DeliveryReplyKey` y répond 403, comme
`/api/SchedulingAccount`) : la porte y répond toujours 404. Rien dans la conception ne l'exclut :
quand `generic` aura l'onglet Application, ce réglage y fonctionnera tel quel.

**11. Une cause de révision propre, `delivery`.** L'historique des révisions
(`calendar_revisions.cause`) dit par quelle porte un changement est arrivé ; l'enregistrer sous
`webmail` attribuerait à l'utilisateur une modification faite pendant qu'il n'était pas connecté.
Un `ALTER` de plus sur l'ENUM, comme `scheduling` en 5e2. `PreferencesDbContext` convertit
`RevisionCause` par `ToString().ToLowerInvariant()` : ajouter le membre à l'enum et passer
l'`ALTER` suffit. Le même enum sert `contact_revisions`, dont l'ENUM MariaDB ne connaîtra pas
`'delivery'` : inoffensif (jamais écrit pour un contact), à signaler dans le script SQL. La
synchronisation CalDAV ignore la cause, rien n'y change.

**12. Le rayon d'action de la clé est étroit, et c'est l'argument principal du schéma.** Qui
détient la clé peut nommer n'importe quelle boîte (le serveur mail les livre toutes), mais ne
peut que réécrire `PARTSTAT` et le tampon d'une ligne `ATTENDEE` **déjà présente**, sur un
événement **dont le webmail est l'organisateur** (`SchedulingOwner == WebmailOwner`,
`InvitationReader.cs`), le `PARTSTAT` validé parmi trois valeurs par `PartStatRewriter`. Ni
créer, ni supprimer, ni déplacer, ni renommer un événement, ni toucher un événement reçu d'un
autre organisateur. Vérifié contre le code par la revue.

**13. Pas de limite par adresse IP sur la porte, une limite de concurrence.** Tous les appels
légitimes viennent d'**une** adresse, le serveur mail : une partition par IP ne séparerait rien,
ce serait un seau global — et n'importe qui sur Internet le viderait en envoyant, à n'importe
quelle boîte de l'hôte, 120 mails portant `text/calendar; method=REPLY` ; les vraies réponses de
la minute prendraient 429, le script sortirait en 0, rien au journal, `last_call_at` immobile :
un interrupteur d'arrêt actionnable de l'extérieur. Ce qui borne la porte : la clé, `allow/deny`
d'nginx, `size :under` dans la règle Sieve, les 5 Mo, et un **limiteur de concurrence** (8 en
cours, 16 en attente, au-delà 503 journalisé à `Warning`) qui protège le processus, pas le
réseau.

## Le microservice

### La porte d'entrée

`POST /api/Delivery/CalendarReplies`. `[AllowAnonymous]` — non pas contre une politique par
défaut (il n'y en a pas : `SecurityConfiguration` ne déclare qu'`Admin` et la politique DAV, et
`Program.cs` ne pose pas de `FallbackPolicy` ; un contrôleur sans `[Authorize]` est déjà
anonyme), mais pour que l'intention soit lisible et que le test de surface l'épingle. Corps lu tel
quel sur `Request.Body` comme le font les contrôleurs DAV (`DavControllerBase`), aucun formateur
MVC ne lisant `message/rfc822`.

- **Corps :** le mail brut, tel que Dovecot le tient (fins de ligne CRLF : `sieve_execute_input_eol`
  vaut `crlf` par défaut, le mail posté n'est pas octet pour octet celui du stockage ; sans effet
  sur MimeKit, nommé pour ne pas le déboguer deux fois). `[RequestSizeLimit(5 Mo)]`.
- **`X-Delivery-Key` :** la clé. Pas dans `Authorization`, que l'authentification JWT lit déjà.
- **`X-Delivery-Mailbox` :** l'identifiant Dovecot du propriétaire de la boîte (`darth@weesky.be`),
  la variable `USER` que Dovecot passe au script.

Contrôles, dans cet ordre :

| Situation | Réponse |
|---|---|
| `X-Delivery-Mailbox` absent, vide, avec un caractère de contrôle, ou pas une adresse | 400 — **validé avant toute journalisation et avant `new User(...)`**, qui lève sur une adresse malformée (500 sinon) |
| réglage désactivé, pas de clé, clé absente ou fausse | 404 nu (`NotFound()`, jamais `NotFoundEnveloppe(message)` qui porte une prose), indistinctement (décision 7) ; comparaison en temps constant (`CryptographicOperations.FixedTimeEquals`) sur l'empreinte ; journal selon la décision 8 |
| corps au-delà de 5 Mo | 413 |
| clé valide | `last_call_at` posé, puis traitement ; 200 dans tous les cas métier |

**CORS :** `X-Delivery-Key` n'est pas dans `WithHeaders` de la politique CORS
(`SecurityConfiguration.cs`) et **ne doit jamais y entrer** : c'est ce qui fait échouer la
pré-vérification d'un navigateur détourné vers cette porte ; `Content-Type: message/rfc822` en
est une seconde barrière. À écrire en commentaire sur la politique.

**Réponse 200 :** `ResultEnveloppe<DeliveryReplyResponse>` comme partout (la charge sous
`Result`), `{ outcome, uid?, detail? }` :

| `outcome` | Quand | `detail` |
|---|---|---|
| `applied` | la ligne de l'invité vient d'être réécrite | — |
| `alreadyApplied` | elle portait déjà cette réponse (`Reply.Applied`) | — |
| `notApplicable` | `ReplyStatus` autre qu'`Applicable`, ou partie trop grande | le `ReplyStatus` (`UnknownUid`, `NotOwner`, `UnknownAttendee`, `Stale`, `Superseded`, `UnsupportedAnswer`, `OccurrenceOnly`), `too_large`, ou `attendee_line_missing` (la ligne de l'invité que la lecture a trouvée n'est pas retrouvée par la réécriture) |
| `notAReply` | pas de partie calendrier, pas un `REPLY`, un `REPLY` sans `ATTENDEE` (`Reply` null dans `ResolveReplyAsync`), une partie illisible, ou un mail au-delà des plafonds MIME | — |
| `unknownMailbox` | pas de ligne `users` | — |
| `conflict` | le `PUT` conditionnel a perdu | le `DavWriteStatus` |

Les valeurs sortent en PascalCase (`Applied`, `AlreadyApplied`, `NotApplicable`, `NotAReply`, `UnknownMailbox`, `Conflict`), convention JSON du backend ; la casse ci-dessus est celle de la conception.

Journal : `Information` pour `applied` et `alreadyApplied`, `Warning` pour `conflict`,
`Information` avec le détail pour le reste. **Tout ce qui vient du mail est assaini à la frontière
du journal** — l'UID, le détail : tronqué, caractères de contrôle retirés. Le puits Serilog
n'échappe pas les retours à la ligne, et `InvitationReplyApplier.cs:41` journalise aujourd'hui
`{Uid}` tel quel : c'était atteignable par un utilisateur authentifié ouvrant un mail, ça le
devient par n'importe quel expéditeur sur Internet ; la même assainissement s'applique aux deux
chemins. Le script ignore la réponse ; le journal sert au diagnostic, et le détail est ce qui lui
donne sa valeur.

### Le traitement

1. **La boîte :** `IWebmailUserStore.FindByEmailAsync(mailbox)` — `Trim().ToLowerInvariant()`,
   sans résolution d'alias. Null → `unknownMailbox`. Le `User` se construit comme dans
   `UserAuthenticator` (adresse, `WebmailUid = Id`), après validation de l'adresse.
2. **Le parse :** `MimeMessage.LoadAsync(options, Request.Body, cancellationToken)` avec un
   `ParserOptions` dont **`MaxMimeDepth` est abaissé** (16) : la profondeur se borne **pendant** le
   parse, pas après — un `multipart` imbriqué à l'infini est le vecteur classique d'épuisement de
   pile, et un `StackOverflowException` ne s'attrape pas, il emporte le processus entier. Le
   nombre de parties se plafonne à la sortie (256). Au-delà → `notAReply`.
3. **La partie calendrier :** la règle de sélection de `MailMessageMapper.CalendarPart`,
   aujourd'hui écrite sur la structure IMAP (`BodyPartBasic`), **réécrite une fois sur ce qu'elle
   lit vraiment** — type MIME, paramètre `method`, nom de fichier — et appelée par les deux chemins
   sur une projection commune (`record CalendarPartCandidate(ContentType, FileName, Specifier)`),
   construite depuis `MessageSummary.BodyParts` côté IMAP et **depuis `MimeMessage.BodyParts`
   côté brut — jamais `MimeIterator`**. Mesuré par la revue sur MailKit/MimeKit 4.17 : les deux
   `BodyParts` s'arrêtent au `message/rfc822`, `MimeIterator` descend ; l'invariant « les deux
   chemins retiennent la même chose » ne tient qu'avec `BodyParts`. Conséquence, à fixer par un
   test : **une réponse enfermée dans un mail transféré n'est retenue par aucun des deux chemins**
   (`notAReply`), alors que `foreverypart` de Sieve, lui, descend et passe ces mails — la règle
   retient trop, décision 2. Décodage : `MimePart.Content.DecodeTo` puis
   `MailMessageMapper.DecodeText(..., part.ContentType.Charset)`, calqué sur
   `ImapMessageCommands` ; le jeu de caractères venant du mail est déjà gardé (`DecodeText`
   retombe sur UTF-8). **Borne de taille avant décodage, en octets, la même que
   `InvitationPartLoader`** (qui répond 422 `invitation_too_large` à l'ouverture) →
   `notApplicable` / `too_large`. `InvitationParser.Read` refait `IcsGuards.CheckSize` sur le
   texte et rendrait `Unreadable` → `notAReply` sans cette borne amont ; la borne amont est ce qui
   distingue « trop gros » d'« illisible ».
4. **L'application :** `InvitationReplyApplier` est coupé en deux. `ApplyAsync(user, connection,
   request)` garde la récupération IMAP de la partie (`InvitationPartLoader`) et l'appel à
   `InvitationReader.Block(parsed, context, request.Part)` — `Part` est un spécificateur IMAP que
   la porte n'a pas, c'est la frontière. Le cœur
   `ApplyIcsAsync(user, ics, cause)` — parse, `ResolveReplyAsync`, `PartStatRewriter.Rewrite`,
   `PutAsync` conditionnel sur l'ETag — rend
   `record IcsReplyOutcome(ParsedInvitation? Parsed, InvitationContext? Context, bool Applied, bool AlreadyApplied, DavWriteStatus? WriteStatus, string? Refusal)`,
   qui **distingue « appliquée » de « déjà appliquée »** (aujourd'hui `ApplyAsync` les confond dans
   le bloc `invitation` qu'il rend). L'ouverture continue de rendre son bloc à partir de
   `Parsed`/`Context` ; la porte projette l'`IcsReplyOutcome` sur `outcome`/`detail`. Cause
   `RevisionCause.Webmail` par l'ouverture, `RevisionCause.Delivery` par la porte (décision 11).
5. **Deux appels simultanés** (livraison et ouverture) : le `PUT` conditionnel fait gagner le
   premier ; le second relit et constate `alreadyApplied` ou `conflict`. Rien de nouveau.

### La clé et le réglage

Table `delivery_reply_key`, une seule ligne (`id = 1`, contrainte `CHECK`), sur le modèle de
`scheduling_service_account` ; entité `DeliveryReplyKey`, `DbSet` et bloc `OnModelCreating` dans
`PreferencesDbContext` ; script SQL manuel pour les deux bases, documenté dans
`docs/superpowers/webmail-delivery-reply-key-table.md` comme les autres tables :

| Colonne | |
|---|---|
| `key_hash` | `VARBINARY(32)`, SHA-256 de la clé — pas de sel : la clé est aléatoire sur 256 bits, un dictionnaire n'a pas de sens |
| `created_at` | `DATETIME(6)` UTC |
| `last_call_at` | `DATETIME(6)` NULL, UTC — dernier appel dont la clé était valide ; remis à NULL à chaque génération |
| `enabled` | `TINYINT(1)` |
| `updated_at` | `DATETIME(6)` UTC — le jeton de concurrence du modèle copié (`SchedulingAccountStore.LastWriterWinsAsync`) : `POST`, `PUT` et `DELETE` de l'écran le comparent ; **`last_call_at` s'écrit par un `ExecuteUpdateAsync` ciblé, sans jeton**, pour qu'une livraison ne perde jamais contre l'écran et ne fasse jamais perdre l'écran |

Et dans `webmail-calendar-tables.md`, § 5e3 (l'ENUM y apparaît **deux fois**, au `CREATE` et à
l'`ALTER` de 5e2 : les deux à modifier) :
`ALTER TABLE calendar_revisions MODIFY cause ENUM('put','webmail','import','delete','rejected','scheduling','delivery') NOT NULL;`
— valeur ajoutée en fin, comme en 5e2, compatible avec le service en place.

La clé : 32 octets de `RandomNumberGenerator`, rendus en base64url (43 caractères). **Cache :**
un fournisseur singleton `IDeliveryKeyProvider` qui tient l'empreinte et `enabled` en mémoire,
invalidé par le contrôleur à chaque écriture — le modèle est `IServiceAccountProvider` +
`InvalidateAsync`, pas le store EF scoped. Enregistrements DI dans
`ApplicationServicesConfiguration`. Même limite connue qu'en 5e2 : avec deux instances, l'autre
ne voit la nouvelle clé qu'au redémarrage.

`/api/DeliveryReplyKey`, politique `Admin` :

| Verbe | Effet |
|---|---|
| `GET` | `{ configured, enabled, createdAt?, lastCallAt? }` |
| `POST` | génère ou régénère ; répond `{ key }` **une seule fois**, `Cache-Control: no-store` ; l'ancienne clé est refusée dès la réponse ; `last_call_at` remis à NULL |
| `PUT` `{ enabled }` | 204 ; 409 `delivery_key_missing` si `enabled` sans clé |
| `DELETE` | supprime la clé et désactive ; 204, 404 sans clé |

`last_call_at` s'écrit à chaque appel accepté : une réponse d'invité est rare, l'écriture ne coûte
rien ; pas d'écriture sur un refus (décision 8).

### Ce que la tranche touche aussi

- `SecurityConfiguration` : le limiteur de concurrence `delivery` (décision 13) rejoint
  `AddLoginRateLimiter`, renommé `AddRateLimiters` avec sa doc XML (« les trois points d'entrée qui
  vérifient un mot de passe » devient faux) ; `[EnableRateLimiting("delivery")]` sur l'action.
- `src/snoopy.microservice/CLAUDE.md` : la liste des contrôleurs, et § « The platform seam » la
  liste des routes du cœur qui portent la politique `Admin` (`/api/DeliveryReplyKey` s'y ajoute).
- `DESIGN.md`, `reverse-proxy-prerequisite.md` (le `location` et `client_max_body_size`).
- Un test de surface de route **dans une classe à part** : `MailRouteSurfaceTests` affirme que
  tout contrôleur de son lot porte `[Authorize]`, ce que la porte contredit ; `ControllerRouteSurface.Of`
  n'expose que verbe et gabarit, les filtres (`[AllowAnonymous]`, `[RequestSizeLimit]`,
  `[EnableRateLimiting]`, la politique `Admin`) se lisent par réflexion comme il le fait déjà.
- Frontend : les méthodes dans `api.js`, un `deliveryReplyKeyErrors.ts` sur le modèle de
  `schedulingAccountErrors.ts`, `delivery_key_missing` dans `CODES` de `lib/apiErrorMessage.ts` et
  dans `errors.json` ; les textes de la section dans `admin.json` `en`/`fr` ; `keys.test.ts` et
  `parity.test.ts`.
- `ApiDocumentation.xml` : réverter la dérive avant chaque commit.

## L'écran

Une section d'Administration > Application, sous le compte d'envoi, même vocabulaire
(`.admin-list-item`, carte, fenêtre en `.field-h`, `useDialogFocusTrap`). Maquette à valider
avant le code de l'interface, comme pour le compte d'envoi.

- **Sans clé :** une phrase d'aide, un bouton « Générer une clé », l'interrupteur grisé. La phrase
  dit ce que fait le mécanisme **et ce qu'il accepte** (décision 4) : « Le serveur mail peut
  appliquer les réponses des invités au moment où il les livre, sans attendre que le mail soit
  ouvert. Les réponses sont alors inscrites sans qu'un utilisateur les ait vues. »
- **Avec une clé :** l'interrupteur « Traiter les réponses à la livraison » ; « Clé créée le … » ;
  « Dernier appel reçu le … » ou « Aucun appel reçu depuis la création de la clé » ; Régénérer et
  Supprimer, chacun derrière une confirmation. Celle de Régénérer prévient que le serveur mail sera
  refusé tant que sa configuration n'aura pas reçu la nouvelle clé.
- **Après génération :** une fenêtre montre la clé, un bouton Copier, rappelle qu'elle ne sera
  plus affichée, et nomme le fichier où la coller côté serveur mail.

La carte ne peut pas distinguer « Dovecot mal configuré » de « personne n'a encore répondu »
après un premier appel réussi ; elle ne le prétend pas (décision 8), et sa phrase d'aide renvoie
à la vérification de la documentation serveur.

Rien ne change dans la carte d'invitation du lecteur : `replyPending` exige `!reply.applied`, une
réponse appliquée à la livraison arrive avec `applied: true` et l'effet d'auto-application ne
part pas.

## Le serveur mail (Dovecot 2.4.5, Pigeonhole)

Documenté dans `docs/superpowers/webmail-delivery-replies-prerequisite.md`, sur le modèle des
prérequis existants. Aucun de ces fichiers n'est versionné ailleurs que dans cette documentation.
Vérifié par la revue contre la documentation 2.4 : le bloc de configuration, la syntaxe de la
règle (une seule option MIME par test `header`, d'où la décomposition `allof`/`anyof` — elle est
requise, pas stylistique), `execute :pipe` qui passe **le mail entier** même dans `foreverypart`,
`USER` parmi les variables passées au programme, le `before` qui tourne sous LMTP comme sous LDA.

**La configuration** (syntaxe 2.4) :

```
sieve_plugins {
  sieve_extprograms = yes
}
sieve_global_extensions {
  vnd.dovecot.execute = yes
}
sieve_execute_bin_dir = /usr/lib/dovecot/sieve-execute
sieve_script calendar_replies {
  type = before
  driver = file
  path = /etc/dovecot/sieve/calendar-replies.sieve
  bin_path = /var/lib/dovecot/sieve/calendar-replies.svbin
}
```

`vnd.dovecot.execute` est activée **dans `sieve_global_extensions` seulement**, jamais dans
`sieve_extensions` : les utilisateurs écrivent leurs propres règles depuis l'onglet Règles du
webmail, et l'extension leur donnerait le droit de faire exécuter un programme par le serveur ;
la documentation Dovecot le confirme (« never available to the user's personal script »). Les
extensions `mime` et `foreverypart` (RFC 5703) sont actives par défaut en 2.4 ; si une
configuration les a coupées, `sievec` refuse la règle et la documentation donne la ligne
`sieve_extensions { mime = yes }`. `bin_path` pointe un répertoire où `vmail` écrit, sinon
`vmail` ne peut pas déposer le `.svbin` dans `/etc/dovecot` et Dovecot recompile à chaque
livraison ; `sievec` à refaire à chaque modification de la règle. `doveconf -n` après édition, pour
vérifier que le bloc est lu tel quel sur 2.4.5.

**La règle** `calendar-replies.sieve` :

```
require ["mime", "foreverypart", "vnd.dovecot.execute"];
if size :under 5M {
  foreverypart {
    if allof (
      anyof (
        allof (header :mime :type "Content-Type" "text",
               header :mime :subtype "Content-Type" "calendar"),
        allof (header :mime :type "Content-Type" "application",
               header :mime :subtype "Content-Type" "ics")),
      header :mime :param "method" "Content-Type" "REPLY") {
      execute :pipe "calendar-reply";
      break;
    }
  }
}
```

`size :under 5M` en tête : un mail énorme ne provoque ni le lancement du programme, ni son
écriture sur disque, à chaque livraison, dans la transaction LMTP — c'est la réponse la moins
chère à un expéditeur qui ferait transiter du volume par ce chemin. Les deux types sont les types
qu'accepte `MailMessageMapper.IsCalendarPart` (qui retient aussi tout fichier nommé `*.ics` quel
que soit son type — celui-là s'applique à l'ouverture, jamais à la livraison, décision 3). La
comparaison de `:param` est insensible à la casse par défaut (`i;ascii-casemap`). La règle
n'arrête rien : pas de `stop`, pas de `discard` ; les règles de l'utilisateur s'exécutent ensuite
et le mail arrive dans sa boîte. Outlook.com, Gmail et Exchange annoncent la méthode ; Thunderbird
à confirmer en recette.

**Le script** `/usr/lib/dovecot/sieve-execute/calendar-reply`, `root:root 0755`, shebang
`#!/bin/sh`, `curl` — **dans cet ordre, et l'ordre est la sécurité** :

1. **Lit tout stdin dans un fichier temporaire, inconditionnellement, comme première
   instruction** (`mktemp`, effacé en sortie par `trap`). Dovecot : un programme qui ne consomme
   pas toute l'entrée fait échouer l'action ; sortir en 0 avant d'avoir lu ne suffit pas, le
   `SIGPIPE` sur le tube non lu donne aussi une sortie non nulle.
2. Ensuite seulement : si `USER` est vide (configuration LDA), sort en 0 ; si le fichier dépasse
   5 Mo (ceinture sous le `size :under` de la règle), sort en 0.
3. Lit l'adresse de la porte et la clé dans `/etc/dovecot/calendar-reply.conf`, **`root:vmail`
   `0640`** (`vmail` lit, ne réécrit pas — et tout processus tournant sous `vmail`, `imap`, `lmtp`,
   `indexer`, la lit aussi, ce que borne la décision 12), au format d'un fichier `--config` de
   `curl` avec le contenu exact donné par la documentation — la clé ne passe jamais en argument de
   commande, où tout compte de la machine la lirait dans la liste des processus.
4. `timeout 8 curl --config … -H "X-Delivery-Mailbox: $USER" -H "Content-Type: message/rfc822"
   --data-binary @fichier --connect-timeout 2 --max-time 5 …` : `--max-time` ne borne que le
   transfert, pas la résolution DNS ni l'écriture du fichier ; `timeout 8` borne tout, sous les
   10 s de `sieve_execute_exec_timeout`. L'adresse de la porte est une IP littérale ou une entrée
   `/etc/hosts`, pas un nom à résoudre.
5. **Sort toujours en 0**, même si le microservice est arrêté, refuse la clé ou ne répond pas.

**Pourquoi le 0 ne suffit pas, et ce que la documentation exige avant d'activer.** Quand
l'action `execute` échoue, Dovecot livre le mail en boîte de réception par la conservation
implicite mais **saute les règles personnelles de l'utilisateur** — un tri en dossier ou une
redirection seraient perdus, pour chaque réponse d'invité, chez tous les utilisateurs, sans
signal. Sortir en 0 couvre le microservice absent ; il ne couvre pas un programme qui ne démarre
pas (mauvais chemin par rapport à `sieve_execute_bin_dir`, pas de `+x`, pas de shebang), une
entrée non consommée, ni le dépassement des 10 s. La documentation impose donc, **avant**
d'activer le réglage :

1. `sievec` sur la règle, sans erreur ; `doveconf -n` montre le bloc.
2. `curl -v` depuis le serveur mail avec le fichier de configuration et **un mail d'exemple qui
   n'est pas une réponse** (sur une porte activée, une vraie réponse s'appliquerait), vers la
   porte : 200 `notAReply` attendu (ou 404 tant que le réglage est désactivé — la preuve que la clé
   et l'adresse sont justes est alors dans le journal du service).
3. Une livraison réelle d'une réponse d'invité, puis la preuve qu'une règle personnelle de
   l'utilisateur s'est encore appliquée sur ce mail.
4. **Le microservice arrêté**, une livraison d'une réponse, puis la même preuve : c'est le mode
   de défaillance que le point 1 du script protège, et rien d'autre dans la procédure ne
   l'attraperait.

**nginx :** `location /api/Delivery/ { allow <ip du serveur mail>; deny all; client_max_body_size
6m; … }` — le défaut d'nginx est 1 Mo, il refuserait une réponse de plus avant que la limite du
service ne joue.

**Identifiant de la boîte :** `USER` doit être l'adresse avec laquelle l'utilisateur se connecte
au webmail. Un utilisateur qui se connecte par un alias a sa ligne `users` sur l'alias ; la
livraison le cherche sous son identifiant Dovecot et répond `unknownMailbox` à chaque fois. La
documentation le dit, et donne le diagnostic (le journal, `unknownMailbox` pour une boîte qui
existe).

**Retour arrière :** désactiver le réglage dans Administration suffit ; la règle Sieve peut
rester, ses appels répondent 404 et le script sort en 0.

## Tests

**Automatiques :**

- La porte : l'ordre des contrôles (en-tête boîte absent, vide, avec un caractère de contrôle ou
  malformé → 400 sans ligne de journal et sans 500 ; désactivé → 404 nu avec la bonne clé ; fausse
  clé → 404 nu ; trop gros → 413) ; les six issues et leurs `detail`, dont le `REPLY` sans
  `ATTENDEE` et le mail au-delà de `MaxMimeDepth` ; `last_call_at` posé sur un appel accepté et
  pas sur un refus, et sans toucher `updated_at` ; l'UID assaini dans le journal ; le test de
  surface de route dans sa classe.
- La sélection de la partie : les mêmes mails d'exemple (Outlook.com, Gmail avec `invite.ics`, un
  `.ics` joint sans méthode, un `PUBLISH`, **une réponse transférée dans un `message/rfc822` —
  retenue par aucun des deux chemins**, un mail au-delà des plafonds de parties) désignent la
  même partie lus en brut (`MimeMessage.BodyParts`) et par la structure IMAP.
- Le cœur `ApplyIcsAsync` : les tests actuels de `InvitationReplyApplier` restent verts par
  l'entrée IMAP ; la porte les rejoue par l'entrée texte (InMemory), la cause `Delivery` sur la
  révision, « appliquée » distinguée de « déjà appliquée ».
- La clé : rendue une seule fois, `no-store` ; régénérer révoque l'ancienne et remet
  `last_call_at` à NULL ; activer sans clé → 409 ; supprimer désactive ; le fournisseur invalidé
  à l'écriture ; le jeton `updated_at` sur les trois écritures de l'écran.
- L'écran : les états de la section, la fenêtre de la clé et le Copier, les deux confirmations ;
  parité FR/EN et insécables.

**Non testé automatiquement :** Sieve et le script — pas de Dovecot dans l'intégration continue.
La procédure d'activation en tient lieu.

**Recette réelle, par l'utilisateur :** la procédure d'activation ci-dessus, ses quatre étapes ;
une invitation à Hotmail et à Gmail, l'agenda à jour avant l'ouverture du mail (`last_call_at`
bouge, la révision porte `delivery`) ; le réglage désactivé, la réponse s'applique à l'ouverture ;
une clé régénérée sans mettre à jour le serveur, « Aucun appel reçu » sur la carte, refus dans le
journal, rattrapage à l'ouverture, les règles personnelles toujours appliquées ; Thunderbird si un
compte est disponible.

## Découpage attendu

Douze tâches, pour que le plan se vérifie : backend — enum `Delivery` + `ALTER` + docs SQL des
deux tables ; entité/store/fournisseur de la clé ; `DeliveryReplyKeyController` + tests ;
extraction d'`ApplyIcsAsync` (refactor pur, tests existants verts) ; projection de parties
partagée + tests des deux chemins ; `DeliveryController` (contrôles, issues, journal,
`last_call_at`, limiteur, commentaire CORS) ; test de surface. Frontend — maquette validée
**avant** le code ; hook + section + fenêtre + confirmations ; locales et tests. Docs — le
prérequis serveur ; `CLAUDE.md`, `DESIGN.md`, `reverse-proxy-prerequisite.md`.

## Hors périmètre

- Une réponse enfermée dans un mail transféré : la règle Sieve la passe, aucun des deux chemins
  ne la retient, et le test le fixe — comme aujourd'hui à l'ouverture.
- Les invitations (`REQUEST`, `CANCEL`) à la livraison : l'invité doit choisir, rien à appliquer
  sans lui.
- Une clé par serveur mail, une rotation programmée, une période de grâce sur l'ancienne clé
  (décision 8 rend la coupure visible ; le reste est un résidu).
- Une date du dernier refus, écrite au plus une fois par heure, qui dirait « quelqu'un appelle
  avec une mauvaise clé » après un premier succès : un résidu, la décision 8 est honnête sur ce
  qu'elle ne voit pas.
- La résolution des alias côté livraison.
