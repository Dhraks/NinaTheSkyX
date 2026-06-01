using NINA.Profile;
using NINA.Profile.Interfaces;
using System;

namespace NinaTheSkyX.Options {

    /// <summary>
    /// Accès typé aux préférences du plugin TheSkyX Guider, persistées par
    /// profil NINA via <see cref="PluginOptionsAccessor"/>. Le GUID DOIT
    /// correspondre à celui déclaré dans Properties/AssemblyInfo.cs.
    /// </summary>
    public class PluginOptions {

        public static readonly Guid PluginGuid =
            Guid.Parse("c3d4e5f6-a7b8-9012-cdef-012345678912");

        private readonly PluginOptionsAccessor _accessor;

        public PluginOptions(IProfileService profileService) {
            _accessor = new PluginOptionsAccessor(profileService, PluginGuid);
        }

        // ---- TheSkyX TCP -------------------------------------------------

        public string TheSkyXHost {
            get => _accessor.GetValueString(nameof(TheSkyXHost), "127.0.0.1");
            set => _accessor.SetValueString(nameof(TheSkyXHost), value);
        }

        public int TheSkyXPort {
            get => _accessor.GetValueInt32(nameof(TheSkyXPort), 3040);
            set => _accessor.SetValueInt32(nameof(TheSkyXPort), value);
        }

        public double TheSkyXGuideExposureSeconds {
            get => _accessor.GetValueDouble(nameof(TheSkyXGuideExposureSeconds), 2.0);
            set => _accessor.SetValueDouble(nameof(TheSkyXGuideExposureSeconds), value);
        }

        /// <summary>
        /// Taille du subframe carré (pixels) centré sur l'étoile guide sélectionnée,
        /// activé avant Calibrate(0) pour éviter qu'une étoile plus brillante
        /// entrant dans le champ ne soit prise à la place.
        /// 0 = désactivé. Default : 300. Range : 0–1000 px.
        /// </summary>
        public int TheSkyXGuiderSubframeSize {
            get => _accessor.GetValueInt32(nameof(TheSkyXGuiderSubframeSize), 300);
            set => _accessor.SetValueInt32(nameof(TheSkyXGuiderSubframeSize),
                       Math.Max(0, Math.Min(1000, value)));
        }

        /// <summary>
        /// Durée d'exposition des images de calibration (prévisualisation + Calibrate).
        /// Distincte de TheSkyXGuideExposureSeconds (guidage actif).
        /// Range : 1–30 s (entiers). Default : 5 s.
        ///
        /// Valeur arrondie à l'entier le plus proche en getter et setter pour éviter
        /// qu'un slider non-snapé produise des valeurs fractionnaires (ex : 3.09595…)
        /// injectées telles quelles dans les scripts JS TheSkyX.
        /// </summary>
        public double TheSkyXCalibrationExposureSeconds {
            get => Math.Round(Math.Max(1.0, Math.Min(30.0,
                       _accessor.GetValueDouble(nameof(TheSkyXCalibrationExposureSeconds), 5.0))));
            set => _accessor.SetValueDouble(nameof(TheSkyXCalibrationExposureSeconds),
                       Math.Round(Math.Max(1.0, Math.Min(30.0, value))));
        }

        // ---- Sélection d'étoile guide par critère ADU (Phase 6) ----------

        /// <summary>
        /// Pic ADU minimum acceptable pour l'étoile guide (échelle brute capteur, 0..2^BITPIX-1).
        /// En dessous : étoile trop faible (SNR insuffisant). Default : 8000. Range : 0–65535.
        /// ⚠ Dépend de la caméra guide (profondeur de bits / gain) — voir BuildDiagnoseAutoSelect.
        /// </summary>
        public int GuideStarMinADU {
            get => _accessor.GetValueInt32(nameof(GuideStarMinADU), 8000);
            set => _accessor.SetValueInt32(nameof(GuideStarMinADU),
                       Math.Max(0, Math.Min(65535, value)));
        }

        /// <summary>
        /// Pic ADU maximum acceptable (doit rester sous la saturation). Au-dessus : étoile rejetée.
        /// Default : 45000. Range : 0–65535. ⚠ Dépend de la caméra guide.
        /// </summary>
        public int GuideStarMaxADU {
            get => _accessor.GetValueInt32(nameof(GuideStarMaxADU), 45000);
            set => _accessor.SetValueInt32(nameof(GuideStarMaxADU),
                       Math.Max(0, Math.Min(65535, value)));
        }

        /// <summary>
        /// Pic ADU cible : l'étoile retenue est celle dont le pic est le plus proche de cette valeur,
        /// dans l'intervalle [<see cref="GuideStarMinADU"/>, <see cref="GuideStarMaxADU"/>].
        /// Meilleur compromis SNR / marge de saturation. Default : 25000. Range : 0–65535.
        /// </summary>
        public int GuideStarOptimumADU {
            get => _accessor.GetValueInt32(nameof(GuideStarOptimumADU), 25000);
            set => _accessor.SetValueInt32(nameof(GuideStarOptimumADU),
                       Math.Max(0, Math.Min(65535, value)));
        }

        // ---- Calibration — dates persistées ------------------------------

        /// <summary>Date/heure de la dernière calibration réussie côté EST. Null si jamais calibré.</summary>
        public DateTime? LastEastCalibrationAt {
            get {
                var s = _accessor.GetValueString(nameof(LastEastCalibrationAt), string.Empty);
                return string.IsNullOrEmpty(s) ? (DateTime?)null
                    : DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            set => _accessor.SetValueString(nameof(LastEastCalibrationAt),
                       value.HasValue ? value.Value.ToString("o") : string.Empty);
        }

        /// <summary>Date/heure de la dernière calibration réussie côté OUEST.</summary>
        public DateTime? LastWestCalibrationAt {
            get {
                var s = _accessor.GetValueString(nameof(LastWestCalibrationAt), string.Empty);
                return string.IsNullOrEmpty(s) ? (DateTime?)null
                    : DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);
            }
            set => _accessor.SetValueString(nameof(LastWestCalibrationAt),
                       value.HasValue ? value.Value.ToString("o") : string.Empty);
        }

        // ---- Calibration — paramètres mémorisés par côté (Phase 6, EXPÉRIMENTAL) ----
        //
        // ⚠ Blob "clé=valeur;…" produit par TheSkyXScriptBuilder.BuildReadCalibration() après une
        // calibration réussie. Réécrit au Start Guiding selon le côté du méridien de l'objet.
        // Vide tant que la stratégie save/restore n'a pas été validée sur ciel.

        /// <summary>Paramètres de calibration TheSkyX mémorisés côté EST (blob "clé=valeur;…"). Vide si non capturé.</summary>
        public string EastCalibrationData {
            get => _accessor.GetValueString(nameof(EastCalibrationData), string.Empty);
            set => _accessor.SetValueString(nameof(EastCalibrationData), value ?? string.Empty);
        }

        /// <summary>Paramètres de calibration TheSkyX mémorisés côté OUEST (blob "clé=valeur;…"). Vide si non capturé.</summary>
        public string WestCalibrationData {
            get => _accessor.GetValueString(nameof(WestCalibrationData), string.Empty);
            set => _accessor.SetValueString(nameof(WestCalibrationData), value ?? string.Empty);
        }

        // ---- Divers ------------------------------------------------------

        public bool DebugLogging {
            get => _accessor.GetValueBoolean(nameof(DebugLogging), false);
            set => _accessor.SetValueBoolean(nameof(DebugLogging), value);
        }
    }
}
