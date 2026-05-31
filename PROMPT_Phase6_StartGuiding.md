# PROMPT — Phase 6 : intégration « Start Guiding » de NINA

> Prompt de démarrage pour la prochaine étape du plugin **TheSkyX Guider**.
> À lire AVANT de commencer : `CLAUDE.md` (conventions, invariants, API confirmée §7)
> et `TECHNICAL_STATE.md` (état courant). Communication et commentaires en français,
> code en anglais standard, `CultureInfo.InvariantCulture` pour tout nombre dans le JS.

---

## 1. Objectif

Quand NINA déclenche l'instruction **« Start Guiding »** (appel de `IGuider.StartGuiding`
sur `TheSkyXGuider`), il faut, **avant** de lancer l'autoguidage :

1. **Sélectionner la bonne calibration** selon la position de l'objet imagé (côté Est / Ouest
   du méridien, c.-à-d. la *pier side*).
2. **Prendre une image** avec la caméra de guidage et **sélectionner automatiquement une étoile
   guide NON saturée**, selon un critère **ADU** (intervalle + ADU optimum, configurables).
3. **Lancer le guidage** une fois l'étoile sélectionnée avec les bons critères.

C'est l'aboutissement « sans intervention » du flux : NINA lance Start Guiding → le plugin
choisit calibration + étoile → guidage démarré.

---

## 2. Contexte — ce qui est déjà fait (au 2026-05-31)

- **Sélection auto de l'étoile guide** : FONCTIONNE, confirmée sur ciel.
  - `BuildAutoSelectGuideStar()` (dans `TheSkyX/TheSkyXScriptBuilder.cs`) : prise de vue guide,
    inventaire via **`ccdsoftAutoguiderImage`** (⚠ PAS `ccdsoftCameraImage`), filtres (marge de
    bord, FWHM/ellipticité relatives à la médiane, rejet saturation via `scanLine`+`BITPIX`),
    unscale binning, écriture + relecture de `GuideStarX/Y`.
  - Sélection actuelle = **la plus brillante NON saturée**. ⚠ Pour le guidage on veut plutôt
    **l'ADU optimum** (voir sous-tâche 2).
- **Wizard de calibration Est + Ouest** (`GuiderCalibrationVM`) : FONCTIONNE sur ciel
  (slew auto, `Calibrate(0)`, subframe anti-hijacking). `PluginOptions` stocke
  `LastEastCalibrationAt` / `LastWestCalibrationAt` (horodatages uniquement).
- **Point d'intégration** : `TheSkyX/TheSkyXGuider.cs`
  - `StartGuiding(bool forceCalibration, IProgress, CancellationToken)` → envoie aujourd'hui
    directement `BuildStartGuiding()` (= `Autoguide()`). C'est ICI qu'on insère les étapes 1 et 2.
  - `AutoSelectGuideStar()` est actuellement un **no-op** (`return Task.FromResult(true)`).
- **API TheSkyX confirmée** (cf. `CLAUDE.md` §7) : `Calibrate(0)`, `GuideStarX/Y` (lecture =
  position sélectionnée, écriture = sélection ; penser `×BinX/BinY`), `ShowInventory()` (retourne
  un code, pas le compte), `InventoryArray(idx)`, `scanLine(i)`, `FITSKeyword("BITPIX")`.
- **Médiateurs NINA** exposés en statique sur `Plugin.cs` : `Plugin.TelescopeMediator`,
  `Plugin.CameraMediator`, `Plugin.Options`.

---

## 3. Sous-tâches

### 3.1 — Sélection de la calibration selon la position de l'objet

**But** : appliquer à TheSkyX la calibration correspondant au côté du méridien où pointe la monture.

**À déterminer (côté NINA)** : la *pier side* / position courante.
- Via `Plugin.TelescopeMediator.GetInfo()` → `TelescopeInfo` (champs `SideOfPier`, `Coordinates`,
  éventuellement `HoursToMeridian` / heure sidérale). Décompiler pour confirmer les champs :
  ```bash
  export PATH="$PATH:$HOME/.dotnet/tools"
  ilspycmd -t "NINA.Equipment.Equipment.MyTelescope.TelescopeInfo" \
    /c/Users/alexa/.nuget/packages/nina.equipment/3.2.0.9001/lib/net8.0-windows7.0/NINA.Equipment.dll
  ```
- Mapper position → Est / Ouest (réutiliser la logique du wizard : HA < 0 = Est, HA > 0 = Ouest ;
  cf. `TECHNICAL_STATE.md` « Formule de pointage RA »).

