# Webmail weesky — Sous-projet 1 : le shell applicatif

**Date :** 2026-07-18
**Statut :** design validé, prêt pour la planification d'implémentation

---

## 1. Contexte

Le dépôt `weesky.net-mail` héberge aujourd'hui un outil d'administration de comptes mail :
une SPA React (`src/frontend`) et une API .NET 10 (`src/snoopy.microservice`) permettant de
gérer alias, mot de passe, règles Sieve et un mode administrateur.

L'objectif à terme est d'en faire un **webmail complet** — mail, calendrier, contacts, plus
l'existant — comparable à Rainloop/Snappymail dans ses fonctions mais avec une interface
moderne, proche d'Outlook dans sa structure, et une identité visuelle propre.

Ce périmètre représente quatre sous-systèmes indépendants. Il a été décidé de les traiter en
cycles séparés (spec → plan → implémentation), et de **commencer par la coquille applicative** :
elle ne dépend d'aucune infrastructure nouvelle, et elle conditionne tout le reste.

### Décision fondatrice : on ne repart pas de zéro

**Backend — étendu, non réécrit.** L'architecture est saine et déjà en couches
(`Controllers → Repositories → Services`, `Result<T>`, EF Core/Pomelo sur la base `dovecot`,
461 tests xUnit). L'authentification, l'admin, les alias, l'intégration doveadm et le client
ManageSieve sont réutilisables tels quels. Le pattern « master user » de ManageSieve servira
de modèle à l'impersonation IMAP.

**Frontend — restructuré, non réécrit.** La qualité est là (100 % de couverture de lignes,
ESLint, dark mode, tokens CSS) mais l'architecture est volontairement minimale et arrive à
sa limite : aucun routing (`react-router-dom` est installé mais jamais importé), aucun dossier
`components/`/`hooks/`/`contexts/`, tout tient dans trois fichiers plats dont `AliasesPage.jsx`
(1390 lignes) et `RulesPage.jsx` (1188 lignes), avec de la duplication réelle entre les deux.
On conserve le dépôt, le tooling, le CSS et les tests ; on construit une nouvelle coquille et
on y porte les pages.

### Résultat attendu

À la livraison de ce sous-projet, **ajouter le module Mail ne doit demander aucune
modification de la coquille**. C'est le critère de réussite.

---

## 2. Décisions de conception validées

| Sujet | Décision |
|---|---|
| Structure du shell | Rail d'applications vertical (style Outlook/Teams) + colonne contextuelle + contenu |
| Identité visuelle | Deux palettes livrées ensemble : **B « Nuit & corail » par défaut**, **A « Continuité »** en option |
| Modes | Clair et sombre pour chaque palette → 4 combinaisons |
| Portage | Portage **avec restructuration** — extraction des composants partagés |
| Langage | **TypeScript progressif** : tout le neuf en `.ts`/`.tsx`, l'existant en `.jsx` migre quand on le touche |
| Modules à venir | Icônes visibles dans le rail, menant à un écran « à venir » |
| Règles & Admin | Cessent d'être des modales, deviennent des **pages routées** |
| Changement de mot de passe | Cesse d'être une modale, devient une **section de `/settings/account`** |
| `AccountPanel` (panneau coulissant) | **Supprimé**, remplacé par un menu d'avatar à trois entrées |
| Calendrier / Contacts (cible) | Tables maison dans MariaDB (décision de cadrage, hors périmètre ici) |
| Snappymail | Cohabitation pendant le développement, retrait à terme → l'interop Sieve Rainloop reste requise |

---

## 3. Périmètre

**Dans le périmètre**

- Routing applicatif et arbre de routes
- Coquille : rail, colonne contextuelle, zone de contenu
- `AuthContext` et `ThemeContext`
- Contrat de tokens CSS par rôle + deux palettes × deux modes
- Bibliothèque de composants et d'icônes partagés
- Portage des pages existantes sous `modules/settings/`
- Écrans « à venir » pour Mail, Calendrier, Contacts
- Mise à jour de `src/frontend/CLAUDE.md` (voir § 9)

**Hors périmètre**

- IMAP / SMTP / MailKit — sous-projet 2
- Calendrier — sous-projet 3
- Contacts — sous-projet 4
- Toute modification du backend

Le shell se livre avec Mail affichant lui aussi un écran « à venir ».

---

## 4. Architecture frontend

### Dépendances

`react-router-dom@^6.26.0` est **déjà présent** dans `src/frontend/package.json` et
inutilisé — aucune dépendance nouvelle pour le routing. TypeScript s'ajoute en
devDependency (`typescript`, `@types/react`, `@types/react-dom`) avec `allowJs: true`
dans `tsconfig.json`, ce qui laisse les `.jsx` existants compiler sans modification.

### Arborescence cible

