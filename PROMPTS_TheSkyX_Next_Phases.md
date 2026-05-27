# Prompts — Prochaines phases TheSkyX Guiding

> Chaque prompt est autonome (nouvelle session). Lire `CLAUDE.md` + `TECHNICAL_STATE.md`
> avant de commencer. Exécuter dans l'ordre recommandé.
>
> Contexte commun à tous les prompts :
> - Plugin NINA 3.x "TheSkyX Guider", C# .NET 8.0-windows, WPF, MEF
> - Namespace racine : `NinaTheSkyX`
> - GUID invariant : `c3d4e5f6-a7b8-9012-cdef-012345678912` — NE JAMAIS CHANGER
> - Build : `dotnet build -c Release` dans `C:\Astro\AstroIQ\NinaTheSkyX`
> - Tests : `dotnet test -c Release --nologo` dans `C:\Astro\AstroIQ\NinaTheSkyX.Tests`
>   Cible actuelle : 56 tests verts.
> - Documentation TheSkyX scripting :
>   `C:\Téléchargements\TheSky-Professional-Scripting-Documentation-and-Examples-ScriptTheSkyX\`
> - L'état d'avancement est dans `TECHNICAL_STATE.md` (ce dossier).
> - Ce plugin est indépendant du plugin LinearFocus (`C:\Astro\AstroIQ\NinaLinearFocus\`).

---

## Prompt A — Recherche Auto-Find Guide Star dans l'API TheSkyX

### Contexte

Le bouton "Auto-Find Guide Star" existe dans l'onglet "Autoguider" de l'interface TheSkyX.
Lors des tests du 2026-05-10, l'appel `ccdsoftAutoguider.AutoFindGuideStar()` a levé une
TypeError ("undefined is not a function"), donc la méthode est marquée `[Obsolete]` dans
`TheSkyX/TheSkyXScriptBuilder.cs`. Cependant, si le bouton existe dans l'UI TheSkyX, la
fonctionnalité doit être exposée d'une façon ou d'une autre dans le moteur de scripting —
peut-être sous un nom différent, sur un objet différent, ou via une propriété setter.

### Objectif

Trouver comment déclencher la sélection automatique de l'étoile guide via TCP scripting,
en lisant systématiquement la documentation officielle TheSkyX disponible localement.

### Étapes de recherche

1. **Lire l'index de la doc** :
   ```
   C:\Téléchargements\TheSky-Professional-Scripting-Documentation-and-Examples-ScriptTheSkyX\html\index.html
   ```
   Chercher toute classe ou méthode contenant "Find", "Auto", "GuideStar", "Select".

2. **Chercher dans toute la doc HTML** les patterns suivants :
   - `AutoFind` (case-insensitive)
   - `FindStar`, `FindGuideStar`, `SelectGuideStar`
   - `AutoGuide` properties liées à la sélection

3. **Examiner `ccdsoftAutoguider` complet** :
   ```
   C:\Téléchargements\...\html\classccdsoft_autoguider.html  (si existe)
   C:\Téléchargements\...\html\classccdsoft_camera.html      (autoguider hérite de ccdsoftCamera)
   ```
   Lister toutes les méthodes publiques. Comparer avec `classccdsoft_camera-members.html`.

4. **Chercher dans les scripts d'exemple** :
   ```
   C:\Téléchargements\...\  (tous les fichiers .js)
   ```
   Grep pour `AutoFind`, `GuideStar`, `FindStar`.

5. **Hypothèses alternatives à vérifier** :
   - `ccdsoftAutoguider.AutoFindGuideStar` est peut-être une *propriété booléenne* (setter
     `= true` au lieu d'appel `()`) — essayer `ccdsoftAutoguider.AutoFindGuideStar = true;`
   - La méthode est peut-être sur `ccdsoftCamera` avec `Autoguider=1` au lieu de
     `ccdsoftAutoguider` directement
   - Le bouton UI appelle peut-être une commande interne non exposée en scripting

### Livrable attendu

Un rapport précis indiquant :
- Si la méthode existe : son nom exact, objet, signature, et un test script TCP à exécuter.
- Si elle n'existe pas en scripting : confirmation avec la page de doc qui liste les méthodes
  disponibles, et recommandation d'alternative (ex: écrire GuideStarX/Y=0 pour forcer
  auto-sélection, déjà confirmé fonctionnel).

Si une méthode valide est trouvée :
- Mettre à jour `TheSkyX/TheSkyXScriptBuilder.cs` : remplacer ou compléter
  `BuildAutoFindGuideStar()` (retirer `[Obsolete]` ou ajouter une nouvelle méthode).
- Ajouter les tests xUnit dans `NinaTheSkyX.Tests/TheSkyXScriptBuilderTests.cs`.
- Mettre à jour `TECHNICAL_STATE.md` (table "API TheSkyX — Confirmée / Infirmée").

---

## Prompt B — Corriger le message de status NINA "démarrage du guidage..." permanent

### Contexte

Quand le guidage est démarré depuis NINA (Equipment → Guider → Start Guiding), la barre
d'état de NINA affiche en permanence :

> `guider : TheSkyX : démarrage du guidage...`

Le message ne disparaît jamais, même quand le guidage est actif et stable. Cela suggère que
`TheSkyXGuider` ne signale pas correctement à NINA la transition "guidage démarré" → "guidage
en cours" → "guidage arrêté".

### Fichiers concernés

```
C:\Astro\AstroIQ\NinaTheSkyX\TheSkyX\TheSkyXGuider.cs
```

### Investigation à mener

1. **Décompiler `IGuider`** pour comprendre le contrat complet :
   ```bash
   export PATH="$PATH:$HOME/.dotnet/tools"
   ilspycmd -t "NINA.Equipment.Interfaces.IGuider" \
     /c/Users/alexa/.nuget/packages/nina.equipment/3.1.0.9001/lib/net8.0-windows7.0/NINA.Equipment.dll
   ```
   Chercher : propriétés de state, événements, méthodes asynchrones liées au status.

2. **Décompiler l'implémentation PHD2** (guider de référence NINA) pour voir comment elle
   signale les transitions d'état :
   ```bash
   ilspycmd -t "NINA.Equipment.Equipment.MyGuider.PHD2Guider" \
     /c/Users/alexa/.nuget/packages/nina.wpf.base/3.1.0.9001/lib/net8.0-windows7.0/NINA.WPF.Base.dll
   ```
   (ou chercher dans `NINA.Equipment.dll` selon où PHD2Guider est défini)

3. **Lire `TheSkyX/TheSkyXGuider.cs`** en entier pour identifier :
   - Où est levé/géré l'état "démarrage du guidage..."
   - Si des événements `GuideEvent` / `OnGuideStopped` / `OnGuideStarted` sont fire-and-forget
   - Si `Autoguide()` est bloquant côté TCP et si le thread se bloque sans jamais signaler
     la fin du démarrage

4. **Hocus Focus** (plugin de référence) a peut-être un guider custom — chercher dans
   `https://github.com/ghilios/joko.nina.plugins/` comment il gère les états IGuider.

