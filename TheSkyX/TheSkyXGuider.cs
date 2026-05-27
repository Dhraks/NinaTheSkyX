using NINA.Astrometry;
using NINA.Core.Interfaces;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Interfaces;
using System;
using System.Collections.Generic;
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
        private readonly double _exposureSeconds;
        private readonly bool _debug;

        public TheSkyXGuider(string host, int port, double exposureSeconds, bool debug) {
            _client          = new TheSkyXTcpClient(host, port);
            _exposureSeconds = exposureSeconds;
            _debug           = debug;
        }

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

        public async Task<bool> StartGuiding(bool forceCalibration,
                                             IProgress<ApplicationStatus> progress,
                                             CancellationToken ct) {
            try {
                progress?.Report(new ApplicationStatus { Status = "TheSkyX : démarrage du guidage..." });

                var script = TheSkyXScriptBuilder.BuildStartGuiding(_exposureSeconds, forceCalibration);
                await _client.ExecuteAsync(script, ct);

                State = "Guiding";
                if (_debug) Logger.Info($"[TheSkyX] Guidage démarré (calibration={forceCalibration}).");
                return true;
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

        public Task<bool> AutoSelectGuideStar() {
            // TheSkyX sélectionne l'étoile guide pendant la calibration ;
            // rien à faire côté NINA. Note : signature sans CancellationToken
            // dans NINA 3.x.
            return Task.FromResult(true);
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
