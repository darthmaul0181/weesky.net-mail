# Contacts 3c — capture automatique des destinataires

Tranche 3c du module Contacts, à la suite de
[3a/3b](2026-07-27-webmail-contacts-3a3b-design.md). Un envoi vers une adresse absente du carnet y
crée une fiche ; une réponse fournit en prime le nom complet de l'émetteur devenu destinataire.

## Ce que fait la tranche

Après un envoi réussi, le composeur compare les destinataires au carnet et crée une fiche par
adresse inconnue. Un toast annonce ce qui a été créé et propose de l'annuler. Un interrupteur dans
Paramètres → Général coupe le mécanisme.

Sur une réponse, les noms portés par les en-têtes du message d'origine voyagent jusqu'à l'envoi et
remplissent prénom et nom des fiches créées.

La tranche active par ailleurs la bascule **« Trust my contacts »**, posée désactivée dans Général
lors d'une tranche antérieure sous la note « Available once Contacts ships ». Contacts a livré ; la
note est devenue fausse et le réglage devient réel.

## Décisions

**1. La capture crée, elle n'enrichit jamais.** Une adresse déjà portée par au moins un contact ne
déclenche rien — ni création, ni mise à jour. Deux conséquences, toutes deux voulues :

- **La question ouverte de 3a/3b se referme sans être tranchée.** « Une adresse entrante déjà connue
  peut désigner plusieurs contacts ; lequel enrichir ? » n'avait de raison d'être que si l'on
  enrichissait. La demande initiale est satisfaite telle quelle : l'émetteur à qui l'on répond est
  inconnu au moment où on lui répond, donc il est *créé* avec son nom.
- **Ce qui a été saisi à la main n'est jamais réécrit par un en-tête de mail.** Un contact dont le
  nom est vide l'est parce que quelqu'un l'a laissé vide. Le cas réel que cela laisse passer —
  fiche née anonyme d'un premier envoi, dont la réponse ultérieure apporterait le nom — reste à la
  charge de l'utilisateur, qui l'édite en deux clics.

**2. La capture est silencieuse, et le toast la rend réversible.** Rien n'est demandé avant
création. Le défaut d'une collecte silencieuse est un carnet qui gonfle sans que personne le
remarque : le toast est précisément ce qui manque, et l'annulation se fait au moment où l'on sait
plutôt qu'au cours d'un ménage six mois plus tard. Une proposition *avant* création a été écartée :
elle ferait de tout premier envoi vers quelqu'un un geste en deux temps, et une proposition qu'on
laisse filer perd l'adresse — c'est-à-dire le cas où la capture automatique servait à quelque chose.

**3. La capture vit côté client.** `ComposeView` tient déjà le carnet, les trois listes de
destinataires, l'identité et les identités d'envoi ; aucun de ces éléments n'a besoin d'être
transporté. Côté serveur il faudrait acheminer les noms sur le fil, brancher le `ContactStore` sur
le chemin d'envoi, et garantir qu'un échec de capture ne fasse jamais échouer un envoi — pour un
bénéfice qui n'existe que le jour où un second client parle à l'API.

**4. Une fiche capturée porte son origine.** Colonne `source` sur `contacts`. Rien ne la lit
aujourd'hui, aucune UI ne change. C'est le raisonnement qui avait fait entrer `uid` et `vcard_raw`
en 3a/3b : l'information ne se reconstitue pas après coup, et sans elle un tri « collectés à part »
à la Gmail ne s'appliquerait jamais qu'aux fiches postérieures à la colonne.

## Schéma

Une seule ligne, à rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`. Le document de
prérequis de 3a/3b la reçoit, comme le reste des tables du module.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `source` ENUM('manual','captured','imported')
    NOT NULL DEFAULT 'manual'
    COMMENT 'Origine de la fiche ; écrite à la création seulement'
    AFTER `is_favorite`;
```

`DEFAULT 'manual'` classe correctement les fiches existantes : à ce jour, toutes ont été saisies.

**La colonne s'écrit à la création et jamais ensuite.** `UpdateAsync` ne la touche pas — éditer une
fiche capturée ne doit pas la faire passer pour saisie à la main. C'est la règle entière ; elle vaut
aussi pour l'import de 3d.

