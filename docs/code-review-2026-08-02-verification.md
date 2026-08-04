# Contre-revue de `backend-refactor-1` — 2026-08-02

Revue de vérification portant sur les 68 commits de la branche, menée contre
`docs/code-review-2026-08-01.md` pris comme spécification. Quatre questions :
les constats étaient-ils réels, les correctifs portent-ils, ont-ils introduit
des régressions ou des brèches, et que reste-t-il à refactorer.

**Méthode.** `git show master:` pour établir que chaque défaut existait avant la branche,
lecture du code actuel pour établir que le correctif porte, et lecture du schéma réel
(`assets/dovecot.sql`) là où le rapport invoquait une contrainte de base. Suite exécutée :
**1919 tests, 0 échec**. Aucun sous-agent : le travail relu est le mien, déléguer la
vérification à des agents que j'aurais briefés reproduirait le biais au lieu de le corriger.

## Verdict

Les correctifs appliqués sont corrects et les constats étaient fondés. Le problème n'est pas
ce qui a été fait, c'est ce qui a été perdu en route : **six constats classés 🔴 ou 🟠 par le
rapport lui-même n'ont jamais été corrigés, et ne figurent pas non plus dans sa liste
« Ce qui reste ouvert, et assumé »**. Les tableaux de vagues suivent ce que les vagues ont
fait, pas ce que la revue avait trouvé, et l'écart n'a jamais été rapproché. Un lecteur du
rapport du 2026-08-01 conclut aujourd'hui que tout est traité.

---

## 1. Les constats étaient-ils réels ?

Oui, sur tout ce qui a pu être recontrôlé. Les deux derniers sont vérifiés contre le schéma
plutôt que sur la foi du rapport.

| Constat | Preuve dans `master` |
|---|---|
| Échappement XSS perdu | `MailHtmlSanitizer.cs:176` finissait sur `document.Body?.InnerHtml` |
| Health check menteur | `await CanConnectAsync(ct);` puis `return Healthy()` — valeur de retour jetée |
| `Alias.GetHashCode` | `$"{Name}@{Domain}".GetHashCode()` contre un `Equals` en `IgnoreCase` |
| JWT dans le corps du login | `ActionResult<AuthToken>` sérialisait le token entier |
| Staging en octets seuls | seul `_reserved` existait, aucun compteur d'entrées |
| Pré-check d'alias | `UNIQUE KEY source_addr_2 (source_addr, source_domain)` — `destination_user` n'y figure pas |
| Cascade domaine → alias | `source_domain_foreign_key … ON DELETE CASCADE`, là où `users_domain_foreign_key` n'a **pas** de cascade |

Ce dernier point mérite d'être souligné : la garde `AnyAsync` sur les users existe parce que
la FK users **refuserait** la suppression de toute façon. Les alias, eux, partent en silence.
La garde protégeait exactement le cas qui se protégeait tout seul.

## 2. Six constats jamais traités ni déclarés

| Sév. | Constat | État réel au 2026-08-02 |
|---|---|---|
| 🔴 | Pré-check d'alias sur domaine partagé | `AliasesRepository.cs:50` filtre toujours `a.DestinationUserId == mailUser.Id` ; aucun `catch (DbUpdateException)` dans le fichier → **500 au lieu de « existe déjà »** |
| 🔴 | Suppression de domaine détruit les alias | `AdminRepository.cs:249` ne teste que `context.Users` |
| 🔴 | Oracle de test de mots de passe | politique `login` toujours partitionnée sur `RemoteIpAddress` seule et partagée avec `/api/login` ; aucun cap de comptes par utilisateur ; le log ne nomme que l'adresse sondée, jamais l'acteur |
| 🔴 | `ProducesResponseType` sans type | **328 non typés, 0 typé** (le rapport en annonçait ~120) |
| 🟠 | AES-GCM sans associated data | **corrigé le 2026-08-04** — voir §7 |
| 🟠 | `HtmlSanitizer` épinglé en bêta | toujours `9.1.949-beta`, sur la barrière XSS principale |

## 3. Une régression de la même classe, à quatre lignes du correctif

`src/frontend/src/modules/mail/queries.ts:188` :

```ts
placeholderData: (previous) => previous,   // useSearchMessages — non gardé
```

C'est le défaut signalé par l'utilisateur, corrigé ligne 125 pour `useMailMessages` et laissé
intact ici. La clé de recherche contient `accountId`, donc au changement de compte le
placeholder rend **les résultats de recherche du compte précédent** sous l'en-tête du nouveau.
`useSearchMessages` est vivant (`MessageList.tsx:81`). Le correctif a traité l'occurrence
rapportée, pas la classe.

