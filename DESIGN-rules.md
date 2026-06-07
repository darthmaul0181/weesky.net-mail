# DESIGN — Extension des fonctionnalités de règles Sieve

> Document de réflexion + design. Capture l'analyse menée sur l'ajout de
> nouvelles fonctionnalités aux règles de courrier (Sieve / Pigeonhole).
> Les arbitrages actés sont dans la section « Décisions ».
>
> **État au 2026-06-07 : toutes les fonctionnalités planifiées sont implémentées.**
> Tests : 359 backend (xUnit) · 223 frontend (Vitest).

## Contexte

Le serveur Dovecot/Pigeonhole annonce les capacités Sieve suivantes :

```
fileinto reject envelope encoded-character vacation subaddress
comparator-i;ascii-numeric relational regex imap4flags copy include body
variables enotify environment mailbox date index ihave duplicate mime
foreverypart extracttext editheader imapsieve vnd.dovecot.imapsieve
```

Cette liste = les extensions Pigeonhole **réellement chargées** sur le serveur.
Tout ce qui y figure est utilisable dans un script `.sieve`. C'est donc le
catalogue de ce qu'on *pourrait* implémenter.

Implémenté dans nos providers :
- `fileinto` (Move to), `redirect` (Forward), `reject`, `discard`
- `imap4flags` — `\Seen` (Mark as read) + `\Flagged` (étoile, slider ON)
- `keep;` standalone (slider ON)
- `body` — conditions sur le corps du message (slider ON)
- `envelope` — conditions sur l'enveloppe SMTP (slider ON, test core = pas de `require`)
- `subaddress` — condition sur le `+detail` d'une adresse (slider ON, `require ["subaddress"]`)
- conditions : From, To, Cc, Recipient (To/Cc), Subject, Header custom, Size, Body\*, EnvelopeFrom\*, EnvelopeTo\*, RecipientDetail\* (\* = slider ON uniquement)
- opérateurs : Contains (`:contains`), Equals (`:is`), Matches (`:matches` / regex), Larger/Smaller (Size uniquement)
- actions multiples par règle (slider ON)

## La contrainte centrale : interopérabilité Rainloop / Snappymail

C'est le nœud de toute la réflexion.

### Comment une règle est stockée (format Rainloop)

Chaque règle est écrite **deux fois** dans le script :

```
/*
BEGIN:FILTER:<id>
BEGIN:HEADER
<JSON base64 = la vérité>     ← c'est CE bloc qui est relu
END:HEADER
*/
if header ... { fileinto ...; }   ← corps Sieve = ce que Dovecot exécute
```

À la relecture (par **nous** comme par **Snappymail**), c'est le **JSON base64**
qui est décodé, jamais le corps Sieve. Le corps n'est qu'une projection
exécutable régénérée à partir du JSON.

**Conséquence clé :** tout ce qui n'existe pas dans le schéma JSON
`RainloopFilter` ne peut pas survivre à un aller-retour via Snappymail. Le schéma
Snappymail est figé :

```
Conditions, ConditionsType, ActionType (un seul) + ActionValue,
Keep, Stop, MarkAsRead
```

Si on ajoute un champ que Snappymail ne connaît pas :
- À la **lecture**, Snappymail ignore les champs inconnus → la règle marche quand même.
- À la **sauvegarde** depuis le webmail, Snappymail réécrit son propre JSON → **nos champs supplémentaires sont perdus pour cette règle**.

### Les deux providers

- `RainloopRuleProvider` — format Snappymail, **contraint** par le schéma ci-dessus.
- `WeeskyRuleProvider` — format natif, **peut tout faire** (pas de contrainte de schéma externe).

La question de fond : **veut-on rester interopérable avec le webmail Snappymail,
ou notre interface devient-elle la source de vérité ?**

## Les trois voies possibles (analyse initiale)

