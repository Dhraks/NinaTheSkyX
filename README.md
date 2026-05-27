# NinaTheSkyX — TheSkyX 64 Guider Plugin for NINA

Plugin C# pour [NINA 3.x](https://nighttime-imaging.eu/) qui fournit l'autoguidage **TheSkyX 64 Guider** via le serveur de scripting JavaScript de TheSkyX (TCP port 3040).

**Aucune dépendance binaire à TheSkyX** — tout passe par TCP/JavaScript.

---

## Fonctionnalités

- Connexion à l'autoguider TheSkyX (`ccdsoftAutoguider`)
- Démarrage / arrêt du guidage
- Dithering (jog RA/Dec)
- **Wizard de calibration Est + Ouest** :
  - Slew automatique, prise de vue auto, AutoContrast Bjorn
  - Sélection automatique de l'étoile guide (`BuildAutoSelectGuideStar`)
  - Subframe anti-hijacking configurable
  - Lecture et affichage des coordonnées GuideStarX/Y avant calibration

## Stack technique

| Composant | Version |
|---|---|
| .NET | 8.0-windows |
| NINA.Plugin (NuGet) | 3.2.0.9001 |
| xUnit | 2.9.2 |
| NSubstitute | 5.3.0 |

## Structure du dépôt

```
NinaTheSkyX/        ← ce dépôt
NinaTheSkyX.Tests/  ← projet de tests (sibling, même répertoire parent)
```

Pour cloner et travailler sur le projet complet, les deux dossiers doivent être côte à côte :

```
MonDossier/
├── NinaTheSkyX/        ← git clone ici
└── NinaTheSkyX.Tests/  ← à créer manuellement ou à récupérer séparément
```

## Build & Tests

```powershell
# Build
cd NinaTheSkyX
dotnet build -c Release

# Tests (65/65) — depuis le dossier sibling
cd ..\NinaTheSkyX.Tests
dotnet test -c Release --nologo

# Pipeline complet : build + tests + ZIP + manifest.json
& ".\NinaTheSkyX\Package.ps1"
```

4 warnings NU1701 sur `ToastNotifications` et `VVVV.FreeImage` sont inévitables — les ignorer.

## Installation

```powershell
$dest = "$env:LOCALAPPDATA\NINA\Plugins\NinaTheSkyX"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item ".\bin\Release\NinaTheSkyX.dll" $dest -Force
# Puis redémarrer NINA
```

## Prérequis

- Windows x64, .NET 8 SDK
- NINA 3.x (nightly ou stable)
- TheSkyX 64 avec le serveur de scripting JavaScript activé (port 3040)

## API TheSkyX — Points d'attention

L'API scripting TheSkyX est peu documentée. Les comportements confirmés en test réel sont dans [`TECHNICAL_STATE.md`](TECHNICAL_STATE.md) (section "API TheSkyX — Confirmée / Infirmée").

Points critiques :
- `ccdsoftAutoguider.Calibrate(0)` ✅ — `Calibrate()` sans argument ❌ SyntaxError
- `Subframe=false` (f minuscule) ✅ — `SubFrame` (F majuscule) silencieusement ignoré
- `AutoContrast(1, 1, 3)` obligatoire pour les caméras non-SBIG

## Contribuer

Voir [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

MIT