```
src/
  main.tsx
  App.tsx                    RouterProvider
  routes.tsx                 arbre de routes
  contexts/
    AuthContext.tsx
    ThemeContext.tsx
  layouts/
    AppShell.tsx             rail + colonne contextuelle + <Outlet/>
    AppRail.tsx
    ContextPane.tsx          2ᵉ colonne, contenu fourni par le module actif
    AvatarMenu.tsx
  components/                Toast, Modal, ConfirmDialog, Switch, Tooltip, QuotaBar…
  icons/                     les 8 SVG aujourd'hui inlinés dans les pages
  hooks/
    useToasts.ts             dédupliqué (écrit 2× aujourd'hui)
  modules/
    mail/            ComingSoon
    calendar/        ComingSoon
    contacts/        ComingSoon
    settings/
      account/       identité, autres domaines, quota, mot de passe
      appearance/    thème (clair/sombre/système) + palette
      aliases/       page portée
      rules/         page portée
      admin/         page portée
  api/
  styles/
    tokens.css       contrat de rôles
    theme-b.css      palette par défaut
    theme-a.css      palette de continuité
```

### Arbre de routes

| Route | Contenu |
|---|---|
| `/mail` | `ComingSoon` (sous-projet 2) |
| `/calendar` | `ComingSoon` (sous-projet 3) |
| `/contacts` | `ComingSoon` (sous-projet 4) |
| `/settings` | redirige vers `/settings/account` |
| `/settings/account` | **page d'atterrissage de la section** — identité, autres domaines, quota, mot de passe |
| `/settings/appearance` | thème + palette |
| `/settings/aliases` | page portée (avec le mode alphabétique comme contrôle local) |
| `/settings/rules` | page portée |
| `/settings/admin` | page portée, protégée par un garde de route |
| `/login` | hors coquille |

La colonne contextuelle affiche, en section Paramètres, la nav :
**Compte · Apparence · Alias · Règles · Administration** (la dernière conditionnée au flag admin).

Le passage des Règles et de l'Admin de modales à pages est le gain direct du routing : il
apporte les liens profonds et le bouton retour, aujourd'hui absents.

### État d'authentification

`AuthContext` remplace l'état mutable de niveau module dans `src/frontend/src/api.js`
(`let isAdmin`, `let unauthorizedHandler`). C'est le changement structurel essentiel :
une seule page consomme l'auth aujourd'hui, sept routes en auront besoin, dont un garde
de route pour l'admin.

Le mécanisme de session reste inchangé et fonctionne : JWT en cookie httpOnly posé par le
backend, `credentials: 'include'` sur chaque requête, drapeau `sessionActive` en `localStorage`
pour le rendu optimiste, et bascule vers `/login` sur 401 via le handler déjà en place.

---

## 5. Le contrat de tokens CSS

Pièce centrale du sous-projet, puisque les deux palettes sont livrées ensemble.

**Règle : un token nomme un rôle, jamais une couleur.** Pas de `--bleu-fonce`, mais
`--rail-bg`. C'est ce qui permet à B d'avoir un rail sombre et un point « non-lu » corail,
là où A garde un rail clair et un point bleu, sans qu'aucun composant ne connaisse la différence.

```
--rail-bg  --rail-fg  --rail-item  --rail-item-active
--bg  --surface  --surface-raised  --surface-sunken
--accent-unread  --action-primary  --action-primary-fg
--text  --text-muted  --border  --danger  --success
--radius-sm  --radius-md  --font
```

**Sélection :** `data-theme="light|dark"` (existant) × `data-palette="b|a"` (nouveau), les
deux appliqués par le script bloquant déjà présent dans `src/frontend/index.html` — le
mécanisme anti-FOUC existe, il gagne un attribut. Préférences persistées en `localStorage`
(`appearance_theme` existant, `appearance_palette` nouveau).

**Valeurs de référence**

| Rôle | B clair | B sombre | A clair | A sombre |
|---|---|---|---|---|
| `--rail-bg` | `#182238` | `#0f1626` | `#e4e9f2` | `#232833` |
| `--bg` | `#faf8f6` | `#17191d` | `#f0f2f5` | `#1e2229` |
| `--surface` | `#ffffff` | `#212429` | `#ffffff` | `#272d38` |
| `--accent-unread` | `#e2674a` | `#f0785c` | `#3450a3` | `#84aad8` |
| `--action-primary` | `#182238` | `#f0785c` | `#3450a3` | `#84aad8` |

En sombre, le rail de B reste **plus sombre que les surfaces** : la structure survit au
changement de mode au lieu de se dissoudre.

**Point de vigilance.** `src/frontend/src/index.css` fait 2247 lignes et référence les tokens
actuels (`--primary`, `--bg`, `--radius`…). Le renommage vers des rôles le traverse largement.
C'est mécanique mais non trivial — à traiter comme une étape isolée et vérifiable, pas noyée
dans le portage des pages.

---

## 6. Modale ou page : la règle

> Une **modale** convient quand la tâche est courte, atomique, interruptive, et qu'on revient
> exactement d'où on vient. Une **page** convient quand c'est une destination — quelque chose
> vers quoi on navigue délibérément.

