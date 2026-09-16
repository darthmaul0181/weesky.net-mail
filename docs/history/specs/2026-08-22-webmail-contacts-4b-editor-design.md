# Contacts 4b — l'éditeur étendu

Deuxième tranche du projet CardDAV, à la suite de
[4a](2026-08-14-webmail-contacts-4a-vcard-model-design.md). Presque entièrement frontend : le
backend n'y gagne qu'un champ, pour une raison donnée à la décision 5.

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 4a | Modèle de données complet + moteur d'aller-retour vCard | livrée |
| **4b** | Éditeur et fiche webmail étendus *(ce document)* | la fiche est faite, l'éditeur non |
| 4c | Serveur CardDAV | à venir |
| 4d | Conformité clients | à venir |

La moitié lecture de 4b est déjà passée : la fiche affiche la carte entière — photo, téléphones,
adresses postales, société, anniversaire, notes. Reste l'éditeur, qui sait écrire cinq champs sur
la vingtaine que la carte porte.

## Ce que fait la tranche

L'éditeur devient capable d'écrire tout ce que la fiche sait lire. Ce n'est pas seulement un
ajout de champs : c'est la première fois qu'un écran de ce carnet écrit une famille répétable
autre que les e-mails, et c'est ce qui met le moteur de 4a à l'épreuve d'une édition réelle avant
qu'un client CardDAV n'en dépende — l'ordre 4a → 4b → 4c existe pour ça.

## Ce que la tranche répare

Deux pertes de données que l'éditeur actuel provoque à chaque enregistrement. Elles ne sont pas
des ajouts : ce sont les raisons pour lesquelles 4b n'est pas un simple élargissement de
formulaire.

**Les positions.** L'éditeur envoie `addresses: string[]`. Sans `position`, `VCardComposer.Paired`
traite chaque adresse comme une ligne neuve : les `EMAIL` de la carte sont reconstruits et perdent
leur groupe (`item1.`), leur bloc de paramètres et leurs `X-`. La spec de 4a l'annonçait — « la
fiche transporte cette position, le `PUT` de 4b la rend ». C'est cette tranche qui la rend.

**Le nom affiché.** `ContactRequest.DisplayName` existe et `ContactValidator` le lit, mais
l'éditeur ne l'envoie jamais. `Apply` calcule alors `write.DisplayName ?? repli`, et une carte
portant `FN:Dr. Ana Ruiz` ressort en `FN:Ana Ruiz` après une correction de pseudo. La fiche affiche
ce nom depuis que sa moitié de 4b est passée, donc la perte est visible à l'écran.

## Décisions

### 1. Champs à la demande, en une colonne

L'essentiel est toujours visible : prénom, nom, pseudo, les trois familles, l'anniversaire, le
favori. Les neuf scalaires restants vivent derrière un menu **« + Ajouter un champ »** — nom
affiché, 2e prénom, préfixe, suffixe, société, service, fonction, site, notes.

La règle qui rend ce motif honnête : **un champ que la carte remplit est toujours affiché**, et il
ne figure pas au menu. On ne peut donc pas ouvrir une fiche venue d'un iPhone et manquer ce
qu'elle porte. Le menu ne cache que du vide.

Trois maquettes ont été comparées et écartées. *Tout visible en deux colonnes* : la seule qui
exploite la pleine largeur, mais elle retombe en une colonne interminable sur téléphone et rend
ambigu l'ordre de lecture. *Blocs par famille* : la seule qui rende les plafonds visibles avant
qu'on les heurte, au prix d'une grille de scalaires qui abandonne l'alignement libellé-à-gauche du
reste de l'app. *Onglets* : hauteur stable, mais on n'embrasse jamais la fiche avant
d'enregistrer, une erreur de validation peut dormir sous un onglet fermé, et aucune autre surface
de l'app n'a d'onglets.