`ContactWrite` gagne un `Source` optionnel, validé contre les trois valeurs et ramené à `manual`
quand il est absent ou inconnu ; côté frontend, `ContactDraft` gagne le même champ optionnel et la
capture est le seul appelant qui le renseigne — l'éditeur ne l'envoie pas. Le client déclare donc sa
propre origine : il n'y a pas de frontière de confiance ici — c'est son carnet, et une valeur
mensongère ne lèse que celui qui l'écrit.

La colonne n'est **pas** servie au client. `ContactListResponse` ne change pas, `Contact` côté
frontend non plus. Une projection qui ramène une donnée que personne ne lit est du poids sur chaque
chargement de page.

## Le chemin du nom

`ComposeSeed` gagne un champ :

```ts
/** Adresse canonique → nom complet, tel que les en-têtes de l'original les portaient. */
nameHints: Record<string, string>
```

`buildComposeSeed` le construit à partir de **toutes** les boîtes que le message d'origine nomme —
`from`, `replyTo`, `to`, `cc`, `bcc`. Un *replyAll* place les autres destinataires en Cc et leurs
noms sont là aussi ; les prendre tous coûte le même parcours que d'en trier une partie.

Le nom ne voyage pas dans les jetons. Un jeton `"Alice Dupont" <alice@x.be>` changerait l'allure du
champ To et le format de ce qui part sur le fil, pour transporter une donnée que seule la capture
consomme.

Deux cartes vides, assumées : un *forward* en produit une inutile — ses destinataires sont tapés à
la main — et `buildDraftSeed` en produit une vide, un brouillon repris ne conservant pas les
en-têtes de l'original. Une réponse enregistrée en brouillon puis reprise perd donc le nom ; la
fiche naît avec la seule adresse.

## La décision — `modules/contacts/captureModel.ts`

Pur, sans dépendance à React ni à l'API.

```ts
export interface CaptureCandidate { firstName: string; lastName: string; address: string }

export function splitFullName(raw: string, address: string): { firstName: string; lastName: string }

export function capturable(
  contacts: Contact[],
  recipients: string[],
  nameHints: Record<string, string>,
  mine: Set<string>,
): CaptureCandidate[]
```

`capturable` écarte, dans cet ordre : les entrées vides, les adresses de l'utilisateur, celles déjà
portées par un contact, et les doublons à l'intérieur d'un même envoi. Les survivants sortent dans
leur ordre d'apparition (To, puis Cc, puis Bcc).

**La comparaison passe par `canonicalAddress`, pas par le `fold` de `contactSearch`.** La question
que pose la capture est « cette ligne existe-t-elle déjà ? », et seule la règle de stockage y
répond : `canonicalAddress` (`trim` + minuscules) est le miroir client d'`IdentityResolver.Canonical`,
sous lequel le backend range toute adresse de contact.

`fold` répond à une autre question. Il retire en plus les diacritiques, parce qu'une *recherche* doit
trouver « José » en tapant « jose ». Employé ici il ferait passer `josé@x.be` pour déjà connue parce
que le carnet contient `jose@x.be` — deux boîtes distinctes, une capture perdue en silence. Un test
fixe la frontière : deux adresses ne différant que par un diacritique sont deux candidates.

Conséquence de rangement : `canonicalAddress` quitte `modules/mail/reader/` pour `src/lib/`, avec son
test. Un module Contacts qui va chercher une règle d'adresse dans le lecteur de courrier est un
couplage que rien ne justifie — c'est le déplacement qu'a connu `useAccountId`, et pour la même
raison.

`splitFullName` :

| Entrée | Sortie |
|---|---|
| vide, ou égale à l'adresse (casse ignorée) | prénom et nom vides |
| contient une virgule | `Nom, Prénom` |
| plusieurs mots | coupe au dernier espace : avant → prénom, après → nom |
| un seul mot | prénom |

Chaque moitié est tronquée à 100 caractères, la borne de `ContactValidator.MaxNameLength` : un nom
plus long ferait refuser la création par le backend, donc perdrait la fiche entière au lieu de sa
fin.

**« Mes adresses »** se compose de `identity.email` et de toutes les identités d'envoi, périmées
comprises — une identité périmée reste une adresse qui a été la tienne. Elle n'inclut pas un alias
vivant dépourvu d'identité : cet alias-là se capturerait. Ajouter `useAliases()` au composeur pour
ce seul cas coûte davantage que le cas ne vaut, et l'Annuler le rattrape.

## Le branchement dans `ComposeView`

Dans `submit().onSuccess`, avant `leave()` — au même endroit et pour la même raison que
`deleteDraft.mutate(...)`, qui part déjà juste avant que le composant se démonte.

