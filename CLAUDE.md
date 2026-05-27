# CLAUDE.md — NinaTheSkyX

> **Lecture obligatoire en début de session.** Ce fichier capture les
> conventions, contraintes et modes opératoires nécessaires pour bosser
> sur ce projet sans casser des choses non-évidentes. Pour l'état
> d'avancement courant et la prochaine action, lire **`TECHNICAL_STATE.md`**.
>
> Ce plugin est issu du split du plugin monolithique `NinaFocusMaxSkyXPlugin`.
> Le plugin LinearFocus autofocus est dans `C:\Astro\AstroIQ\NinaLinearFocus\`.

---

## 1. Objectif du Projet

Plugin C# pour **NINA 3.x** (Nighttime Imaging 'N' Astronomy, logiciel d'astrophotographie
open-source) qui fournit l'autoguidage **TheSkyX 64 Guider** :

- Autoguidage via le serveur de scripting JavaScript de TheSkyX (TCP port 3040).
- Aucune dépendance binaire à TheSkyX — tout passe par TCP/JS.
- Connexion, démarrage/arrêt du guidage, dithering.
- Wizard de calibration Est + Ouest avec slew automatique, prise de vue, lecture GuideStarX/Y
  et subframe anti-hijacking configurable.

Public visé : astrophotographes amateurs utilisant TheSkyX 64 comme logiciel de contrôle.

---

## 2. Stack Technique

| Composant | Version | Notes |
|---|---|---|
| .NET SDK | 8.0+ | `<TargetFramework>net8.0-windows</TargetFramework>` |
| Plate-forme | Windows x64 uniquement | WPF |
| WPF | `<UseWPF>true</UseWPF>` | XAML auto-compilé en BAML |
| `NINA.Plugin` (NuGet) | **3.2.0.9001** | https://www.nuget.org/packages/NINA.Plugin |
| xUnit | 2.9.2 | tests |
| NSubstitute | 5.3.0 | mocking de `IProfileService` etc. |
| ilspycmd | 10.0.1.x | décompilation des assemblies NINA — outil critique |

NINA n'a pas besoin d'être installé pour build ou tester — tout passe par les NuGet.

---

## 3. Architecture & Arborescence

### Dossier plugin

```
NinaTheSkyX/
├── NinaTheSkyX.csproj                 # SDK-style, GenerateAssemblyInfo=false
├── Plugin.cs                          # PluginBase, statiques : TelescopeMediator, CameraMediator
├── Properties/AssemblyInfo.cs         # GUID, AssemblyTitle="TheSkyX Guider"
├── Package.ps1                        # build → tests → ZIP → manifest.json (SHA256)
├── TECHNICAL_STATE.md                 # état d'avancement courant + prochaine action
├── CLAUDE.md                          # ce fichier
│
├── Options/
│   ├── Options.xaml                   # ResourceDictionary, x:Key="TheSkyX Guider_Options"
│   ├── Options.xaml.cs                # InitializeComponent uniquement, bindings XAML
│   ├── PluginOptions.cs               # PluginOptionsAccessor, GUID stable
│   └── OptionsViewModel.cs            # [Export], expose GuiderCalibration, paramètres TCP
│
└── TheSkyX/
    ├── TheSkyXTcpClient.cs            # client TCP, envoi JS, parse "Out = …"
    ├── TheSkyXScriptBuilder.cs        # snippets JS statiques et purs (testables)
    ├── TheSkyXGuider.cs               # IGuider NINA : Connect/Start/Stop/Dither
    ├── TheSkyXGuiderProvider.cs       # [Export(typeof(IEquipmentProvider))], liste le guider
    └── GuiderCalibrationVM.cs         # wizard calibration Est + Ouest (machine à états)
```

### Dossier de tests (sibling)

```
NinaTheSkyX.Tests/
├── NinaTheSkyX.Tests.csproj
├── PluginOptionsDefaultsTests.cs
├── TheSkyXResponseParserTests.cs
└── TheSkyXScriptBuilderTests.cs       # 33 [Fact]/[Theory] — tests scripts JS purs
```

---

## 4. Build, Tests, Packaging — Commandes

```powershell
# Build du plugin seul
cd C:\Astro\AstroIQ\NinaTheSkyX
dotnet build -c Release

# Tests (build + run)
cd C:\Astro\AstroIQ\NinaTheSkyX.Tests
dotnet test -c Release --nologo
# Cible : 56/56 verts

# Pipeline complet : build + tests + ZIP + manifest.json
& "C:\Astro\AstroIQ\NinaTheSkyX\Package.ps1"