Un champ ajouté depuis le menu puis vidé reste affiché jusqu'au démontage du formulaire : le
retirer sous les doigts de l'utilisateur qui vient d'effacer une faute de frappe serait pire que
la ligne vide.

### 2. L'éditeur charge le détail, et l'attend

Il reçoit aujourd'hui un `Contact` — la vue liste, qui ne porte ni positions, ni types, ni les
neuf scalaires. Il lui faut le `ContactDetail`.

`ContactsLayout` garde déjà l'éditeur démonté tant que le carnet n'a pas répondu (`editorReady`),
pour la raison écrite dans son commentaire : *le formulaire s'amorce depuis son contact une seule
fois, au montage*. La même raison vaut pour le détail, donc la même porte : en mode édition,
`editorReady` attend aussi le détail. En création il n'y en a pas, et rien ne change.

### 3. Chaque ligne transporte sa position

`ContactDraft` cesse de porter des chaînes. Les trois familles deviennent des objets portant leur
`position` — celle que le détail a donnée, `null` pour une ligne neuve. C'est exactement ce que
`ContactEmailPayload`, `ContactPhonePayload` et `ContactAddressPayload` attendent déjà sur le fil,
et ce que `Paired` lit pour éditer en place au lieu de reconstruire.

Une conséquence à ne pas manquer : **la position n'est pas un rang d'affichage**. Une adresse
supprimée laisse un trou, et deux lignes ne doivent jamais réclamer la même place. Le brouillon
transporte la position telle quelle et ne la recalcule jamais.

### 4. Les types : liste fixe, type inconnu préservé

Un menu déroulant par ligne, sur la table de correspondance que l'export CSV utilise déjà :

| Jeton | Libellé |
|---|---|
| `CELL` | Mobile |
| `HOME,VOICE` | Domicile |
| `WORK,VOICE` | Bureau |
| `HOME,FAX` | Fax domicile |
| `WORK,FAX` | Fax bureau |
| `VOICE` | Autre |

Postal : `HOME` → Domicile, `WORK` → Bureau.

**Un type que la carte porte et que la table ne contient pas s'ajoute au menu, tel quel, et reste
sélectionné.** Aurélie Etienne, importée d'un vrai `.vcf`, en porte deux : `TEL;TYPE=OTHER` et
`ADR;TYPE=HOME,POSTAL`. Sans cette règle, ouvrir sa fiche pour corriger une faute de frappe les
ramènerait au libellé le plus proche et les enregistrerait ainsi — une destruction silencieuse sur
une intention qui ne les visait pas. La promesse est celle de la fiche : rendre ce que la carte
dit.

Le texte libre a été écarté : il est parfaitement fidèle, mais demande à l'utilisateur de connaître
les jetons vCard.

`PREF` ne circule pas par ce champ. `ApplyType` le retire des jetons, et la décision suivante dit
par où il passe.

### 5. `pref` rejoint les payloads d'écriture

C'est le seul changement backend de la tranche, et il est imposé par la décision 3.

« Rendre principale » fonctionne aujourd'hui par réordonnancement, et cela ne marche que parce que
l'éditeur reconstruit tout le bloc `EMAIL`. Dès qu'il rend les positions, le composeur remet chaque
ligne à sa place et l'ordre de la liste soumise n'a plus aucun effet. Or `Preference` n'est jamais
écrit depuis une écriture : `ApplyType` retire explicitement `PREF` des jetons, avec la raison que
la projection l'échoue en retour dans le champ `type`.

Le côté lecture porte déjà `pref` par ligne, et la liste trie sur `(pref, position)`. C'est le
côté écriture qui a le trou. On l'ouvre :

- `ContactEmailPayload`, `ContactPhonePayload` et `ContactAddressPayload` gagnent `int? Pref` ;
- `ContactWriteEmail`/`Phone`/`Address` le portent jusqu'au composeur ;
- `TextLine` et `PostalLine` posent `parameters.Preference` quand l'écriture le nomme, et le
  laissent intact quand elle ne le nomme pas — la règle « PREF excepté » d'`ApplyType` ne tombe
  pas, elle cède à un seul endroit nommé, hors du chemin des jetons.