## 4. Un correctif livré à moitié

La revue adverse de la vague 1 avait trouvé que le drapeau `Truncated` « n'atteignait
personne » et concluait : *amputé en silence, strictement pire qu'avant le correctif*. Le champ
a bien été ajouté à `MailMessageDetail` côté backend — mais l'interface TypeScript homonyme
(`mailTypes.ts:98`) ne le déclare pas, et rien dans l'UI ne le lit. Seul `MailMessageSource` a
sa bannière (`MessageSourceView.tsx:72`).

Un mail dépassant 2 M caractères ou 20 000 nœuds est donc toujours tronqué sans que le lecteur
l'apprenne. Le défaut est passé de « invisible dans l'API » à « invisible dans l'UI ».

## 5. Ce qui a résisté à l'examen adverse

Pour être juste sur la qualité du travail livré, ces points ont été attaqués sans résultat :

- **Le scanner du sanitiseur** — `ReadTag` / `EndOfTag` / `SkipRawText` / `EndOfComment` relus
  en cherchant une divergence avec le tokeniser HTML5. Les frontières sont les bonnes (`<`
  n'est pas un terminateur de nom, le guillemet n'ouvre une valeur qu'après `=`, `--!>` ferme
  un commentaire), la progression de `i` est stricte donc aucune boucle infinie, et l'élision
  de profondeur est symétrique entre balise ouvrante et fermante.
- **Le compteur de staging** — équilibré sur les six chemins, y compris le retour anticipé
  « dépasse la limite », et les deux dictionnaires se purgent à zéro.
- **La purge de dossier** — `1:*` est équivalent au `SEARCH ALL` de `master` ; la disparition
  du court-circuit sur dossier vide ne coûte qu'un aller-retour no-op.
- **Le découpage des contrôleurs** — les quatre portent `[Route("api/Mail")]` en dur, aucun
  jeton `[controller]` résiduel.
- **`ManageSieveWire.TryStartTlsAsync`** — le `SslStream` devient propriétaire avant de pouvoir
  lever, et la garde sur le tampon pré-STARTTLS est correcte.
- **Le cache d'époque admin** — publication après l'`await`, jamais via une fabrique.

## 6. Durcissement incomplet (mineur)

Le plafond de 1 Mo de `ManageSieveWire` borne **une ligne**, pas un dialogue.
`ReadCapabilitiesAsync` (client) et `ListScriptsCoreAsync` (session) accumulent sans borne de
cardinalité — un serveur bavard n'est limité que par le timeout. C'est la classe que la vague 4
déclarait fermée. L'hôte est configuré par un administrateur, donc 🟠 et non 🔴.

---

## 7. AES-GCM sans associated data — corrigé le 2026-08-04

**Décision du 2026-08-02 : constat accepté, correction reportée** (menace retenue : un **DBA mal
intentionné**). **Reprise et livrée le 2026-08-04** — voir « Ce qui a été livré » en fin de
section. L'analyse ci-dessous est conservée telle quelle : elle explique pourquoi le correctif a
la forme qu'il a, et notamment pourquoi lier le seul identifiant de compte n'aurait pas suffi.

### Le dispositif

```
KEK = PBKDF2(mot de passe principal, sel par utilisateur, 600 000)
row.Cipher = nonce(12) ‖ tag(16) ‖ AES-256-GCM(KEK, mot de passe du compte connecté)
```

La KEK dérive du mot de passe **principal** : il y a donc **une seule clé par utilisateur**,
partagée par tous ses comptes connectés. C'est ce qui rend le reste possible.

À la résolution (`AccountConnectionResolver.cs:49`), le serveur déchiffre `row.Cipher`, puis
choisit **où** envoyer ce mot de passe à partir de deux autres colonnes de la même ligne :
`row.DomainId` (qui pointe une ligne `external_domains` portant hôte/port/sécurité) et
`row.Email`. Le chiffré n'est lié ni à l'une ni à l'autre.

### Le problème réel

Le modèle de menace est écrit en tête du fichier : *« le serveur seul ne peut jamais déchiffrer
ce qu'il stocke »*. La construction existe pour survivre à une compromission de la base. Un
attaquant disposant d'une **écriture** sur `connected_accounts` est donc dans le périmètre par
construction. Il n'a pas le mot de passe principal et ne peut rien déchiffrer — mais il n'en a
pas besoin, il lui suffit de changer la destination.

