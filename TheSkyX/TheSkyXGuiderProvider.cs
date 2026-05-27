using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NinaTheSkyX.Options;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace NinaTheSkyX.TheSkyX {

    /// <summary>
    /// Provider MEF qui expose le guider TheSkyX dans la liste de
    /// périphériques NINA (Options → Equipment → Guider).
    /// </summary>
    [Export(typeof(IEquipmentProvider))]
    public class TheSkyXGuiderProvider : IEquipmentProvider<IGuider> {

        private readonly IProfileService _profileService;

        [ImportingConstructor]
        public TheSkyXGuiderProvider(IProfileService profileService) {
            _profileService = profileService;
        }

        public string Name        => "TheSkyX Autoguider";
        public string Description => "Autoguidage via le module Camera Add-On de TheSkyX 64.";
        public string ContentId   => GetType().FullName;

        public IList<IGuider> GetEquipment() {
            var options = new PluginOptions(_profileService);
            return new List<IGuider> {
                new TheSkyXGuider(
                    options.TheSkyXHost,
                    options.TheSkyXPort,
                    options.TheSkyXGuideExposureSeconds,
                    options.DebugLogging)
            };
        }
    }
}
