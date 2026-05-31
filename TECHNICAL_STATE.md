# Résumé d'État Technique — Plugin NINA "TheSkyX Guider"

> Document de passation pour reprendre le projet dans une nouvelle session.
> Lire dans l'ordre les sections.
>
> **Ce plugin est issu du split du plugin monolithique `NinaFocusMaxSkyXPlugin`.**
> Le plugin LinearFocus autofocus est maintenant dans `NinaLinearFocus/`.

---

## 1. Objectif du Plugin

**TheSkyX 64 Guider** — autoguidage via le serveur de scripting JavaScript de TheSkyX
(TCP port 3040). Aucune dépendance binaire à TheSkyX, tout en TCP/JS.

Fonctionnalités :
- Connexion à l'autoguider TheSkyX (ccdsoftAutoguider)
- Démarrage / arrêt du guidage
- Dithering
- **Wizard de calibration** Est + Ouest avec slew automatique, prise de vue, lecture
  GuideStarX/Y et subframe anti-hijacking

---

## 2. Architecture Actuelle

### Stack

- .NET 8.0-windows, x64, WPF, `<UseWPF>true</UseWPF>`
- `NINA.Plugin` 3.2.0.9001 (NuGet)
- Tests : xUnit 2.9.2 + NSubstitute 5.3.0 (sibling projet `NinaTheSkyX.Tests`)

### Invariants d'identité

| Clé | Valeur |
|---|---|
| GUID | `c3d4e5f6-a7b8-9012-cdef-012345678912` |
| AssemblyName | `NinaTheSkyX` |
| RootNamespace | `NinaTheSkyX` |
| DLL | `NinaTheSkyX.dll` |
| AssemblyTitle (UI) | `"TheSkyX Guider"` |
| x:Key Options.xaml | `"TheSkyX Guider_Options"` (espace dans la clé — doit correspondre exactement) |
| Install path | `%LOCALAPPDATA%\NINA\Plugins\NinaTheSkyX\` |
| InternalsVisibleTo | `NinaTheSkyX.Tests` |

### Arborescence

```
NinaTheSkyX/
├── NinaTheSkyX.csproj
├── Plugin.cs                          (PluginBase, statiques : TelescopeMediator, CameraMediator, Options)
├── Properties/AssemblyInfo.cs         (GUID ci-dessus, AssemblyTitle="TheSkyX Guider")
├── Package.ps1                        (build + test + ZIP + manifest.json)
├── TECHNICAL_STATE.md                 (ce fichier)
│
├── Options/
│   ├── Options.xaml                   (ResourceDictionary, x:Key="TheSkyX Guider_Options")
│   ├── Options.xaml.cs                (InitializeComponent uniquement, bindings XAML)
│   ├── PluginOptions.cs               (PluginOptionsAccessor, GUID stable)
│   └── OptionsViewModel.cs            ([Export], expose GuiderCalibration, paramètres TCP)
│
└── TheSkyX/
    ├── TheSkyXTcpClient.cs            ← client TCP, envoi JS, parse "Out = …"
    ├── TheSkyXScriptBuilder.cs        ← snippets JS statiques et purs (testables)
    ├── TheSkyXGuider.cs               ← IGuider NINA : Connect/Start/Stop/Dither
    ├── TheSkyXGuiderProvider.cs       ← [Export(typeof(IEquipmentProvider))], liste le guider
    └── GuiderCalibrationVM.cs         ← wizard calibration Est + Ouest (machine à états)

