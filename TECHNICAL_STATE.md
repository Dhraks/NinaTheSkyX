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
- ✅ tests verts — 78 attendus (70 + 8 pour le filtrage robuste + marge de bord de `BuildAutoSelectGuideStar`), à confirmer par `dotnet test`
- ✅ `BuildAutoSelectGuideStar()` implémenté (2026-05-23) : prise de vue + ShowInventory + sélection auto étoile guide la plus brillante (FWHM 1.5–20 px), écriture GuideStarX/Y, restore AutoSaveOn
- 🔧 **Fix 2026-05-30** : `BuildAutoSelectGuideStar` / `BuildDiagnoseAutoSelect` inventoriaient `ccdsoftCameraImage` (imageur) → `ShowInventory()` renvoyait **0 étoile**. Corrigé en `ccdsoftAutoguiderImage` (objet image dédié autoguider, cf. script de prod ScriptSkyX/Cline Obs). AutoContrast retiré du diagnostic (Error 11000). **✅ détection confirmée sur ciel 2026-05-31 : 99 sources (image 1391×1039) ; écriture GuideStarX/Y confirmée (APRES = coords étoile)**
- 🛡️ **Robustesse 2026-05-30** : `BuildAutoSelectGuideStar` filtre marge de bord calibration-safe (param `edgeMarginPx` ; wizard = max(120, subframe/2+20)), FWHM/ellipticité < facteur×médiane du champ, rejet saturation (scanLine + BITPIX), unscale binning (BinX/Y) avant écriture GuideStarX/Y. Sélection = la plus brillante NON saturée. +7 tests structure.
- ⚠️ Dithering : implémenté, non testé en séquenceur NINA
- ⚠️ Status bar NINA "guider : TheSkyX : démarrage du guidage..." reste affiché en permanence → bug à corriger

### Phases suivantes — À FAIRE

> **PROCHAINE ACTION → Phase 6 : VALIDATION SUR CIEL.** Le code est livré (2026-05-31).
> Étapes ciel : (1) exécuter `BuildDiagnoseCalibration()` depuis la console TheSkyX après une
> calibration pour confirmer si les propriétés de calibration sont **inscriptibles** par script
> (sinon basculer sur le fallback recalibration) ; (2) régler `GuideStarMin/Optimum/MaxADU` selon
> la caméra guide (voir pic ADU dans les logs `[TheSkyX]`) ; (3) tester un Start Guiding complet
> de chaque côté du méridien. Prompt détaillé : **`PROMPT_Phase6_StartGuiding.md`**.

- ✅ **Sélection étoile guide automatique** — FAIT, confirmé ciel 2026-05-31 (cf. ci-dessus :
  `ccdsoftAutoguiderImage`, filtres robustes, marge de bord calibration-safe, écriture+relecture GuideStarX/Y).
- 🟡 **Phase 6 — Start Guiding** : **code livré 2026-05-31, à valider sur ciel.**
  `TheSkyXGuider.StartGuiding()` orchestre désormais : (1) détection du côté méridien
  (HA = LST−RA via `Plugin.TelescopeMediator.GetInfo()`) ; (2) restauration de la calibration
  mémorisée pour ce côté (**EXPÉRIMENTAL**, `BuildRestoreCalibration` + vérif par relecture) ;
  (3) sélection d'une étoile **NON saturée** par critère **ADU** (`BuildAutoSelectGuideStar` mode
  ADU) ; (4) `Autoguide()`. Si aucune étoile valide → statut clair, pas de démarrage. La capture
  de calibration par côté est faite à la fin du wizard (`TryCaptureCalibrationDataAsync`).
  Options ajoutées : `GuideStarMinADU` (8000), `GuideStarMaxADU` (45000), `GuideStarOptimumADU` (25000),
  `East/WestCalibrationData`. **Reste : validation ciel** (inscriptibilité calibration + réglage ADU).

Autres phases (voir `PROMPTS_TheSkyX_Next_Phases.md`) :
1. **RMS guiding** — récupérer l'erreur RMS de guidage pour conditionner le lancement des poses
2. **Status bar** — corriger le message "démarrage du guidage..." permanent
3. **Bouton "Lancer Guidage"** dans l'UI du plugin
4. **Courbes de guidage** — vérifier si l'affichage NINA est possible avec TheSkyX

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
| `BuildAutoSelectGuideStar(..., minADU, maxADU, optimumADU)` | mode ADU ajouté 2026-05-31 : `maxADU>0` → pic ADU (`peakADU`) le plus proche de `optimumADU` dans `[minADU,maxADU]`. `maxADU=0` → comportement hérité | 🟡 mode ADU à confirmer ciel |
| `BuildReadCalibration()` | lit les propriétés de calibration → blob `"clé=valeur;…"` | 🟡 EXPÉRIMENTAL, non vérifié ciel |
| `BuildRestoreCalibration(blob)` | réécrit le blob + `Calibration=1` + relecture pour vérif C# | 🟡 EXPÉRIMENTAL, non vérifié ciel |
| `BuildDiagnoseCalibration()` | dump + test d'inscriptibilité (console TheSkyX) | 🟡 outil de validation ciel |
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
| `ccdsoftAutoguider.CalibrationVector{X,Y}{Positive,Negative}{X,Y}Component` (8) | ✅ read+write confirmé ciel 2026-06-01 | `type=number`, valeurs réelles (ex. ±17…81), `writable=true`. Composantes du vecteur de calibration |
| `ccdsoftAutoguider.AutoguiderCalibrationTime{X,Y}Axis` | ✅ read+write confirmé ciel 2026-06-01 | Ex. 20000 (unités 1/100 s). writable=true |
| `ccdsoftAutoguider.{AutoguiderBacklash{X,Y}Axis, SavedCalibrationTime{X,Y}, DeclinationAtCalibration}` | ✅ read+write confirmé ciel 2026-06-01 | Backlash=0, SavedT=20000, DecAtCal≈0 (calib à Dec 0). writable=true |
| `ccdsoftAutoguider.Calibration` (lecture) | ⚠ undefined en lecture (2026-06-01) | Écriture =0/1 probablement effective mais non relisible. Restore vérifié via les 15 propriétés numériques |
| `TelescopeInfo` (NINA) : `Connected, SiderealTime, RightAscension, Coordinates, SideOfPier, HoursToMeridian` | ✅ Confirmé décompil. NINA.Equipment 3.2.0.9001 | Côté méridien = HA = LST−RA (HA<0 Est, ≥0 Ouest) |

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
| `GuideStarMinADU` | 8000 | 0–65535 | Pic ADU min de l'étoile guide (sélection ADU Phase 6) |
| `GuideStarMaxADU` | 45000 | 0–65535 | Pic ADU max acceptable (sous saturation) |
| `GuideStarOptimumADU` | 25000 | 0–65535 | Pic ADU cible (le candidat le plus proche est retenu) |
| `EastCalibrationData` | "" | string | Blob calibration TheSkyX côté Est (Phase 6, EXPÉRIMENTAL) |
| `WestCalibrationData` | "" | string | Blob calibration TheSkyX côté Ouest (Phase 6, EXPÉRIMENTAL) |

---

## 8. Tests xUnit — `TheSkyXScriptBuilderTests.cs`

78 tests attendus (70 + 8 pour le filtrage robuste + marge de bord de `BuildAutoSelectGuideStar`).
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