**Variante A — copie de chiffré.** Copier le `cipher` de la ligne A dans la ligne B. Même
utilisateur, donc même KEK, donc ça déchiffre. Le serveur envoie le mot de passe **de A** vers
l'hôte **de B**.

**Variante B — simple redirection.** Ne toucher à aucun chiffré : repointer `row.DomainId` vers
une ligne `external_domains` créée par l'attaquant, avec `host = attaquant.example`.
L'utilisateur sélectionne son compte, le serveur déchiffre fidèlement et **envoie le mot de
passe en clair à l'attaquant**. SASL PLAIN transmet les identifiants avant toute réponse du
serveur — ce que `MailConnectionFactory` documente déjà : *« AuthenticateAsync sends the
password whether or not the login succeeds »*. TLS ne protège pas : l'attaquant présente un
certificat valide pour son propre domaine.

**Le rapport du 2026-08-01 décrit mal ce constat.** Il affirme qu'échanger le cipher de A dans
la ligne B « ouvre la boîte B avec le mot de passe de A » : c'est faux, un mot de passe de A
face à la boîte B échoue simplement à s'authentifier, sauf si les deux coïncident. Le gain réel
est **l'exfiltration** du mot de passe du fournisseur externe, sans jamais connaître le mot de
passe principal.

### Pourquoi le correctif proposé par le rapport est insuffisant

Le rapport propose de « passer le GUID du compte en AAD ». Cela ferme la variante A. Cela ne
fait **rien** contre la variante B, où le GUID de la ligne n'a pas bougé — or la variante B est
plus simple, ne demande aucune manipulation cryptographique et a la même charge utile. Le GUID
seul fermerait la porte en laissant la fenêtre ouverte.

### La solution retenue le jour où ce sera corrigé

Lier dans l'AAD **tout ce qui décide où va le secret et sous quelle identité** :

```csharp
// L'AAD ne protège pas le secret, elle protège son contexte : un chiffré ne déchiffre
// que sous la ligne qui l'a produit. Repointer la ligne ailleurs casse le tag plutôt que
// d'envoyer le mot de passe à l'hôte qu'on vient d'y inscrire.
private static byte[] Context(Guid accountId, Guid userId, string email, Guid? domainId) =>
    Encoding.UTF8.GetBytes($"v2|{accountId}|{userId}|{email}|{domainId}");
```

Le comportement en cas d'altération est le bon : `AesGcm.Decrypt` lève, `Decrypt` renvoie déjà
`CredentialsInvalid`, le contrôleur répond 409 et le client demande de ressaisir le mot de
passe. **Échouer fermé au lieu d'exfiltrer en silence.**

**L'opération est peu coûteuse ici** : `ConnectedAccountsController` n'expose que `POST`
(création), `PUT {id}/Password` et `DELETE`. **Aucun chemin légitime ne modifie l'email ou le
domaine d'un compte existant** — ces champs sont immuables en pratique, donc les lier dans
l'AAD ne casse aucun scénario réel. C'est rarement le cas avec un AAD, et cela plaide pour le
faire complet plutôt qu'à moitié.

**Le vrai risque est la migration, pas la crypto.** Les chiffrés existants n'ont pas d'AAD ;
les déchiffrer avec en casserait tous les comptes connectés d'un coup, obligeant chaque
utilisateur à ressaisir ses mots de passe fournisseur. Il faut un octet de version en tête du
blob : `0x02` → chemin AAD, sinon chemin hérité, et ré-chiffrement en v2 dès que le clair est
en main. `AccountController.cs:144-160` (changement de mot de passe principal) déchiffre et
re-chiffre déjà tout : c'est le point de migration naturel. Côté place, `cipher` est
`VARBINARY(512)` et `MaxSecretLength` vaut 484 = 512−12−16 ; l'octet de version le ramène à
483, sans effet pratique.

### Ce que l'AAD ne couvrira pas, et qu'il faut assumer

Un attaquant qui modifie la ligne `external_domains` **elle-même** — changer l'hôte d'un
fournisseur que l'utilisateur emploie légitimement — reste hors d'atteinte, à moins de mettre
hôte et port dans l'AAD, ce qui casserait tous les chiffrés le jour où un administrateur
corrige légitimement l'hôte d'un fournisseur. Cette surface relève de l'autorisation admin et
de l'audit sur `external_domains`, pas de la cryptographie.