```
préférence coupée            → rien
carnet non chargé            → rien
préférences non chargées     → rien
capturable(...) vide         → rien
sinon                        → un POST par candidat, en parallèle
```

**Les deux gardes de chargement sont load-bearing.** Ne pas savoir ce que le carnet contient, c'est
tout dupliquer ; l'envoi a déjà réussi, et une capture manquée est invisible là où une capture
fautive ne l'est pas. En pratique `useContacts()` a répondu depuis longtemps — le composeur
l'interroge dès son montage pour l'autocomplétion.

**Un échec de capture est avalé sans un mot.** Le message est parti, c'est ce qui comptait ; le
plafond de 5000 contacts est le cas concret. `Promise.allSettled` : une adresse refusée n'emporte
pas les autres.

La navigation n'attend pas les créations. Les mutations survivent au démontage — le
`queryClient` est au niveau de l'application, et `onNotify` est le `addToast` de `MailLayout`, hôte
des toasts et parent du composeur, que le retour à la liste ne démonte pas.

## Le toast

`useToasts.addToast` gagne un troisième paramètre optionnel :

```js
addToast(message, type = 'success', action)   // action: { label, onClick }
```

`Toasts.jsx` rend le bouton quand il existe ; un clic exécute l'action puis retire le toast. **Un
toast porteur d'action tient 8 s au lieu de 3** : le délai actuel ne laisse pas le temps de lire
puis de décider.

Le bouton est transparent, souligné en `currentColor` — aucune couleur nouvelle, donc il ne rouvre
pas la famille de littéraux figés qui reste en suspens sur `.toast-success` et `.toast-error`.

| Cas | Texte |
|---|---|
| une fiche | `Alice Dupont added to contacts` |
| une fiche sans nom | `alice@x.be added to contacts` |
| plusieurs | `3 contacts added` |

Le nom vient de `displayNameOf`, la fonction qui nomme déjà un contact partout ailleurs — quatre
écrans nommant un contact de quatre façons est le défaut qu'elle existe pour empêcher.

`Undo` supprime les ids créés. **Un échec de suppression parle**, contrairement à un échec de
capture : l'un est un geste demandé, l'autre pas.

## Le réglage

`contacts.captureRecipients`, défaut `"true"`, valeurs `true`/`false`, dans le registre
`UserPreferences`. Accesseur `captureRecipientsOf(preferences)` sur le modèle de `showPreviewOf` :
actif sauf si la valeur stockée vaut exactement `'false'`, puisque le défaut est l'activation.

**Première clé hors du préfixe `mail.`**, et c'est délibéré : la préférence gouverne une écriture
dans le carnet d'adresses, déclenchée par un envoi. La nommer `mail.` décrirait le déclencheur au
lieu de l'effet, et 3d aura ses propres réglages d'import à loger.

Sa ligne va dans Paramètres → Général avec les autres bascules. Un module Contacts n'a pas de page
de réglages et n'en mérite pas une pour une case.

## Les images des contacts — « Trust my contacts »

Dans ce code, **un expéditeur de confiance est un expéditeur dont les images distantes se chargent
sans qu'on demande** — rien d'autre. `TrustedSendersController` le dit dans son propre résumé, et
c'est tout ce que `MessageReader` en fait. Le réglage signifie donc : *charger sans demander les
images d'un expéditeur présent dans mon carnet.*

**Il est renommé « Always show images from my contacts ».** « Trust my contacts » ne dit pas ce que
faire confiance produit, et il se pose juste sous « Always show remote images », qui le dit. C'est la
règle que le lecteur s'applique déjà à lui-même une ligne plus bas : une entrée dont l'effet est
invisible induit en erreur.

**Clé `mail.trustContacts`**, défaut `"false"`. Le préfixe est `mail.` et non `contacts.` par la même
règle qui a donné `contacts.` à la capture : on nomme d'après l'effet, pas d'après le déclencheur.
Ici l'appartenance au carnet est le déclencheur, et l'effet est le comportement du lecteur de
courrier. Le défaut est `false` comme celui d'`alwaysShowImages` : charger une image distante
signale à l'expéditeur que le message a été ouvert, et cela ne s'active pas dans le dos de personne.

**Le lecteur tient désormais deux booléens distincts, et les confondre serait le défaut.**

```
senderApproved  — l'adresse figure sur la liste explicite des expéditeurs approuvés
showImages      — imagesShown || alwaysShow || senderApproved || contactTrusted
```

