/* ============================================================================
   Diagnostic calibration TheSkyX - Phase 6 (EXPERIMENTAL)
   ----------------------------------------------------------------------------
   A executer dans TheSkyX : Tools > Run JavaScript > (Open... ce fichier) > Run.
   A lancer JUSTE APRES une calibration reussie de l'autoguideur.
   Resultat dans la zone "Script Output" (variable Out).

   Objectif :
     (A) Ces membres sont-ils des PROPRIETES (typeof number) ou des METHODES
         (typeof function) ? -> determine comment les lire/ecrire.
     (B) Si ce sont des methodes, valeur via appel ().
     (C) Sont-ils INSCRIPTIBLES par script (writable=true/false) ? -> determine
         si la strategie save/restore par script est viable.
   ============================================================================ */

var cr = "\n";
function g(v){ return (v == undefined || isNaN(v)) ? 0 : v; }

Out  = "=== DiagnoseCalibration (EXPERIMENTAL) ===" + cr;
Out += "Calibration=" + ccdsoftAutoguider.Calibration + cr;

/* --- (A) Acces PROPRIETE : typeof + valeur -------------------------------- */
Out += cr + "--- (A) Acces propriete : typeof + valeur ---" + cr;
Out += "VecXPosX  type=" + (typeof ccdsoftAutoguider.CalibrationVectorXPositiveXComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorXPositiveXComponent) + cr;
Out += "VecXPosY  type=" + (typeof ccdsoftAutoguider.CalibrationVectorXPositiveYComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorXPositiveYComponent) + cr;
Out += "VecXNegX  type=" + (typeof ccdsoftAutoguider.CalibrationVectorXNegativeXComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorXNegativeXComponent) + cr;
Out += "VecXNegY  type=" + (typeof ccdsoftAutoguider.CalibrationVectorXNegativeYComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorXNegativeYComponent) + cr;
Out += "VecYPosX  type=" + (typeof ccdsoftAutoguider.CalibrationVectorYPositiveXComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorYPositiveXComponent) + cr;
Out += "VecYPosY  type=" + (typeof ccdsoftAutoguider.CalibrationVectorYPositiveYComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorYPositiveYComponent) + cr;
Out += "VecYNegX  type=" + (typeof ccdsoftAutoguider.CalibrationVectorYNegativeXComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorYNegativeXComponent) + cr;
Out += "VecYNegY  type=" + (typeof ccdsoftAutoguider.CalibrationVectorYNegativeYComponent) + " val=" + g(ccdsoftAutoguider.CalibrationVectorYNegativeYComponent) + cr;
Out += "CalTimeX  type=" + (typeof ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis) + " val=" + g(ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis) + cr;
Out += "CalTimeY  type=" + (typeof ccdsoftAutoguider.AutoguiderCalibrationTimeYAxis) + " val=" + g(ccdsoftAutoguider.AutoguiderCalibrationTimeYAxis) + cr;
Out += "BklashX   type=" + (typeof ccdsoftAutoguider.AutoguiderBacklashXAxis) + " val=" + g(ccdsoftAutoguider.AutoguiderBacklashXAxis) + cr;
Out += "BklashY   type=" + (typeof ccdsoftAutoguider.AutoguiderBacklashYAxis) + " val=" + g(ccdsoftAutoguider.AutoguiderBacklashYAxis) + cr;
Out += "SavedTX   type=" + (typeof ccdsoftAutoguider.SavedCalibrationTimeX) + " val=" + g(ccdsoftAutoguider.SavedCalibrationTimeX) + cr;
Out += "SavedTY   type=" + (typeof ccdsoftAutoguider.SavedCalibrationTimeY) + " val=" + g(ccdsoftAutoguider.SavedCalibrationTimeY) + cr;
Out += "DecAtCal  type=" + (typeof ccdsoftAutoguider.DeclinationAtCalibration) + " val=" + g(ccdsoftAutoguider.DeclinationAtCalibration) + cr;

/* --- (B) Si (A) montre type=function : valeur via appel () ----------------- */
Out += cr + "--- (B) Valeur via appel methode () (si type=function en A) ---" + cr;
try { Out += "VecXPosX() = " + ccdsoftAutoguider.CalibrationVectorXPositiveXComponent() + cr; }
catch (eb) { Out += "VecXPosX() -> " + eb + cr; }

/* --- (C) Test d'inscriptibilite (writable) : ecrit val+1, relit, restaure -- */
Out += cr + "--- (C) Test ecriture (writable) ---" + cr;

try {
  var o1 = g(ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis);
  ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis = o1 + 1;
  var r1 = g(ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis);
  ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis = o1;
  Out += "AutoguiderCalibrationTimeXAxis writable=" + (Math.abs(r1 - (o1 + 1)) < 0.5) + cr;
} catch (e1) { Out += "AutoguiderCalibrationTimeXAxis writable=ERROR(" + e1 + ")" + cr; }

try {
  var o2 = g(ccdsoftAutoguider.CalibrationVectorXPositiveXComponent);
  ccdsoftAutoguider.CalibrationVectorXPositiveXComponent = o2 + 1;
  var r2 = g(ccdsoftAutoguider.CalibrationVectorXPositiveXComponent);
  ccdsoftAutoguider.CalibrationVectorXPositiveXComponent = o2;
  Out += "CalibrationVectorXPositiveXComponent writable=" + (Math.abs(r2 - (o2 + 1)) < 0.5) + cr;
} catch (e2) { Out += "CalibrationVectorXPositiveXComponent writable=ERROR(" + e2 + ")" + cr; }

try {
  var o3 = g(ccdsoftAutoguider.DeclinationAtCalibration);
  ccdsoftAutoguider.DeclinationAtCalibration = o3 + 1;
  var r3 = g(ccdsoftAutoguider.DeclinationAtCalibration);
  ccdsoftAutoguider.DeclinationAtCalibration = o3;
  Out += "DeclinationAtCalibration writable=" + (Math.abs(r3 - (o3 + 1)) < 0.5) + cr;
} catch (e3) { Out += "DeclinationAtCalibration writable=ERROR(" + e3 + ")" + cr; }

/* La fenetre "Run JavaScript" affiche la variable Out. */
Out;
