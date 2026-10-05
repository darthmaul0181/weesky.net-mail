# Administration sur la plateforme generic — conception

Date : 2026-10-05 · Branche : `generic-admin`

## Le but

Proposer l'écran Administration sur les deux plateformes, et quel que soit le compte actif. Seul
son contenu varie selon la plateforme.

Critères de réussite :

- un administrateur voit l'entrée **Administration** dans les réglages, même quand le compte actif
  est un compte connecté (Outlook, etc.) ;
- en `generic`, l'Administration montre **Domaines externes** et **Application**, et rien d'autre ;
- en `weesky`, elle montre en plus **Comptes**, **Domaines** et **Domaines virtuels**, comme
  aujourd'hui ;
- en `generic`, l'opérateur désigne les administrateurs par une ligne de configuration ;
- un utilisateur hors de cette liste reçoit 403 sur toute route d'administration, comme en
  `weesky`.

Hors champ : gérer la liste des admins `generic` depuis l'écran ; les onglets Comptes, Domaines
et Domaines virtuels en `generic`, puisqu'il n'y a pas d'annuaire derrière les boîtes.

## L'état de départ

- L'entrée Administration exige trois conditions : `isAdmin`, compte actif principal
  (`isPrimary`) et `capabilities.admin` (`SettingsLayout.tsx`, `layouts/gates.ts`).
- En `generic`, personne n'est admin. `ClaimsAccountInfoProvider` renvoie `IsAdmin = false`, et
  la policy `Admin` n'a pas de handler, donc elle ne peut être satisfaite par personne.
- Les domaines externes sont stockés dans le cœur (base des préférences, `ExternalDomainStore`).
  Seules leurs quatre routes vivent dans l'`AdminController` du provider weesky, qui n'est pas
  chargé en `generic`.
- Les routes de l'onglet Application (logo et nom, compte de planification, réponses à la
  livraison) sont déjà dans le cœur. Elles portent la policy `Admin`.

## Les décisions

### 1. Les admins generic viennent de la configuration

Nouveau réglage `Generic:Administrators` : une chaîne d'adresses séparées par des virgules.

```
Generic__Administrators=michael@exemple.be,anne@exemple.be
```

- Une chaîne plutôt qu'un tableau JSON : en variable d'environnement, `Generic__Administrators__0`,
  `__1`… est pénible à écrire et à relire. Une virgule suffit, puisqu'une adresse n'en contient pas.
- Les espaces autour des entrées sont retirés, et une entrée vide (virgule finale) est ignorée.
- La comparaison ignore la casse. Elle porte sur l'adresse de connexion (`Upn@Dns`), que le serveur
  IMAP vient de valider avec le mot de passe.
- Une entrée sans `@`, ou avec un `@` en tête ou en fin, empêche le démarrage. Le message nomme le
  réglage et l'entrée fautive : un admin mal orthographié qui ne marche pas en silence est
  exactement ce que la règle « ne jamais deviner » du projet interdit.
- Réglage absent ou vide : personne n'est admin, comme aujourd'hui. Ce n'est pas une erreur, car une
  installation peut vouloir s'en passer.
- En `weesky`, le réglage n'est pas lu. Le drapeau admin de la base reste la seule source.

Écartés :

- **une table d'admins gérée depuis l'écran** : il faudrait quand même la configuration pour le
  premier admin, donc deux mécanismes au lieu d'un ;
- **le premier compte connecté devient admin** : sur une installation exposée, n'importe qui peut
  prendre la place.

Limite assumée : ajouter ou retirer un admin `generic` demande un redémarrage.

### 2. Un handler generic pour la policy Admin

`GenericAdminRequirementHandler`, dans `Platform/Generic`, valide `AdminRequirement` quand l'adresse
de connexion figure dans la liste. Il est enregistré par `AddGenericPlatform`.