`senderApproved` seul commande l'entrée « Block sender's images » du menu. Le lecteur la réserve déjà
à un expéditeur approuvé *et* au cas où le réglage global est coupé, avec cette raison : le réglage
global actif, révoquer ne change rien à l'écran. `contactTrusted` masque exactement de la même
façon, donc il rejoint la même garde. Un expéditeur de confiance uniquement parce qu'il est dans le
carnet n'a rien à révoquer : on le retire du carnet, ou on coupe le réglage.

La bannière d'images bloquées ne se montre que si `!showImages` : le message d'un contact n'en a
donc aucune, et l'entrée « Always show images from this sender » qu'elle porte reste hors d'atteinte
dans ce cas. Cohérent, rien à ajouter.

**L'appartenance se teste avec `canonicalAddress`**, la même règle que la capture et que la liste
explicite — c'est celle sous laquelle les adresses de contact sont stockées.

**Aucune ligne n'est écrite dans `trusted_senders`.** La confiance par le carnet est calculée, pas
enregistrée : elle échappe donc au plafond de la table et au balayage de `TrustedSenderSweeper`, et
retirer un contact la retire du même coup. C'est ce qui la distingue d'un clic sur « Always show
images from this sender », qui, lui, pose une ligne.

**`useContacts` gagne un paramètre `enabled` valant `true` par défaut**, sur le modèle de
`useFolders`. Le lecteur ne demande le carnet que si le réglage est actif : sans cela, tout compte
qui n'ouvre jamais Contacts paierait quand même une requête par session pour un réglage coupé.
`ComposeView`, qui appelle `useContacts()` sans argument, ne change pas.

## Tests

Le gros de la valeur est dans les deux fonctions pures.

**`captureModel`** — adresse inconnue capturée ; adresse connue ignorée ; adresse propre ignorée ;
même adresse deux fois dans un envoi → un seul candidat ; casse et espaces sans effet sur la
comparaison ; **deux adresses ne différant que par un diacritique sont deux candidates** — la
frontière entre `canonicalAddress` et `fold` ; les six lignes de `splitFullName`, dont la troncature
à 100.

**`buildComposeSeed`** — `nameHints` d'une réponse porte l'émetteur ; d'un *replyAll* porte aussi
les destinataires du Cc ; d'un *forward* et d'un brouillon est vide.

**`ComposeView`** — la capture part quand la préférence est active et ne part pas quand elle est
coupée ; ne part pas tant que le carnet n'a pas répondu ; le libellé du toast au singulier et au
pluriel ; `Undo` supprime exactement les ids créés ; un `POST` refusé n'empêche ni l'envoi ni les
autres créations.

**`useToasts` / `Toasts`** — le bouton n'apparaît qu'avec une action ; un clic l'exécute et retire
le toast ; un toast porteur d'action ne disparaît pas au bout de 3 s.

**`MessageReader`** — réglage actif et expéditeur dans le carnet → images chargées, aucune bannière ;
réglage coupé et même expéditeur → bannière ; expéditeur hors carnet → bannière ; l'entrée « Block
sender's images » reste absente pour un expéditeur de confiance par le seul carnet, et présente pour
un expéditeur explicitement approuvé ; le carnet n'est pas demandé quand le réglage est coupé.

**Backend** — le registre expose les deux nouvelles clés avec leurs défauts (`contacts.captureRecipients`
à `true`, `mail.trustContacts` à `false`) ; `Source` absent ou inconnu retombe sur `manual` ;
`UpdateAsync` laisse `source` intact.

## Hors périmètre

- **Enrichissement d'une fiche existante** — décision 1.
- **Séparation « Autres contacts »** — la colonne `source` la rend possible ; rien ne l'affiche.
- **Capture à la lecture ou à la réception.** Seul l'envoi déclenche. Recevoir n'est pas un acte
  d'intention, et une boîte aux lettres remplirait le carnet de tout ce qui la traverse.
- **Capture depuis un brouillon enregistré.** Enregistrer n'est pas envoyer.
- **Un plafond de captures par envoi.** Un envoi à trente inconnus crée trente fiches, et le toast
  le dit — `30 contacts added`, avec son Annuler.
- **Toute autre acception de « confiance ».** Un contact ne devient ni exempt d'anti-spam, ni
  dispensé de la jauge, ni traité à part dans une règle Sieve. La table `trusted_senders` ne
  gouverne que les images distantes, et cette tranche ne lui en fait pas gouverner davantage.