### Comportement attendu après fix

- Pendant le démarrage : `"TheSkyX : démarrage du guidage..."` OU un spinner NINA natif
- Guidage actif et stable : le message disparaît ou passe à `"TheSkyX : guidage actif"`
- Guidage arrêté : retour à l'état idle NINA

### Livrable

- `TheSkyX/TheSkyXGuider.cs` corrigé, buildable, avec le contrat `IGuider` respecté.
- Tests xUnit si du code pur peut être extrait (ex: parsing de réponse TCP).
- Mise à jour `TECHNICAL_STATE.md` (section "État d'intégration dans NINA").

---

## Prompt C — Récupérer le RMS de guidage TheSkyX et conditionner les poses

### Contexte

Pour la qualité des images, il ne faut lancer une pose que quand le guidage est stable.
Le critère usuel : erreur RMS de guidage < seuil configurable (défaut 3 px, range 0.1–10 px).
TheSkyX expose l'erreur de guidage instantanée via son API de scripting (`ErrorX`, `ErrorY`
sur `ccdsoftAutoguider`), mais il faut vérifier le nom exact des propriétés et s'il y a
une valeur RMS calculée ou s'il faut la calculer côté plugin.

### Étapes

**1. Identifier les propriétés d'erreur disponibles dans TheSkyX**