C'est le modèle de la vCard elle-même : la RFC 6350 dit que la préférence est `PREF`, pas l'ordre
des lignes. Et 4c en aura besoin de toute façon — un client CardDAV pose un `PREF` et s'attend à le
relire.

Sur le fil, `null` garde le sens qu'il a partout ailleurs — « la requête ne nomme pas le champ, la
carte garde le sien » — et **`101` est l'effacement**, la valeur que la projection donne déjà à une
ligne dont la carte ne dit rien. L'éditeur envoie donc, sur chaque e-mail, `1` pour la principale et
`101` pour les autres : sans cet effacement, désigner B principale laisserait A revendiquer la
première place aussi.

Le champ existe sur les trois payloads, par uniformité et parce que 4c en aura l'usage, mais
**seule la famille des e-mails l'écrit en 4b** : les téléphones et les adresses postales renvoient
`null`, donc la carte garde ce qu'elle dit. Le webmail n'offre pas de téléphone principal.

Le bouton « ↑ rendre principale » devient un vrai choix et non un déplacement, ce qui a un bénéfice
de lisibilité : la ligne ne bouge plus sous le curseur.

### 6. L'éditeur possède désormais les trois familles

Jusqu'ici l'éditeur n'envoyait pas `Phones` ni `PostalAddresses` : `null` arrivait au store, et sur
ces champs `null` veut dire « la requête ne les nomme pas, la carte garde les siens ». C'est ce qui
a protégé les fiches importées d'être vidées par une simple correction de nom.

À partir de 4b l'éditeur les envoie **toujours**, liste vide comprise, sinon les vider serait
impossible. **La convention documentée dans `ContactWrite` ne change pas** : c'est l'appelant qui
cesse d'omettre. Les autres producteurs — la capture depuis un mail, l'import — continuent de les
omettre et restent protégés par la même règle.

Deux conséquences à tenir dans le formulaire :

- une ligne vide est déposée avant l'envoi, comme aujourd'hui pour les adresses ;
- **une adresse postale dont les sept composantes sont vides est déposée même si elle porte un
  type.** `ContactValidator.IsMeaningful(ContactWriteAddress)` renvoie vrai dès que le type est non
  vide : sans ce filtre côté éditeur, ouvrir un bloc d'adresse puis changer d'avis poserait une
  `ADR` vide dans la carte.

### 7. L'anniversaire : un champ texte guidé

Un sélecteur de date natif ne sait exprimer qu'une date complète. La spec de 4a exige quatre
formes : le jour complet, la date sans année, l'année seule, et le texte libre — parce que la carte
en porte quatre et que le composeur les ré-impose telles quelles (décision 11 de 4a).

Donc un champ texte, avec un exemple de format, qui normalise ce qui est reconnaissable et laisse
passer le reste. Pas de bascule « format libre » : un mode de plus dont l'état initial dépendrait de
ce que la carte porte. La lecture reste formatée comme aujourd'hui — la fiche affiche déjà
« 27 octobre 1979 » à partir d'un `19791027T115900Z`.

### 8. Les plafonds sont tenus dans l'interface

50 adresses, 10 téléphones, 10 adresses postales — les constantes de `ContactValidator`. Le bouton
« + Ajouter » disparaît au plafond plutôt que de laisser l'enregistrement échouer sur une bannière :
c'est la raison déjà écrite en tête de `ContactEditView` pour `maxLength`, appliquée aux familles.

### 9. Ce que l'éditeur ne touche pas