| Voie | Principe | Avantage | Inconvénient |
|------|----------|----------|--------------|
| **A — Interop stricte** | On se limite à ce que le schéma Rainloop sait relire | Webmail 100 % synchro | Pas de flags étendus, ni body/envelope/date |
| **B — Best-effort** | Garder Rainloop, **étendre le JSON** avec champs optionnels | On garde les fonctionnalités ; Snappymail reste fonctionnel | Les extras d'une règle sont perdus **si** l'utilisateur ré-édite cette règle précise via le webmail (perte silencieuse) |
| **C — Weesky maître** | Bascule sur `WeeskyRuleProvider` | Accès à TOUT (body, envelope, date…) | Snappymail n'est plus synchronisé du tout |

> **Décision retenue : approche par toggle (voir ci-dessous), qui supplante ce
> tableau.** Au lieu d'imposer une voie globale, on laisse l'utilisateur choisir
> A ou C **par compte**, via un slider explicite. On écarte la Voie B (perte de
> données silencieuse) au profit d'un basculement explicite avec confirmation.

## Approche retenue : slider « Extended rules »

Un slider dans la popup Rules, libellé **« Extended rules »** :
- **Activé** → provider **Weesky** maison (mode étendu, toutes les
  fonctionnalités, pas d'interop Snappymail = Voie C).
- **Désactivé** → provider **Rainloop** (interop webmail Snappymail, jeu de
  fonctionnalités restreint = Voie A).

> ⚠️ **Sens du slider :** ON = étendu (Weesky), OFF = Rainloop. C'est l'inverse
> d'un éventuel « Rainloop compatible » — bien y penser dans tout le code.

L'utilisateur arbitre lui-même, par compte, entre interopérabilité et richesse
fonctionnelle. Pas de perte silencieuse : tout basculement risqué est confirmé.

### Aide contextuelle (tooltip)

À côté du label « Extended rules », un petit rond avec un point d'interrogation,
**réutilisant le pattern existant** `HelpTooltip` (`AliasesPage.jsx`, classes
`.help-tooltip-wrap` / `.help-tooltip-icon` = le `?` / `.help-tooltip-bubble`).
Le composant n'est pas exporté aujourd'hui → **l'exporter** (ou le dupliquer)
pour l'utiliser dans `RulesPage`.

Texte du tooltip (anglais, UI) — sens : *les règles créées en mode étendu ne
sont pas compatibles avec l'éditeur de règles Rainloop et n'y seront plus
visibles*. Ex. :

> « Rules created in extended mode are not compatible with the Rainloop rules
> editor and will no longer be visible there. »

### Ancrage dans le code existant (constats)

1. **Le modèle `SieveRule` est partagé** entre les deux providers. Le provider
   n'agit qu'au `Compile` (format sur disque) et au `Parse`. Donc « convertir »
   = **recompiler le même `SieveRule[]` avec l'autre provider**. Les règles en
   mémoire ne changent pas de forme.

2. **L'état du slider n'a pas besoin d'être persisté séparément.** Le provider
   est déjà *détecté* depuis le script actif (`SieveRepository.GetRuleSetAsync`
   renvoie `ProviderId` via `RuleProviderRegistry.Detect`). Au chargement :
   slider ON si `providerId === 'weesky'`, OFF sinon (Rainloop). L'état du slider
   = quel script est actif. Aucune colonne en base.

3. ✅ **Nettoyage de l'ancien script.** `SaveRulesAsync` écrit sous le nom
   par défaut du provider cible et l'active, puis appelle
   `CleanupOtherManagedScriptsAsync` qui supprime les scripts des autres providers
   (jamais l'actif, jamais un script inconnu/Advanced). Garantit un seul script
   géré quel que soit l'historique.

4. ✅ **Lister *toutes* les règles incompatibles** : `IRuleProvider.CanRepresent(rule)`
   implémenté (Weesky = superset via `Validate` ; Rainloop = whitelist stricte).
   Endpoint `POST /api/Rules/CompatibilityCheck` renvoie la liste complète.

5. ✅ **Le slider bride l'éditeur.** `RuleEditorModal` reçoit `extended` : en mode
   Rainloop (OFF), le bouton « Add » des actions est masqué quand une action
   primaire existe déjà. En mode étendu (ON), pas de limite.

### Comportement au changement d'état du slider

- **rainloop → weesky** (slider ON, on *active* Extended rules) : Weesky est un
  superset → **aucune perte**. Recompile + save Weesky, supprime `rainloop.user`.
  Pas de confirmation.
- **weesky → rainloop** (slider OFF, on *désactive* Extended rules) : appeler le
  *compatibility check*.
  - Si aucune règle incompatible → conversion directe.
  - Sinon → **modal** listant les règles perdues (nom + raison), boutons
    **Annuler** / **Confirmer**. Sur Confirmer : retirer ces règles localement,
    save Rainloop, supprimer `weesky-rules`.

  *Alternative (zéro perte involontaire)* : **bloquer** la conversion tant que
  l'utilisateur n'a pas corrigé/supprimé lui-même les règles fautives. Retenu
  comme repli ; le choix principal est supprimer-avec-confirmation.

### Esquisse technique

**Backend**
- `IRuleProvider.CanRepresent(SieveRule rule) : Result` — validation par règle.
  - `WeeskyRuleProvider` : `Result.Success()` (superset).
  - `RainloopRuleProvider` : **whitelist, jamais blacklist** ⭐. N'accepter que
    l'explicitement supporté (conditions ∈ {From, To, Cc, Recipient, Subject,
    Header, Size} ; ≤ 1 action primaire ∈ {FileInto, Redirect, Reject, Discard} ;
    flags ∈ {`\Seen`} uniquement) et **rejeter tout le reste par défaut**.
    Ainsi toute future fonctionnalité Weesky est signalée incompatible
    *automatiquement* (un oubli échoue côté sûr = règle marquée incompatible,
    pas de perte silencieuse).