La liste est lue et validée une seule fois au démarrage, dans un ensemble insensible à la casse :
le handler ne fait aucune entrée-sortie à chaque requête. Le handler et
`ClaimsAccountInfoProvider` partagent ce même objet (`GenericAdministrators`). La règle « qui est
admin » n'existe donc qu'en un seul endroit.

### 3. `IsAdmin` et capabilities

- `ClaimsAccountInfoProvider` renvoie `IsAdmin = true` pour une adresse de la liste. Le front
  reçoit donc `isAdmin` par `/api/Account`, comme en `weesky`.
- `CapabilitiesController` : `Admin` vaut le drapeau admin du compte, sans la condition
  `isWeesky`. Le champ `Platform` existe déjà et dit au front quels onglets montrer.

### 4. Les routes des domaines externes passent dans le cœur

Un nouveau `ExternalDomainsController` dans `scotty.microservice/Controllers` reprend les quatre
routes (lister, créer, modifier, supprimer) avec leurs helpers (`Describe`, `ToEntity`,
`ProtectedSecret`, `Validate`…). Il est déplacé tel quel, pas réécrit.

- Les URL ne changent pas : `api/Admin/domains/external`. Le front et la documentation de l'API
  restent valables.
- La classe garde `[Authorize(Policy = AdminRequirement.PolicyName)]`.
- L'`AdminController` weesky perd ces routes ainsi que ses dépendances `IExternalDomainStore`,
  `IClientSecretProtector` et `IOptionsMonitor<MailOptions>`, si plus rien ne s'en sert.
- Les tests existants de ces routes suivent le contrôleur.

### 5. Le menu Réglages

L'entrée Administration ne dépend plus du compte actif : la condition `isPrimary` est retirée. Elle
reste soumise à `isAdmin && capabilities.admin !== false`. L'administration concerne l'installation,
pas la boîte qu'on lit. Le gate de route `allowAdmin` ne testait déjà pas `isPrimary` : il ne change
pas.

### 6. Les onglets de l'Administration

`AdminPage` filtre ses onglets :

- toujours : **Domaines externes**, **Application** ;
- seulement si `capabilities.platform !== 'generic'` : **Comptes**, **Domaines**, **Domaines
  virtuels**.

`!== 'generic'` suit la convention `!== false` du projet : pendant le chargement, ou face à un
backend antérieur au champ, on montre la version weesky plutôt qu'un écran qui clignote.

L'onglet ouvert par défaut est le premier onglet visible : Comptes en `weesky`, Domaines externes
en `generic`. Si les capabilities arrivent après le premier rendu et masquent l'onglet actif,
l'onglet affiché retombe sur le premier visible. Aucun onglet masqué ne reste affiché.

### 7. La documentation

- `install/README.md` : la section « Features of the weesky platform » ne dit plus que la connexion
  Outlook et les réponses à la livraison exigent `weesky` (les réponses à la livraison exigent
  toujours Dovecot). Le tableau comparatif passe à ✓ dans les deux colonnes pour les réglages
  admin. Une étape décrit `Generic__Administrators`.
- `README.md` : la description de `generic` mentionne les écrans d'administration qu'elle offre.
- `appsettings.json` n'est pas modifié : l'installation de référence est `weesky`.

## Les tests

Backend :

- lecture de `Generic:Administrators` : liste normale, espaces, casse, virgule finale, réglage
  absent, entrée invalide qui empêche le démarrage ;
- handler : adresse dans la liste → accordé ; hors liste → refusé ; casse différente → accordé ;
  claims manquants → refusé ;
- `ClaimsAccountInfoProvider` : `IsAdmin` suit la liste ;
- capabilities en `generic` : `Admin` vrai pour un admin, faux sinon ;
- routes déplacées : 403 pour un non-admin, et un passage complet en `generic` avec un admin
  configuré (la route répond 200).

Frontend :

- `SettingsLayout` : entrée Administration visible avec un compte connecté actif ;
- `AdminPage` : cinq onglets en `weesky` (et capabilities nulles), deux en `generic` ; onglet par
  défaut ; repli quand l'onglet actif disparaît.