Il édite **la première occurrence** de chaque famille scalaire, jamais les suivantes : une carte
portant deux `NOTE` en garde deux, la seconde intacte (décision 5 de 4a). Il ne modélise ni les
propriétés `X-`, ni les groupes, ni les composantes RFC 9554 d'une `ADR`, ni la photo — le
composeur les préserve, et 4b n'ouvre aucune porte d'écriture photo (décision 12 de 4a).

C'est aussi ce qui rend la décision 3 vitale : **c'est la position qui fait tenir cette
préservation.** Sans elle, tout ce paragraphe est faux.

## Le modèle de brouillon

`ContactDraft` passe de cinq champs à la carte entière. Les scalaires restent `string | null`, avec
la convention actuelle — `null` = vidé, ce que le validateur lit pour les champs que l'éditeur
possède. Les trois familles deviennent des listes d'objets portant `position`, `pref`, `type` et
leurs valeurs.

`ContactDetail` sert de source à l'amorçage ; le brouillon n'est pas le détail — il n'a ni `params`
ni `groupName`, que rien dans l'éditeur ne montre et que le serveur préserve seul.

## Ce qui ne change pas

Le validateur, le composeur, le store et le projecteur, à l'exception du fil de `pref` décrit en
décision 5. Aucune migration : les colonnes existent depuis 4a. Aucune route nouvelle.

## Fichiers

**Backend** — `Models/Contacts/ContactLine.cs` (les trois payloads), `Models/Contacts/ContactWrite.cs`,
`Services/ContactValidator.cs`, `Services/Contacts/VCardComposer.cs` (`TextLine`, `PostalLine`).
Le projecteur ne bouge pas : il dérive déjà `pref` de la carte, c'est lui qui donne son sens au
`101`.

**Frontend** — `modules/contacts/ContactEditView.tsx` (le gros du travail),
`modules/contacts/contactTypes.ts`, `modules/contacts/ContactsLayout.tsx` (la porte du détail),
`modules/contacts/queries.ts`, `locales/{en,fr}/contacts.json`, `index.css`.

## Tests

**Backend** — `pref` posé par une écriture atteint `Preference` et ressort dans la projection ; une
écriture qui ne le nomme pas laisse le `PREF` de la carte intact ; le jeton `PREF` dans le champ
`type` reste ignoré comme aujourd'hui.

**Frontend**, dans `ContactEditView.test.tsx` :

- un enregistrement renvoie la `position` de chaque ligne amorcée, et `null` pour une ligne neuve ;
- un type absent de la table survit à un enregistrement qui ne touche pas sa ligne ;
- un `FN` personnalisé survit à un enregistrement qui ne touche pas le nom affiché ;
- vider une famille envoie `[]` et non l'omission ;
- une adresse postale dont les sept composantes sont vides n'est pas envoyée, type ou pas ;
- le menu « ajouter un champ » ne propose pas un champ que la carte remplit ;
- au plafond, le bouton d'ajout de la famille disparaît ;
- « rendre principale » envoie `pref` et ne réordonne pas la liste.

Et la parité i18n en + fr, que la suite vérifie déjà.

## Ce que la tranche ne fait pas

- **Pas de porte d'écriture photo** (décision 12 de 4a) : la fiche l'affiche, l'éditeur ne la
  remplace pas.
- **Pas de deuxième occurrence.** Un utilisateur ne peut pas ajouter une seconde `ORG` depuis le
  webmail ; la carte peut en porter deux, l'éditeur en montre une.
- **Les quatre points différés vers 4b** dans `contacts-4a-residuals.md` sont dans le périmètre du
  plan, pas de ce document : le `URL;TYPE=PREF` perdu sur un aller-retour 3.0, `Fold` qui compte des
  unités UTF-16, les `X-` perdus quand une édition ramène une famille effondrée à une occurrence, et
  l'absence de tests de troncature. Le troisième mérite l'attention : il est rare aujourd'hui
  *parce qu'aucun éditeur ne l'écrit*, et 4b est exactement ce qui le rendra courant.