**⚠ Question d'API TheSkyX à investiguer (NON tranchée)** : comment persister/restaurer une
calibration côté TheSkyX par script ?
- TheSkyX ne garde qu'**une** calibration active à la fois. Le wizard calibre Est puis Ouest, mais
  la 2ᵉ écrase la 1ʳᵉ dans l'état actif de TheSkyX.
- Pistes de propriétés `ccdsoftAutoguider` à explorer (doc Bisque
  `classccdsoft_camera-members.html` + tests JS réels) :
  `SavedCalibrationTimeX` / `SavedCalibrationTimeY`,
  `AutoguiderCalibrationTimeXAxis` / `AutoguiderCalibrationTimeYAxis`,
  et l'**angle de calibration** (chercher `*Angle*` dans les membres).
- **Stratégie proposée** (à valider sur ciel) :
  1. Après chaque calibration du wizard, **lire** les paramètres de calibration (angle + temps/taux
     par axe) et les **stocker par côté** dans `PluginOptions` (`EastCalibration*` / `WestCalibration*`).
  2. Au Start Guiding : déterminer le côté → **réécrire** ces paramètres dans TheSkyX + `Calibration = 1`
     avant `Autoguide()`.
- **Fallback si la restauration par script n'est pas possible** : soit forcer une recalibration si le
  côté a changé depuis la dernière calib (comparer aux `Last*CalibrationAt` + côté mémorisé), soit
  documenter l'usage de l'option TheSkyX « calibration des deux côtés du méridien ». **Trancher avec
  Alexandre** après l'investigation.

**Toujours indiquer explicitement ce qui est non vérifié** (API scripting TheSkyX peu documentée).

### 3.2 — Sélection d'une étoile guide NON saturée par critère ADU

**But** : pour le guidage, choisir une étoile bien exposée (bon SNR) mais **pas saturée** —
critère ADU plutôt que « la plus brillante ».

- Étendre `BuildAutoSelectGuideStar()` (ou créer une variante `BuildSelectGuideStarByADU()`) :
  - Réutiliser le calcul du **pic ADU** par candidat (déjà fait dans `notSat()` via `scanLine` sur
    une boîte ~2×FWHM) — le refactorer pour **renvoyer le pic ADU** au lieu d'un simple booléen.
  - Conserver les filtres existants (marge de bord, FWHM/ellipticité médiane).
  - **Nouveau critère** : garder les candidats dont le pic ADU ∈ `[minADU, maxADU]`, puis choisir
    celui dont le pic est **le plus proche de `optimumADU`** (`|pic − optimum|` minimal).
  - Si aucun candidat dans l'intervalle → retour « 0,0,N » (sélection manuelle / message clair),
    comme pour la marge de bord.
- Signature suggérée (rétro-compatible — défauts neutres) :
  ```csharp
  BuildAutoSelectGuideStar(double exposureSeconds, double minFwhmPx = 1.5, double maxFwhmPx = 20.0,
      double maxFwhmMedianFactor = 2.5, double maxEllipticityMedianFactor = 2.5,
      double saturationFraction = 0.9, int edgeMarginPx = 0,
      int minADU = 0, int maxADU = 0, int optimumADU = 0)
  ```
  - `maxADU == 0` → comportement actuel (la plus brillante non saturée).
  - `maxADU > 0` → sélection par proximité à `optimumADU` dans `[minADU, maxADU]`.
- Le flux **calibration** garde la sélection « plus brillante » ; le flux **guidage** passe les
  valeurs ADU des options.

### 3.3 — Options plugin (intervalle + ADU optimum)

Ajouter dans `Options/PluginOptions.cs` (+ `OptionsViewModel.cs` + `Options/Options.xaml` + valeurs
par défaut + tests de défauts) :

| Propriété | Type | Défaut (à valider avec la caméra guide) | Rôle |
|---|---|---|---|
| `GuideStarMinADU` | int | ex. 8000 | borne basse du pic ADU acceptable |
| `GuideStarMaxADU` | int | ex. 45000 | borne haute (sous la saturation) |
| `GuideStarOptimumADU` | int | ex. 25000 | cible de pic ADU (meilleur compromis SNR/saturation) |