# Déploiement local
$dest = "$env:LOCALAPPDATA\NINA\Plugins\NinaTheSkyX"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "C:\Astro\AstroIQ\NinaTheSkyX\bin\Release\NinaTheSkyX.dll" $dest -Force
# Puis redémarrer NINA
```

**Build attendu** : 0 erreur. 4 warnings NU1701 inévitables (`ToastNotifications`, `VVVV.FreeImage`). **Les ignorer.**

---

## 5. Conventions de Code

### Langue & formatage
- Commentaires et chaînes UI en **français**.
- Code C# : noms anglais standard.
- Indentation 4 espaces, accolades K&R.
- Préfixe `_` pour les champs privés.
- `CultureInfo.InvariantCulture` pour toute sérialisation numérique dans les scripts JS.

### Documentation
- **XML doc obligatoire** sur les classes publiques et les méthodes.
- Marqueurs `// TODO(api)` pour les signatures TheSkyX incertaines.

### Logging
- `NINA.Core.Utility.Logger.Info` / `Warning` / `Error`.
- Préfixe : `[TheSkyX]`.
- Logs DEBUG conditionnels via `_debug` (depuis `PluginOptions.DebugLogging`).

### Tests
- xUnit avec `[Fact]` ou `[Theory]` + `[InlineData]`.
- Naming : `MethodeOuContexte_Condition_RésultatAttendu`.
- `TheSkyXScriptBuilderTests` : vérifier les fragments JS attendus par index/Contains.

---

## 6. Patterns NINA SDK Critiques

### MEF Exports

| Type | Attribut |
|---|---|
| Plugin manifest | `[Export(typeof(IPluginManifest))]` |
| Equipment provider (guider) | `[Export(typeof(IEquipmentProvider))]` |
| ViewModel d'options | `[Export]` (untyped) |

### Namespaces piégeux

| Type | Vrai namespace |
|---|---|
| `IEquipmentProvider<T>` | `NINA.Equipment.Interfaces.ViewModel` |
| `PluginOptionsAccessor` | `NINA.Profile` |
| `IGuideStep` | `NINA.Core.Interfaces` |
| `BaseINPC` | `NINA.Core.Utility` |
| `RelayCommand` | `NINA.Core.Utility` |

Quand un namespace est inconnu, **toujours décompiler** avec ilspycmd.

### Convention View Plugin Options
- `Options.xaml` = `ResourceDictionary` avec `x:Class="NinaTheSkyX.Options.Options"`
- DataTemplate keyé : `x:Key="TheSkyX Guider_Options"` (= AssemblyTitle, **espace obligatoire**)
- DataContext via `{x:Static local:Plugin.Options}` (singleton)
- `Plugin.Initialize()` merge explicite dans `Application.Current.Resources`

### Mediators exposés statiquement sur `Plugin.cs`
```csharp
Plugin.TelescopeMediator
Plugin.CameraMediator
Plugin.Options  // singleton OptionsViewModel
```

---

## 7. Invariants à NE PAS Casser

### GUID figé
```
c3d4e5f6-a7b8-9012-cdef-012345678912
```
Dans `Properties/AssemblyInfo.cs` ET `Options/PluginOptions.cs`. **Ne JAMAIS changer.**

### Noms d'assembly conservés
- `AssemblyName = NinaTheSkyX`
- `RootNamespace = NinaTheSkyX`
- DLL = `NinaTheSkyX.dll`
- Install path = `%LOCALAPPDATA%\NINA\Plugins\NinaTheSkyX\`
- `[InternalsVisibleTo("NinaTheSkyX.Tests")]`
- AssemblyTitle user-facing = `"TheSkyX Guider"`

### ⚠️ x:Key Options.xaml — espace obligatoire
`x:Key="TheSkyX Guider_Options"` — l'**espace** dans la clé est obligatoire.
Il doit correspondre exactement à `AssemblyTitle = "TheSkyX Guider"`.

### API TheSkyX — résultats confirmés en test réel
| Script JS | Statut |
|---|---|
| `ccdsoftAutoguider.AutoFindGuideStar()` | ❌ N'existe pas (TypeError) |
| `ccdsoftAutoguider.Calibrate()` | ❌ SyntaxError ("too few arguments") |
| `ccdsoftAutoguider.Calibrate(0)` | ✅ Fonctionne |
| `Subframe=false` (f minuscule) | ✅ Obligatoire — 'SubFrame' (F maj.) ignoré silencieusement |
| `SubframeLeft/Top/Right/Bottom` | ✅ Coordonnées absolues (pas Width/Height) |

### Limitation mock PluginOptionsAccessor
`TryGetValue()` retourne toujours false avec NSubstitute → le getter revient toujours
au défaut. Ne pas écrire de tests setter→getter clamped avec ce mock.

---

## 8. Sources de Vérité Externes

### Décompilation NINA SDK
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
ilspycmd -l "interface,class" /c/Users/alexa/.nuget/packages/nina.<package>/3.1.0.9001/lib/net8.0-windows7.0/NINA.<Package>.dll | grep -i <pattern>
ilspycmd -t "NINA.Foo.Bar" <dll>
```

