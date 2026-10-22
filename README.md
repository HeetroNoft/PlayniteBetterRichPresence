# Better Rich Presence

Extension Playnite qui remplace le Rich Presence Discord intégré.

Le Rich Presence de Playnite affiche toujours « Joue à Playnite », avec le nom du jeu en petit dessous. Better Rich Presence affiche **« Joue à Elden Ring »** quand Discord connaît le jeu, avec sa jaquette, et laisse personnaliser chaque ligne.

## Fonctionnalités

- **Le vrai nom du jeu comme activité** : le jeu est retrouvé dans la liste publique des jeux détectables par Discord (≈ 25 000 jeux), par ID Steam, puis par nom, puis par exécutable lancé. La liste est mise en cache 7 jours.
- **Application de repli** (« Playnite ») pour les jeux inconnus de Discord. L'ID d'application et l'image (clé d'asset ou URL) se changent dans les paramètres. Le nom du jeu apparaît alors dans les détails et dans la liste des membres (`status_display_type`).
- **Textes personnalisables** avec des variables : `{name}` `{platform}` `{source}` `{library}` `{playtime}` `{playcount}` `{completion}` `{genre}` `{developer}` `{year}` `{score}`. `{library}` donne la bibliothèque d'où vient le jeu (Steam, Ubisoft Connect, EA app, GOG…), ou « Playnite » pour un jeu ajouté à la main. Les séparateurs orphelins sont nettoyés, et une ligne dont toutes les valeurs sont vides est masquée.
- **Image du jeu choisie dans Playnite** (icône ou couverture). Discord n'affiche que des images en ligne, donc le plugin les convertit en PNG et les envoie une seule fois sur ImgBB, où elles sont publiques. Il faut entrer sa propre clé API ImgBB gratuite (api.imgbb.com) : sans clé, rien n'est envoyé. Catbox ne convient pas, car le relais d'images de Discord n'arrive pas à lire ses fichiers, et Imgur n'accepte plus de nouvelles applications. Sans cette option, ou pendant l'envoi, l'extension utilise la couverture Steam ou l'icône Discord du jeu.
- **Masquer un jeu** : clic droit sur un jeu → *Better Rich Presence* → *Hide activity from Discord*.
- **Forcer une application Discord** pour un jeu précis : clic droit → *Set Discord application ID…*
- **Présence au repos** optionnelle (« Browsing the library »).
- Plusieurs jeux lancés en même temps : le plus récent est affiché, puis le précédent quand il se ferme.
- Reconnexion automatique si Discord est lancé après le jeu.

## Désactiver le Rich Presence intégré de Playnite

Playnite a son propre Rich Presence Discord, activé par défaut. Il faut le couper pour utiliser Better Rich Presence.

### Pourquoi

- **Deux activités en même temps** : les deux se connectent à Discord en parallèle. Ton profil affiche alors à la fois « Joue à Playnite » (le Rich Presence intégré) et l'activité du plugin. Selon Discord, c'est parfois celle de Playnite qui passe en premier.
- **Pas d'option dans les paramètres** : Playnite 10 n'a aucune case pour le désactiver. Le réglage existe seulement dans son fichier de configuration, sous le nom `DiscordPresenceEnabled`.
- **Playnite réécrit ce fichier en quittant** : si tu le modifies pendant que Playnite tourne, même réduit dans la zone de notification, ta modification est écrasée à la fermeture. Il faut donc d'abord quitter Playnite complètement.

### Comment

1. **Quitte Playnite complètement** : clic droit sur son icône dans la zone de notification (à côté de l'horloge) → **Quitter**. Fermer la fenêtre ne suffit pas si Playnite est configuré pour se réduire dans la zone de notification.
2. **Ouvre le fichier de configuration** : appuie sur `Win + R`, colle `%APPDATA%\Playnite\config.json`, puis **OK**. Choisis le Bloc-notes si Windows demande avec quoi l'ouvrir.
   - Pour une installation portable de Playnite, `config.json` se trouve dans le dossier de Playnite lui-même.
3. **Trouve le réglage** : `Ctrl + F`, puis cherche `DiscordPresenceEnabled`. Tu trouveras cette ligne :

   ```json
   "DiscordPresenceEnabled": true,
   ```

4. **Remplace `true` par `false`**, sans toucher aux guillemets ni à la virgule :

   ```json
   "DiscordPresenceEnabled": false,
   ```

5. **Enregistre** (`Ctrl + S`) et relance Playnite.

Au démarrage, Better Rich Presence affiche une notification tant que le Rich Presence intégré est actif. Si elle n'apparaît plus, c'est bon.

Pour revenir au Rich Presence de Playnite, refais la même manipulation en remettant `true`, puis désactive Better Rich Presence dans **Add-ons**.

## Traductions

Le plugin suit la langue choisie dans Playnite (**Paramètres → General → Playnite language**). Il est disponible en 12 langues : anglais, français, allemand, espagnol, italien, portugais brésilien, polonais, russe, turc, chinois simplifié, japonais et coréen. Pour une autre langue, ou s'il manque une traduction, les textes s'affichent en anglais.

Hors anglais et français, les traductions ont été générées automatiquement. Les corrections de locuteurs natifs sont les bienvenues.

Sont traduits : l'écran des paramètres, le menu clic droit, les messages, et les textes par défaut de la présence Discord (« Parcourt sa bibliothèque », « 12h 30m de jeu »…). Les textes que tu as personnalisés ne changent pas.

**Ajouter une langue :**

1. Copie `BetterRichPresence/Localization/en_US.xaml` sous le code de langue de Playnite, par exemple `de_DE.xaml` (la liste des codes se trouve dans le dossier `Localization` de Playnite).
2. Traduis le texte entre les balises, sans toucher aux clés `x:Key` ni aux variables entre accolades (`{playtime}`, `{0}`…).
3. Recompile : tous les fichiers de ce dossier sont intégrés automatiquement à la DLL.

## Installation pour le développement

Prérequis : SDK .NET 8 ou plus récent (le projet cible .NET Framework 4.6.2 comme Playnite).

```powershell
.\build.ps1
```

Dans Playnite : **Paramètres → For developers → External extensions → Add**, puis choisis le dossier `BetterRichPresence\bin\Debug`. Après chaque build, redémarre Playnite pour recharger l'extension.

Pense aussi à [désactiver le Rich Presence intégré de Playnite](#désactiver-le-rich-presence-intégré-de-playnite).

## Créer un paquet .pext

```powershell
.\build.ps1 Release
& "$env:LOCALAPPDATA\Playnite\Toolbox.exe" pack BetterRichPresence\bin\Release dist
```

## Fonctionnement

L'extension parle directement au pipe IPC local de Discord (`\\.\pipe\discord-ipc-N`) au lieu d'utiliser `DiscordRPC.dll`. Playnite charge déjà sa propre version de cette bibliothèque, et une autre version dans le même processus provoquerait des conflits. Ça permet aussi d'utiliser des champs récents comme `status_display_type`.

## Licence

[MIT](LICENSE)
