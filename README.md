# CL-Wallpaper

Fonds d'écran animés (vidéos) ou fixes (images) pour Windows 10/11, avec trois interfaces au choix, chacune en clair ou sombre.

- **Un seul fichier** : `CL-Wallpaper.exe` (~430 Ko). Rien à installer : il utilise .NET Framework 4.8 et Media Foundation, déjà présents dans Windows.
- **Léger** : la vidéo est décodée par le GPU (Media Foundation). Quand la fenêtre est fermée, seul un petit processus d'arrière-plan reste actif (icône dans la barre des tâches). Il se met en pause tout seul si le bureau est caché, la session verrouillée ou l'écran éteint.

## Télécharger

Dernière version : **[CL-Wallpaper.exe](https://github.com/janoudyannis-lgtm/CL-Wallpaper/releases/latest/download/CL-Wallpaper.exe)** (page des versions : [Releases](https://github.com/janoudyannis-lgtm/CL-Wallpaper/releases)).

Windows 10 ou 11. L'exe n'est pas signé : au premier lancement, Windows SmartScreen peut afficher « Windows a protégé votre ordinateur » → *Informations complémentaires* → *Exécuter quand même*.

Pour les vidéos AV1, HEVC ou VP9, installez si besoin l'extension gratuite correspondante depuis le Microsoft Store (l'app vous le signale et donne le lien).

## Démarrer

1. Double-cliquez sur `CL-Wallpaper.exe`.
2. Au premier lancement, choisissez où créer le **répertoire de l'application** (par défaut `C:\Users\<vous>\CL-Wallpaper`). Options :
   - copier l'exe dans ce répertoire (l'app est alors autonome : on peut déplacer tout le dossier) ;
   - raccourci dans le menu Démarrer ou sur le Bureau.
