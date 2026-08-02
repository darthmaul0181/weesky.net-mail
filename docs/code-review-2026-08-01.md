# Code review backend `snoopy.microservice` — 2026-08-01

Revue menée en vue d'un refactoring (propreté, duplication, standards, performance, sécurité).
Cinq axes revus en parallèle : contrôleurs+auth, services mail (IMAP/SMTP/Sieve), repositories+EF,
services transverses (crypto/sanitiser/CSV/staging), configuration+models+rules.

## Verdict d'ensemble

Code globalement de bonne facture — documentation des décisions, câblage DI, crypto des comptes
connectés et sanitiseur HTML au-dessus de la moyenne. La revue révèle **deux générations de code**
qui cohabitent : la partie récente (9 stores `Preferences`, résolution de compte, factories mail)
applique les règles du projet ; la partie ancienne (`AdminRepository`, `UsersRepository`,
`AliasesRepository`, 5 contrôleurs, le client ManageSieve) les ignore. L'essentiel des constats se
concentre là. Aucun problème d'architecture fondamental : les correctifs sont localisés.

Légende sévérité : 🔴 critique/majeur · 🟠 mineur notable · ⚪ mineur.
« ✅ vérifié » = constat recontrôlé manuellement dans le code après les rapports.

## Statut — vague 1 livrée le 2026-08-01

Les neuf correctifs de la vague 1 sont sur `backend-refactor-1` (9 commits, `ea8a851`..`7f0e52e`).
Build Release **0 avertissement**, suite **1765 tests verts** (1684 au départ, +81).

| Constat | Statut |
|---|---|
| Injection CRLF ManageSieve | ✅ `QuoteString` → `QuoteName` renvoyant `Result<string>`, refus de tout caractère de contrôle + attributs sur les 2 DTO |
| Échappement perdu à la re-sérialisation | ✅ `HtmlFormatter.Instance` sur la sortie finale |
| Connexions en clair | ✅ opt-in `Mail:AllowCleartext` (défaut off), warning nommant l'hôte, **écriture admin refusée** de la même façon, sonde de mot de passe alignée |
| JWT dans le corps du login | ✅ record `LoginResponse(ExpiresIn)`, token cookie-only |
| Health check menteur | ✅ valeur de retour honorée, `ex.Message` ne fuit plus |
| Borne sur le HTML entrant | ✅ plafond 2M caractères, drapeau `Truncated` additif |
| Cap d'entrées staging | ✅ 50 entrées/compte, compteur équilibré sur les 6 chemins, `_reserved` ne croît plus indéfiniment |
| Tick initial des sweepers | ✅ balayage au démarrage + gigue (30 s / 5 s) |
| 404 des lectures IMAP | ✅ sentinelle posée côté session **et** mappée côté contrôleur (`IsMissing`) — voir réserve ci-dessous |

**Réserve importante sur le dernier point.** La revue affirmait que le contrôleur répondait 502 au lieu
de 404 ; en réalité il ne testait `FolderNotFound` **nulle part** (sauf `GetFolderStatus`), donc corriger
la session seule n'aurait rien changé pour l'utilisateur. Les deux couches sont désormais alignées pour
les lectures. **Les endpoints d'écriture (Move / Copy / Delete / Empty / Flags) gardent leur 502** : leur
contrat documenté dans `CLAUDE.md` est « 204/400/401/502 » et l'élargir demande une décision produit.

**Revue adverse (Opus, lecture seule) sur les 4 correctifs sécurité.** Verdicts : ManageSieve **CLOS**
(`QuoteName` est prouvé unique chemin vers le fil ; `char.IsControl` couvre U+0085, et U+2028/2029 sont
inoffensifs car UTF-8 ne produit jamais `0x0A`/`0x0D` en octet de continuation), login **CLOS** (aucune
route ne sérialise plus le JWT ; enveloppe 401 identique au bit près). Deux défauts réels trouvés et
**corrigés dans la foulée** (commit `9ff4f34`) :

1. **Le drapeau `Truncated` n'atteignait personne** — posé sur `SanitizedHtml` mais jamais recopié dans
   `MailMessageDetail` : un mail de plus de 2M caractères était amputé en silence, strictement pire
   qu'avant le correctif. Champ ajouté et propagé.
2. **`Auto` et `StartTlsWhenAvailable` échappaient à la fois à la barrière et à l'avertissement** — tous
   deux retombent silencieusement en clair si le serveur n'annonce pas STARTTLS (ou si un attaquant le
   retire de la bannière), et le test portait sur `is None`, c'est-à-dire la seule valeur que la doc
   désigne comme *non* dangereuse. Le refus se décide désormais sur `client.IsSecure` **après** le
   connect et **avant** l'authentification, ce qui couvre le stripping quelle que soit la configuration.
   Deux tests sur `FakeImapServer` (loopback réel) verrouillent refus et opt-in.