NinaTheSkyX.Tests/
├── NinaTheSkyX.Tests.csproj
├── PluginOptionsDefaultsTests.cs
├── TheSkyXResponseParserTests.cs
└── TheSkyXScriptBuilderTests.cs       (33 [Fact]/[Theory] — tests scripts JS purs)
```

---

## 3. État d'Avancement

### Phase 1 — DONE et validé en NINA

- ✅ Plugin chargé et reconnu par NINA 3.3 NIGHTLY
- ✅ "TheSkyX 64" apparaît dans Equipment → Guider
- ✅ Options panel rendu avec les paramètres TCP et le wizard de calibration

### Guider — DONE complet (2026-05-20)

- ✅ `TheSkyXGuiderProvider` exporté → guider visible dans Equipment → Guider
- ✅ `TheSkyXGuider` : Connect / StartGuiding / StopGuiding / Dither fonctionnels
- ✅ Wizard de calibration (`GuiderCalibrationVM`) : **confirmé fonctionnel sur ciel réel**
  - Slew automatique Est / Ouest
  - Prise de vue auto après slew (plus de confirmation intermédiaire)
  - Affichage Heuristic AutoContrast (Bjorn) : étoiles visibles dans fenêtre Autoguider
  - Lecture GuideStarX/Y et affichage coordonnées pixel avant calibration
  - Subframe anti-hijacking autour de l'étoile sélectionnée (configurable 0–1000 px)
  - Subframe remis à false après `Calibrate(0)` → guidage normal non perturbé
  - Slider exposition images (1–30 s) distinct de l'exposition de guidage
- ✅ tests verts — 77 attendus (70 + 7 pour le filtrage robuste de `BuildAutoSelectGuideStar`), à confirmer par `dotnet test`
- ✅ `BuildAutoSelectGuideStar()` implémenté (2026-05-23) : prise de vue + ShowInventory + sélection auto étoile guide la plus brillante (FWHM 1.5–20 px), écriture GuideStarX/Y, restore AutoSaveOn
- 🔧 **Fix 2026-05-30** : `BuildAutoSelectGuideStar` / `BuildDiagnoseAutoSelect` inventoriaient `ccdsoftCameraImage` (imageur) → `ShowInventory()` renvoyait **0 étoile**. Corrigé en `ccdsoftAutoguiderImage` (objet image dédié autoguider, cf. script de prod ScriptSkyX/Cline Obs). AutoContrast retiré du diagnostic (Error 11000). **✅ détection confirmée sur ciel 2026-05-31 : 99 sources (image 1391×1039) ; écriture GuideStarX/Y confirmée (APRES = coords étoile)**
- 🛡️ **Robustesse 2026-05-30** : `BuildAutoSelectGuideStar` filtre marge de bord (TrackBoxX/Y), FWHM/ellipticité < facteur×médiane du champ, rejet saturation (scanLine + BITPIX), unscale binning (BinX/Y) avant écriture GuideStarX/Y. Sélection = la plus brillante NON saturée. +7 tests structure.
- ⚠️ Dithering : implémenté, non testé en séquenceur NINA
- ⚠️ Status bar NINA "guider : TheSkyX : démarrage du guidage..." reste affiché en permanence → bug à corriger

### Phases suivantes — À FAIRE

Voir fichier `PROMPTS_TheSkyX_Next_Phases.md` pour le détail des 5 phases suivantes :
1. **RMS guiding** — récupérer l'erreur RMS de guidage pour conditionner le lancement des poses
2. **Status bar** — corriger le message "démarrage du guidage..." permanent
3. **Bouton "Lancer Guidage"** dans l'UI du plugin
4. **Courbes de guidage** — vérifier si l'affichage NINA est possible avec TheSkyX
5. **Sélection étoile guide améliorée** — explorer l'API TheSkyX

---

## 4. Machine à États — `CalibrationWizardStage`

Flux nominal (Est puis Ouest) — prise de vue auto après le slew :

```
Idle
  → ConfirmStartEast              [bouton OK → slew auto]
  → SlewingToEast                 [async NINA SlewToCoordinatesAsync]
  → TakingImageEast               [auto après slew : ccdsoftAutoguider.TakeImage + AutoContrast]
  → AwaitingImageValidationEast   [user clique étoile dans TheSkyX, puis ✅ Image OK | ❌ Jog+Retry]
  → RunningCalibrationEast        [async Calibrate(0) — bloquant 2–4 min, timeout 10 min]
  → AwaitingCalibrationValidationEast  [boutons : ✅ Calib OK | 🔄 Recommencer]
  → ConfirmStartWest … (même séquence côté Ouest)
  → Done