### Ce qui a été livré le 2026-08-04

Le format du chiffré devient `0x02 ‖ nonce(12) ‖ tag(16) ‖ ciphertext`, avec pour données
associées `accountId | userId | domainId | email` — les quatre colonnes qui décident où part le
secret et sous quelle identité. `MaxSecretLength` passe de 484 à 483, le blob remplissant
toujours exactement les 512 octets de la colonne.

**La migration est ce qui donne sa valeur au correctif.** Les lignes antérieures s'ouvrent
toujours (pas de version, pas d'AAD) — refuser aurait coupé tous les comptes connectés le jour du
déploiement, et ces mots de passe fournisseur ne sont pas les nôtres à redemander. Elles sont
**reliées à leur ligne dès la première lecture réussie**, dans `AccountConnectionResolver`, en
best-effort : une écriture qui échoue ne fait pas échouer l'ouverture de la boîte, la requête
suivante réessaie. Le changement de mot de passe principal migre également tout le lot au
passage. Sans cette reprise, seuls les comptes créés après le déploiement auraient été protégés.

**Trois pièges rencontrés, tous fermés.** `ConnectedAccountStore.CreateAsync` générait l'`Id`
*après* que le contrôleur avait chiffré : l'id est désormais frappé par l'appelant et le store ne
l'écrase plus. Le store canonicalise l'email qu'il écrit, donc un contexte bâti sur la saisie
brute aurait produit un chiffré ne se rouvrant jamais — `Context(row)` canonicalise lui-même. Et
le marqueur de version est un **indice, jamais une décision** : une ligne antérieure commence sur
un octet de nonce aléatoire, donc vaut `0x02` une fois sur 256 ; c'est le tag qui tranche, et une
lecture liée qui échoue retombe sur la lecture non liée.

**13 tests ajoutés** (suite 1927 → 1940) : altération de chacun des quatre champs — dont la
variante « ligne repointée vers un autre domaine » que le remède du GUID seul aurait laissée
passer — le cas local sans domaine, la lecture d'une ligne antérieure, celle dont le premier octet
imite le marqueur, le refus d'une ligne antérieure sous une mauvaise clé, et la migration
elle-même vue depuis le résolveur.

Le commentaire de tête de `ConnectedAccountCipher` a été réécrit : il énonce désormais ce que le
code tient réellement, à savoir que la liaison protège la **destination** du secret et non le
secret lui-même.

### Ce qui reste hors d'atteinte, et assumé

Un attaquant qui modifie la ligne `external_domains` **elle-même** — changer l'hôte d'un
fournisseur légitimement utilisé — n'est pas couvert : il faudrait hôte et port dans l'AAD, ce qui
casserait tous les chiffrés le jour où un administrateur corrige légitimement un hôte. Cette
surface relève de l'autorisation admin et de l'audit sur `external_domains`, pas de la
cryptographie.

---

## 8. Arbitrage sur le reste

### Corrigé dans la foulée de cette revue (voir §9)

1. `useSearchMessages` — le garde de la ligne 125 recopié
2. Pré-check d'alias : retrait de `DestinationUserId` + filet `DbUpdateException`
3. Cascade domaine → alias : garde avant suppression
4. Bannière de troncature dans le détail du message
5. Ligne d'audit nommant l'acteur sur la sonde de mot de passe
6. `UsersRepository` : le `8` magique remplacé par la constante partagée

### Écarté délibérément — excès de zèle

- **Relations EF sur `ApplicationDbContext`** : les repositories écrivent des joins explicites
  qui fonctionnent ; déclarer les arêtes change l'ordre d'INSERT d'EF pour un gain nul ici.
- **`ProducesResponseType` typés** : 328 attributs à toucher. À ne faire que si un consommateur
  lit réellement l'OpenAPI ; sinon c'est du bruit de diff.
- **`MailMessageRepository`** et son paramètre `User` mort (12 méthodes), le set de schémes
  dupliqué entre les deux sanitiseurs, `ConditionsType` de Rainloop traité silencieusement :
  cosmétique.
- **`AllowInvalidCertificate`, `ClockSkew`, effacement mémoire de la KEK** : théoriques dans ce
  modèle de menace.
- **`LCASE`/index — clos définitivement le 2026-08-04, ne pas rouvrir.** Voir §10.

### Restant ouvert, non planifié

- L'oracle de test de mots de passe garde sa limite par IP partagée avec `/api/login` et n'a
  toujours pas de cap de comptes connectés par utilisateur. Seule la ligne d'audit est traitée
  ici — c'était la partie la moins chère et la plus utile.
- `HtmlSanitizer` reste en `9.1.949-beta`.
- Les bornes de cardinalité ManageSieve du §6.
- Les restes déjà listés dans le rapport du 2026-08-01 (`Total` « au moins », pagination
  `GET /api/Admin/users`, divergence `isAdmin` de `GET /api/account`, compteur plutôt qu'arbre
  dans le sanitiseur, `api.js` et sa branche `ProblemDetails` morte, `AdminOwnershipRequest`,
  `ApiDocumentation.xml`).

## 9. Correctifs livrés

Backend **1927 tests** (1919 + 8), build Release 0 avertissement. Frontend **2281 tests**,
typecheck et lint propres.

| # | Correctif | Verrou |
|---|---|---|
| 1 | `useSearchMessages` : placeholder gardé sur `accountId` | 2 tests — le changement de compte vide le placeholder, la page suivante du même compte le garde |
| 2 | Pré-check d'alias élargi à `(source_addr, source_domain)` + `catch (DbUpdateException)` | 2 tests — refus, et aucune écriture |
| 3 | Suppression de domaine : cascade annoncée puis confirmée, jamais silencieuse | 5 tests back + 4 front — voir la correction ci-dessous |
| 4 | Bannière de troncature dans le lecteur | 3 tests — présente, absente, et absente aussi quand l'API ne renvoie pas le champ |
| 5 | Audit `connect_account` nommant l'acteur **et** la cible, sur refus comme sur succès | — |
| 6 | `PasswordPolicy.MinimumLength` partagé par les deux repositories | — |

### Correction du n°3 — le premier remède était trop strict

La première version refusait purement et simplement la suppression d'un domaine portant des
alias. **C'était une erreur d'arbitrage**, relevée en répondant à la question « pourquoi refuser ? »
et corrigée dans la foulée. Trois faits l'ont établie :

- Il n'existe **aucun endpoint admin** listant ou supprimant des alias — la surface admin couvre
  users, domaines, propriétaires de domaines virtuels et domaines externes.
- `AliasesController` n'est pas réservé aux admins et passe par `UserOwnsDomainAsync` : **être
  admin n'y donne aucun droit**.
- `GetAliasesAsync` filtre sur `alias.DestinationUserId == usr.Id` : **un utilisateur ne voit que
  ses propres alias**, même sur un domaine qu'il possède.

Un refus sec rendait donc un domaine d'alias **indélébile depuis l'application** : il aurait fallu
que chaque détenteur d'alias se connecte et supprime les siens, sans que l'admin puisse seulement
savoir de qui il s'agit. Or un domaine virtuel porte des alias par définition — c'est l'onglet
« Virtual domains » — et la cascade est déclarée délibérément dans le schéma : l'auteur *voulait*
que les alias partent avec le domaine. Le garde combattait l'intention du schéma au lieu de
corriger ce qui manquait.

**Le défaut était le silence, pas la suppression.** La version livrée :

```
DELETE /api/Admin/domains/{id}                     → 400 « would also delete 3 aliases »
DELETE /api/Admin/domains/{id}?deleteAliases=true  → 204 + ligne d'audit avec le compte
```

`GetAllDomainsAsync` porte désormais `AliasCount`, sur la même requête — la confirmation n'a
aucune autre source pour dire ce que la suppression coûte, et un aller-retour par domaine aurait
réintroduit le N+1 que ce repository venait de perdre. Côté UI, `DeleteConfirmModal` reçoit un
`message` nommant le nombre d'alias et le fait que le courrier vers ces adresses cessera d'être
distribué ; confirmer **est** l'acquittement envoyé sur le fil. La ligne d'audit est la seule
trace qui subsiste des lignes détruites.

**La leçon.** Le rapport du 2026-08-01 proposait deux remèdes — « garde `AnyAsync` **ou** remonter
le compte d'alias dans la confirmation » — et le premier jet a pris le premier sans arbitrer.
Quand un constat offre deux remèdes, le choix entre eux est une décision à motiver, pas un détail
d'implémentation.

**Choix à connaître sur le n°2.** Le pré-check ne demande plus si *cet utilisateur* détient déjà
l'adresse mais si *quiconque* la détient, ce qui est exactement ce que dit la contrainte unique.
Le message perd son « for this user », devenu faux. Le `catch (DbUpdateException)` est le filet
sur la course entre le check et l'insert : il détache l'entrée refusée plutôt que de la laisser
`Added`, sans quoi un `SaveChanges` ultérieur sur le même contexte scopé rejouerait la ligne qui
vient de rebondir. **Ce chemin n'est pas couvert par un test** : le provider InMemory n'applique
aucun index unique, donc rien ne peut lever `DbUpdateException` en test — c'est une garde de
production, assumée comme telle.

**Le n°4 teste l'absence du champ**, parce que le frontend et le microservice se déploient par
deux canaux sans ordre garanti : contre une API antérieure, `truncated` arrive `undefined`, et
une bannière sur chaque message serait pire que le silence qu'elle corrige.

**Le n°5 n'a pas de test.** Une assertion sur une ligne de log est fragile et vérifie la mise en
forme plutôt que le comportement ; le reste de l'audit du projet n'en a pas non plus.

Ce qui restait ouvert au §8 le reste : rien ici ne touche à la limite par IP de l'oracle, au cap
de comptes connectés ni à la version bêta de `HtmlSanitizer`. L'AAD du §7, laissé ouvert à cette
date, a été livré le 2026-08-04.

---

## 10. `LCASE`/index — clos définitivement le 2026-08-04

**Décision : on ne touche à rien.** Ce constat a été soulevé par la revue du 2026-07-25 (écarté),
re-signalé par celle du 2026-08-01 (🔴, sans connaître l'arbitrage précédent), puis ré-analysé le
2026-08-04 avec les données de production sous les yeux. La réponse est la même. **Cette section
existe pour qu'il ne remonte pas une quatrième fois.**

### Ce que le code fait réellement

`DatabaseConfiguration.cs:44` active `EnableStringComparisonTranslations()`, qui traduit
`string.Equals(col, val, InvariantCultureIgnoreCase)` en `LCASE(col) = LCASE(@p)`. Une fonction
sur la colonne rend le prédicat non-sargable : l'index `UNIQUE KEY (username, domain)` de `users`
n'est pas utilisable, MySQL se restreint à un domaine via `KEY users_domain_foreign_key (domain)`
puis évalue `LCASE()` ligne par ligne.

### Les deux faits qui ferment le dossier

**1. Le `LCASE()` n'est pas décoratif — c'est tout le mécanisme d'insensibilité à la casse au
login.** Il n'existe aucun `ToLower` en C# sur ce chemin : `LoginController` →
`UserAuthenticator` (passe-plat) → `VerifyCredentialsAsync` (`email.Split('@')`, parties brutes)
→ `FindMailUserAsync`. Le repli se fait en SQL. **Le remède proposé par la revue du 2026-08-01 —
remplacer par `==` tout court — aurait donc confié le login à la collation**, sans que personne
l'ait vérifiée. C'est le piège que l'arbitrage du 2026-07-25 avait pressenti sans le nommer.

**2. Une asymétrie existe déjà, et elle est bénigne.** Dans le même `where`, le domaine est
comparé par `domain.Name == domainName` — **sans `LCASE`**. La moitié domaine dépend donc déjà de
la collation, et fonctionne en production. Les deux moitiés d'une même adresse sont traitées par
deux mécanismes différents ; rien ne le documente ailleurs qu'ici.

### Le correctif qui aurait été sûr, et pourquoi il n'a pas été fait

Vérifié en production le 2026-08-04 : **tous les `username` sont en minuscules, tous les
`domains.id` en majuscules.** Le `LCASE()` côté colonne est donc un no-op pour toute ligne
existante. Replier l'entrée en C# (`name.ToLowerInvariant()`) puis comparer par `==` aurait donné
le même résultat, rendu l'index utilisable, et **supprimé toute dépendance à la collation** —
laquelle n'a jamais pu être lue, les requêtes `information_schema` n'ayant rien renvoyé de
concluant.

Écarté malgré tout, et c'est le bon arbitrage : le `LCASE()` ne porte que sur les utilisateurs
d'**un seul domaine**, soit une fraction de milliseconde à cette échelle. Le gain mesurable est
nul, le risque n'est pas nul, et le chemin touché est celui du login. Rien ne justifie d'y toucher
tant que la volumétrie ne change pas.

### Ce qui rouvrirait légitimement le dossier

Un domaine unique dépassant l'ordre de la dizaine de milliers de comptes, mesures de latence de
login à l'appui. Rien d'autre. **Une revue statique qui re-signale ce constat sans ces mesures
doit être renvoyée à cette section.**
