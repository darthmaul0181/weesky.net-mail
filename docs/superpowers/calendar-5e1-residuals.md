# Agenda 5e1 — résidus

Les mineurs relevés par les relectures de tâche et laissés ouverts, tous triés « restent différés »
par la revue finale du 12 septembre 2026 (aucun ne touche l'écran ni un comportement). À reprendre
quand le code voisin bouge, 5e2 au plus tard pour ceux qui touchent le répondeur.

| Tâche | Résidu |
|---|---|
| 1 | FindByUidAsync — le départage Order/DisplayName entre agendas non-défaut n'est pas testé |
| 1 | UserAddresses.Distinct trime les adresses, ce que l'ancien code du principal ne faisait pas (voulu par le plan) |
| 2 | StripMethod filtre des lignes physiques, Rewrite des lignes logiques (METHOD replié) |
| 2 | SequenceOf rend 0 sans maître alors que ParsedInvitation.Sequence rend celui du composant — Ruling: laisser, un fichier occurrenceOnly n'atteint jamais la comparaison |
| 2 | XML summaries manquants sur InvitationMethod, InvitationPerson, InvitationAttendee, Unparsable, Unfold |
| 2 | pas de test pour une continuation par tabulation ni pour un fichier sans newline final |
| 2 | CheckSize exécuté deux fois par lecture (préexistant dans CheckAll) |
| 2 | .gitattributes *.ics text eol=crlf au lieu de -text (vérifié sûr par le relecteur : aucun test ne compare les octets du corpus ICalendar) |
| 3 | XML summaries manquants sur IInvitationReader, InvitationReader, TooLarge, ReadAsync, ResolveAsync, Block |
| 3 | ReadPartTextAsync duplique le boilerplate GetStreamAsync/TryParse/DecodeToAsync de GetAttachmentAsync |
| 4 | pas de test pour un CN contenant un guillemet |
| 4 | EscapeText n'échappe pas un \r isolé |
| 4 | le VCALENDAR REPLY émis n'est pas replié à 75 octets |
| 5 | le triplet FolderNotFound/MessageNotFound/AttachmentNotFound recopié depuis MailControllerBase.IsMissing (protected, inaccessible) |
| 5 | le plafond de taille est jugé après la copie complète du flux |
| 5 | XML docs manquants sur InvitationAnswer, Folder, Uid |
| 5 | AlreadyExists, PreconditionFailed, NotFound-sur-delete, InvalidCard sans test dans le mapping du writer |
| 6 | commentaires de plus de trois lignes dans MessageReader.tsx |
| 6 | savedPartStat DECLINED tombe sur « Dans votre agenda · X » (le backend ne le produit jamais : un refus supprime) |
| 6 | l'en-tête de la sonde dit « re-run to confirm » après avoir donné les chiffres mesurés (formulation) |
| 7 | UserIcon/PeopleIcon sans aria-hidden dans EventPreview — incohérence préexistante du jeu d'icônes (MapPinIcon, CalendarIcon idem) |