- Endpoint preview : `POST /api/Rules/CompatibilityCheck { providerId, rules }`
  → `{ compatible: bool, incompatible: [{ id, name, reason }] }`.
- `SieveRepository.SaveRulesAsync` : après PUT+SETACTIVE du script cible,
  **supprimer les scripts des autres providers** (`weesky-rules` /
  `rainloop.user`) pour garantir un seul script géré.

**Frontend** (popup Rules)
- Slider **« Extended rules »** + `HelpTooltip` à côté, initialisé depuis
  `ruleSet.providerId` (ON si `providerId === 'weesky'`).
- Au flip **ON** (Extended, vers weesky) : save direct avec `providerId='weesky'`.
- Au flip **OFF** (vers rainloop) : `CompatibilityCheck` → si liste non vide,
  modal de confirmation → save avec `providerId='rainloop'` (rules épurées).
- Éditeur de règle conditionné par l'état du slider (masquer/désactiver les
  fonctionnalités non-Rainloop quand OFF / Rainloop).

## Catalogue des fonctionnalités candidates

### Tier 1 — fort gain, attendu par les utilisateurs

- **Forward+Keep** (`fileinto "INBOX"; redirect "..."`) — ✅ **Compatible Rainloop**.
  Le champ `Keep=true` du JSON Rainloop représente exactement ce pattern ; notre
  modèle l'encode comme `[FileInto("INBOX"), Redirect("...")]`.
  `CanRepresent` Rainloop l'accepte déjà (test `CanRepresent_ForwardWithKeepInbox_Succeeds`).

- **`keep;` standalone / Move+Keep** — ❌ **Weesky-only (slider ON)**.
  Un `keep;` seul ou un Move vers un dossier + copie en INBOX n'a aucune
  représentation dans le schéma Snappymail.
  `CanRepresent` Rainloop les rejette (`SieveActionType.Keep` → failure explicite).