Lire la doc :
```
C:\Téléchargements\TheSky-Professional-Scripting-Documentation-and-Examples-ScriptTheSkyX\html\classccdsoft_camera.html
```
Chercher : `ErrorX`, `ErrorY`, `RMSX`, `RMSY`, `GuideError`, `Correction`, `RA`, `Dec`.
Confirmer le nom exact des propriétés disponibles en scripting.

**2. Ajouter `BuildReadGuideError()` dans `TheSkyX/TheSkyXScriptBuilder.cs`**

Script JS à envoyer (adapter selon propriétés confirmées) :
```javascript
Out = ccdsoftAutoguider.ErrorX + "," + ccdsoftAutoguider.ErrorY;
```
Si `RMSX`/`RMSY` existent, préférer ces valeurs (déjà moyennées sur N images).
Si seulement `ErrorX`/`ErrorY` (correction instantanée), le plugin devra faire une moyenne
glissante sur les N dernières valeurs.

**3. Ajouter `GuidingRmsThreshold` dans `Options/PluginOptions.cs`**

```csharp
/// <summary>
/// Seuil d'erreur RMS de guidage (pixels) en dessous duquel une pose peut être lancée.
/// Range : 0.1–10.0 px. Défaut : 3.0 px.
/// </summary>
public double GuidingRmsThreshold {
    get => _accessor.GetValueDouble(nameof(GuidingRmsThreshold), 3.0);
    set => _accessor.SetValueDouble(nameof(GuidingRmsThreshold),
               Math.Max(0.1, Math.Min(10.0, value)));
}
```

Exposer dans `Options/OptionsViewModel.cs` (propriété bindable) et dans `Options/Options.xaml`
(slider 0.1–10 avec pas 0.1, label "Seuil RMS guidage (px)").

**4. Implémenter la logique de polling dans `TheSkyX/TheSkyXGuider.cs`**

Méthode `WaitForStableGuidingAsync(CancellationToken ct)` :
- Boucle avec intervalle 500 ms.
- Lit `BuildReadGuideError()` via TCP.
- Calcule RMS = sqrt(ErrorX² + ErrorY²) (ou utilise RMSX/Y si disponibles).
- Retourne quand RMS < `_options.GuidingRmsThreshold` pendant N mesures consécutives
  (N = 3, non configurable — évite un spike fugace).
- Timeout configurable (défaut : 30 s). Si timeout → `Logger.Warning` + retour quand même
  (ne pas bloquer indéfiniment une séquence).

**5. Intégration avec NINA**

Vérifier si `IGuider` a un mécanisme standard pour signaler "guidage stable" à NINA
(décompiler avec ilspycmd). Si oui, utiliser ce mécanisme. Sinon, la logique reste interne
au plugin.

**6. Tests xUnit**

Dans `NinaTheSkyX.Tests/TheSkyXScriptBuilderTests.cs` :
```csharp
[Fact] BuildReadGuideError_ContainsErrorXAndY()
[Fact] BuildReadGuideError_OutputFormat_IsCommaSeparated()
```

Dans un nouveau `NinaTheSkyX.Tests/TheSkyXGuiderRmsTests.cs` si du code pur peut être extrait
(ex: parsing de la réponse CSV, calcul RMS, logique seuil).

### Livrable

- `TheSkyX/TheSkyXScriptBuilder.cs` : `BuildReadGuideError()`
- `Options/PluginOptions.cs` : `GuidingRmsThreshold`
- `Options/OptionsViewModel.cs` + `Options/Options.xaml` : slider seuil RMS
- `TheSkyX/TheSkyXGuider.cs` : `WaitForStableGuidingAsync`
- Tests xUnit correspondants
- Mise à jour `TECHNICAL_STATE.md`

---

## Prompt D — Ajouter un bouton "Lancer Guidage" dans l'UI du plugin

### Contexte

