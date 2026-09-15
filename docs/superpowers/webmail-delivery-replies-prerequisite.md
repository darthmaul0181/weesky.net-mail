# Prérequis serveur — réponses d'invités appliquées à la livraison (5e3)

Dovecot 2.4.5 (Pigeonhole). **Rien n'est à activer côté webmail tant que la procédure de
vérification (§ Avant d'activer) n'est pas passée** : un programme Sieve qui échoue fait sauter
les règles personnelles de tous les utilisateurs, sans signal.

Prérequis : la table `delivery_reply_key` (`webmail-delivery-reply-key-table.md`) et l'`ALTER`
de `calendar_revisions` (`webmail-calendar-tables.md` § 5e3), sur les deux bases.

## 1. Dovecot

Dans la configuration (`doveconf -n` doit montrer le bloc après édition) :

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

`vnd.dovecot.execute` **dans `sieve_global_extensions` seulement, jamais dans `sieve_extensions`** :
les utilisateurs écrivent leurs règles depuis l'onglet Règles du webmail, et cette extension leur
donnerait le droit de faire exécuter un programme par le serveur. `mime` et `foreverypart` sont
actives par défaut ; si `sievec` refuse la règle sur l'une d'elles, le bloc suivant — Dovecot 2.4 sépare les réglages d'un bloc par un retour à la ligne, jamais par `;` :

```
sieve_extensions {
  mime = yes
  foreverypart = yes
}
```

`/var/lib/dovecot/sieve/` doit exister et appartenir à `vmail` (le `.svbin` s'y dépose, sinon
Dovecot recompile à chaque livraison).

## 2. La règle — `/etc/dovecot/sieve/calendar-replies.sieve`

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

Compiler : `sievec /etc/dovecot/sieve/calendar-replies.sieve /var/lib/dovecot/sieve/calendar-replies.svbin`
puis `chown vmail /var/lib/dovecot/sieve/calendar-replies.svbin`. **À refaire à chaque modification.**
La règle n'arrête rien (ni `stop` ni `discard`) : les règles de l'utilisateur s'appliquent ensuite.

### Pilote sur une seule boîte (facultatif)

Pour n'appliquer le mécanisme qu'à un compte de test avant de le généraliser, la règle se garde
par l'adresse de livraison : c'est l'adresse que Dovecot a reçue de Postfix pour cette boîte
(`RCPT TO`, extension `envelope`), pas l'en-tête `To:`, que l'expéditeur écrit comme il veut et
qui ne dit rien sur la boîte visée quand le mail arrive par copie ou par alias. Seule la première
ligne du `require` et le `if` extérieur changent :

```
require ["envelope", "mime", "foreverypart", "vnd.dovecot.execute"];
if allof (envelope :is "to" "webmail-test@weesky.be", size :under 5M) {
  foreverypart {
    …   # inchangé
  }
}
```

Un mail envoyé à un alias du compte arrive avec l'alias comme adresse de livraison : lister
les alias (`envelope :is "to" ["webmail-test@weesky.be", "autre@weesky.be"]`). Pour généraliser,
retirer le test `envelope` et recompiler avec `sievec` ; rien d'autre ne change, ni côté
webmail ni côté script. Une règle personnelle du compte ne peut pas servir de pilote :
`vnd.dovecot.execute` n'est autorisée que dans les règles globales, et c'est voulu.

## 3. Le fichier de configuration — `/etc/dovecot/calendar-reply.conf`

`root:vmail`, mode `0640`. Contenu exact (une option `curl` par ligne) :

```
url = "https://<ip-ou-hôte-du-microservice>/api/Delivery/CalendarReplies"
header = "X-Delivery-Key: <la clé générée dans Administration>"
header = "Content-Type: message/rfc822"
connect-timeout = 2
max-time = 5
silent
output = "/dev/null"
```

Adresse : une IP littérale ou une entrée `/etc/hosts`, pas un nom à résoudre à chaque livraison.
Tout processus tournant sous `vmail` peut lire ce fichier ; ce que la clé permet est borné (spec
5e3, décision 12 : réécrire la réponse d'un invité déjà présent, rien d'autre).

## 4. Le script — `/usr/lib/dovecot/sieve-execute/calendar-reply`

`root:root`, mode `0755`. **L'ordre des instructions est la sécurité** : tout stdin est lu avant
toute décision, sinon Dovecot fait échouer l'action ; y compris quand `mktemp` lui-même échoue : sortir avant d'avoir vidé le tube laisserait Dovecot voir un `SIGPIPE` sur une entrée non lue.

```sh
#!/bin/sh
# Applies a guest's calendar REPLY at delivery through the webmail's internal door (5e3).
# Always exits 0: a failed Sieve action skips the user's own rules for this mail.
tmp=$(mktemp) || { cat >/dev/null; exit 0; }
trap 'rm -f "$tmp"' EXIT
cat > "$tmp"                                   # read everything first, unconditionally
[ -n "$USER" ] || exit 0                        # LDA without a user: nothing to do
[ "$(wc -c < "$tmp")" -le 5242880 ] || exit 0   # belt under the rule's size :under 5M
timeout 8 curl --config /etc/dovecot/calendar-reply.conf \
  -H "X-Delivery-Mailbox: $USER" --data-binary "@$tmp" >/dev/null 2>&1
exit 0
```

`timeout` (coreutils) borne tout, résolution comprise, sous les 10 s de
`sieve_execute_exec_timeout` ; `--max-time` seul ne borne que le transfert.

## 5. nginx

```
location /api/Delivery/ {
    allow <ip du serveur mail>;
    deny all;
    client_max_body_size 6m;   # le défaut, 1 Mo, refuserait une réponse plus grosse
    # … le proxy_pass et les en-têtes du bloc /api/ existant
}
```

## 6. Avant d'activer — la procédure

1. `sievec` sans erreur ; `doveconf -n | grep -A4 calendar_replies` montre le bloc.
2. Depuis le serveur mail, sous `vmail` : `curl -v --config /etc/dovecot/calendar-reply.conf
   -H "X-Delivery-Mailbox: <une boîte>" --data-binary @<un mail qui n'est PAS une réponse>`
   → `404` tant que le réglage est désactivé (la clé et l'adresse sont justes si le journal du
   service porte « Delivery calls refused … door closed »), `200` avec `NotAReply` une fois activé.
   **Jamais une vraie réponse ici** : sur une porte activée, elle s'appliquerait.
3. Dans Administration > Application : activer l'interrupteur.
4. Une livraison réelle d'une réponse d'invité (répondre depuis Gmail ou Outlook.com à une
   invitation du webmail) : l'agenda est à jour **avant** d'ouvrir le mail, la carte affiche
   « Dernier appel reçu le … », le journal porte `Applied`. Puis vérifier qu'une règle
   personnelle de cet utilisateur (un tri en dossier) s'est encore appliquée sur ce mail.
5. **Le microservice arrêté** (`systemctl stop snoopy.microservice`), une seconde réponse : le
   mail arrive, la règle personnelle s'applique encore, le journal Dovecot ne porte aucune
   erreur `execute`. Redémarrer le service ; ouvrir le mail applique la réponse en secours.

## 7. Identifiant de la boîte

`USER` est l'identifiant Dovecot du propriétaire de la boîte ; il doit être l'adresse avec
laquelle l'utilisateur se connecte au webmail. Un utilisateur qui se connecte par un alias a sa
ligne `users` sur l'alias : la livraison répond `UnknownMailbox` à chaque fois (visible dans le
journal), et ses réponses ne s'appliquent qu'à l'ouverture.

## 8. Retour arrière

Désactiver l'interrupteur dans Administration suffit : la porte répond 404, le script sort en 0,
la règle peut rester. La version précédente du service ne connaît pas la table ni la route.