- **`mailbox` / auto-create** (`fileinto :create`, RFC 5490) — ~~quasi-compatible~~ ❌ **Weesky-only (slider ON)**.
  Le flag `:create` vit dans le corps Sieve exécutable, pas dans le JSON
  base64. À la sauvegarde depuis Snappymail, le corps est **régénéré** depuis le
  JSON → `:create` est silencieusement perdu. Il n'y a aucun champ dans le
  schéma Rainloop pour l'y stocker.

- **`imap4flags` étendu** (RFC 5232) — au-delà de `\Seen` : étoile/drapeau
  (`\Flagged`), marquer répondu (`\Answered`), brouillon (`\Draft`), labels/
  mots-clés custom.
  **Incompatible** avec le schéma Rainloop (seul `MarkAsRead` existe) →
  provider Weesky / mode étendu (slider ON) uniquement.

### Fonctionnalités étendues retenues (provider Weesky uniquement)

- **Actions multiples** — plusieurs actions par règle (Rainloop n'autorise qu'une
  action primaire). Déjà supporté par le backend `WeeskyRuleProvider` (il émet
  toutes les actions) → il reste surtout à **débrider l'éditeur** quand slider ON.
- **`body`** (RFC 5173) — filtrer sur le **contenu du corps** du message
  (`body :contains "text"`), pas seulement les en-têtes.
- **`envelope`** (RFC 5228 + ext) — filtrer sur l'expéditeur/destinataire
  **réel SMTP** (utile anti-spam et catch-all). À packager avec subaddress.
- **`subaddress`** (RFC 5233) — matcher le `+detail` d'une adresse
  (`moi+shopping@` → dossier Shopping). Complément léger au système d'alias.

### Écartées / plus tard (hors périmètre actuel)

- `date` (RFC 5260) — filtrer selon date/heure/jour. _Plus tard._
- `relational` + `comparator-i;ascii-numeric` (RFC 5231) — comparaisons
  numériques, `:count`. _Niche._
- `duplicate` (RFC 7352) — anti-doublon. _Niche._
- `enotify` (RFC 5435) — notifications. ❌ Déconseillé (config serveur,
  délivrabilité).
- `editheader` (RFC 5293) — ajouter/supprimer des en-têtes. ❌ Déconseillé.

## Tableau d'impact interop pour les fonctionnalités prioritaires

| Fonctionnalité | Champ JSON Rainloop ? | Verdict interop |
|---|---|---|
| **Forward+Keep** | Oui — `Keep=true` + `ActionType=Forward` = `fileinto "INBOX"; redirect "..."` | ✅ Compatible Rainloop |
| **`keep;` standalone / Move+Keep** | Non — aucune représentation dans le schéma Snappymail | ❌ Weesky-only (slider ON) |
| **Auto-create folder** | Non — `:create` dans le corps Sieve, perdu à la réécriture Snappymail (lu depuis le JSON, pas le corps) | ❌ Weesky-only (slider ON) |
| **Flags étendus** | Non — seulement le booléen `MarkAsRead` | ❌ Weesky-only (slider ON) |

> Avec l'approche par slider : les fonctionnalités non-Rainloop (flags étendus,
> body, envelope, date…) sont disponibles **uniquement** côté provider Weesky
> (slider ON). Côté Rainloop (slider OFF), on s'en tient au schéma figé.

## Plan d'implémentation

1. **Socle du slider** — ✅ **implémenté (2026-06-07)** :
   - `IRuleProvider.CanRepresent(rule)` (Weesky = superset via `Validate` ;
     Rainloop = whitelist stricte) + endpoint `POST /api/Rules/CompatibilityCheck`.
   - Défaut nouveau compte = Rainloop : `RuleProviderRegistry.NewAccountDefault`,
     utilisé par `GetRuleSetAsync` quand aucun script n'existe.
   - `CleanupOtherManagedScriptsAsync` dans `SaveRulesAsync` : supprime les scripts
     des autres providers après chaque sauvegarde (ne touche jamais le script actif
     ni un script inconnu/Advanced).
   - Slider « Extended rules » + `HelpTooltip` + modal `ConvertConfirmModal`
     (weesky→rainloop) + bridage éditeur (`RuleEditorModal extended`).
     `api.checkCompatibility` côté front.