```

Fallback manuel (monture non connectée dans NINA) : si `SlewToCoordinatesAsync` échoue,
les états `ConfirmAfterSlewEast` / `ConfirmAfterSlewWest` permettent un positionnement manuel.

États terminaux : `Done`, `Cancelled`, `Error`.

### Comportement `AwaitingImageValidationEast/West`

Quand l'utilisateur clique **✅ Image OK** (`OnImageOkAsync`) :
1. Envoie `BuildReadGuideStarPosition()` → lit `ccdsoftAutoguider.GuideStarX/Y`.
2. Si `x ≈ 0, y ≈ 0` : affiche "⚠ Aucune étoile sélectionnée !".
3. Sinon : affiche "📍 Étoile sélectionnée : X = xxx px, Y = yyy px".
4. Lance immédiatement `RunCalibrationAsync`.

Nouveau flux avec `BuildAutoSelectGuideStar()` (2026-05-23) :
1. Le script prend l'image (AutoSaveOn temporairement activé), analyse via `ShowInventory()`,
   sélectionne automatiquement l'étoile la plus brillante avec FWHM ∈ [1.5–20 px].
2. Si une étoile est trouvée : `GuideStarX/Y` sont écrits dans TheSkyX → statut "⭐ Étoile auto-sélectionnée".
3. Si aucune étoile valide : statut "⚠ Sélection manuelle requise" → l'utilisateur clique dans TheSkyX.

**🔧 Fix 2026-05-30** : l'inventaire utilisait `ccdsoftCameraImage` (objet image de
l'imageur) → `ShowInventory()` renvoyait 0 étoile. Corrigé en `ccdsoftAutoguiderImage`
(objet image dédié à l'autoguider). L'écriture de GuideStarX/Y reste à confirmer sur ciel réel.

L'utilisateur **peut toujours cliquer manuellement une autre étoile** dans TheSkyX avant de valider "Image OK".

---

## 5. Scripts TheSkyX — `TheSkyXScriptBuilder.cs`

| Méthode | Script JS envoyé | Statut |
|---|---|---|
| `BuildConnect()` | `ccdsoftAutoguider.Connect();Out=…` | ✅ |
| `BuildDisconnect()` | `ccdsoftAutoguider.Disconnect();Out=0;` | ✅ |
| `BuildTakeGuiderImage(exp)` | `Subframe=false;ExposureTime=exp;TakeImage();ccdsoftCameraImage.AttachToActiveAutoguider();AutoContrast(1,1,3);Out=0;` | ✅ corrigé 2026-05-20 |
| `BuildReadGuideStarPosition()` | `Out=ccdsoftAutoguider.GuideStarX+","+ccdsoftAutoguider.GuideStarY;` | ✅ ajouté 2026-05-20 |
| `BuildCalibrateAutoguider(exp, subframeSize)` | `[subframe setup;]ExposureTime=exp;Calibrate(0);Subframe=false;Out=0;` | ✅ corrigé 2026-05-20 |
| `BuildClearCalibration()` | `ccdsoftAutoguider.Calibration=0;Out=0;` | ✅ |
| `BuildStartGuiding(exp, force)` | `[Calibration=1;]ExposureTime=exp;Autoguide();Out=0;` | ✅ |
| `BuildStopGuiding()` | `ccdsoftAutoguider.Abort();Out=0;` | ✅ |
| `BuildDither(arcsec)` | `sky6RASCOMTele.Jog(v,'East');Jog(v,'North');Out=0;` | ✅ |
| `BuildJogRA(arcsec)` | `sky6RASCOMTele.Jog(v,'East'\|'West');Out=0;` | ✅ |
| `BuildAutoSelectGuideStar(exp, minFwhm, maxFwhm, fwhmFac, ellipFac, satFrac)` | `ccdsoftAutoguiderImage` : ShowInventory + filtres (bord/FWHM/ellipticité-médiane, saturation scanLine+BITPIX) + unscale binning + écriture/relecture GuideStarX/Y. Retour `"X,Y,N"\|"0,0,N"\|"0,0,0"` | 🛡️ Robuste 2026-05-30, **à confirmer sur ciel réel** |
| `BuildAutoFindGuideStar()` | — | ❌ [Obsolete] — N'existe pas. Voir `BuildAutoSelectGuideStar()` |
| `BuildCenterBrightestObject()` | `ccdsoftAutoguider.CenterBrightestObject();Out=0;` | ❌ [Obsolete] — Test réel 2026-05-22 : centrage physique de monture, pas sélection d'étoile |

**Notes `BuildTakeGuiderImage`** :
- `Subframe=false` (f minuscule) : force plein capteur avant preview.
- `ccdsoftCameraImage.AttachToActiveAutoguider()` : attache l'objet image global à l'autoguider.
- `AutoContrast(1, 1, 3)` : `cdAutoContrastBjorn=1` (Heuristic), `cdBgWeak=1`, `cdHLStrong=3`.
  Corrige le défaut TheSkyX (algo SBIG par défaut → étoiles invisibles sur cameras non-SBIG).

**Notes `BuildCalibrateAutoguider`** :
- Si `subframeSize > 0` : subframe carré centré sur GuideStarX/Y avant `Calibrate(0)`
  pour éviter que TheSkyX bascule sur une autre étoile.
- `Subframe=false` **toujours** après `Calibrate(0)` : remet le capteur en plein champ.

### Formule de pointage RA

```
HA = LST − RA  →  RA = LST − HA

