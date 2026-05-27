using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NinaTheSkyX.Options;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace NinaTheSkyX {

    /// <summary>
    /// Point d'entrée NINA pour le plugin TheSkyX Guider.
    ///
    /// Exporte IPluginManifest pour la découverte MEF, importe le mediator
    /// télescope nécessaire au wizard de calibration guidage (pointage
    /// automatique Est/Ouest) et l'expose en statique pour les VMs.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Plugin : PluginBase {

        [ImportingConstructor]
        public Plugin(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            ICameraMediator cameraMediator) {

            TelescopeMediator = telescopeMediator;
            CameraMediator    = cameraMediator;

            Options = new OptionsViewModel(profileService);
        }

        /// <summary>VM d'options exposée via {x:Static local:Plugin.Options} dans le XAML.</summary>
        public static OptionsViewModel Options { get; private set; }

        /// <summary>
        /// Médiateur télescope — utilisé par GuiderCalibrationVM pour pointer
        /// la monture à Est/Ouest lors du wizard de calibration.
        /// </summary>
        public static ITelescopeMediator TelescopeMediator { get; private set; }

        /// <summary>Médiateur caméra (réservé pour extensions futures).</summary>
        public static ICameraMediator CameraMediator { get; private set; }

        public override Task Initialize() {
            NINA.Core.Utility.Logger.Info("[TheSkyX Guider] Plugin initialisé.");
            try {
                var app = System.Windows.Application.Current;
                if (app != null) {
                    var dict = new System.Windows.ResourceDictionary {
                        Source = new System.Uri(
                            "/NinaTheSkyX;component/Options/Options.xaml",
                            System.UriKind.Relative)
                    };
                    app.Resources.MergedDictionaries.Add(dict);
                }
            } catch (System.Exception ex) {
                NINA.Core.Utility.Logger.Warning(
                    $"[TheSkyX Guider] Merge ResourceDictionary échoué : {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public override Task Teardown() {
            NINA.Core.Utility.Logger.Info("[TheSkyX Guider] Plugin déchargé.");
            return Task.CompletedTask;
        }
    }
}
