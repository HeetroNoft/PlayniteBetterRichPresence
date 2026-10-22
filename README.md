# Better Rich Presence

Extension Playnite qui remplace le Rich Presence Discord intégré.

Le Rich Presence de Playnite affiche toujours « Joue à Playnite », avec le nom du jeu en petit dessous. Better Rich Presence affiche **« Joue à Elden Ring »** quand Discord connaît le jeu, avec sa jaquette, et laisse personnaliser chaque ligne.

## Fonctionnalités

- **Le vrai nom du jeu comme activité** : le jeu est retrouvé dans la liste publique des jeux détectables par Discord (≈ 25 000 jeux), par ID Steam, puis par nom, puis par exécutable lancé. La liste est mise en cache 7 jours.
- **Application de repli** (« Playnite ») pour les jeux inconnus de Discord. L'ID d'application et l'image (clé d'asset ou URL) se changent dans les paramètres. Le nom du jeu apparaît alors dans les détails et dans la liste des membres (`status_display_type`).
- **Textes personnalisables** avec des variables : `{name}` `{platform}` `{source}` `{library}` `{playtime}` `{playcount}` `{completion}` `{genre}` `{developer}` `{year}` `{score}`. `{library}` donne la bibliothèque d'où vient le jeu (Steam, Ubisoft Connect, EA app, GOG…), ou « Playnite » pour un jeu ajouté à la main. Les séparateurs orphelins sont nettoyés, et une ligne dont toutes les valeurs sont vides est masquée.
- **Image du jeu choisie dans Playnite** (icône ou couverture). Discord n'affiche que des images en ligne, donc le plugin les convertit en PNG et les envoie une seule fois sur ImgBB, où elles sont publiques. Il faut entrer sa propre clé API ImgBB gratuite (api.imgbb.com) : sans clé, rien n'est envoyé. Catbox ne convient pas, car le relais d'images de Discord n'arrive pas à lire ses fichiers, et Imgur n'accepte plus de nouvelles applications. Sans cette option, ou pendant l'envoi, l'extension utilise la couverture Steam ou l'icône Discord du jeu.
- **Bouton** optionnel vers un lien du jeu (boutique, site…).
- **Masquer un jeu** : clic droit sur un jeu → *Better Rich Presence* → *Hide activity from Discord*.
- **Forcer une application Discord** pour un jeu précis : clic droit → *Set Discord application ID…*
- **Présence au repos** optionnelle (« Browsing the library »).
- Plusieurs jeux lancés en même temps : le plus récent est affiché, puis le précédent quand il se ferme.
- Reconnexion automatique si Discord est lancé après le jeu.

## Installation pour le développement

Prérequis : SDK .NET 8 ou plus récent (le projet cible .NET Framework 4.6.2 comme Playnite).

```powershell
.\build.ps1
```

Dans Playnite : **Paramètres → For developers → External extensions → Add**, puis choisis le dossier `BetterRichPresence\bin\Debug`. Après chaque build, redémarre Playnite pour recharger l'extension.

**Désactive le Rich Presence intégré**, sinon Discord affichera deux activités. Playnite ne propose pas cette option dans ses paramètres : ferme Playnite (y compris depuis la zone de notification), puis passe `"DiscordPresenceEnabled"` à `false` dans `%APPDATA%\Playnite\config.json`. L'extension te prévient au démarrage s'il est encore actif.

## Créer un paquet .pext

```powershell
.\build.ps1 Release
& "$env:LOCALAPPDATA\Playnite\Toolbox.exe" pack BetterRichPresence\bin\Release dist
```

## Fonctionnement

L'extension parle directement au pipe IPC local de Discord (`\\.\pipe\discord-ipc-N`) au lieu d'utiliser `DiscordRPC.dll`. Playnite charge déjà sa propre version de cette bibliothèque, et une autre version dans le même processus provoquerait des conflits. Ça permet aussi d'utiliser des champs récents comme `status_display_type`.

## Licence

[MIT](LICENSE)
