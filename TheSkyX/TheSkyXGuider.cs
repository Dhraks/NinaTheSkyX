using NINA.Astrometry;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Interfaces;
using NinaTheSkyX.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NinaTheSkyX.TheSkyX {

    /// <summary>
    /// Implémentation NINA <see cref="IGuider"/> qui pilote l'autoguider
    /// de TheSkyX (objet script <c>ccdsoftAutoguider</c>) via le serveur
    /// JS sur TCP.
    ///
    /// L'autoguider doit être configuré au préalable dans TheSkyX :
    /// caméra guide assignée, monture connectée, étoile guide
    /// sélectionnable automatiquement.
    /// </summary>
    public class TheSkyXGuider : BaseINPC, IGuider {

        private readonly TheSkyXTcpClient _client;
        private readonly PluginOptions _options;
        private readonly string _host;
        private readonly int _port;
        private readonly double _exposureSeconds;
        private readonly bool _debug;

        /// <summary>Timeout étendu pour le lancement du guidage : Autoguide() est bloquant côté TheSkyX
        /// (peut recalibrer si le côté du méridien a changé).</summary>
        private static readonly TimeSpan StartGuidingTimeout = TimeSpan.FromMinutes(10);

        /// <summary>Timeout pour la sélection d'étoile / restauration de calibration (prise de vue + inventaire).</summary>
        private static readonly TimeSpan SelectStarTimeout = TimeSpan.FromMinutes(2);

        /// <summary>Fenêtre pendant laquelle une sélection d'étoile récente est réutilisée
        /// (évite une double prise de vue quand NINA appelle AutoSelectGuideStar() puis StartGuiding()).</summary>
        private static readonly TimeSpan ReselectGuard = TimeSpan.FromSeconds(45);

        private DateTime _lastStarSelectUtc = DateTime.MinValue;

        public TheSkyXGuider(PluginOptions options) {
            _options         = options ?? throw new ArgumentNullException(nameof(options));
            _host            = options.TheSkyXHost;
            _port            = options.TheSkyXPort;
            _exposureSeconds = options.TheSkyXGuideExposureSeconds;
            _debug           = options.DebugLogging;
            _client          = new TheSkyXTcpClient(_host, _port);
        }

        /// <summary>Crée un client TCP éphémère (timeout optionnel) vers le même serveur TheSkyX.</summary>
        private TheSkyXTcpClient CreateClient(TimeSpan? timeout = null)
            => new TheSkyXTcpClient(_host, _port, timeout);

        // ---- IDevice : identité ------------------------------------------

        public string Id          => "NinaTheSkyX.TheSkyX.Guider";
        public string Name        => "TheSkyX Autoguider (plugin)";
        // DisplayName : ajouté à l'IDevice 3.x — on renvoie Name.
        public string DisplayName => Name;
        public string Category    => "TheSkyX";
        public string Description => "Autoguidage via TheSkyX 64 (ccdsoftAutoguider over TCP).";
        public string DriverInfo  => "Scripting JS via TCP";
        public string DriverVersion => "1.0";
        public bool   HasSetupDialog => false;

        public IList<string> SupportedActions => Array.Empty<string>();

        // ---- État connexion ----------------------------------------------

        private bool _connected;
        public bool Connected {
            get => _connected;
            private set { _connected = value; RaisePropertyChanged(); }
        }

        public async Task<bool> Connect(CancellationToken ct) {
            try {
                await _client.ExecuteAsync(TheSkyXScriptBuilder.BuildConnect(), ct);
                Connected = true;
                if (_debug) Logger.Info("[TheSkyX] Autoguider connecté.");
                return true;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] Échec de connexion à l'autoguider", ex);
                Connected = false;
                return false;
            }
        }

        public void Disconnect() {
            // Important : Disconnect() est une méthode synchrone appelée par NINA
            // depuis le thread UI. Appeler directement ExecuteAsync().GetAwaiter().GetResult()
            // provoquerait un deadlock (le thread UI attend la task async, qui
            // elle-même tente de se compléter sur le thread UI → blocage permanent).
            // Fix : Task.Run déplace l'IO sur un thread pool, .Wait() avec timeout
            // évite de bloquer indéfiniment si TheSkyX ne répond pas.
            Connected = false; // marquer déconnecté immédiatement pour l'UI
            try {
                Task.Run(async () => {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _client.ExecuteAsync(TheSkyXScriptBuilder.BuildDisconnect(), cts.Token);
                }).Wait(TimeSpan.FromSeconds(6));
                if (_debug) Logger.Info("[TheSkyX] Autoguider déconnecté.");
            } catch (Exception ex) {
                // On log en Warning (pas Error) : une déconnexion qui échoue côté
                // TheSkyX n'est pas critique — la session est de toute façon terminée.
                Logger.Warning($"[TheSkyX] Déconnexion autoguider (ignorée) : {ex.Message}");
            }
        }

        public void SetupDialog() { /* config dans TheSkyX */ }

        // ---- IGuider : capabilities --------------------------------------

        public bool CanClearCalibration => true;
        public bool CanSetShiftRate     => false;
        public bool CanGetLockPosition  => false;

        // PixelScale : NINA y stocke l'échelle du guideur pour ses calculs
        // de RMS arcsec. TheSkyX ne l'expose pas via l'autoguider scripté ;
        // on laisse 0 et NINA traitera les valeurs en pixels.
        public double PixelScale { get; set; } = 0.0;

        // State : libellé court affiché dans la HUD guidage de NINA. On
        // remonte simplement l'état logique connu côté plugin.
        private string _state = "Idle";
        public string State {
            get => _state;
            private set { _state = value; RaisePropertyChanged(); }
        }

        // Pas de pilotage de shift rate (CanSetShiftRate = false).
        public bool ShiftEnabled => false;
        public SiderealShiftTrackingRate ShiftRate => SiderealShiftTrackingRate.Disabled;

        public event EventHandler<IGuideStep> GuideEvent;

        /// <summary>
        /// Petit helper pour neutraliser le warning CS0067 sur l'event qui
        /// n'est jamais levé (TheSkyX ne push pas de IGuideStep). Appelé
        /// nulle part en pratique — uniquement référencé pour silence.
        /// </summary>
        private void RaiseGuideEventDummy(IGuideStep step) => GuideEvent?.Invoke(this, step);

        // ---- Guidage -----------------------------------------------------

        /// <summary>
        /// Démarrage « sans intervention » du guidage (Phase 6) :
        /// <list type="number">
        ///   <item>applique la calibration mémorisée pour le côté du méridien de l'objet
        ///     (Est/Ouest) — sauf si <paramref name="forceCalibration"/> (on laisse alors
        ///     Autoguide() recalibrer) ;</item>
        ///   <item>prend une image guide et sélectionne automatiquement une étoile NON saturée
        ///     selon le critère ADU (min / optimum / max des options) ;</item>
        ///   <item>lance le guidage via <c>Autoguide()</c>.</item>
        /// </list>
        /// Si aucune étoile ne satisfait le critère ADU, le guidage <b>n'est pas démarré</b> et un
        /// statut clair est remonté (évite un démarrage silencieux voué à l'échec).
        /// </summary>
        public async Task<bool> StartGuiding(bool forceCalibration,
                                             IProgress<ApplicationStatus> progress,
                                             CancellationToken ct) {
            try {
                progress?.Report(new ApplicationStatus { Status = "TheSkyX : préparation du guidage..." });

                // 1) Calibration selon le côté du méridien (expérimental). En recalibration forcée,
                //    on ne restaure rien : Autoguide() recalibrera (Calibration=1).
                if (!forceCalibration) {
                    await TryApplyCalibrationForCurrentSideAsync(progress, ct);
                } else if (_debug) {
                    Logger.Info("[TheSkyX] forceCalibration=true → recalibration laissée à Autoguide(), pas de restauration.");
                }

                // 2) Sélection auto d'une étoile guide NON saturée par critère ADU.
                bool starOk = await EnsureGuideStarSelectedAsync(progress, ct);
                if (!starOk) {
                    const string msg = "TheSkyX : aucune étoile guide valide (critère ADU) — guidage NON démarré.";
                    progress?.Report(new ApplicationStatus { Status = msg });
                    Logger.Warning("[TheSkyX] " + msg +
                        " Ajuster ADU min/optimum/max dans les options, ou sélectionner une étoile manuellement.");
                    return false;
                }

                // 3) Lancement du guidage.
                progress?.Report(new ApplicationStatus { Status = "TheSkyX : démarrage du guidage..." });
                var script = TheSkyXScriptBuilder.BuildStartGuiding(_exposureSeconds, forceCalibration);
                await CreateClient(StartGuidingTimeout).ExecuteAsync(script, ct);

                State = "Guiding";
                if (_debug) Logger.Info($"[TheSkyX] Guidage démarré (recalibration forcée={forceCalibration}).");
                return true;
            } catch (OperationCanceledException) {
                Logger.Info("[TheSkyX] StartGuiding annulé.");
                return false;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] StartGuiding a échoué", ex);
                return false;
            }
        }

        public async Task<bool> StopGuiding(CancellationToken ct) {
            try {
                await _client.ExecuteAsync(TheSkyXScriptBuilder.BuildStopGuiding(), ct);
                State = "Idle";
                if (_debug) Logger.Info("[TheSkyX] Guidage arrêté.");
                return true;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] StopGuiding a échoué", ex);
                return false;
            }
        }

        public async Task<bool> Dither(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            try {
                progress?.Report(new ApplicationStatus { Status = "TheSkyX : dithering..." });

                // TheSkyX gère le dither via la boîte de dialogue Autoguide
                // (option « Move telescope randomly between exposures »).
                // Ici on déclenche un offset manuel sur la monture via
                // sky6RASCOMTele.Jog avant de relancer une exposition.
                //
                // TODO(api) : si tu utilises l'option « Direct Guide » au
                // lieu de la monture, remplacer Jog par les méthodes
                // appropriées de ccdsoftAutoguider.
                await _client.ExecuteAsync(TheSkyXScriptBuilder.BuildDither(arcsec: 5.0), ct);

                if (_debug) Logger.Info("[TheSkyX] Dither effectué.");
                return true;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] Dither a échoué", ex);
                return false;
            }
        }

        /// <summary>
        /// Sélection auto de l'étoile guide (appelée par NINA, parfois juste avant
        /// <see cref="StartGuiding"/>). Prend une image guide et sélectionne une étoile NON saturée
        /// par critère ADU. Note : signature sans CancellationToken dans NINA 3.x.
        /// </summary>
        public async Task<bool> AutoSelectGuideStar() {
            try {
                return await EnsureGuideStarSelectedAsync(progress: null, ct: CancellationToken.None);
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] AutoSelectGuideStar a échoué", ex);
                return false;
            }
        }

        // ---- Helpers Phase 6 : sélection ADU + calibration par côté ----------

        private enum MeridianSide { Unknown, East, West }

        /// <summary>
        /// Prend une image guide et sélectionne automatiquement une étoile NON saturée selon le
        /// critère ADU (options Min/Optimum/Max). Réutilise une sélection récente (&lt; <see cref="ReselectGuard"/>)
        /// pour éviter une double prise de vue. Retourne true si une étoile valide a été écrite dans
        /// GuideStarX/Y (vérifié par relecture côté script).
        /// </summary>
        private async Task<bool> EnsureGuideStarSelectedAsync(IProgress<ApplicationStatus> progress,
                                                              CancellationToken ct) {
            if ((DateTime.UtcNow - _lastStarSelectUtc) < ReselectGuard) {
                if (_debug) Logger.Info("[TheSkyX] Étoile guide déjà sélectionnée récemment — nouvelle sélection ignorée.");
                return true;
            }

            progress?.Report(new ApplicationStatus { Status = "TheSkyX : sélection auto de l'étoile guide (ADU)..." });

            // Marge de bord cohérente avec le wizard (couvre la track box pendant le guidage).
            int edgeMargin = _options.TheSkyXGuiderSubframeSize / 2 + 20;

            var script = TheSkyXScriptBuilder.BuildAutoSelectGuideStar(
                exposureSeconds: _exposureSeconds,
                edgeMarginPx:    edgeMargin,
                minADU:          _options.GuideStarMinADU,
                maxADU:          _options.GuideStarMaxADU,
                optimumADU:      _options.GuideStarOptimumADU);

            string raw;
            try {
                raw = await CreateClient(SelectStarTimeout).ExecuteAsync(script, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] Sélection auto de l'étoile guide a échoué", ex);
                return false;
            }

            // Réponse "X,Y,N" : X,Y = coords étoile (0,0 si aucune valide) ; N = sources détectées.
            double x = 0.0, y = 0.0; int n = 0;
            var parts = (raw ?? string.Empty).Split(',');
            if (parts.Length >= 3) {
                double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x);
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
                int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            }

            if (x >= 1.0 || y >= 1.0) {
                _lastStarSelectUtc = DateTime.UtcNow;
                if (_debug) {
                    Logger.Info($"[TheSkyX] Étoile guide sélectionnée : X={x.ToString("F1", CultureInfo.InvariantCulture)}, " +
                                $"Y={y.ToString("F1", CultureInfo.InvariantCulture)} px ({n} sources ; " +
                                $"ADU [{_options.GuideStarMinADU}–{_options.GuideStarMaxADU}], optimum {_options.GuideStarOptimumADU}).");
                }
                return true;
            }

            Logger.Warning($"[TheSkyX] Aucune étoile guide ne satisfait le critère ADU " +
                           $"[{_options.GuideStarMinADU}–{_options.GuideStarMaxADU}] (réponse '{raw}', {n} sources détectées).");
            return false;
        }

        /// <summary>
        /// ⚠ EXPÉRIMENTAL (non vérifié sur ciel) — détermine le côté du méridien de l'objet via NINA
        /// et réécrit la calibration TheSkyX mémorisée pour ce côté, avec vérification par relecture.
        /// Non bloquant : en cas d'échec (côté inconnu, blob vide, propriétés en lecture seule), on
        /// conserve la calibration active de TheSkyX et on logue clairement.
        /// </summary>
        private async Task TryApplyCalibrationForCurrentSideAsync(IProgress<ApplicationStatus> progress,
                                                                  CancellationToken ct) {
            var side = DetermineMeridianSide();
            if (side == MeridianSide.Unknown) {
                Logger.Warning("[TheSkyX] Côté du méridien indéterminé (monture non connectée dans NINA ?) — " +
                               "calibration TheSkyX active conservée.");
                return;
            }

            var sideStr = side == MeridianSide.East ? "Est" : "Ouest";
            var blob = side == MeridianSide.East ? _options.EastCalibrationData : _options.WestCalibrationData;
            if (string.IsNullOrWhiteSpace(blob)) {
                Logger.Warning($"[TheSkyX] Aucune calibration mémorisée côté {sideStr} — calibration active conservée " +
                               $"(calibrer ce côté avec le wizard pour activer la restauration auto).");
                return;
            }

            progress?.Report(new ApplicationStatus { Status = $"TheSkyX : restauration calibration {sideStr} (expérimental)..." });

            try {
                var raw = await CreateClient(SelectStarTimeout).ExecuteAsync(
                    TheSkyXScriptBuilder.BuildRestoreCalibration(blob), ct);

                if (VerifyCalibrationApplied(blob, raw)) {
                    if (_debug) Logger.Info($"[TheSkyX] Calibration {sideStr} restaurée et vérifiée (relecture conforme).");
                } else {
                    Logger.Warning($"[TheSkyX] ⚠ Restauration calibration {sideStr} NON confirmée par relecture — " +
                                   $"propriétés probablement en lecture seule. Le guidage utilisera la calibration active de " +
                                   $"TheSkyX (recalibrer si le côté a changé). Réponse : '{raw}'.");
                }
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Warning($"[TheSkyX] Restauration calibration {sideStr} échouée (non bloquant) : {ex.Message}");
            }
        }

        /// <summary>
        /// Côté du méridien de l'objet pointé, via <see cref="Plugin.TelescopeMediator"/>.
        /// HA = LST − RA (heures) ; HA &lt; 0 = EST, HA ≥ 0 = OUEST (cf. TECHNICAL_STATE « Formule de pointage RA »).
        /// Retourne Unknown si la monture n'est pas connectée dans NINA.
        /// </summary>
        private static MeridianSide DetermineMeridianSide() {
            try {
                var mediator = Plugin.TelescopeMediator;
                if (mediator == null) return MeridianSide.Unknown;

                var info = mediator.GetInfo();
                if (info == null || !info.Connected) return MeridianSide.Unknown;

                double lst = info.SiderealTime;     // heures
                double ra  = info.RightAscension;   // heures (pointage courant)
                double ha  = lst - ra;
                while (ha < -12.0) ha += 24.0;
                while (ha >= 12.0) ha -= 24.0;

                return ha < 0.0 ? MeridianSide.East : MeridianSide.West;
            } catch {
                return MeridianSide.Unknown;
            }
        }

        /// <summary>
        /// Compare le blob écrit et le blob relu (retournés par BuildRestoreCalibration) : vrai si
        /// toutes les valeurs écrites se retrouvent dans la relecture à une tolérance relative près.
        /// Faux si une clé manque ou diverge (= écriture ignorée par TheSkyX, propriétés en lecture seule).
        /// </summary>
        private static bool VerifyCalibrationApplied(string writtenBlob, string readbackBlob) {
            var written = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in TheSkyXScriptBuilder.ParseCalibrationBlob(writtenBlob)) {
                written[kv.Key] = kv.Value;
            }
            if (written.Count == 0) return false;

            var read = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in TheSkyXScriptBuilder.ParseCalibrationBlob(readbackBlob)) {
                read[kv.Key] = kv.Value;
            }

            foreach (var kv in written) {
                if (!read.TryGetValue(kv.Key, out var rv)) return false;
                var tol = Math.Max(1e-6, Math.Abs(kv.Value) * 1e-3);
                if (Math.Abs(rv - kv.Value) > tol) return false;
            }
            return true;
        }

        public async Task<bool> ClearCalibration(CancellationToken ct) {
            try {
                await _client.ExecuteAsync(TheSkyXScriptBuilder.BuildClearCalibration(), ct);
                return true;
            } catch (Exception ex) {
                Logger.Error("[TheSkyX] ClearCalibration a échoué", ex);
                return false;
            }
        }

        // ---- Shift / Lock : non supportés par ce backend -----------------

        public Task<bool> SetShiftRate(SiderealShiftTrackingRate shiftTrackingRate, CancellationToken ct)
            => Task.FromResult(false);

        public Task<bool> StopShifting(CancellationToken ct)
            => Task.FromResult(false);

        public Task<LockPosition> GetLockPosition()
            // CanGetLockPosition=false → NINA ne devrait pas appeler ceci ;
            // si jamais c'est le cas on renvoie un LockPosition par défaut.
            => Task.FromResult<LockPosition>(null);

        // ---- Action / Send command ---------------------------------------

        public string Action(string actionName, string actionParameters) => string.Empty;
        public string SendCommandString(string command, bool raw) => string.Empty;
        public bool SendCommandBool(string command, bool raw) => false;
        public void SendCommandBlind(string command, bool raw) { }
    }
}