- ⚠ Valeurs **dépendantes de la caméra guide** (profondeur de bits, gain). Donner des défauts 16 bits
  raisonnables et expliquer à Alexandre comment les régler (voir la sortie pic ADU d'un diagnostic).
- Rappel mock : `PluginOptionsAccessor.TryGetValue()` renvoie toujours `false` avec NSubstitute →
  n'écrire que des **tests de valeurs par défaut** (cf. `CLAUDE.md` §7 / §9).

### 3.4 — Lancement du guidage

- Câbler le tout dans `TheSkyXGuider.StartGuiding()` : (1) appliquer calibration du bon côté →
  (2) `BuildAutoSelectGuideStar(...ADU...)` → si étoile valide : (3) `BuildStartGuiding()` /
  `Autoguide()`. Si pas d'étoile valide : remonter un statut clair via `IProgress<ApplicationStatus>`
  et ne pas démarrer un guidage voué à l'échec.
- Décider si la logique vit dans `StartGuiding()` ou si on implémente enfin `AutoSelectGuideStar()`
  (NINA peut l'appeler séparément selon le séquenceur) — vérifier l'ordre d'appel NINA
  (`GuiderVM` / instruction de séquence) par décompilation.

---

## 4. Fichiers concernés

- `TheSkyX/TheSkyXScriptBuilder.cs` — sélection ADU (refactor `notSat` → pic ADU), éventuel nouveau builder.
- `TheSkyX/TheSkyXGuider.cs` — `StartGuiding()` (orchestration), peut-être `AutoSelectGuideStar()`.
- `Options/PluginOptions.cs`, `Options/OptionsViewModel.cs`, `Options/Options.xaml` — options ADU + (calibration Est/Ouest si stockage des paramètres).
- `GuiderCalibrationVM.cs` — si on capture/stocke les paramètres de calibration par côté après `Calibrate(0)`.
- `NinaTheSkyX.Tests/TheSkyXScriptBuilderTests.cs` + `PluginOptionsDefaultsTests.cs` — tests structure + défauts.

---

## 5. Points d'API à investiguer (lister les résultats dans `CLAUDE.md` §7 / `TECHNICAL_STATE.md`)

1. **NINA** : champs exacts de `TelescopeInfo` (`SideOfPier`, coords, méridien) et signature précise
   de `IGuider.AutoSelectGuideStar()` / ordre d'appel par NINA (décompiler `NINA.Equipment` / `NINA.WPF.Base`).
2. **TheSkyX** : propriétés de calibration lisibles/écrivables (`SavedCalibrationTime*`,
   `AutoguiderCalibrationTime*Axis`, angle) — tester read après `Calibrate(0)` puis write-back +
   `Calibration=1` + `Autoguide()`. Confirmer si la restauration par script est possible.
3. **TheSkyX** : confirmer que le **pic ADU** via `scanLine` est exploitable pour le critère
   (échelle ADU réelle vs `BITPIX`, cas 12 bits encapsulés en 16 bits — cf. `saturationFraction`).

---

## 6. Critères d'acceptation

- Sur « Start Guiding » côté NINA, sans intervention manuelle :
  - la calibration appliquée correspond au côté du méridien de l'objet (ou recalibration/fallback
    documenté et validé) ;
  - une étoile guide **non saturée**, de pic ADU dans l'intervalle et proche de l'optimum, est
    sélectionnée (vérifiable via log `[TheSkyX]` + relecture `GuideStarX/Y`) ;
  - le guidage démarre ; si aucune étoile valide, statut clair et pas de démarrage silencieux raté.
- Build : 0 erreur (4 warnings NU1701 ignorés). Tous les tests verts (`dotnet test`).
- `CLAUDE.md` §7 et `TECHNICAL_STATE.md` mis à jour avec les résultats d'API (confirmés/infirmés),
  en datant et en marquant ce qui reste **non vérifié sur ciel**.

---

## 7. Rappels / pièges (cf. `CLAUDE.md`)

- Inventaire guide = **`ccdsoftAutoguiderImage`**, jamais `ccdsoftCameraImage`.
- `ShowInventory()` retourne un **code** (0 = OK), pas le nombre → `InventoryArray(0).length`.
- `GuideStarX/Y` : écrire en **coords capteur** (`×BinX/BinY`) ; relire pour vérifier l'effet.
- `Subframe` ('f' minuscule) ; remettre `Subframe=false` pour le guidage plein champ après tout subframe.
- GUID figé, `AssemblyTitle="TheSkyX Guider"`, `x:Key="TheSkyX Guider_Options"` (espace) — NE PAS toucher.
- Workflow : `git pull` → build → tests → `Package.ps1` → copier la DLL dans
  `%LOCALAPPDATA%\NINA\Plugins\NinaTheSkyX\` → MAJ `TECHNICAL_STATE.md` → `git commit`/`push`.
- API TheSkyX peu documentée : toujours signaler explicitement ce qui n'est pas vérifié.
