# Prérequis opérateur — fournisseur OAuth pour les comptes connectés

**À appliquer avant tout déploiement portant le code OAuth des comptes connectés**, sur les deux
environnements. La création de base est manuelle dans ce projet — pas de migrations EF, la même
voie que `docs/superpowers/mail-2a5-database-prerequisite.md`.

**Le secret client doit être saisi via l'écran d'administration (Settings → Administration →
External domains), jamais écrit directement dans la colonne** : l'API fait passer la valeur collée
par Data Protection à l'entrée, donc `external_domains.oauth_client_secret` contient des octets
protégés, pas du texte, et une valeur en clair collée dans la colonne ne s'ouvrira tout simplement
jamais.

## Ce qu'un opérateur doit faire

**1. Enregistrer l'application** dans Microsoft Entra ID (portail Azure → App registrations → New
registration) :

- Types de comptes pris en charge : *Accounts in any organizational directory and personal
  Microsoft accounts* — c'est ce qui permet de connecter aussi bien une boîte Office 365 qu'une
  boîte Outlook.com.
- URI de redirection, type **Web** — celle de l'**API**, pas celle du webmail : Microsoft redirige
  vers le callback du microservice, qui échange le code côté serveur. Enregistre les deux, elles
  coexistent sur le même enregistrement :
  - prod `https://api.mail.weesky.net/api/ConnectedAccounts/OAuth/Callback`
  - dev `https://api-dev.mail.weesky.net/api/ConnectedAccounts/OAuth/Callback`

  Elle doit correspondre octet pour octet à `Mail:OAuthRedirectUri` (étape 3) : même schéma, même
  casse dans le chemin, pas de barre oblique finale. Microsoft compare littéralement.
- Certificates & secrets → New client secret. **Copie-le immédiatement** ; le portail ne l'affiche
  qu'une fois. Note son échéance — Microsoft la plafonne à 24 mois, et une boîte cesse de se
  rafraîchir le jour où elle expire.
- API permissions, **depuis deux API différentes** — c'est l'étape qui fait trébucher, parce que
  les scopes de messagerie ne sont pas des scopes Graph et ne figurent pas dans la liste que le
  portail propose en premier :
  - *APIs my organization uses* → **Office 365 Exchange Online** → Delegated permissions →
    `IMAP.AccessAsUser.All` et `SMTP.Send`. Le service les demande par leur URI complète,
    `https://outlook.office.com/IMAP.AccessAsUser.All` et
    `https://outlook.office.com/SMTP.Send`.
  - *Microsoft Graph* → Delegated permissions → `offline_access`, `openid`, `email`, `profile`.

**2. Appliquer le changement de schéma** sur `snoopy_webmail`. La création de base est manuelle
dans ce projet, comme le consigne `docs/superpowers/mail-2a5-database-prerequisite.md` ; ceci suit
la même voie.

```sql
ALTER TABLE external_domains
  ADD COLUMN auth_mode              VARCHAR(16)    NOT NULL DEFAULT 'Password',
  ADD COLUMN oauth_authorization_url VARCHAR(512)  NULL,
  ADD COLUMN oauth_token_url        VARCHAR(512)   NULL,
  ADD COLUMN oauth_scopes           VARCHAR(1024)  NULL,
  ADD COLUMN oauth_client_id        VARCHAR(255)   NULL,
  ADD COLUMN oauth_client_secret    VARBINARY(1024) NULL;

ALTER TABLE connected_accounts
  ADD COLUMN auth_mode VARCHAR(16) NOT NULL DEFAULT 'Password',
  MODIFY COLUMN cipher VARBINARY(8192) NOT NULL;
```

**3. Renseigner les deux réglages** committés vides sous `"Mail"`.

Il y en a deux parce que le fournisseur remet le code d'autorisation **au serveur**, jamais au
navigateur — c'est un secret, et l'URL d'une page se lit trop facilement. Microsoft redirige donc
vers l'API, alors que l'utilisateur, lui, doit finir sur la page de réglages du webmail, qui est un
autre hôte. Le service a besoin des deux adresses :