Est  (HA = −2h) : offsetH = +2h → RA = LST + 2h  ✅
Ouest (HA = +2h) : offsetH = −2h → RA = LST − 2h  ✅
```

---

## 6. API TheSkyX — Confirmée / Infirmée

| Méthode / Propriété JS | Statut | Notes |
|---|---|---|
| `ccdsoftAutoguider.AutoFindGuideStar()` | ❌ N'existe pas | TypeError (test réel 2026-05-10). Recherche doc exhaustive 2026-05-22 : aucun équivalent (AutoFind, FindStar, FindGuideStar absent de classccdsoft_camera-members.html) |
| `ccdsoftAutoguider.CenterBrightestObject()` | ❌ Inutile pour sélection | Test réel 2026-05-22 : GuideStarX/Y reste 0,0 après l'appel. Fait un centrage physique de monture (impulsions guidage), pas une sélection d'étoile guide. |
| `ccdsoftAutoguider.GuideStarX` (écriture) | ✅ Confirmé ciel 2026-05-31 | Écriture par script effective : APRES=(522.83,520.802)=coords étoile. `GuideStarX/Y` = position de l'étoile guide sélectionnée (équivalent au clic utilisateur). |
| `ccdsoftAutoguiderImage` (objet image autoguider) | ✅ Confirmé ciel 2026-05-31 | **Objet distinct de `ccdsoftCameraImage` (imageur).** Pour les frames autoguider il FAUT utiliser `ccdsoftAutoguiderImage`. Test ciel : 99 sources sur image 1391×1039 (via `ccdsoftCameraImage` → 0). Source : ScriptSkyX / Cline Obs |
| `ccdsoftAutoguiderImage.ShowInventory()` | ✅ Confirmé ciel 2026-05-31 | ⚠ **Retourne 0 = code de SUCCÈS, PAS le nombre de sources.** Le nombre réel = `InventoryArray(0).length` (= 99 au test). Notre code utilise bien `.length`. |
| `ccdsoftAutoguiderImage.InventoryArray(idx)` | ✅ Confirmé ciel 2026-05-31 | idx : cdX=0, cdY=1, cdMagnitude=2, cdFWHM=4, cdEllipticity=8. Test ciel : 99 sources. |
| `ccdsoftAutoguider.AutoSaveOn` (lecture/écriture) | ⚠ Non testé | Propriété read/write. Sauvegarde/restaure l'état dans `BuildAutoSelectGuideStar()` |
| `ccdsoftAutoguider.GuideErrorX` | ⚠ Non testé | Présent dans la doc ("x guide error"). Confirmer nom exact et unités lors du test ciel (pixels ?) |
| `ccdsoftAutoguider.GuideErrorY` | ⚠ Non testé | Présent dans la doc ("y guide error"). Idem |
| `ccdsoftAutoguider.Calibrate()` | ❌ SyntaxError | "too few arguments" |
| `ccdsoftAutoguider.Calibrate(0)` | ✅ Confirmé | 0 = standard non-AO (test réel 2026-05-14) |
| `ccdsoftAutoguider.GuideStarX` (lecture) | ✅ Confirmé | Coordonnée X pixel du dernier clic |
| `ccdsoftAutoguider.GuideStarY` (lecture) | ✅ Confirmé | Retourne 0.0 si aucune étoile sélectionnée |
| `ccdsoftAutoguider.Subframe` (bool, 'f' min.) | ✅ Confirmé | 'SubFrame' (F maj.) est silencieusement ignoré |
| `ccdsoftAutoguider.SubframeLeft/Top/Right/Bottom` | ✅ Confirmé | Coordonnées absolues (pas Width/Height) |
| `ccdsoftCameraImage.AttachToActiveAutoguider()` | ✅ Confirmé | Doc + ShowInventory.js (2026-05-20) |
| `ccdsoftCameraImage.AutoContrast(Method,Bg,HL)` | ✅ Confirmé | Enums : Bjorn=1, BgWeak=1, HLStrong=3 |

---

## 7. Paramètres Persistés (`PluginOptions`)

| Propriété | Défaut | Range | Description |
|---|---|---|---|
| `TheSkyXHost` | `"127.0.0.1"` | — | Adresse IP du serveur TheSkyX |
| `TheSkyXPort` | 3040 | — | Port TCP du serveur TheSkyX |
| `TheSkyXGuideExposureSeconds` | 2.0 s | — | Exposition pendant le guidage actif |
| `TheSkyXCalibrationExposureSeconds` | 5.0 s | 1–30 s | Exposition pour TakeImage() et Calibrate(0) |
| `TheSkyXGuiderSubframeSize` | 300 px | 0–1000 px | Subframe autour de l'étoile guide avant Calibrate(0). 0 = désactivé |
| `DebugLogging` | false | — | Activation des logs DEBUG |
| `LastEastCalibrationAt` | null | DateTime? | Horodatage de la dernière calibration Est (ISO-8601) |
| `LastWestCalibrationAt` | null | DateTime? | Horodatage de la dernière calibration Ouest (ISO-8601) |

---

## 8. Tests xUnit — `TheSkyXScriptBuilderTests.cs`

77 tests attendus (70 + 7 pour le filtrage robuste de `BuildAutoSelectGuideStar`).
Tests ajoutés en 2026-05-20 :

```csharp
[Fact] ReadGuideStarPosition_ContainsGuideStarXAndYAndOut()
[Fact] ReadGuideStarPosition_UsesCcdsoftAutoguider()
[Fact] TakeGuiderImage_AppliesHeuristicAutoContrastAfterTakeImage()
       // Vérifie : AttachToActiveAutoguider() + AutoContrast(1,1,3) présents