DLLs disponibles après `dotnet restore` :
```
/c/Users/alexa/.nuget/packages/nina.core/3.1.0.9001/...
/c/Users/alexa/.nuget/packages/nina.equipment/3.1.0.9001/...
/c/Users/alexa/.nuget/packages/nina.plugin/3.1.0.9001/...
/c/Users/alexa/.nuget/packages/nina.profile/3.1.0.9001/...
/c/Users/alexa/.nuget/packages/nina.wpf.base/3.1.0.9001/...
```

### Plugin de référence — Hocus Focus
https://github.com/ghilios/joko.nina.plugins/tree/develop/Joko.NINA.Plugins/Joko.NINA.Plugins.HocusFocus

---

## 9. Pièges Connus / FAQ

### Le guider "TheSkyX 64" n'apparaît pas dans Equipment → Guider
- Vérifier que `TheSkyXGuiderProvider` porte `[Export(typeof(IEquipmentProvider))]`.
- NE PAS utiliser `IEquipmentProvider<T>` générique.

### Le panneau Options ne s'affiche pas
- Vérifier `x:Key="TheSkyX Guider_Options"` (avec espace, exact).
- Vérifier que `Plugin.Initialize()` merge la ResourceDictionary.

### Erreur XAML "MC3000 : XML comment cannot contain '--'"
- `<!-- ----- -->` interdit. Utiliser `<!-- titre -->` sans séparateurs.

### Erreur CS0579 "Attribut AssemblyXxx en double"
- `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` doit être présent dans .csproj.

### `BuildTakeGuiderImage` — étoiles invisibles dans TheSkyX
- L'algo AutoContrast par défaut de TheSkyX est SBIG → invisible sur autres cameras.
- `AutoContrast(1, 1, 3)` (Bjorn Heuristic) est obligatoire. Ne pas retirer ce flag.

### `BuildCalibrateAutoguider` — TheSkyX bascule sur une autre étoile
- Activer le subframe (`subframeSize > 0`) pour restreindre la zone de recherche.
- Toujours remettre `Subframe=false` après `Calibrate(0)` pour le guidage normal.

### Tests de clamp échouent
- `PluginOptionsAccessor.TryGetValue()` retourne toujours false en NSubstitute.
- Seuls les tests de valeurs par défaut sont fiables. Voir section 7.

---

## 10. Workflow Recommandé pour Modifications

1. **`git pull`** dans `C:\Astro\AstroIQ\` — intégrer les commits des autres développeurs avant de commencer.
2. **Lire `TECHNICAL_STATE.md`** : état d'avancement + prochaine action.
3. **TodoWrite si > 3 étapes**.
4. **Modif code** : toujours `Read` avant `Edit`. Pour nouveau fichier : `Write`.
5. **Build incrémental** après chaque changement structurel :
   ```powershell
   cd C:\Astro\AstroIQ\NinaTheSkyX && dotnet build -c Release
   ```
6. **Tests** : faire passer les 65+ tests avant de livrer.
7. **Repackage** : `& Package.ps1`
8. **Déployer** : copier la DLL dans `%LOCALAPPDATA%\NINA\Plugins\NinaTheSkyX\`.
9. **Mettre à jour `TECHNICAL_STATE.md`**.
10. **`git add . && git commit -m "feat/fix: ..."` puis `git push`** — rendre les modifications disponibles.

---

## 11. Phases du Roadmap

- ✅ **Phase 1** : connexion TCP, parsing TheSkyX, packaging
- ✅ **Phase 1.5** : UI Options (wizard calibration, paramètres TCP)
- ✅ **Phase 2** : Wizard calibration — confirmé fonctionnel sur ciel réel (2026-05-20) :
  slew auto, prise de vue auto, AutoContrast Bjorn, lecture GuideStarX/Y, subframe
  anti-hijacking, reset Subframe après calibration
- ⏳ **Phase 3** : Récupération RMS de guidage + statut connecté/déconnecté
- ⏳ **Phase 4** : Correction status bar NINA "démarrage du guidage..." permanent
- ⏳ **Phase 5** : Bouton "Lancer Guidage" dans l'UI, courbes de guidage, sélection étoile améliorée

---

## 12. Communication avec l'Utilisateur

Alexandre, astrophotographe amateur, Newton-ASI1600 + FSQ106/ZWO6200MM/FLI Atlas,
TheSkyX64, NINA 3.3 nightly.

Lui donner :
- Des explications techniques claires sur les choix
- Des commandes PowerShell prêtes à coller
- Des scripts JS TheSkyX commentés quand on modifie `TheSkyXScriptBuilder.cs`
- Pas de jargon C# obscur sans explication

Quand incertain sur une API TheSkyX : toujours indiquer explicitement que c'est non vérifié.
L'API scripting TheSkyX est peu documentée — se référer aux tests réels en section 7 et au
`TECHNICAL_STATE.md` section "API TheSkyX — Confirmée / Infirmée".