- `OAuthRedirectUri` — ce que le service **annonce à Microsoft**, dans la demande d'autorisation
  puis à l'échange du code. Microsoft la compare littéralement à celles enregistrées à l'étape 1 :
  ce n'est pas une adresse à joindre, c'est une chaîne à faire correspondre. C'est le callback de
  l'**API**.
- `WebmailBaseUrl` — où le service **renvoie le navigateur** une fois le code échangé. Il ne
  connaît que sa propre adresse ; sans ce réglage il ne sait pas où se trouve la **SPA**. Le
  service ajoute lui-même `/settings/accounts`, donc ne mets que la racine, sans barre oblique
  finale.

Les valeurs ne se mettent pas dans `appsettings.json` : elles se posent dans l'`EnvironmentFile`
que l'unité systemd utilise déjà pour `Cors__AllowedOrigins__0`, une unité par environnement,
puis `systemctl restart`.

Unité de prod (`snoopy.microservice`) :

```
Mail__WebmailBaseUrl=https://account.mail.weesky.net
Mail__OAuthRedirectUri=https://api.mail.weesky.net/api/ConnectedAccounts/OAuth/Callback
```

Unité de dev (`snoopy.microservice-dev`) :

```
Mail__WebmailBaseUrl=https://account-dev.mail.weesky.net
Mail__OAuthRedirectUri=https://api-dev.mail.weesky.net/api/ConnectedAccounts/OAuth/Callback
```

Laissé vide, `WebmailBaseUrl` produit une redirection relative qui ne fonctionne que si l'API et la
SPA partagent l'origine — ce qui n'est pas le cas ici : l'utilisateur reviendrait sur l'API et
verrait une page d'erreur au lieu de ses réglages.

**4. Créer la ligne de domaine via l'écran d'administration** — connecte-toi en admin, Settings →
Administration → External domains → Add :

- IMAP `outlook.office365.com` port `993` `SSL/TLS`, SMTP `smtp.office365.com` port `587`
  `STARTTLS` ; laisse les champs Sieve vides (Outlook n'offre pas ManageSieve).
- Authentication : **OAuth 2.0**. Les champs du fournisseur apparaissent :
  - Authorization URL `https://login.microsoftonline.com/common/oauth2/v2.0/authorize`
  - Token URL `https://login.microsoftonline.com/common/oauth2/v2.0/token`
  - Scopes `offline_access openid email profile https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send`
  - Client id : l'Application (client) ID de l'étape 1.
  - Client secret : la valeur du secret copiée à l'étape 1, collée une seule fois. **Il est en
    écriture seule** : après l'enregistrement, la boîte de dialogue d'édition indique seulement
    qu'un secret est stocké ; laisser le champ vide lors d'une édition ultérieure le conserve,
    saisir une nouvelle valeur le remplace (c'est aussi ainsi qu'on effectue la rotation d'un
    secret Entra expiré). Les deux URL doivent être en https, et l'API refuse d'enregistrer un
    domaine OAuth 2.0 s'il manque un champ du fournisseur — une ligne qui s'enregistre est une
    ligne que le flux de consentement accepte.

La tuile du domaine porte une étiquette `OAuth` une fois enregistrée, et le formulaire de connexion
propose « Sign in with &lt;nom&gt; » à la place d'un champ mot de passe.

## Ne bascule pas un domaine Password existant vers OAuth 2.0 par-dessus des lignes connectées

L'`auth_mode` d'un compte connecté est figé à la création, délibérément (voir le document de
conception) : les lignes attachées pendant que le domaine était en mode Password continuent de
rejouer leur mot de passe stocké après la bascule. Le fournisseur le refuse, toute requête de
messagerie sur cette boîte répond 502, et la ligne dans les réglages a toujours l'air saine
(`credentialsValid` dit seulement que le chiffré s'ouvre). Il n'existe aucune réparation sur place
pour une telle ligne — le consentement Reconnect appartient aux lignes OAuth, et ressaisir un mot
de passe aboutit à un serveur qui n'en accepte plus. Chaque utilisateur concerné doit
**déconnecter la boîte et la rattacher** via la connexion fournisseur que le domaine basculé
propose désormais.