2. **Fonctionnalités étendues** (provider Weesky / slider ON) — ✅ **implémenté (2026-06-07)** :
   - **`keep;` standalone** : option « Keep in inbox » dans l'éditeur étendu.
   - **Actions multiples** : bouton « Add » débloqué quand slider ON.
   - **Flag `\Flagged`** : checkbox « Mark as flagged ⭐ » dans l'éditeur étendu.
   - **`body`** : champ « Body » dans le sélecteur de conditions (étendu seulement) ;
     compile en `body :text :contains/:matches "..."` ; ajoute `"body"` à `require[]` ;
     opérateurs : Contains / Matches uniquement.
   - **`envelope`** : champs « Envelope from » / « Envelope to » (étendu seulement) ;
     compile en `envelope :op "from"/"to" "..."` ; `envelope` est un test core Sieve
     (RFC 5228) → pas de `require` nécessaire.
   - **`subaddress`** : champ « Recipient +detail » (étendu seulement) ;
     compile en `address :detail :op ["To","Cc"] "..."` ; ajoute `"subaddress"` à
     `require[]`.

**Tests finaux : 359 backend · 223 frontend.**

## Décisions

- **Stratégie d'interopérabilité :** ✅ **Approche par slider « Extended rules »**
  (choix par compte). ON = provider Weesky (étendu) ; OFF = provider Rainloop
  (interop). La Voie B (best-effort, perte silencieuse) est écartée.
- **Aide :** ✅ `HelpTooltip` (`?`) à côté du label, expliquant que les règles du
  mode étendu ne sont pas compatibles avec l'éditeur Rainloop et n'y seront plus
  visibles. Implémenté en copie locale dans `RulesPage.jsx` (même pattern/classes
  que `AliasesPage`, pas d'export nécessaire).
- **Provider par défaut (nouveau compte, aucun script) :** ✅ **Rainloop**
  (slider OFF par défaut) → le webmail Snappymail voit les règles dès le départ.
  Implémenté via `RuleProviderRegistry.NewAccountDefault` (="rainloop") utilisé
  par `GetRuleSetAsync` quand aucun script n'existe. `Default` du registre reste
  "weesky" (fallback de compilation, tests épinglés inchangés).
- **Conversion :** ✅ rainloop→weesky direct (sans perte) ; weesky→rainloop avec
  *compatibility check* + modal de confirmation listant les règles supprimées.
- **Nettoyage :** ✅ `SaveRulesAsync` supprime les scripts des autres providers.
- **Fonctionnalités confirmées :**
  - *Compatibles (deux modes)* : Forward+Keep.
  - *Étendues (slider ON, toutes implémentées)* : `keep;` standalone, flags `\Flagged`,
    actions multiples, `body`, `envelope`, `subaddress`.
- **Écartées :** `vacation` ❌ ; `enotify` ❌ ; `editheader` ❌ ; Auto-create folder ❌.
  **Plus tard :** `date`, `:count`/relational, `duplicate`.
- **`subaddress` + `envelope` :** packagés ensemble (subaddress plus fiable via
  l'envelope).
- **Flags à exposer dans l'UI :** ✅ `\Flagged` uniquement (étoile/drapeau).
  `\Answered`, `\Draft`, labels custom : écartés.

## Questions ouvertes

- **`subaddress` — validation empirique :** `recipient_delimiter = +` ✅ confirmé
  côté Dovecot. Reste à vérifier par un mail de test réel que le `+detail` survit
  à la couche alias / Postfix avant d'exposer la fonctionnalité aux utilisateurs.