3. Importez des vidéos ou des images (bouton **Importer**, ou glisser-déposer depuis l'Explorateur).
4. Double-cliquez sur un fond, ou bien sélectionnez-le puis cliquez sur **Définir comme fond d'écran**.

Contenu du répertoire :

```
CL-Wallpaper\
├─ CL-Wallpaper.exe        (si vous avez choisi de le copier)
├─ Bibliothèque\           vos fonds, rangés dans des dossiers (= vos bibliothèques)
└─ .clwallpaper\           (caché) réglages, vignettes, état
```

## Interfaces

À changer dans **Réglages → Interface**. Le changement s'applique immédiatement, sans redémarrer l'app.

| | |
|---|---|
| **macOS** | Barre latérale translucide (Mica), boutons de fenêtre rouge / jaune / vert, accent bleu |
| **Base** | Inspirée de l'app Claude : fond ivoire (ou anthracite chaud en sombre), accent terracotta, titres à empattement, boutons de fenêtre Windows |
| **Old School** | Façon Windows 98 : barre de titre bleu marine en dégradé, boutons `_ □ ×`, biseaux gris, arbre avec `+`/`−`, barres de défilement à flèches, police pixel |

**Réglages → Apparence** (Système / Clair / Sombre) fonctionne avec les trois interfaces.

## Fonctionnalités

| | |
|---|---|
| **Formats** | Vidéos : MP4, MOV, MKV, WEBM, AVI, WMV, M4V, MPG, TS… en H.264, HEVC, VP9 ou **AV1** (avec les extensions vidéo gratuites du Microsoft Store) · Images : JPG, PNG, BMP, GIF animé, WEBP, TIFF, HEIC |
| **Codecs** | À l'import et dans l'inspecteur, l'app détecte le codec de chaque vidéo et prévient s'il manque à Windows (AV1, HEVC, VP9, MPEG-2), avec un lien direct vers l'extension du Microsoft Store |
| **GIF animés** | Convertis en MP4 H.264 à l'import (souvent 5 à 10× plus petits, et décodés par la carte graphique au lieu du processeur). Désactivable dans Réglages |
| **Moteur vidéo** | Media Foundation *Media Engine* + Direct3D 11, décodage par la carte graphique, synchronisé sur le rafraîchissement de l'écran ; l'ancien moteur (MFPlay) sert de secours |
| **Bibliothèques** | Dossiers et sous-dossiers, comme dans l'Explorateur : créer, renommer, déplacer (glisser une vignette sur un dossier), mettre à la corbeille, rechercher. Les changements faits dans l'Explorateur apparaissent automatiquement. |
| **Affichage** | Remplir (recadré), Ajuster (bandes noires), Étirer |
| **Temps** | Partie lue (début / fin de la vidéo avec un curseur double), vitesse 0,5× → 2× |
| **Boucle** | En boucle · Une fois (reste sur la dernière image) · Avec pause (1 à 60 s entre deux lectures) · option **Fondu à chaque boucle** : la dernière image se fond dans la première (0,8 s, sur la carte graphique), pour les vidéos qui ne bouclent pas proprement |
| **Jour et nuit** | Clic droit sur un fond ou un dossier → *Fond de jour* / *Fond de nuit*, heures réglables dans Réglages. Un fond choisi à la main reste affiché jusqu'à la bascule suivante |
| **Lecture de dossier** | Un dossier entier en fond : ordre normal ou aléatoire, sous-dossiers inclus ou non, durée de chaque image (5 s → 1 h), nombre de lectures de chaque vidéo avant de passer à la suivante |
| **Aperçu** | L'inspecteur (à droite) lit la vidéo en direct avec vos réglages ; ils s'appliquent aussi en direct au fond actif |
| **Économie** | Pause si une app est en plein écran, si une fenêtre agrandie recouvre l'écran, sur batterie, et toujours quand la session est verrouillée, ouverte à distance (Bureau à distance) ou que l'écran est éteint |
| **Écrans** | Même fond partout, écran principal seul, ou **un fond par écran** (l'app demande alors sur quel écran appliquer) |
| **Transitions** | Fondu enchaîné entre deux fonds (désactivable dans Réglages) |
| **Erreurs** | Message clair si un fichier ne se lit pas (codec manquant, format non pris en charge, fichier introuvable ou abîmé), dans l'app et en notification |
| **Démarrage** | Option « Lancer au démarrage de Windows » (Réglages) |
| **Mises à jour** | Réglages → Mises à jour : source préremplie avec ce dépôt GitHub (ou un manifeste JSON en https), vérification au démarrage en option, ou installation depuis un fichier. Voir ci-dessous |
| **Barre des tâches** | Clic = ouvrir · clic droit = pause, fond suivant, arrêter |

Raccourcis : `Entrée` appliquer/ouvrir · `Suppr` corbeille · `F2` renommer · `Ctrl+I` importer · `Ctrl+N` nouveau dossier · `Ctrl+F` rechercher · `Retour arrière` / `Alt+←` dossier précédent.

## Recompiler

Aucun SDK nécessaire, le compilateur C# de Windows suffit :

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Publier une version (signature et mises à jour)

Signer l'exe évite l'avertissement SmartScreen chez les autres et permet à l'updater de vérifier l'éditeur. Il faut un certificat de signature de code (acheté chez une autorité, ou Azure Trusted Signing) :

```powershell
# avec un fichier .pfx
powershell -ExecutionPolicy Bypass -File build.ps1 -Pfx moncert.pfx -PfxPassword "..." -UpdateJson -DownloadUrl "https://exemple.fr/CL-Wallpaper.exe"
# ou avec un certificat installé (clé USB, etc.)
powershell -ExecutionPolicy Bypass -File build.ps1 -Thumbprint 0123ABCD... -UpdateJson
```

`-UpdateJson` écrit `update.json` (version + empreinte SHA-256) à côté de l'exe : publiez les deux, puis mettez l'adresse de `update.json` comme source dans les Réglages. Avec GitHub, publiez simplement l'exe dans une *release* et utilisez `github:vous/cl-wallpaper`.

L'updater n'installe une version que si son empreinte SHA-256 correspond et, si l'app installée est signée, si la nouvelle est signée par le même certificat. L'ancien exe est renommé en `.old` (supprimé au lancement suivant), puis l'app et le fond redémarrent.

Le numéro de version se change dans `src\Program.cs` (attributs `Assembly…Version`) et `src\Core\Store.cs` (`Store.Version`).

Pour tester le mode « un fond par écran » sur un seul moniteur : lancer le fond avec la variable d'environnement `CLW_FAKE_SCREENS=2` (l'écran est coupé en deux).

## Code

```
src\Program.cs              point d'entrée (fenêtre, ou « --host » pour le fond d'écran)
src\Core\MediaEnginePlayer.cs  lecteur principal (Media Engine + Direct3D 11) : AV1, découpe, vitesse, boucles, recadrage
src\Core\VideoPlayer.cs     interface commune + lecteur de secours (MFPlay, DirectX 9)
src\Core\MfTools.cs         détection des codecs, encodeur H.264 (GIF → MP4)
src\Ui\Updater.cs           mises à jour : manifeste / GitHub, SHA-256, signature, remplacement de l'exe
src\Core\DesktopHost.cs     place la fenêtre entre le fond Windows et les icônes (gère Windows 11 24H2+)
src\Core\Store.cs           répertoire de l'app, réglages, communication fenêtre ↔ fond d'écran
src\Host\                   processus d'arrière-plan : une fenêtre par écran, playlists, pause auto, icône
src\Ui\                     interface WPF (XAML chargé à l'exécution), vignettes, inspecteur, feuilles
src\Ui\UiApp.cs             thèmes : palettes des 3 interfaces × clair/sombre, polices, arrondis
src\Ui\Xaml\Modern.xaml     modèles des contrôles pour macOS et Base
src\Ui\Xaml\Retro.xaml      modèles des contrôles pour Old School (biseaux Windows 98)
assets\                     icône et manifeste (DPI par écran)
tools\MakeIcon.cs           génère l'icône
```

## Désinstaller

**Réglages → Désinstaller…** : arrête le fond (Windows reprend son fond habituel), puis supprime l'app, ses réglages et vignettes, ses raccourcis, le lancement au démarrage et `%APPDATA%\CL-Wallpaper`. Vos vidéos et images sont conservées, sauf si vous cochez la case pour les envoyer à la corbeille.