Points mineurs également fermés : `[StringLength(128)]` cassait l'aller-retour d'un nom de script
préexistant plus long (porté à 512) ; la doc de `Mail:AllowCleartext` laissait croire qu'elle couvrait
ManageSieve (elle ne gouverne qu'IMAP/SMTP, `Sieve:AllowCleartext` est un drapeau distinct).

**Restent ouverts, assumés :** (a) la profondeur d'imbrication HTML n'est pas bornée — 2M caractères
autorisent ~400 000 `<div>` imbriqués et les parcours AngleSharp sont récursifs ; `OutgoingMailSanitizer`
documente ce danger vers ~7 000 niveaux, le sanitiseur d'affichage n'a ni garde ni test. Non vérifié
faute de mesure, à traiter en vague 2. (b) Un refus de politique répond 404 `account_not_found`, donc
indiscernable d'une boîte supprimée — conséquence assumée de la règle 4, seul le log serveur dit pourquoi.
(c) Les endpoints d'écriture gardent leur 502 (voir ci-dessus).

Impacts de déploiement à connaître : une ligne domaine externe stockant `None` cesse de résoudre (404
`account_not_found`, remède au choix — corriger la ligne ou activer `Mail:AllowCleartext`, sans redémarrage) ;
**toute connexion IMAP/SMTP qui s'avère non chiffrée est désormais refusée** sauf opt-in, ce qui inclut
un `StartTlsWhenAvailable` contre un serveur sans STARTTLS ; un consommateur d'API tiers lisant `.token`
dans la réponse de login casse ; le corps de `Detail` gagne un champ `truncated`.

## Statut — vague 2 (performance) livrée le 2026-08-01

32 commits sur `backend-refactor-1`, build Release **0 avertissement**, suite **1861 tests verts**
(1767 après la vague 1, +94). Aucun test ignoré : le verrou écrit pour le fetch de message est passé
au vert.

| Constat | Statut |
|---|---|
| Ouverture d'un mail téléchargeant tout le message | ✅ `BODYSTRUCTURE` + `BODY.PEEK[HEADER]` + une partie texte ; ~27 Mo → quelques Ko |
| Recherche tous-dossiers non bornée | ✅ fenêtre de pagination pour les enveloppes, budget de 2000 candidats pour le filtre pièces jointes, réparti par besoin |
| Purge d'un dossier énumérant chaque UID | ✅ `1:*` + `\Deleted` + `EXPUNGE` |
| Pièce jointe copiée deux fois en mémoire | ✅ `GetStreamAsync` + blocs mutualisés ; plus aucune allocation LOH |
| Teardown IMAP/SMTP bloquant | ✅ borné à 2 s (30 s auparavant) |
| ManageSieve sans timeout | ✅ un budget unique couvre tout le dialogue ; littéral serveur plafonné à 1 Mo |
| N+1 page réglages | ✅ N+1 requêtes → 2, quel que soit le nombre de boîtes |
| Requêtes admin | ✅ projection sans le hash de mot de passe, flag admin caché 60 s, filtrage serveur |
| Profondeur/coût du sanitiseur | ✅ borne structurelle pré-analyse ; pire cas 22–71 s → **1,68 s mesuré** |

**Le `LCASE`/index reste écarté**, conformément à l'arbitrage du 2026-07-25 (voir ci-dessous).

### Revue adverse — la vague avait introduit des régressions

Une passe adverse (Opus, lecture seule, IL de MailKit/MimeKit à l'appui) a validé la réhydratation des
en-têtes (règle 7 préservée), l'abandon de l'item `Envelope`, la résolution des jeux de caractères,
l'argument de fusion des recherches et les trois sous-règles de la règle 6. Elle a aussi trouvé quatre
défauts réels, tous corrigés :

1. **Critique — le déni de service était rouvert.** Le scanner pré-analyse lisait `<script_x>` comme le
   nom `script` (arrêt au `_`, que HTML5 ne traite pas comme terminateur), en déduisait du texte brut et
   cessait de compter, pendant qu'AngleSharp y voyait un élément ordinaire et analysait la suite comme du
   balisage. Les deux plafonds contournés d'un coup. Traité comme une **classe** : quatre divergences de
   même nature trouvées (troncature du nom, caractère d'ouverture, terminateur `--!>`, fin de texte brut),
   toutes fermées. Mesure : la charge n'a pas terminé en 21 minutes avant correction, 1,05 s après.
2. **Haut — le cache admin avait bien une fenêtre.** `GetOrCreateAsync` ne valide l'entrée qu'après le
   retour de la requête : un lecteur entré dans la fabrique avant le `SaveChanges` du révocateur inscrivait
   l'ancienne valeur *après* l'invalidation. Corrigé par une invalidation par époque (et non par retrait de
   clé, qui n'aurait fait que rétrécir la fenêtre). Fenêtre résiduelle nulle pour toute écriture applicative.
3. **Haut — `format=flowed` n'était plus déroulé.** `MimeMessage.TextBody` passe par `FlowedToText` ;
   le chemin réduit appelait `TextPart.Text` directement. Tout mail texte (défaut de Thunderbird et
   d'Apple Mail) s'affichait coupé à ~72 colonnes. Corrigé en reproduisant le chemin interne de MimeKit.
4. **Moyen — troncature par UID sur serveur sans `SORT`**, en contradiction avec la règle 2 que le
   commentaire invoquait ; et budget de balayage dépensé dans l'ordre alphabétique des dossiers. Les deux
   corrigés (toutes les clés de fusion récupérées sans `SORT`, répartition par besoin).

**Un faux positif de la revue adverse**, réfuté preuve à l'appui : le NRE annoncé sur un `body_fld_enc`
valant `NIL` ne se reproduit pas — `MimeUtils.TryParse(null, …)` renvoie `false` sans lever sur MimeKit
4.17. La protection a été ajoutée en durcissement, pas en correction.

### Restes assumés

- `Total` devient un « au moins » quand le budget tronque le balayage pièces jointes, sans champ pour
  le signaler ; le rendre explicite coûterait un booléen sur `MailSearchPage` plus le rendu côté frontend.
- Pagination de `GET /api/Admin/users` non ajoutée (changement de contrat ; ~1,5 Mo par chargement à
  10 000 comptes).
- `GET /api/account` renvoie `isAdmin` non caché alors que l'autorisation répond depuis le cache : après
  une modification hors application, le lien Administration et l'API peuvent diverger 60 s.
- Le scan du sanitiseur reste un compteur, pas un arbre : un document pathologiquement mal imbriqué peut
  être aplati au-delà de 1024 niveaux — le contenu survit, la mise en page non.

## Statut — vague 3 (standards mécaniques) livrée le 2026-08-02

9 commits, suite **1914 tests verts** (1863 après la vague 2, +51), build Release 0 avertissement.

| Constat | Statut |
|---|---|
| `CancellationToken` absent des 3 anciens repositories | ✅ obligatoire (jamais `= default`) sur les ~20 méthodes et chez tous les appelants |
| Deux formes d'erreur incompatibles | ✅ `InvalidModelStateResponseFactory` répond l'enveloppe ; une seule forme sur toute l'API |
| DTO sans attributs de validation | ✅ annotés là où la contrainte est une **forme** ; 7 DTO laissés à leur validation manuelle, avec raison |
| Bug `Alias.Equals`/`GetHashCode` | ✅ hachage aligné sur l'égalité, épinglé par un test de **contrat** (`HashSet` fusionne) |
| `DomainOwnershipInfo` code mort | ✅ supprimé après vérification |
| `Quote()` dupliqué entre RuleProviders | ✅ extrait dans `SieveQuoting` |
| Constructeurs primaires restants | ✅ 6 classes converties |
| Health check sur une seule base | ✅ couvre les deux, en nommant celle qui tombe |
| `DateTime.UtcNow` dans `TokenBuilder` | ✅ `TimeProvider` injecté ; l'expiration du JWT est pilotable |

**Le piège qui aurait tout annulé en silence :** `PostConfigure`, pas `Configure`. `AddSnoopyOptions`
s'exécute avant `AddControllers()`, dont le `ApiBehaviorOptionsSetup` réassigne la fabrique
**inconditionnellement** — un `Configure` aurait été écrasé sans le moindre signal. Le test résout la
fabrique à travers la même composition DI que `Program`, précisément parce qu'un test du délégué isolé
serait passé malgré l'écrasement.

**Pourquoi les messages n'ont pas changé :** le `required` implicite des types non-nullables utilise
`AllowEmptyStrings = true`, donc les DTO portant `= string.Empty` ne le déclenchaient jamais et leurs
gardes manuels étaient réellement atteints. Les attributs reprennent **mot pour mot** le libellé du
contrôleur, vérifié : « A folder is required », « Uids must hold between 1 and 200 entries ».

**Les gardes manuels sont conservés délibérément.** Ils sont inatteignables en production — le binder
refuse avant, avec le même message — mais restent la seule façon d'exercer ces chemins depuis les tests
qui invoquent les actions directement. Défense en profondeur, pas code mort.

## Statut — vague 4 (architecture) livrée le 2026-08-02

11 commits, suite **1919 tests verts**, build Release 0 avertissement. Aucun changement de comportement.

| Constat | Statut |
|---|---|
| Résolution de compte en 3 exemplaires | ✅ une seule : `ApiBaseController.ConnectedAccountError` + `AccountResolution<T>` |
| `MailController`, 1214 lignes, 5 responsabilités | ✅ 4 contrôleurs + base, 29 à 372 lignes |
| `ImapSession`, 1289 lignes, 4 responsabilités | ✅ 6 fichiers, façade de 198 lignes |
| ~100 lignes dupliquées ManageSieve | ✅ `ManageSieveWire` ; 355 lignes supprimées, −103 net |
| 9 stores quasi identiques | ✅ 5 sur `ScopedStore`, 4 exclus avec raison ; **+17 lignes nettes, assumé** |

**Les preuves, pas les affirmations.** Le découpage des contrôleurs est épinglé par
`MailRouteSurfaceTests`, une énumération par réflexion des 25 couples (verbe, gabarit), de leurs statuts
et de leur jeu de filtres, **passée d'abord contre le `MailController` intact** puis inchangée après :
ensemble d'avant = ensemble épinglé = ensemble d'après. Elle vérifie aussi que
`AttachmentSizeLimitFilter` est sur `POST Attachments` et nulle part ailleurs. `IImapSession` est
**byte-identique à master** (diff vide), donc aucun de ses six consommateurs ne peut être affecté. Et la
conservation des assertions est démontrée par multiensemble : les 272 lignes `Assert.*` retirées,
normalisées, égalent exactement celles ajoutées.

**Le piège du découpage :** `[Route("api/[controller]")]` sur quatre classes aux noms différents donne
quatre préfixes différents. L'application compile, les tests unitaires passent, et **toutes les URL
changent**. Chaque contrôleur porte donc une route explicite, et la raison est écrite dans le code.

**Deux trouvailles hors périmètre.** Le plafond d'allocation de 1 Mo de la vague 2 ne couvrait que la
moitié session de ManageSieve : côté client, une bannière sans saut de ligne faisait croître un tampon
sans borne **pendant le handshake, avant authentification**. Corrigé. Et l'introduction d'un lecteur
bufferisé côté client ouvrait une surface de TLS stripping — des octets tamponnés avant STARTTLS rejoués
comme protégés — refermée par une garde qui teste le tampon avant l'enveloppement (RFC 5804 : le serveur
ne doit rien envoyer entre le `OK` et la négociation).

**Trois constats de cette revue se sont révélés faux à l'usage.** Les trois corps d'upsert n'étaient pas
« identiques au caractère près » ; `MailController` avait 25 actions et non 22 ; et l'invariant
`AsNoTracking` de `ConnectedAccountStore` était déjà couvert par un test. Le commentaire de `ContactStore`
que le §2 déclare factuellement faux est à considérer comme **non vérifié** : personne n'a contrôlé ce
que MariaDB émet réellement.

## Recoupement avec la revue du 2026-07-25

`docs/code-review-microservice-2026-07-25.md` couvrait le même périmètre et porte un statut par
constat. Recoupement fait après coup :

- **Aucun des correctifs de la vague 1 ci-dessous n'y avait été écarté.** Les deux points sécurité les
  plus proches y sont *corrigés* et servent de précédent : 1.2 (STARTTLS ManageSieve → opt-in
  `Sieve:AllowCleartext`) est exactement le modèle à recopier pour le refus du clair côté mail, et 1.5
  (plafond d'upload) a fermé la limite en **octets** — le cap en **nombre d'entrées** reste ouvert.
- **Un constat de la vague 2 avait été explicitement rejeté** : le 2.4 (`EnableStringComparisonTranslations`
  → `LCASE()`), marqué « écarté, constat révisé ». Motif retenu alors : le domaine est résolu d'abord
  par une égalité indexable donc le `LOWER()` ne porte que sur un domaine, et surtout passer à `==`
  risquerait de rendre le login sensible à la casse pour d'éventuelles lignes `username` historiques
  en majuscules — invérifiable sans la base de production, pour un prix d'erreur élevé (utilisateur
  enfermé dehors). **La revue 2026-08-01 l'a re-signalé sans connaître cet arbitrage.** Élément neuf
  depuis : les quatre tables sont en `utf8mb4_general_ci` (`assets/dovecot.sql:28,39,50,68`), collation
  insensible à la casse, donc `col = @p` resterait insensible à la casse côté MySQL. Cela affaiblit
  l'argument technique mais pas l'argument de risque. **À rouvrir seulement avec la base de production
  sous les yeux**, sinon maintenir le rejet.
- **Constats re-trouvés indépendamment, toujours ouverts** dans la revue précédente : 1.6 (doveadm en
  HTTP clair), 1.7 (pas de rate limiting hors login), 1.10 (allocation non bornée pilotée par le
  serveur ManageSieve), 2.2 (`GetTreeAsync` cher et fréquent), 2.6, 2.8 (recherche multi-dossiers),
  3.6 et 3.7 (duplication ManageSieve / SieveRepository), 5.2 (`DeleteUserAsync` ne nettoie pas les
  alias). Le fait que deux revues indépendantes les retrouvent renforce leur légitimité.
- 1.11 (validation de la clé JWT au démarrage) est « écarté pour l'instant » : un seuil plus strict
  que les 16 octets de `Microsoft.IdentityModel` bloquerait au login un déploiement qui fonctionne.
  Le constat ⚪ correspondant du §1 ci-dessous doit être lu avec cette réserve.

---

## 1. Sécurité

- 🔴 **Injection CRLF dans les commandes ManageSieve** — `Services/ManageSieveSession.cs:259` +
  `Repositories/SieveRepository.cs:75` + DTO `SaveRulesRequest.cs:17` / `SieveRawScript.cs:16`.
  `QuoteString` n'échappe que `"` et `\`, pas CR/LF ; `ScriptName` voyage depuis le corps de requête
  sans validation jusqu'à `PUTSCRIPT/SETACTIVE/DELETESCRIPT`. Un nom contenant `\r\n` injecte une
  commande arbitraire dans la session authentifiée. ✅ vérifié.
  *Fix :* refuser tout caractère de contrôle dans `QuoteString` (le garde protocolaire est porteur,
  `SieveRepository` étant atteignable depuis plusieurs DTO) + `[RegularExpression]`/longueur sur les DTO.

- 🔴 **Perte de l'échappement XSS à la re-sérialisation** — `Services/MailHtmlSanitizer.cs:176`.
  Le sanitiseur d'affichage termine par `document.Body?.InnerHtml`, or le formateur AngleSharp par
  défaut n'échappe pas `<`/`>` dans les valeurs d'attributs ; `OutgoingMailSanitizer.cs:48-50`
  documente ce piège et utilise `HtmlFormatter.Instance`. Le sanitiseur *d'entrée* — celui face à
  l'input hostile — jette ce durcissement. ✅ vérifié.
  *Fix :* `document.Body?.ChildNodes.ToHtml(Ganss.Xss.HtmlFormatter.Instance)` + test d'assertion.

- 🔴 **Connexions en clair acceptées pour les domaines externes** — `Services/MailConnectionBuilder.cs:45`.
  `TryParseSecurity` accepte `SecureSocketOptions.None` : une ligne domaine externe peut faire partir
  le mot de passe sur un socket non chiffré, sans log — alors que `ManageSieveClient.cs:80` refuse ce
  downgrade par défaut. Politiques opposées sur le même credential.
  *Fix :* refuser `None` sauf opt-in `Mail:AllowCleartext` explicite, et logger un warning nommant le domaine.

- 🔴 **JWT renvoyé dans le corps du login** — `Controllers/LoginController.cs:62-81`.
  Le body porte le JWT brut en plus du cookie HttpOnly/Secure/SameSite=Strict ; rien côté front ne lit
  `.token`. Un token 48 h exposé au JS de la page et aux logs intermédiaires pour aucun consommateur.
  *Fix :* répondre `{ expiresIn }` (ou 204), garder le token cookie-borne.

- 🔴 **Oracle de test de mots de passe** — `Controllers/ConnectedAccountsController.cs:117`.
  Un utilisateur authentifié peut soumettre un email+mot de passe arbitraire et déclencher un vrai
  login IMAP. Seul frein : limiteur 5/min **par IP** partagé avec `/api/login` ; pas de plafond de
  comptes par utilisateur ; l'acteur authentifié n'est pas loggé (seule l'adresse sondée l'est).
  *Fix :* policy de rate-limit partitionnée par `WebmailUid`, cap de comptes connectés par utilisateur,
  ligne d'audit nommant l'acteur (refus **et** succès).

- 🔴 **Pas de borne sur le HTML entrant** — `Services/MailHtmlSanitizer.cs:99` (appelé depuis
  `ImapSession.cs:733`). Trois arbres DOM + deux sérialisations par appel, aucune limite de taille ;
  un corps de plusieurs Mo (choisi par l'attaquant) transforme un `GET /Messages/Detail` en centaines
  de Mo d'allocations.
  *Fix :* plafond (ex. 2 Mo) → renvoyer une version tronquée/texte avec un flag, sans parser.

- 🔴 **Staging contournable par petits fichiers** — `Services/StagedAttachmentStore.cs:46`.
  Le garde-fou anti-abus compte les octets uniquement ; après sauvegarde la réservation retombe à la
  taille réelle, donc une boucle de fichiers de 1 Ko stocke un nombre illimité d'entrées (inodes,
  mémoire, coût du sweep) pendant les 12 h de TTL.
  *Fix :* cap d'entrées par compte (~50) dans la même étape d'admission.

- 🟠 **AES-GCM sans associated data** — `Services/ConnectedAccountCipher.cs:41-42,63-64`.
  Pas d'AAD → un ciphertext est interchangeable entre deux comptes connectés du **même** utilisateur
  (KEK par-utilisateur, pas par-compte) ; avec accès en écriture à la base, échanger le cipher de A
  dans la ligne B ouvre la boîte B avec le mot de passe de A.
  *Fix :* passer le GUID du compte en AAD, versionner la colonne, traiter un mismatch en `CredentialsInvalid`.

- 🟠 **`HtmlSanitizer` épinglé en version bêta** — `snoopy.microservice.csproj:33` (`9.1.949-beta`),
  pour le composant qui est la barrière XSS principale. Une stable (`9.0.892`) est déjà en cache local.
  *Fix :* passer sur la stable, ou documenter le besoin précis de la bêta.

- ⚪ Algorithme JWT et clock skew non épinglés (`AuthorizationExtension.cs:35`) — accepter
  `ValidAlgorithms = [HmacSha256]`, `ClockSkew = Zero`.
- ⚪ Clé de signature non validée au démarrage (`TokenConstants` / `ApplicationServicesConfiguration.cs:16`,
  `appsettings.json` livre `"Key": ""`) — `[Required]/[MinLength(32)]` + `ValidateDataAnnotations().ValidateOnStart()`.
- ⚪ KEK et plaintext jamais effacés de la mémoire (`ConnectedAccountCipher.cs:28,48,65`) — `CryptographicOperations.ZeroMemory`.
- ⚪ `AllowInvalidCertificate` global s'applique aussi au serveur maison (`MailConnectionFactory.cs:102`) — le mettre par-endpoint ou le refuser hors Development.
- ⚪ Payload d'erreur doveadm loggé verbatim avec l'email (`DovecotQuotaClient.cs:117`).
- ⚪ Fallback v2 malformé traité comme mot de passe IMAP → logins échoués répétés (`MailCredentialStore.cs:69`).
- ⚪ `Path.GetFileName` ne traite pas `\` sous Linux + nom non borné (`StagedAttachmentStore.cs:74`).
- ⚪ Injection de formule CSV non appliquée aux colonnes d'adresses (`ContactCsvExporter.cs:40`) — argument non testé.
- ⚪ `AttachmentSizeLimitFilter` peut no-op silencieusement (`:27`) ; `FormOptions.MultipartBodyLengthLimit`
  figé au démarrage alors que le filtre lit `IOptionsMonitor` (incohérence rule 1).

---

## 2. Bugs avérés

- 🔴 **Le health check ment** — `HealthChecks/DatabaseHealthCheck.cs:12`. `CanConnectAsync` retourne
  `false` (sans lever) quand la base est injoignable, et la valeur de retour est **ignorée** → base
  morte déclarée `Healthy`. Le test existant passe pour une mauvaise raison (contexte disposé). ✅ vérifié.
  *Fix :* `return await ...CanConnectAsync(ct) ? Healthy() : Unhealthy("database unreachable");`

- 🔴 **`Alias.Equals`/`GetHashCode` violent leur contrat** — `Models/Alias.cs:28-46`. Égalité
  insensible à la casse (`InvariantCultureIgnoreCase`), hash sensible à la casse → deux alias égaux
  peuvent tomber dans des buckets différents.
  *Fix :* hacher via `StringComparer.InvariantCultureIgnoreCase.GetHashCode(...)`.

- 🔴 **Pré-check d'alias faux sur domaine partagé** — `Repositories/AliasesRepository.cs:59`. Le check
  filtre sur `DestinationUserId`, absent de la contrainte UNIQUE `(source_addr, source_domain)` ; un
  alias déjà pris par un **autre** utilisateur passe le check → `DbUpdateException` → 500 au lieu du
  message « existe déjà ».
  *Fix :* retirer `DestinationUserId` du prédicat + catch `DbUpdateException` en filet.

- 🔴 **Suppression de domaine détruit les alias silencieusement** — `Repositories/AdminRepository.cs:207`.
  Le refus ne considère que les users ; `source_domain_foreign_key` est `ON DELETE CASCADE`, donc
  supprimer un domaine alias-only détruit tous ses alias sans avertir l'admin.
  *Fix :* garde `AnyAsync` sur les alias, ou remonter le compte d'alias dans la confirmation.

- 🔴 **Le sweeper des trusted senders ne tourne probablement jamais** — `Services/TrustedSenderSweeper.cs:19`.
  `PeriodicTimer(1 jour)` sans tick initial + redéploiement à chaque push (CLAUDE.md) → la rétention
  365 j n'est jamais appliquée.
  *Fix :* un `SweepOnceAsync` au démarrage (dans le même try/catch, léger délai aléatoire).
  Même forme pour `StagedAttachmentSweeper.cs:17` (les orphelins ne sont récupérés qu'après 1 h).

- 🔴 **404 mappé en 502 sur 4 lectures IMAP** — `ImapSession.cs:638,781,902,507`. `ListMessages`,
  `GetMessage`, `GetAttachment`, `Search` ne passent pas le sentinel `FolderNotFound`, contrairement
  aux 7 autres méthodes ; un dossier supprimé par un autre client (course ordinaire) répond 502 au
  lieu de 404. Le commentaire de la classe reconnaît un gel volontaire faute de couverture.
  *Fix :* passer le sentinel sur les 4 + tests.

- ⚪ `WebmailUserStore.cs:37` traite toute `DbUpdateException` comme course d'insert → masque les vraies erreurs.
- ⚪ `ContactStore.cs:118` commentaire faux (EF ne fusionne pas Deleted+Added en Modified — il émet DELETE puis INSERT).
- ⚪ `AliasesRepository.cs:114` comparaison ordinale case-sensitive incohérente avec le reste (fast-path « own domain »).

---

## 3. Performance

Par rentabilité décroissante :

1. 🔴 **Ouvrir un mail télécharge le message entier, pièces jointes comprises** — `ImapSession.cs:724`.
   `GetMessageAsync` FETCH le `BODYSTRUCTURE` puis `GetMessageAsync(uid)` → tout le RFC822 pour lire
   `HtmlBody`/`TextBody`. Un mail avec un PDF de 20 Mo tire 20 Mo pour afficher quelques Ko.
   *Fix :* localiser les parties `text/html`+`text/plain` via le BODYSTRUCTURE déjà en main et ne fetcher
   qu'elles (`GetBodyPartAsync`) + un FETCH HEADER. **Le changement le plus rentable du fichier.**

2. 🔴 **`LCASE()` désactive les index à chaque login** — `EnableStringComparisonTranslations`
   (`DatabaseConfiguration.cs:44`) traduit chaque `InvariantCultureIgnoreCase` en `LCASE(col)=LCASE(@p)`,
   non-sargable → full scan sur `users`/`domains` à chaque login, validation de token, check admin.
   Les tables sont déjà `utf8mb4_general_ci` : un simple `==` suffit et reste indexé.
   Sites : `UsersRepository.cs:228`, `AdminRepository.cs:31,86`, `AliasesRepository.cs:59,98,141,142,150`.
   *Fix :* remplacer par `==` puis retirer `EnableStringComparisonTranslations()`.

3. 🔴 **Recherche tous-dossiers non bornée** — `ImapSession.cs:450`. `{hasAttachment:true, allFolders:true}`
   compile en `SearchQuery.All` (car `MailSearchQueryBuilder` ne compile pas `HasAttachment` mais
   `HasAnyCriterion` l'accepte seul) → FETCH enveloppe + BODYSTRUCTURE de toute la boîte pour une page de 50.
   *Fix :* borner le merge (SORT + `(page+1)*pageSize` candidats/dossier), `Total` en « au moins ».

4. 🔴 **ManageSieve sans timeout après le connect** — `ManageSieveClient.cs:45`, `ManageSieveSession.cs:128`.
   `TcpClient.ReceiveTimeout/SendTimeout` n'affectent que l'I/O synchrone ; tout le dialogue est async
   → un serveur muet bloque la requête indéfiniment, y compris le `LOGOUT` du `DisposeAsync` (token `None`).
   *Fix :* un CTS `CancelAfter` couvrant tout le handshake, propagé aux read/write ; LOGOUT sous CTS 2 s.

5. 🟠 **Vider un dossier énumère chaque UID** — `ImapSession.cs:351`. Le purge fait `SEARCH ALL` +
   set UID explicite (ligne de commande de centaines de Ko sur 100k messages) au lieu de `1:* \Deleted`
   + `EXPUNGE` (ce que décrit son propre commentaire).

6. 🟠 **Téléchargement de pièce jointe : 2-3 copies mémoire complètes** — `ImapSession.cs:880`.
   Part encodée entière en mémoire puis `DecodeToAsync` dans un `MemoryStream` qui double sur le LOH.
   *Fix :* `GetStreamAsync` + `FilteredStream`/`DecoderFilter`, ou au moins pré-dimensionner le `MemoryStream`.

7. 🟠 **N+1 sur la page réglages** — `ConnectedAccountsController.cs:77`. Une requête `sending_identities`
   par compte connecté dans la boucle de réponse.

8. 🟠 **`GetAllUsersAsync` matérialise toute la table users** trackée, hash inclus, sans projection ni
   pagination (`AdminRepository.cs:37`). `AddAlias`/`DeleteAlias` ~6 allers-retours chacun (`:39,78`).

9. ⚪ LOGOUT/QUIT de fin de requête sans token court → jusqu'à 30 s sur connexion morte
   (`ImapSession.cs:1067`, `SmtpSession.cs:36`).
10. ⚪ `IsAdminAsync` requête la base à chaque endpoint admin sans le cache 60 s de `SessionGuard`
    (`AdminRepository.cs:25`, `AdminRequirementHandler.cs:23`).
11. ⚪ `PreviewText` déclenche un FETCH body supplémentaire par page (`ImapSession.cs:640`).
12. ⚪ `SieveRepository` ouvre une session ManageSieve par méthode (jusqu'à 2 par requête) — un
    `ISieveSessionProvider` scoped mirrorerait le design IMAP.
13. ⚪ CSV : export/import matérialisés ~4× (`CsvWriter.cs:21`, `CsvReader.cs:18`, `ContactsController.cs:161`).
14. ⚪ `StagedContentUrl.cs:26` construit un `Regex` à chaque appel (par pièce jointe par envoi) — `[GeneratedRegex]`.
15. ⚪ `_reserved`/`_counts` de staging croissent de façon monotone sans nettoyage (`StagedAttachmentStore.cs:20,169`).
16. ⚪ Pas d'`EnableRetryOnFailure` sur les deux contextes EF (`DatabaseConfiguration.cs:43`).

---

## 4. Duplication — chantiers de refactoring

- 🔴 **`MailController` = 5 contrôleurs dans une classe** (1214 lignes, 10 dépendances, 22 actions).
  Découpage : `MailFoldersController` / `MailMessagesController` / `MailAttachmentsController` /
  `MailComposeController` sur une base `MailControllerBase : ApiBaseController`, routes `api/Mail/...`
  inchangées. Les helpers privés (`TryResolveAsync`, `NormalizeOutgoing`, `RefusedBuild`) deviennent partageables.

- 🔴 **`ImapSession` = 4 responsabilités** (1086 lignes) : adaptateur protocole ; mapping des résumés
  (`FillSummary`, `ToAddressInfos`, `ApplyThreading`) ; utilitaires de pagination (`ComputePageWindow`,
  `PageOf`, `InOrderOf`) ; catalogue statique SPECIAL-USE (`ResolveSpecialUses`) que `FolderRoleResolver.cs:88`
  vient chercher dans une classe porteuse de connexion. À éclater en `SpecialUseCatalog`,
  `MailSummaryMapper`, `MailPaging` + `ImapFolderOperations`/`ImapMessageOperations`.

- 🔴 **Résolution de compte dupliquée 3×** — `MailController.cs:56` / `RulesController.cs:28` (verbatim) /
  `IdentitiesController.cs:103` (moitié 404). C'est « le seul endroit où les 3 statuts sont produits »
  selon CLAUDE.md. À remonter dans `ApiBaseController` (`ToError` / extension sur `Result<MailAccountConnection>`).

- 🔴 **~100 lignes de « wire » ManageSieve dupliquées** entre `ManageSieveClient.cs:157` et
  `ManageSieveSession.cs:148` (writer, reader, `StartsWithKeyword`, parser OK/NO/BYE, unquote, record
  `Status`), avec deux readers déjà divergents (unbuffered vs buffered — bug latent). À extraire en un
  type `ManageSieveWire` construit par le client et passé à la session.

- 🔴 **15 gardes « body requis » + 15 gardes « folder requis »** manuels dans les actions (`MailController`
  et autres) — inatteignables en prod (`[ApiController]` rejette déjà). Un `InvalidModelStateResponseFactory`
  émettant `ResultEnveloppe` + DataAnnotations les remplace tous, et **unifie les deux formes d'erreur**
  de l'API (enveloppe maison vs `ValidationProblemDetails`).

- 🟠 **9 stores quasi identiques** sur `PreferencesDbContext` : même read `AsNoTracking().Where().OrderBy().ToList`,
  même upsert (3 corps byte-for-byte identiques : `AppSettingStore:20`, `UserPreferenceStore:21`,
  `FolderRoleStore:22`), même delete load+RemoveRange. Base `ScopedStore<TEntity>` générique.

- ⚪ `MailMessageRepository` : 13 méthodes pass-through `WithSessionAsync`, paramètre `User` mort partout — supprimer le paramètre ou le repository.
- ⚪ `Quote()`/`QuoteSimple()` byte-for-byte entre `WeeskyRuleProvider.cs:417` et `RainloopRuleProvider.cs:514` → `SieveQuoting`.
- ⚪ 3 teardowns « graceful close + swallow » divergents (`ImapSession:1067`, `SmtpSession:36`, `ManageSieveSession:128`).
- ⚪ Mapping sentinel 404/502 écrit 5× dans `MailController` (494,567,612,1018,1169), 3 orthographes.
- ⚪ Résolution rôle+tree répétée 4× (`MailController:86,131,311,365`) → `IFolderRoleContext` scoped mémoïsé.
- ⚪ `DomainOwnershipInfo` : code mort confirmé (identique à `VirtualDomainInfo`).
- ⚪ Set de schémes du sanitiseur dupliqué (`MailHtmlSanitizer:92`, `OutgoingMailSanitizer:25`) → `MailSchemes`.
- ⚪ 3 règles de « nom d'affichage » divergentes (`ContactCsvExporter:62,71`, `ContactVCardWriter:84`).
- ⚪ `MinPasswordLength` constante ici, magic `8` là, messages divergents (`AdminRepository:11` vs `UsersRepository:185`).

---

## 5. Standards / cohérence

- 🔴 **`CancellationToken` absent des 20 méthodes des 3 anciens repositories** (`IAdminRepository`,
  `IUsersRepository`, `IAliasesRepository`) — violation directe de la règle « ALWAYS use cancellation
  tokens ». Une requête abandonnée poursuit son travail DB. Correctif mécanique (EF accepte déjà le token,
  les contrôleurs le reçoivent). Concerne aussi `UserAuthenticator.cs:38` (`CancellationToken.None`).

- 🔴 **~120 attributs `ProducesResponseType` sans type** (ex. `MailController.cs:121`) → l'OpenAPI généré
  ne documente aucun schéma de réponse. `[ProducesResponseType<T>(...)]` + `<ResultEnveloppe>` sur les erreurs.

- 🔴 **Deux formes d'erreur incompatibles** — `ResultEnveloppe` (manuel) vs `ValidationProblemDetails`
  (framework quand la validation `[ApiController]` trip). Configurer `InvalidModelStateResponseFactory`
  (aussi l'enabler des dédup de validation ci-dessus).

- 🟠 **Deux générations de style** : 5 contrôleurs + 7 repositories/stores en constructeurs classiques
  contre la règle « primary constructors » (`AdminController:23`, `LoginController:19`, `AdminRepository:14`, etc.).

- ⚪ `DateTime.UtcNow` direct dans `TokenBuilder.cs:56` alors que `TimeProvider.System` est dans le DI.
- ⚪ `AdminController.cs:66,129` : 201 sans header `Location` (utiliser `CreatedAtAction`).
- ⚪ `PreferencesController:61`/`AppSettingsController:69` : `StatusCode(204)` vs `NoContent()` ailleurs.
- ⚪ `AliasesController` : `[Authorize]` répété par action alors que la classe le porte ; XML doc `200` vs 204 réel + typos.
- ⚪ Surcharges mortes `TokenBuilder.AddClaims/AddClaim` ; membres `ContactsController` déclarés au milieu de la classe.
- ⚪ Health check ne couvre pas `PreferencesDbContext` ; `ex.Message` (host/port/user) exposé.
- ⚪ Collection expressions manquées (`new List<...>()` → `[]`) ; `throw new ArgumentNullException("literal")` → `ThrowIfNull`.

---

## 6. Modèle de données

- ⚪ `ApplicationDbContext` ne déclare **aucune** relation : `MailUser→MailDomain`, `MailAlias→...`,
  `MailDomainOwnership→...` existent en FK réelles mais sont invisibles d'EF (chaque repository ré-écrit
  les mêmes joins). Même classe de problème que le gap connu de `PreferencesDbContext`. Déclarer les
  arêtes `HasOne<T>().WithMany().HasForeignKey(...)` sans navigation, comme `PreferencesDbContext.cs:34`.
- ⚪ Aucun index unique déclaré côté EF pour `aliases(source_addr,source_domain)` / `users(username,domain)` — racine du bug alias ci-dessus.
- ⚪ Aucun token de concurrence sur les entités → last-write-wins entre deux admins (`AdminRepository:109`, `ExternalDomainStore:42`).
- ⚪ 4 check-then-insert sans transaction (`AdminRepository:258`, `ExternalDomainStore:29`, `ConnectedAccountStore:29`, `ContactStore:70`) + `FolderRoleStore.UpsertAsync:22`.
- ⚪ `MailUser.FullName` `string?` vs colonne `NOT NULL` ; `Name`/`FullName` sans `[StringLength]`.
- ⚪ `ExternalDomain.ImapSecurity/SmtpSecurity` en free string → `EnumToStringConverter` (source du chemin « transport ne parse plus → 404 »).
- ⚪ `Contact.updated_at` en `TIMESTAMP` (décalage fuseau) alors que les autres tables sont en `DATETIME`.
- ⚪ `TrustedSenderStore.SweepExpiredAsync` full scan (pas d'index sur `last_used`) + matérialise tout en mémoire.

---

## 7. RuleProviders

- ⚪ `RainloopRuleProvider.cs:105` : `ConditionsType` autre que `"Any"` traité silencieusement comme
  `"All"` (même garbage), contre la philosophie whitelist-and-reject du fichier. Échouer le parse sur valeur inconnue.
- ⚪ Duplication `Quote()` (voir §4).

---

## Bilan des quatre vagues

Les quatre vagues prévues sont livrées et validées manuellement. Suite passée de **1684 à 1919 tests**,
build Release sans avertissement à chaque étape.

| Vague | Objet | Résultat |
|---|---|---|
| 1 | Sécurité + bugs | 9 correctifs ; 2 défauts trouvés ensuite par revue adverse et corrigés |
| 2 | Performance | ouverture d'un mail : ~27 Mo → quelques Ko ; sanitiseur : 22-71 s → 1,7 s pire cas |
| 3 | Standards mécaniques | une seule forme d'erreur sur toute l'API ; annulation propagée partout |
| 4 | Architecture | 2 fichiers de plus de 1000 lignes découpés ; 3 duplications éliminées |

**Ce que la revue adverse a rapporté.** Lancée après les vagues 1 et 2, elle a trouvé trois régressions
réelles qu'aucun agent n'avait vues sur son propre code — dont la réouverture du déni de service que la
vague 2 devait fermer, et l'affichage cassé de tout mail en texte brut. Sans elle, le tout partait en
déploiement avec la suite au vert. C'est la leçon la plus transférable de l'exercice.

### Ce qui reste ouvert, et assumé

- **Le `LCASE`/index reste écarté** conformément à l'arbitrage du 2026-07-25 ; à ne rouvrir qu'avec la
  base de production sous les yeux (voir ci-dessous).
- `Total` devient un « au moins » quand le budget de balayage tronque, sans champ pour le signaler :
  un booléen sur `MailSearchPage` plus un rendu côté frontend.
- Pas de pagination sur `GET /api/Admin/users` (~1,5 Mo par chargement à 10 000 comptes) — changement
  de contrat.
- `GET /api/account` renvoie `isAdmin` non caché alors que l'autorisation répond depuis un cache de 60 s :
  le lien Administration et l'API peuvent diverger après une modification hors application.
- Le scan du sanitiseur reste un compteur, pas un arbre : un document pathologiquement mal imbriqué peut
  être aplati au-delà de 1024 niveaux — le contenu survit, la mise en page non.
- `api.js` peut perdre sa branche `ProblemDetails`, devenue morte depuis la vague 3.
- `AdminOwnershipRequest` est du code mort, repéré au passage.
- `ApiDocumentation.xml` est un artefact versionné chroniquement périmé que chaque `dotnet test` salit ;
  à régénérer en un commit dédié, ou à sortir du versionnement.
- Une seule assertion à la montre subsiste (`ImapSessionDisposeTests`), pilotée par minuteur donc peu
  sensible à la vitesse de la machine, mais non nulle sur un conteneur affamé.

---

*Contrainte projet respectée dans cette revue : le stockage plaintext des mots de passe (trigger
MariaDB) n'est jamais signalé comme un bug ; TTL staging 12 h intentionnel.*
