using NINA.Astrometry;
using NINA.Core.Utility;
using NinaTheSkyX.Options;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NinaTheSkyX.TheSkyX {

    // =========================================================================
    // Enum — étapes de la machine à états
    // =========================================================================

    /// <summary>
    /// Étapes séquentielles du wizard de calibration du guidage TheSkyX.
    ///
    /// Flux nominal (slew automatique réussi) :
    ///   Idle
    ///   → ConfirmStartEast              (OK → slew auto)
    ///   → SlewingToEast                 (async: NINA pointe à l'est)
    ///   → TakingImageEast               (async: prise de vue immédiate après slew)
    ///   → AwaitingImageValidationEast   (utilisateur sélectionne l'étoile dans TheSkyX, puis "Image OK")
    ///   → RunningCalibrationEast        (async: Calibrate, bloquant 2–4 min)
    ///   → AwaitingCalibrationValidationEast (Calibration OK? ou retry depuis image)
    ///   → ConfirmStartWest              (OK → slew auto)
    ///   → SlewingToWest                 (async: NINA pointe à l'ouest)
    ///   → TakingImageWest               (prise de vue immédiate)
    ///   → AwaitingImageValidationWest
    ///   → RunningCalibrationWest
    ///   → AwaitingCalibrationValidationWest
    ///   → Done
    ///
    /// Flux alternatif (monture non connectée → positionnement manuel) :
    ///   → ConfirmAfterSlewEast/West     (l'utilisateur positionne manuellement et clique OK)
    ///   → TakingImageEast/West          (suite identique au flux nominal)
    ///
    /// Note : ccdsoftAutoguider.AutoFindGuideStar() n'existe pas dans l'API de scripting TheSkyX
    /// (confirmé 2026-05-10). La sélection de l'étoile guide se fait manuellement dans la fenêtre
    /// Autoguider de TheSkyX avant de cliquer "Image OK".
    /// </summary>
    public enum CalibrationWizardStage {
        Idle,

        // ---- Côté EST -------------------------------------------------------
        /// <summary>Demande de confirmation pour démarrer (présente les prérequis).</summary>
        ConfirmStartEast,
        /// <summary>NINA pointe la monture vers 0° Dec, côté est du méridien (async).</summary>
        SlewingToEast,
        /// <summary>
        /// Fallback manuel : monture non connectée dans NINA — l'utilisateur positionne
        /// manuellement et clique OK pour déclencher la prise de vue guide.
        /// </summary>
        ConfirmAfterSlewEast,
        /// <summary>Prise de vue guide en cours (async).</summary>
        TakingImageEast,
        /// <summary>En attente de validation visuelle de l'image et sélection manuelle de l'étoile guide dans TheSkyX.</summary>
        AwaitingImageValidationEast,
        /// <summary>Calibration ccdsoftAutoguider.Calibrate() en cours (async, bloquant 2–4 min).</summary>
        RunningCalibrationEast,
        /// <summary>En attente de validation de la calibration dans TheSkyX (est).</summary>
        AwaitingCalibrationValidationEast,

        // ---- Côté OUEST -----------------------------------------------------
        /// <summary>Confirmation pour passer à la calibration ouest.</summary>
        ConfirmStartWest,
        /// <summary>NINA pointe la monture vers 0° Dec, côté ouest du méridien (async).</summary>
        SlewingToWest,
        /// <summary>
        /// Fallback manuel : monture non connectée dans NINA — l'utilisateur positionne
        /// manuellement et clique OK pour déclencher la prise de vue guide.
        /// </summary>
        ConfirmAfterSlewWest,
        /// <summary>Prise de vue guide en cours (async).</summary>
        TakingImageWest,
        /// <summary>En attente de validation visuelle de l'image et sélection manuelle de l'étoile guide dans TheSkyX.</summary>
        AwaitingImageValidationWest,
        /// <summary>Calibration ccdsoftAutoguider.Calibrate() en cours (async, bloquant 2–4 min).</summary>
        RunningCalibrationWest,
        /// <summary>En attente de validation de la calibration dans TheSkyX (ouest).</summary>
        AwaitingCalibrationValidationWest,

        // ---- États terminaux ------------------------------------------------
        /// <summary>Wizard terminé avec succès (Est + Ouest calibrés).</summary>
        Done,
        /// <summary>Wizard annulé par l'utilisateur.</summary>
        Cancelled,
        /// <summary>Erreur lors d'une étape — détails dans StatusText.</summary>
        Error
    }

    // =========================================================================
    // ViewModel
    // =========================================================================

    /// <summary>
    /// ViewModel du wizard de calibration du guidage TheSkyX.
    ///
    /// Pilote la machine à états <see cref="CalibrationWizardStage"/> avec
    /// des opérations async non-bloquantes pour l'UI NINA.
    ///
    /// Pointage monture : utilise <see cref="Plugin.TelescopeMediator"/> pour
    /// slewer automatiquement à 0° Dec, 2h à l'est ou à l'ouest du méridien.
    /// Si le pointage automatique réussit, la prise de vue guide est déclenchée
    /// immédiatement sans confirmation supplémentaire. Si le telescope n'est
    /// pas connecté dans NINA, un message s'affiche (ConfirmAfterSlew*) et
    /// l'utilisateur peut positionner manuellement avant de cliquer OK.
    ///
    /// Calibration TheSkyX : <c>ccdsoftAutoguider.Autoguide()</c> est bloquant
    /// côté serveur JS — le client TCP utilise un timeout de 10 min.
    /// </summary>
    public class GuiderCalibrationVM : BaseINPC {

        // ---- Constantes -----------------------------------------------------

        /// <summary>Timeout TCP étendu pour Calibrate() bloquant pendant la calibration.</summary>
        private static readonly TimeSpan CalibrationTimeout = TimeSpan.FromMinutes(10);

        /// <summary>Amplitude du jog RA quand l'image est rejetée (arc-secondes).</summary>
        private const double JogArcsec = 300.0;

        /// <summary>Décalage en heures par rapport au méridien pour le pointage de calibration.</summary>
        private const double MeridianOffsetHours = 2.0;

        // ---- Champs ---------------------------------------------------------

        private readonly PluginOptions _options;
        private CancellationTokenSource _cts;

        /// <summary>
        /// Info sur l'étoile guide lue juste avant de lancer Calibrate()
        /// (coordonnées GuideStarX/Y lues depuis TheSkyX). Incluse dans le
        /// message de statut pendant la calibration pour confirmation visuelle.
        /// Vide si la lecture a échoué.
        /// </summary>
        private string _guideStarInfo = string.Empty;

        // ---- Constructeur ---------------------------------------------------

        public GuiderCalibrationVM(PluginOptions options) {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Toutes les lambdas async créent des delegates async void (fire-and-forget)
            // qui est le pattern correct pour ICommand WPF — les exceptions sont
            // interceptées dans chaque méthode async et converties en Error state.

            StartCommand = new RelayCommand(
                async _ => await StartAsync(),
                _ => Stage == CalibrationWizardStage.Idle
                  || Stage == CalibrationWizardStage.Done
                  || Stage == CalibrationWizardStage.Cancelled
                  || Stage == CalibrationWizardStage.Error);

            ConfirmCommand = new RelayCommand(
                async _ => await OnConfirmAsync(),
                _ => ShowConfirmButton && !IsBusy);

            ImageOkCommand = new RelayCommand(
                async _ => await OnImageOkAsync(),
                _ => Stage == CalibrationWizardStage.AwaitingImageValidationEast
                  || Stage == CalibrationWizardStage.AwaitingImageValidationWest);

            JogAndRetryCommand = new RelayCommand(
                async _ => await JogAndRetryAsync(),
                _ => Stage == CalibrationWizardStage.AwaitingImageValidationEast
                  || Stage == CalibrationWizardStage.AwaitingImageValidationWest);

            CalibrationOkCommand = new RelayCommand(
                _ => OnCalibrationOk(),
                _ => Stage == CalibrationWizardStage.AwaitingCalibrationValidationEast
                  || Stage == CalibrationWizardStage.AwaitingCalibrationValidationWest);

            RetryCalibrationCommand = new RelayCommand(
                async _ => await RetryCalibrationAsync(),
                _ => Stage == CalibrationWizardStage.AwaitingCalibrationValidationEast
                  || Stage == CalibrationWizardStage.AwaitingCalibrationValidationWest);

            CancelCommand = new RelayCommand(
                _ => Cancel(),
                _ => Stage != CalibrationWizardStage.Idle
                  && Stage != CalibrationWizardStage.Done
                  && Stage != CalibrationWizardStage.Error
                  && Stage != CalibrationWizardStage.Cancelled);

            SetStage(CalibrationWizardStage.Idle,
                "Cliquez '🎯 Calibrer guidage' pour démarrer la procédure de calibration guidage Est + Ouest.");
        }

        // =========================================================================
        // Propriétés d'état
        // =========================================================================

        private CalibrationWizardStage _stage;

        /// <summary>Étape courante — bindée pour contrôler la visibilité des boutons.</summary>
        public CalibrationWizardStage Stage {
            get => _stage;
            private set {
                _stage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ShowStartButton));
                RaisePropertyChanged(nameof(ShowConfirmButton));
                RaisePropertyChanged(nameof(ConfirmButtonContent));
                RaisePropertyChanged(nameof(ShowImageValidationButtons));
                RaisePropertyChanged(nameof(ShowCalibValidationButtons));
                RaisePropertyChanged(nameof(IsBusy));
                RaisePropertyChanged(nameof(ShowCancelButton));
            }
        }

        private string _statusText;

        /// <summary>Message d'état affiché dans l'UI à chaque étape (multiligne).</summary>
        public string StatusText {
            get => _statusText;
            private set { _statusText = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// Durée d'exposition (secondes) des images de calibration (preview + Calibrate).
        /// Bindée sur le slider dans Options.xaml — persiste via PluginOptions.
        /// </summary>
        public double CalibrationExposureSeconds {
            get => _options.TheSkyXCalibrationExposureSeconds;
            set {
                _options.TheSkyXCalibrationExposureSeconds = value;
                RaisePropertyChanged();
            }
        }

        // =========================================================================
        // Propriétés dérivées pour le binding XAML
        // =========================================================================

        /// <summary>Afficher le bouton "🎯 Calibrer guidage".</summary>
        public bool ShowStartButton =>
            Stage == CalibrationWizardStage.Idle
            || Stage == CalibrationWizardStage.Done
            || Stage == CalibrationWizardStage.Cancelled
            || Stage == CalibrationWizardStage.Error;

        /// <summary>
        /// Afficher le bouton de confirmation générique (OK / Continuer).
        /// Couvre ConfirmStart* (démarrage wizard) et ConfirmAfterSlew* (fallback
        /// positionnement manuel si monture non connectée dans NINA).
        /// </summary>
        public bool ShowConfirmButton =>
            Stage == CalibrationWizardStage.ConfirmStartEast
            || Stage == CalibrationWizardStage.ConfirmAfterSlewEast
            || Stage == CalibrationWizardStage.ConfirmStartWest
            || Stage == CalibrationWizardStage.ConfirmAfterSlewWest;

        /// <summary>Texte contextuel du bouton de confirmation selon l'étape courante.</summary>
        public string ConfirmButtonContent => _stage switch {
            CalibrationWizardStage.ConfirmStartEast     => "▶ OK — Démarrer (pointer à l'est automatiquement)",
            CalibrationWizardStage.ConfirmAfterSlewEast => "▶ OK — Monture en position (prendre l'image guide)",
            CalibrationWizardStage.ConfirmStartWest     => "▶ OK — Pointer à l'ouest automatiquement",
            CalibrationWizardStage.ConfirmAfterSlewWest => "▶ OK — Monture en position (prendre l'image guide)",
            _                                           => "▶ OK"
        };

        /// <summary>Afficher les boutons "✅ Image OK" et "❌ Déplacer en RA".</summary>
        public bool ShowImageValidationButtons =>
            Stage == CalibrationWizardStage.AwaitingImageValidationEast
            || Stage == CalibrationWizardStage.AwaitingImageValidationWest;

        /// <summary>Afficher les boutons "✅ Calibration OK" et "🔄 Recommencer".</summary>
        public bool ShowCalibValidationButtons =>
            Stage == CalibrationWizardStage.AwaitingCalibrationValidationEast
            || Stage == CalibrationWizardStage.AwaitingCalibrationValidationWest;

        /// <summary>
        /// True pendant les opérations async (slew, prise de vue, calibration).
        /// Désactive ConfirmCommand.
        /// </summary>
        public bool IsBusy =>
            Stage == CalibrationWizardStage.SlewingToEast
            || Stage == CalibrationWizardStage.SlewingToWest
            || Stage == CalibrationWizardStage.TakingImageEast
            || Stage == CalibrationWizardStage.TakingImageWest
            || Stage == CalibrationWizardStage.RunningCalibrationEast
            || Stage == CalibrationWizardStage.RunningCalibrationWest;

        /// <summary>Afficher le bouton "⏹ Annuler".</summary>
        public bool ShowCancelButton =>
            Stage != CalibrationWizardStage.Idle
            && Stage != CalibrationWizardStage.Done
            && Stage != CalibrationWizardStage.Cancelled
            && Stage != CalibrationWizardStage.Error;

        /// <summary>
        /// Résumé des dernières calibrations persistées, affiché en bas du panneau.
        /// </summary>
        public string LastCalibrationInfo {
            get {
                var east = _options.LastEastCalibrationAt.HasValue
                    ? _options.LastEastCalibrationAt.Value.ToString("dd/MM/yyyy HH:mm")
                    : "jamais";
                var west = _options.LastWestCalibrationAt.HasValue
                    ? _options.LastWestCalibrationAt.Value.ToString("dd/MM/yyyy HH:mm")
                    : "jamais";
                return $"Dernière calibration — Est : {east}  |  Ouest : {west}";
            }
        }

        // =========================================================================
        // Commands
        // =========================================================================

        /// <summary>Démarre le wizard (affiche la confirmation de départ).</summary>
        public ICommand StartCommand { get; }

        /// <summary>
        /// Confirmation générique "OK" — déclenche l'action suivante selon le stage courant :
        /// slew, ou prise de vue (fallback manuel uniquement).
        /// </summary>
        public ICommand ConfirmCommand { get; }

        /// <summary>Image guide validée + étoile sélectionnée → lancer la calibration.</summary>
        public ICommand ImageOkCommand { get; }

        /// <summary>Image rejetée → jog 5' en RA puis retake.</summary>
        public ICommand JogAndRetryCommand { get; }

        /// <summary>Calibration validée → enregistrer et passer au côté suivant (ou Done).</summary>
        public ICommand CalibrationOkCommand { get; }

        /// <summary>Calibration rejetée → recommencer depuis la prise de vue guide.</summary>
        public ICommand RetryCalibrationCommand { get; }

        /// <summary>Annuler le wizard en cours.</summary>
        public ICommand CancelCommand { get; }

        // =========================================================================
        // Transitions de la machine à états
        // =========================================================================

        /// <summary>
        /// Initialise le wizard : crée un CancellationToken et passe au premier
        /// stage de confirmation.
        /// </summary>
        private Task StartAsync() {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var expSec = _options.TheSkyXCalibrationExposureSeconds;

            SetStage(CalibrationWizardStage.ConfirmStartEast,
                "🎯 Calibration du guidage TheSkyX — Est + Ouest\n\n" +
                "Déroulement :\n" +
                $"  1. NINA pointe la monture à l'est du méridien (0° Dec, 2h est)\n" +
                $"  2. Prise de vue guide {expSec:F0} s automatique — image visible dans TheSkyX\n" +
                "  3. Cliquez l'étoile guide dans TheSkyX, puis 'Image OK'\n" +
                "  4. Calibration TheSkyX (~2-4 min) → vous validez le résultat\n" +
                "  5. NINA pointe à l'ouest → mêmes étapes 2, 3 et 4\n\n" +
                "Prérequis :\n" +
                "  • TheSkyX lancé, serveur de scripting activé (Tools → Run Java Script)\n" +
                "  • Caméra guide et monture connectées dans TheSkyX\n" +
                "  • Monture connectée dans NINA (pour le pointage automatique)\n\n" +
                "Cliquez '▶ OK' pour démarrer — la monture va pointer automatiquement.");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Dispatch de la confirmation générique selon le stage courant.
        /// Les stages ConfirmAfterSlew* ne sont atteints qu'en fallback manuel
        /// (monture non connectée dans NINA).
        /// </summary>
        private async Task OnConfirmAsync() {
            var ct = _cts?.Token ?? CancellationToken.None;
            switch (_stage) {
                case CalibrationWizardStage.ConfirmStartEast:
                    await SlewToCalibrationPositionAsync(CalibrationSide.East, ct);
                    break;
                case CalibrationWizardStage.ConfirmAfterSlewEast:
                    await TakeGuideImageAsync(
                        CalibrationWizardStage.TakingImageEast,
                        CalibrationWizardStage.AwaitingImageValidationEast, ct);
                    break;
                case CalibrationWizardStage.ConfirmStartWest:
                    await SlewToCalibrationPositionAsync(CalibrationSide.West, ct);
                    break;
                case CalibrationWizardStage.ConfirmAfterSlewWest:
                    await TakeGuideImageAsync(
                        CalibrationWizardStage.TakingImageWest,
                        CalibrationWizardStage.AwaitingImageValidationWest, ct);
                    break;
            }
        }

        /// <summary>
        /// Image validée. Avant de lancer la calibration, lit les coordonnées
        /// GuideStarX/Y depuis TheSkyX pour confirmer qu'une étoile a bien été
        /// sélectionnée (clic dans la fenêtre Autoguider). Affiche un avertissement
        /// si les coordonnées sont (0,0) — signe qu'aucune étoile n'a été cliquée.
        ///
        /// ⚠ Ne jamais écraser GuideStarX/Y ici : (0,0) = coin supérieur gauche
        /// = pixel chaud → calibration échoue (confirmé 2026-05-17).
        /// </summary>
        private async Task OnImageOkAsync() {
            var ct = _cts?.Token ?? CancellationToken.None;
            bool isEast = _stage == CalibrationWizardStage.AwaitingImageValidationEast;

            // Utiliser TakingImage* comme stage temporaire pendant la lecture TCP
            // (déclenche IsBusy=true, désactive les boutons de validation)
            var tempBusy = isEast ? CalibrationWizardStage.TakingImageEast
                                  : CalibrationWizardStage.TakingImageWest;

            SetStage(tempBusy, "🔍 Lecture de la position de l'étoile sélectionnée dans TheSkyX...");

            _guideStarInfo = string.Empty;
            try {
                var raw = await CreateClient().ExecuteAsync(
                    TheSkyXScriptBuilder.BuildReadGuideStarPosition(), ct);

                var parts = raw.Split(',');
                if (parts.Length == 2
                    && double.TryParse(parts[0].Trim(), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out var gx)
                    && double.TryParse(parts[1].Trim(), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out var gy)) {
                    if (gx < 1.0 && gy < 1.0) {
                        _guideStarInfo =
                            "⚠ GuideStarX/Y ≈ (0, 0) — aucune étoile sélectionnée !\n" +
                            "La calibration risque de cibler le coin supérieur gauche (pixel chaud).\n" +
                            "Cliquez une étoile dans la fenêtre Autoguider de TheSkyX, puis réessayez.\n\n";
                    } else {
                        _guideStarInfo = $"📍 Étoile sélectionnée : X = {gx:F1} px,  Y = {gy:F1} px\n\n";
                    }
                }
            } catch (OperationCanceledException) {
                SetStage(CalibrationWizardStage.Cancelled, "⏹ Annulé.");
                return;
            } catch (Exception ex) {
                // Non critique — on continue sans les coordonnées
                Logger.Warning($"[TheSkyX] Lecture GuideStarX/Y échouée (non critique) : {ex.Message}");
            }

            var runningStage = isEast ? CalibrationWizardStage.RunningCalibrationEast
                                      : CalibrationWizardStage.RunningCalibrationWest;
            var awaitingStage = isEast ? CalibrationWizardStage.AwaitingCalibrationValidationEast
                                       : CalibrationWizardStage.AwaitingCalibrationValidationWest;
            await RunCalibrationAsync(runningStage, awaitingStage, ct);
        }

        /// <summary>
        /// Image rejetée : jog de 5' en RA dans TheSkyX, puis retake.
        /// </summary>
        private async Task JogAndRetryAsync() {
            var ct = _cts?.Token ?? CancellationToken.None;

            var takingStage   = _stage == CalibrationWizardStage.AwaitingImageValidationEast
                                ? CalibrationWizardStage.TakingImageEast
                                : CalibrationWizardStage.TakingImageWest;
            var awaitingStage = _stage == CalibrationWizardStage.AwaitingImageValidationEast
                                ? CalibrationWizardStage.AwaitingImageValidationEast
                                : CalibrationWizardStage.AwaitingImageValidationWest;

            SetStage(takingStage,
                $"↔ Déplacement de la monture de {JogArcsec:F0}\" (~5') en RA...");
            try {
                await CreateClient().ExecuteAsync(TheSkyXScriptBuilder.BuildJogRA(JogArcsec), ct);
            } catch (OperationCanceledException) {
                SetStage(CalibrationWizardStage.Cancelled, "⏹ Annulé pendant le déplacement.");
                return;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] Jog monture échoué", ex);
                SetStage(CalibrationWizardStage.Error,
                    $"⚠ Échec du déplacement monture : {ex.Message}\n" +
                    "Vérifiez que TheSkyX est connecté à la monture (sky6RASCOMTele).");
                return;
            }

            await TakeGuideImageAsync(takingStage, awaitingStage, ct);
        }

        /// <summary>
        /// Calibration validée : enregistre la date et avance vers le côté
        /// suivant ou Done.
        /// </summary>
        private void OnCalibrationOk() {
            if (_stage == CalibrationWizardStage.AwaitingCalibrationValidationEast) {
                _options.LastEastCalibrationAt = DateTime.Now;
                RaisePropertyChanged(nameof(LastCalibrationInfo));
                Logger.Info(
                    $"[TheSkyX] Calibration EST enregistrée : {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");

                SetStage(CalibrationWizardStage.ConfirmStartWest,
                    "✅ Calibration EST enregistrée avec succès !\n\n" +
                    "Passez maintenant à la calibration côté OUEST.\n" +
                    "NINA va pointer la monture vers 0° Dec, côté ouest du méridien.\n" +
                    "(Si votre monture nécessite un Meridian Flip, il sera effectué automatiquement.)\n\n" +
                    "Cliquez '▶ OK' pour démarrer le pointage vers l'ouest.");

            } else if (_stage == CalibrationWizardStage.AwaitingCalibrationValidationWest) {
                _options.LastWestCalibrationAt = DateTime.Now;
                RaisePropertyChanged(nameof(LastCalibrationInfo));
                Logger.Info(
                    $"[TheSkyX] Calibration OUEST enregistrée : {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");

                SetStage(CalibrationWizardStage.Done,
                    "✅ Calibration terminée avec succès !\n\n" +
                    $"Est  : {_options.LastEastCalibrationAt:dd/MM/yyyy HH:mm}\n" +
                    $"Ouest: {_options.LastWestCalibrationAt:dd/MM/yyyy HH:mm}\n\n" +
                    "TheSkyX a sauvegardé les données de calibration pour les deux côtés.\n" +
                    "Le guidage est prêt : connectez le guideur dans l'onglet Equipment de NINA, " +
                    "puis démarrez le guidage normalement (Start Guiding).");
            }
        }

        /// <summary>
        /// Calibration rejetée : recommence depuis la prise de vue guide pour
        /// le côté courant.
        /// </summary>
        private async Task RetryCalibrationAsync() {
            var ct = _cts?.Token ?? CancellationToken.None;
            if (_stage == CalibrationWizardStage.AwaitingCalibrationValidationEast)
                await TakeGuideImageAsync(
                    CalibrationWizardStage.TakingImageEast,
                    CalibrationWizardStage.AwaitingImageValidationEast, ct);
            else if (_stage == CalibrationWizardStage.AwaitingCalibrationValidationWest)
                await TakeGuideImageAsync(
                    CalibrationWizardStage.TakingImageWest,
                    CalibrationWizardStage.AwaitingImageValidationWest, ct);
        }

        /// <summary>Annule le wizard. Si une opération tourne dans TheSkyX, stopper manuellement.</summary>
        public void Cancel() {
            _cts?.Cancel();
            SetStage(CalibrationWizardStage.Cancelled,
                "⏹ Calibration annulée.\n" +
                "Si une calibration ou un guidage était en cours dans TheSkyX, " +
                "cliquez sur 'Stop' dans la fenêtre Autoguider.");
            Logger.Info("[TheSkyX] Wizard de calibration guidage annulé par l'utilisateur.");
        }

        // =========================================================================
        // Opérations async
        // =========================================================================

        /// <summary>
        /// Pointe la monture vers 0° de déclinaison, à <see cref="MeridianOffsetHours"/>
        /// heures à l'est ou à l'ouest du méridien, via <see cref="Plugin.TelescopeMediator"/>.
        ///
        /// Flux nominal : si le slew réussit, la prise de vue guide est déclenchée
        /// directement sans confirmation supplémentaire.
        ///
        /// Flux alternatif : si le telescope n'est pas connecté dans NINA, affiche un
        /// message explicite et passe au stage <c>ConfirmAfterSlew*</c> pour un
        /// positionnement manuel par l'utilisateur.
        /// </summary>
        private async Task SlewToCalibrationPositionAsync(
            CalibrationSide side,
            CancellationToken ct) {

            var busyStage    = side == CalibrationSide.East
                               ? CalibrationWizardStage.SlewingToEast
                               : CalibrationWizardStage.SlewingToWest;
            var confirmStage = side == CalibrationSide.East
                               ? CalibrationWizardStage.ConfirmAfterSlewEast
                               : CalibrationWizardStage.ConfirmAfterSlewWest;
            var takingStage  = side == CalibrationSide.East
                               ? CalibrationWizardStage.TakingImageEast
                               : CalibrationWizardStage.TakingImageWest;
            var awaitingStage = side == CalibrationSide.East
                                ? CalibrationWizardStage.AwaitingImageValidationEast
                                : CalibrationWizardStage.AwaitingImageValidationWest;
            var sideStr      = side == CalibrationSide.East ? "est" : "ouest";

            Logger.Info($"[TheSkyX] SlewToCalibrationPosition — démarrage côté {sideStr}.");
            SetStage(busyStage,
                $"🔭 Pointage vers 0° Dec, côté {sideStr} du méridien (+{MeridianOffsetHours}h)...\n" +
                "(Logs détaillés dans %APPDATA%\\NINA\\Logs\\)");

            var telescopeMediator = Plugin.TelescopeMediator;
            if (telescopeMediator == null) {
                Logger.Error("[TheSkyX] Plugin.TelescopeMediator est null — " +
                             "ITelescopeMediator non injecté dans Plugin.cs.");
                SetStage(confirmStage,
                    $"⚠ Monture non disponible (médiateur null — voir log NINA).\n\n" +
                    $"Pointez manuellement la monture côté {sideStr} du méridien, " +
                    "à 0° de déclinaison.\nCliquez '▶ OK' quand la monture est stabilisée.");
                return;
            }

            var info = telescopeMediator.GetInfo();
            Logger.Info($"[TheSkyX] TelescopeInfo — Connected={info.Connected}, " +
                        $"Name={info.Name}, SiderealTime={info.SiderealTime:F4}h.");

            if (!info.Connected) {
                Logger.Warning(
                    $"[TheSkyX] Monture non connectée dans NINA (TelescopeInfo.Connected=false). " +
                    $"Basculement en positionnement manuel ({sideStr}).");
                SetStage(confirmStage,
                    $"⚠ Monture non connectée dans NINA.\n\n" +
                    $"Connectez la monture dans NINA (onglet Equipment → Telescope), " +
                    $"puis relancez le wizard.\n\n" +
                    $"Ou pointez manuellement côté {sideStr} du méridien, " +
                    "à 0° de déclinaison, et cliquez '▶ OK'.");
                return;
            }

            try {
                double lst     = info.SiderealTime;

                // RA cible = LST ± 2h (Est : LST+2h, Ouest : LST−2h)
                double offsetH  = side == CalibrationSide.East ? +MeridianOffsetHours : -MeridianOffsetHours;
                double targetRa = ((lst + offsetH) % 24.0 + 24.0) % 24.0;

                Logger.Info($"[TheSkyX] LST={lst:F4}h, offset={offsetH:+0.0;-0.0}h, " +
                            $"RA cible={targetRa:F4}h ({targetRa * 15.0:F2}°), Dec=0°.");

                var coords = new Coordinates(targetRa, 0.0, Epoch.JNOW, Coordinates.RAType.Hours);

                Logger.Info($"[TheSkyX] Appel SlewToCoordinatesAsync — " +
                            $"RA={coords.RAString}, Dec={coords.DecString}, Epoch=JNOW...");

                bool ok = await telescopeMediator.SlewToCoordinatesAsync(coords, ct);

                Logger.Info($"[TheSkyX] SlewToCoordinatesAsync retourné : ok={ok}.");

                if (!ok) {
                    SetStage(CalibrationWizardStage.Error,
                        $"⚠ Pointage côté {sideStr} refusé par NINA (SlewToCoordinatesAsync=false).\n\n" +
                        "Causes possibles :\n" +
                        "  • Limite de sécurité de la monture atteinte\n" +
                        "  • Meridian Flip désactivé et monture à l'ouest du méridien\n" +
                        "  • Objet sous l'horizon (altitude < 0°)\n\n" +
                        "Consultez le log NINA pour plus de détails.");
                    return;
                }

                // Flux nominal : slew réussi → prise de vue automatique sans confirmation
                Logger.Info($"[TheSkyX] Pointage {sideStr} terminé. Prise de vue guide automatique.");
                await TakeGuideImageAsync(takingStage, awaitingStage, ct);

            } catch (OperationCanceledException) {
                Logger.Info("[TheSkyX] Pointage annulé par l'utilisateur.");
                SetStage(CalibrationWizardStage.Cancelled, "⏹ Pointage annulé.");
            } catch (Exception ex) {
                Logger.Error($"[TheSkyX] Pointage monture ({sideStr}) — exception : {ex.GetType().Name} : {ex.Message}", ex);
                SetStage(CalibrationWizardStage.Error,
                    $"⚠ Erreur lors du pointage monture : {ex.Message}\n\n" +
                    "Consultez le log NINA (%APPDATA%\\NINA\\Logs\\) pour le détail.\n\n" +
                    "Vérifiez la connexion de la monture dans NINA (onglet Equipment → Telescope).\n" +
                    "Vous pouvez aussi positionner manuellement et relancer le wizard.");
            }
        }

        /// <summary>
        /// Prend une image avec la caméra guide via TheSkyX et tente de sélectionner
        /// automatiquement l'étoile guide la plus brillante dans la plage FWHM valide.
        ///
        /// Commence par connecter explicitement <c>ccdsoftAutoguider</c> via
        /// <see cref="TheSkyXScriptBuilder.BuildConnect"/> avant d'appeler
        /// <c>TakeImage()</c>. L'appel <c>Connect()</c> est idempotent dans TheSkyX :
        /// sans effet si l'autoguider est déjà connecté, erreur claire s'il ne l'est pas.
        ///
        /// Ce Connect() explicite est nécessaire car le wizard crée ses propres clients
        /// TCP indépendamment de <c>TheSkyXGuider</c> (l'onglet Equipment → Guider
        /// de NINA peut être déconnecté pendant la calibration).
        ///
        /// Utilise <see cref="TheSkyXScriptBuilder.BuildAutoSelectGuideStar"/> :
        /// active temporairement AutoSaveOn pour que ShowInventory() puisse analyser
        /// le champ, sélectionne l'étoile la plus brillante dans la plage FWHM
        /// [1.5–20 px], écrit GuideStarX/Y si une étoile valide est trouvée.
        ///
        /// Résultat parsé "X,Y,N" :
        ///   X/Y > 1  → étoile auto-sélectionnée (GuideStarX/Y mis à jour dans TheSkyX).
        ///   0,0,0   → champ vide.
        ///   0,0,N   → N étoiles détectées, aucune dans la plage FWHM → sélection manuelle requise.
        ///
        /// <b>⚠ Non vérifié sur ciel réel (2026-05-23)</b> : l'écriture de GuideStarX/Y
        /// et ShowInventory/InventoryArray en contexte autoguider doivent être confirmés.
        /// </summary>
        private async Task TakeGuideImageAsync(
            CalibrationWizardStage busyStage,
            CalibrationWizardStage nextStage,
            CancellationToken ct) {

            var expSec = _options.TheSkyXCalibrationExposureSeconds;
            SetStage(busyStage,
                $"📷 Prise de vue guide ({expSec:F0} s) + sélection automatique étoile...\n" +
                "L'image sera visible dans la fenêtre Autoguider de TheSkyX.");
            try {
                // Connexion explicite avant TakeImage() — indispensable si le guider
                // n'est pas connecté via l'onglet Equipment de NINA (wizard indépendant).
                // Idempotent : sans effet si ccdsoftAutoguider est déjà connecté.
                await CreateClient().ExecuteAsync(TheSkyXScriptBuilder.BuildConnect(), ct);

                // Prise de vue + analyse du champ + sélection automatique de l'étoile guide.
                // BuildAutoSelectGuideStar active AutoSaveOn temporairement (requis par
                // ShowInventory pour lire le fichier image sauvegardé sur disque).
                var raw = await CreateClient().ExecuteAsync(
                    TheSkyXScriptBuilder.BuildAutoSelectGuideStar(expSec), ct);

                // Parse le résultat "X,Y,N" et construit le message de statut contextuel.
                string autoSelectMsg;
                var parts = raw?.Split(',');
                if (parts != null && parts.Length >= 3
                    && double.TryParse(parts[0].Trim(), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out var gx)
                    && double.TryParse(parts[1].Trim(), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out var gy)
                    && int.TryParse(parts[2].Trim(), out var nStars)) {

                    if (gx > 1.0 || gy > 1.0) {
                        // Étoile valide trouvée — GuideStarX/Y déjà écrits dans TheSkyX
                        Logger.Info($"[TheSkyX] Auto-sélection étoile guide : X={gx:F1}, Y={gy:F1} ({nStars} étoile(s) dans le champ).");
                        autoSelectMsg =
                            $"⭐ Étoile guide sélectionnée automatiquement : X = {gx:F1} px,  Y = {gy:F1} px " +
                            $"({nStars} étoile(s) dans le champ).\n" +
                            "Vérifiez visuellement dans la fenêtre Autoguider de TheSkyX.\n" +
                            "Vous pouvez cliquer une autre étoile si nécessaire.\n\n";
                    } else if (nStars == 0) {
                        Logger.Warning("[TheSkyX] Auto-sélection : aucune étoile détectée dans le champ.");
                        autoSelectMsg =
                            "⚠ Aucune étoile détectée dans le champ.\n" +
                            "Vérifiez le temps de pose et la mise au point de la caméra guide.\n" +
                            "Cliquez manuellement une étoile dans la fenêtre Autoguider de TheSkyX.\n\n";
                    } else {
                        Logger.Warning($"[TheSkyX] Auto-sélection : {nStars} étoile(s) trouvée(s) mais FWHM hors plage acceptable.");
                        autoSelectMsg =
                            $"⚠ {nStars} étoile(s) détectée(s) dans le champ mais FWHM hors plage (1.5–20 px).\n" +
                            "Cliquez manuellement une étoile dans la fenêtre Autoguider de TheSkyX.\n\n";
                    }
                } else {
                    // Résultat non parseable — autoguider peut-être non connecté ou API
                    // ShowInventory/InventoryArray non disponible dans ce contexte TSX.
                    Logger.Warning($"[TheSkyX] Auto-sélection : résultat non parseable (raw='{raw}'). Sélection manuelle requise.");
                    autoSelectMsg =
                        "⚠ Sélection automatique indisponible — réponse inattendue de TheSkyX.\n" +
                        "Cliquez manuellement une étoile dans la fenêtre Autoguider de TheSkyX.\n\n";
                }

                SetStage(nextStage,
                    "🔍 Vérifiez l'image dans la fenêtre Autoguider de TheSkyX.\n\n" +
                    autoSelectMsg +
                    "Le champ est-il exploitable ?\n" +
                    "  • Étoiles rondes (pas de coma ni de suivi raté)\n" +
                    "  • Pas de nuage ni de voile couvrant le champ\n" +
                    "  • Au moins une étoile visible et non saturée\n\n" +
                    "  ✅ Image OK + étoile confirmée → cliquez 'Image OK'\n" +
                    "  ❌ Mauvais champ → cliquez 'Déplacer en RA' (jog 5') et retenter");

            } catch (OperationCanceledException) {
                SetStage(CalibrationWizardStage.Cancelled, "⏹ Prise de vue annulée.");
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] TakeGuiderImage / AutoSelectGuideStar échoué", ex);
                SetStage(CalibrationWizardStage.Error,
                    $"⚠ Échec de la prise de vue guide : {ex.Message}\n\n" +
                    "Vérifiez :\n" +
                    "  • TheSkyX est lancé\n" +
                    "  • Serveur de scripting activé (Tools → Run Java Script)\n" +
                    "  • Caméra guide connectée dans TheSkyX\n" +
                    $"  • Hôte/port : {_options.TheSkyXHost}:{_options.TheSkyXPort}");
            }
        }

        /// <summary>
        /// Lance la calibration TheSkyX via <c>ccdsoftAutoguider.Calibrate(0)</c>.
        ///
        /// <c>Calibrate()</c> est bloquant côté serveur TheSkyX (2–4 min) mais
        /// NE démarre PAS le guidage — il retourne quand les vecteurs RA/Dec sont
        /// mesurés et sauvegardés par TheSkyX. Timeout TCP : 10 min.
        /// </summary>
        private async Task RunCalibrationAsync(
            CalibrationWizardStage busyStage,
            CalibrationWizardStage nextStage,
            CancellationToken ct) {

            var subframeInfo = _options.TheSkyXGuiderSubframeSize > 0
                ? $"Subframe {_options.TheSkyXGuiderSubframeSize}×{_options.TheSkyXGuiderSubframeSize} px activé — l'étoile sélectionnée est verrouillée.\n"
                : "Subframe désactivé (plein capteur).\n";

            SetStage(busyStage,
                "⏳ Calibration en cours dans TheSkyX...\n\n" +
                _guideStarInfo +
                subframeInfo +
                "TheSkyX déplace la monture pour mesurer les vecteurs RA et Dec.\n" +
                "Durée typique : 2–4 minutes selon la monture et le seeing.\n" +
                "Ne touchez pas à la monture ni à la caméra pendant la calibration.\n\n" +
                "Suivez la progression dans la fenêtre Autoguider de TheSkyX.");
            try {
                await CreateCalibrationClient().ExecuteAsync(
                    TheSkyXScriptBuilder.BuildCalibrateAutoguider(
                        _options.TheSkyXCalibrationExposureSeconds,
                        _options.TheSkyXGuiderSubframeSize), ct);

                Logger.Info("[TheSkyX] Calibration terminée (Calibrate() retourné). Guidage NON démarré.");

                SetStage(nextStage,
                    "🔎 Vérifiez la qualité de la calibration dans TheSkyX.\n\n" +
                    "Dans la fenêtre Autoguider, observez :\n" +
                    "  • Vecteurs RA et Dec perpendiculaires et stables\n" +
                    "  • Pas de messages d'erreur dans le log TheSkyX\n\n" +
                    "  ✅ Calibration OK → cliquez 'Calibration OK'\n" +
                    "  ❌ Calibration mauvaise → cliquez 'Recommencer' (reprend depuis la prise de vue)");
            } catch (OperationCanceledException) {
                SetStage(CalibrationWizardStage.Cancelled,
                    "⏹ Calibration annulée.\n" +
                    "La calibration partielle n'a pas été sauvegardée dans TheSkyX.");
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] Calibration guidage échouée", ex);
                SetStage(CalibrationWizardStage.Error,
                    $"⚠ Échec de la calibration : {ex.Message}\n\n" +
                    "Causes possibles :\n" +
                    "  • Caméra guide ou monture non connectée dans TheSkyX\n" +
                    "  • Timeout dépassé (calibration > 10 min)\n" +
                    "  • Erreur interne TheSkyX (consulter le log TheSkyX)\n\n" +
                    "Cliquez '🎯 Calibrer guidage' pour réessayer depuis le début.");
            }
        }

        // =========================================================================
        // Helpers privés
        // =========================================================================

        private void SetStage(CalibrationWizardStage stage, string status) {
            Stage      = stage;
            StatusText = status;
        }

        private TheSkyXTcpClient CreateClient()
            => new TheSkyXTcpClient(_options.TheSkyXHost, _options.TheSkyXPort);

        private TheSkyXTcpClient CreateCalibrationClient()
            => new TheSkyXTcpClient(_options.TheSkyXHost, _options.TheSkyXPort, CalibrationTimeout);

        // ---- Enum interne ---------------------------------------------------

        private enum CalibrationSide { East, West }
    }
}