Pour vérifier que le guidage TheSkyX fonctionne correctement après la calibration, il faut
actuellement lancer une séquence complète dans NINA. Il serait plus pratique d'avoir un bouton
"▶ Lancer Guidage" dans l'onglet Options du plugin, à côté du wizard de calibration, pour
démarrer/arrêter le guidage directement et observer le résultat.

### Périmètre fonctionnel

Le panneau de contrôle guidage dans `Options/Options.xaml` doit permettre :
- **▶ Lancer** : connecter l'autoguider (`BuildConnect`), démarrer le guidage
  (`BuildStartGuiding(exp, forceCalibration: false)`). Si calibration absente → proposer
  de recalibrer (`forceCalibration: true`).
- **⏹ Arrêter** : envoyer `BuildStopGuiding()`, puis `BuildDisconnect()`.
- **Affichage en temps réel** (polling toutes les 500 ms pendant le guidage) :
  - Erreur courante : `ErrorX = x.xx px | ErrorY = y.yy px`
  - RMS glissant sur les 20 dernières mesures : `RMS = x.xx px`
  - Indicateur coloré : 🟢 < seuil, 🟡 1–2× seuil, 🔴 > 2× seuil
    (seuil = `PluginOptions.GuidingRmsThreshold` — requiert Prompt C)

### Architecture suggérée