[Fact] TakeGuiderImage_AutoContrastAfterTakeImage_OrderIsCorrect()
       // Vérifie : index(TakeImage) < index(AutoContrast)
[Fact] CalibrateAutoguider_WithSubframe_ResetsSubframeAfterCalibrate()
       // Vérifie : index(Subframe=false) > index(Calibrate(0))
[Fact] CalibrateAutoguider_WithoutSubframe_AlsoResetsSubframeAfterCalibrate()
```

Tests ajoutés en 2026-05-23 (BuildAutoSelectGuideStar) :

```csharp
[Fact] AutoSelectGuideStar_ContainsTakeImageAndShowInventory()
[Fact] AutoSelectGuideStar_SavesAndRestoresAutoSaveOn()
[Fact] AutoSelectGuideStar_AutoSaveOnBeforeTakeImage_RestoredAfterInventory()
[Fact] AutoSelectGuideStar_WritesGuideStarCoordinates()
[Fact] AutoSelectGuideStar_UsesInventoryArrayWithCorrectIndices()
[Fact] AutoSelectGuideStar_FormatsExposureAndFwhmWithInvariantCulture()
[Fact] AutoSelectGuideStar_AppliesAutoContrastBjornAfterTakeImage()
[Fact] AutoSelectGuideStar_OrderIsCorrect_TakeBeforeShowInventoryBeforeGuideStarWrite()
[Fact] AutoSelectGuideStar_DisablesSubframeBeforeTakeImage()
[Theory] AutoSelectGuideStar_OutputsZeroZeroZeroWhenNoStars(...)
```

**Total tests projet** : 65/65 verts (après suppression des 4 tests de clamp incompatibles
avec le mock `PluginOptionsAccessor`).

---

## 9. Détails Critiques (invariants à NE PAS casser)

### GUID figé
```
c3d4e5f6-a7b8-9012-cdef-012345678912
```
Présent dans `Properties/AssemblyInfo.cs` ET `Options/PluginOptions.cs`. **Ne jamais changer.**

### x:Key Options.xaml
`"TheSkyX Guider_Options"` — l'**espace** dans la clé est obligatoire (doit correspondre
exactement à `AssemblyTitle = "TheSkyX Guider"`).

### Build NU1701 warnings — IGNORER
4 warnings sur `ToastNotifications` 2.5.1 et `VVVV.FreeImage` 3.15.1.1. Pas réparables.

### Mock PluginOptionsAccessor — limitation connue
`TryGetValue()` retourne toujours false avec NSubstitute → getter revient toujours au défaut.
Ne pas écrire de tests setter→getter clamped avec ce mock. Seuls les tests de valeurs par
défaut sont fiables.

### Pipeline de validation (Windows)

```powershell
# Build + tests
cd C:\Astro\AstroIQ\NinaTheSkyX.Tests
dotnet test -c Release --nologo
# Cible : 56/56 verts

# Pipeline complet (build + tests + ZIP + manifest.json)
& "C:\Astro\AstroIQ\NinaTheSkyX\Package.ps1"

# Déploiement local
$dest = "$env:LOCALAPPDATA\NINA\Plugins\NinaTheSkyX"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "C:\Astro\AstroIQ\NinaTheSkyX\bin\Release\NinaTheSkyX.dll" $dest -Force
# Puis redémarrer NINA
```

### Source de vérité pour API NINA
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
ilspycmd -t "NINA.Foo.Bar" /c/Users/alexa/.nuget/packages/nina.<package>/3.1.0.9001/lib/net8.0-windows7.0/NINA.<Package>.dll
```
Référence open source : [Hocus Focus](https://github.com/ghilios/joko.nina.plugins/)