Le changement de mot de passe est aujourd'hui une modale parce qu'il n'existe nulle part où
le mettre : la modale compense une absence d'architecture. Dès que `/settings/account` existe,
une page dont le contenu principal serait un bouton ouvrant autre chose n'a plus de sens.

**Restent des modales :** `DeleteConfirmModal` (alias, utilisateur, domaine),
`ConvertConfirmModal` (bascule de provider de règles), `RuleEditorModal` (assistant à étapes,
déjà écrit et testé ainsi).

**Deviennent des pages :** Règles, Administration, Changement de mot de passe.

---

## 7. Stratégie de portage

Les 309 tests existants servent de filet. Ordre d'exécution :

1. **Extraire les partagés d'abord** — `useToasts`, `Toasts`, `DeleteConfirmModal`,
   `HelpTooltip`, `TrashIcon`, `PencilIcon` sont aujourd'hui écrits **deux fois**, à
   l'identique, dans `AliasesPage.jsx` et `RulesPage.jsx`. Les extraire, faire passer les tests.
2. **Renommer les tokens** dans `index.css`, vérifier visuellement.
3. **Poser la coquille** — routing, contexts, layouts, écrans « à venir ».
4. **Déplacer les pages** sous `modules/settings/`, une par une, tests verts à chaque étape.
5. **Réécrire ce qui ne se porte pas** — voir ci-dessous.

La convention actuelle « tout composant testé porte un `export` nommé en plus du default »
disparaît d'elle-même : un composant extrait dans son propre fichier s'importe normalement.
`src/frontend/CLAUDE.md` documente cette contrainte — à retirer.

### Zone à risque identifiée

La suppression de l'`AccountPanel` est **le seul endroit où le filet de tests ne joue pas**.
Ce composant est couvert par une partie des 92 tests de `AliasesPage.admin.test.jsx` et des
68 de `AliasesPage.main.test.jsx` ; ces tests ne se portent pas, ils se réécrivent contre
`/settings/account` et `AvatarMenu`. C'est là que les régressions passeront si on n'y prête
pas attention.

Le menu d'avatar qui le remplace porte trois entrées : identité, lien vers Paramètres,
Déconnexion.

---

## 8. Backend

**Aucune modification dans ce sous-projet.**

Deux constats relevés lors de l'exploration, à traiter **au moment du sous-projet Mail** :

- Le JWT expire en **30 minutes sans mécanisme de refresh**
  (`TokenConstants.ExpiryInMinutes` dans `appsettings.json`). Un webmail ouvert toute la
  journée s'y heurtera.
- `OnTokenValidated` dans `Authentication/Extensions/AuthorizationExtension.cs` fait un
  **aller-retour base de données à chaque requête** pour vérifier que l'utilisateur existe
  toujours. Supportable pour un panneau d'alias, pas pour un client mail bavard.

---

## 9. Documentation à corriger

`src/frontend/CLAUDE.md` est périmé sur trois points et induira en erreur tout le travail
suivant s'il n'est pas repris :

- La section « Token persistence » décrit un token bearer en `localStorage` ; le commit
  `c98e1e3` est passé à un cookie httpOnly.
- Le composant nommé `OwnershipTab` s'appelle aujourd'hui `VirtualDomainsTab`.
- La contrainte d'`export` nommé pour les tests devient caduque (§ 7).

À noter aussi, découvert lors de l'exploration mais hors périmètre :
`src/snoopy.microservice/CLAUDE.md` documente des routes admin `/api/Admin/ownerships`
qui n'existent pas — le code expose `/api/Admin/domains/virtuals`.

---

## 10. Vérification

1. `npm run lint` — sans erreur.
2. `npm run test` — les 309 tests existants restent verts ; nouveaux tests sur `AuthContext`,
   `ThemeContext`, `AppShell`, le garde de route admin et `/settings/account`.
3. `npm run test:coverage` — la couverture ne régresse pas.
4. `npm run build` — compilation TypeScript sans erreur.
5. **Vérification manuelle des 4 combinaisons de thème** (B/A × clair/sombre) sur chaque
   route. C'est le seul moyen de détecter les couleurs codées en dur, et c'est l'étape à ne
   pas sauter.
6. Navigation : liens profonds fonctionnels sur chaque route, bouton retour cohérent,
   `/settings` redirige bien vers `/settings/account`, `/settings/admin` inaccessible à un
   compte non-admin.
7. Session : un 401 depuis n'importe quelle route ramène à `/login`.

---

## 11. Suite

| Sous-projet | Contenu | Dépend de |
|---|---|---|
| 1. Shell | ce document | — |
| 2. Mail | MailKit côté backend (IMAP/SMTP), vue 3 panneaux, rédaction ; refresh token | 1 |
| 3. Calendrier | tables MariaDB, API REST, vues mois/semaine/jour | 1 |
| 4. Contacts | tables MariaDB, API REST, carnet + intégration à la rédaction | 1, 2 |

Chaque sous-projet suit son propre cycle spec → plan → implémentation.