Nouveau `TheSkyX/GuidingControlVM.cs` (VM interne à `OptionsViewModel`, pas d'export MEF) :
```csharp
namespace NinaTheSkyX.TheSkyX {
    public class GuidingControlVM : BaseINPC {
        // IsGuiding (bool) → bascule ▶/⏹ dans le bouton
        // StatusText (string) → message d'état courant
        // ErrorDisplay (string) → "ErrorX = x.xx | ErrorY = y.yy | RMS = x.xx px"
        // RmsColor (Brush) → vert/jaune/rouge selon seuil
        // StartStopCommand (ICommand) → toggle
        // _pollingCts (CancellationTokenSource) → cancel quand stop
    }
}
```

`Options/OptionsViewModel.cs` : ajouter `GuidingControl = new GuidingControlVM(_options);`

`Options/Options.xaml` : nouveau `GroupBox "Contrôle guidage"` sous le GroupBox du wizard
de calibration, bindé sur `{Binding GuidingControl}` :
- Bouton ▶/⏹ (Content bindé sur IsGuiding)
- TextBlock `StatusText`
- TextBlock `ErrorDisplay` (monospace, Foreground = RmsColor)

### Prérequis

- Prompt C (lecture ErrorX/Y) doit être implémenté en premier.
- `PluginOptions.TheSkyXGuideExposureSeconds` (déjà présent) est utilisé pour `BuildStartGuiding`.

### Livrable

- `TheSkyX/GuidingControlVM.cs` (nouveau fichier)
- `Options/OptionsViewModel.cs` modifié
- `Options/Options.xaml` modifié
- Build propre 0 erreur dans `C:\Astro\AstroIQ\NinaTheSkyX`
- Tests xUnit si code pur extrayable (parsing polling, logique couleur)
- Mise à jour `TECHNICAL_STATE.md`

---

## Prompt E — Afficher les courbes de guidage TheSkyX dans l'onglet Autoguider de NINA

### Contexte

NINA dispose d'un onglet "Autoguider" dans la section Imaging qui affiche en temps réel
les courbes de corrections RA/Dec du guider connecté. Ces courbes sont alimentées par les
événements `IGuideStep` que l'implémentation `IGuider` doit envoyer pendant le guidage.
Actuellement, `TheSkyX/TheSkyXGuider.cs` démarre le guidage mais n'envoie aucun `IGuideStep`
→ les courbes NINA restent vides.

### Objectif

Comprendre exactement ce que NINA attend de `IGuider` pour peupler l'onglet Autoguider,
et implémenter le polling TheSkyX correspondant.

### Phase 1 — Recherche API NINA (ne pas écrire de code avant d'avoir fini cette phase)

**1a. Décompiler `IGuider`** :
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
ilspycmd -t "NINA.Equipment.Interfaces.IGuider" \
  /c/Users/alexa/.nuget/packages/nina.equipment/3.1.0.9001/lib/net8.0-windows7.0/NINA.Equipment.dll
```
Identifier :
- L'événement qui transporte les `IGuideStep` (probablement `event EventHandler<IGuideStep> GuideEvent`
  ou similaire).
- La signature complète de `IGuideStep` et ses propriétés (RADistanceRaw, DECDistanceRaw,
  RAPulse, DECPulse, etc.).

**1b. Décompiler `IGuideStep`** :
```bash
ilspycmd -t "NINA.Core.Interfaces.IGuideStep" \
  /c/Users/alexa/.nuget/packages/nina.core/3.1.0.9001/lib/net8.0-windows7.0/NINA.Core.dll
```
Lister toutes les propriétés attendues.

**1c. Chercher l'implémentation PHD2Guider** pour voir comment elle fire les événements
et construit les `IGuideStep` à partir des données brutes de PHD2.

**1d. Rechercher dans la doc TheSkyX** les propriétés de corrections disponibles :
```
C:\Téléchargements\...\html\classccdsoft_camera.html
```
Chercher : `RACorrection`, `DecCorrection`, `ErrorX`, `ErrorY`, `RMSX`, `RMSY`,
`RAGuideSpeed`, `DecGuideSpeed`. Confirmer les noms exacts disponibles en scripting.

Script de polling TheSkyX à construire (adapter selon propriétés confirmées) :
```javascript
Out = ccdsoftAutoguider.ErrorX + "," + ccdsoftAutoguider.ErrorY;
// OU si corrections disponibles :
// Out = ccdsoftAutoguider.RACorrection + "," + ccdsoftAutoguider.DecCorrection;
```

### Phase 2 — Implémentation (seulement après Phase 1 complète)

**2a. Créer une implémentation concrète de `IGuideStep`** :
```csharp
// TheSkyX/TheSkyXGuideStep.cs
namespace NinaTheSkyX.TheSkyX {
    internal class TheSkyXGuideStep : IGuideStep {
        // Peupler à partir des valeurs lues via TCP
        // Si NINA attend RA/Dec en arcsec et TheSkyX donne des pixels,
        // vérifier le facteur de conversion (arcsec/pixel = PlateScale du setup)
        // ou utiliser pixels directement si NINA accepte des unités arbitraires.
    }
}
```

**2b. Boucle de polling dans `TheSkyX/TheSkyXGuider.cs`** :
- Démarre en parallèle lors de `StartGuiding()` (task séparée + CancellationToken).
- Toutes les 500 ms : lit ErrorX/Y via TCP, construit un `TheSkyXGuideStep`, fire l'événement.
- S'arrête proprement lors de `StopGuiding()` via `CancellationTokenSource`.

**2c. Vérifier la compatibilité unités** :
TheSkyX retourne `ErrorX/Y` en pixels (unité de la caméra guide). NINA s'attend à des
arcsec ou des pixels ? Décompiler `IGuideStep` pour voir s'il y a une propriété Scale ou si
NINA convertit en interne. Si arcsec requis → il faudra une propriété configurable
`GuiderPixelScale` (arcsec/px) dans `Options/PluginOptions.cs` (calculée à partir de la
focale et du pixel size de la caméra guide — ou saisie directement).

### Livrable Phase 1 (rapport, pas de code)

Un document `RESEARCH_TheSkyX_GuiderCurves.md` dans `C:\Astro\AstroIQ\NinaTheSkyX\`,
contenant :
- Signature complète de `IGuider` et `IGuideStep` décompilées
- Liste des propriétés TheSkyX disponibles pour le polling
- Comparaison et plan de mapping (propriété TheSkyX → champ IGuideStep)
- Question ouverte sur les unités (pixels vs arcsec) avec recommandation

### Livrable Phase 2 (si Phase 1 est favorable)

- `TheSkyX/TheSkyXGuideStep.cs` (nouveau)
- `TheSkyX/TheSkyXGuider.cs` modifié (boucle polling + fire events)
- `Options/PluginOptions.cs` : `GuiderPixelScale` si nécessaire
- Tests xUnit sur `TheSkyXGuideStep` (construction, parsing) dans `NinaTheSkyX.Tests/`
- Mise à jour `TECHNICAL_STATE.md`
