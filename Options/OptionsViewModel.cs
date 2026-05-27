using NINA.Core.Utility;
using NINA.Profile.Interfaces;
using NinaTheSkyX.TheSkyX;
using System.ComponentModel.Composition;

namespace NinaTheSkyX.Options {

    /// <summary>
    /// ViewModel d'options du plugin TheSkyX Guider.
    /// Expose les paramètres TCP et le wizard de calibration guidage.
    /// </summary>
    [Export]
    public class OptionsViewModel : BaseINPC {

        private readonly PluginOptions _options;

        [ImportingConstructor]
        public OptionsViewModel(IProfileService profileService) {
            _options          = new PluginOptions(profileService);
            GuiderCalibration = new GuiderCalibrationVM(_options);
        }

        public PluginOptions PluginOptions => _options;

        /// <summary>VM du wizard de calibration guidage TheSkyX.</summary>
        public GuiderCalibrationVM GuiderCalibration { get; }

        // ---- Options TCP -------------------------------------------------

        public string TheSkyXHost {
            get => _options.TheSkyXHost;
            set { _options.TheSkyXHost = value; RaisePropertyChanged(); }
        }

        public int TheSkyXPort {
            get => _options.TheSkyXPort;
            set { _options.TheSkyXPort = value; RaisePropertyChanged(); }
        }

        public double TheSkyXGuideExposureSeconds {
            get => _options.TheSkyXGuideExposureSeconds;
            set { _options.TheSkyXGuideExposureSeconds = value; RaisePropertyChanged(); }
        }

        public int TheSkyXGuiderSubframeSize {
            get => _options.TheSkyXGuiderSubframeSize;
            set { _options.TheSkyXGuiderSubframeSize = value; RaisePropertyChanged(); }
        }

        public bool DebugLogging {
            get => _options.DebugLogging;
            set { _options.DebugLogging = value; RaisePropertyChanged(); }
        }
    }
}
