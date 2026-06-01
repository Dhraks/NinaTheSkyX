using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NinaTheSkyX.TheSkyX {

    /// <summary>
    /// Construction des snippets JavaScript envoyés au serveur de scripting
    /// de TheSkyX. Tout est statique et pur, sans IO, pour rester testable
    /// sans matériel ni socket.
    ///
    /// Règles strictes :
    ///  - Toute valeur numérique est sérialisée avec
    ///    <see cref="CultureInfo.InvariantCulture"/> pour éviter les
    ///    virgules décimales en culture fr-FR (TheSkyX attendrait alors
    ///    des arguments JS invalides).
    ///  - Tous les scripts terminent par une affectation de la variable
    ///    globale <c>Out</c> (le client TCP la récupère côté réponse).
    /// </summary>
    internal static class TheSkyXScriptBuilder {

        /// <summary>
        /// Connecte <c>ccdsoftAutoguider</c> (caméra guide) et renvoie l'état
        /// de connexion.
        ///
        /// Note : on ne connecte intentionnellement NI <c>ccdsoftCamera</c>
        /// (caméra principale) NI <c>sky6FilterWheel</c> (roue à filtres).
        /// Ces connexions étaient nécessaires dans FocusMax (qui utilisait la
        /// caméra TheSkyX pour les images de focus), mais notre plugin utilise
        /// les médiateurs NINA pour tout l'imagerie — TheSkyX ne gère que le
        /// guidage via <c>ccdsoftAutoguider</c>.
        /// </summary>
        public static string BuildConnect()
            => "ccdsoftAutoguider.Connect();Out = ccdsoftAutoguider.Connected;";

        /// <summary>Déconnecte ccdsoftAutoguider.</summary>
        public static string BuildDisconnect()
            => "ccdsoftAutoguider.Disconnect();Out=0;";

        /// <summary>
        /// Démarre le guidage. Si <paramref name="forceCalibration"/> est
        /// vrai, on force <c>Calibration = 1</c> avant <c>Autoguide()</c>
        /// pour qu'une nouvelle calibration soit refaite.
        /// </summary>
        public static string BuildStartGuiding(double exposureSeconds, bool forceCalibration) {
            var calibration = forceCalibration ? "ccdsoftAutoguider.Calibration = 1;" : "";
            // "R" garantit un round-trip complet du double (suffisant pour les expositions
            // couramment utilisées : 0.1 .. 60 s).
            var exposure = exposureSeconds.ToString("R", CultureInfo.InvariantCulture);
            return $"{calibration}ccdsoftAutoguider.ExposureTime = {exposure};ccdsoftAutoguider.Autoguide();Out=0;";
        }

        /// <summary>Arrête le guidage en cours (Abort).</summary>
        public static string BuildStopGuiding()
            => "ccdsoftAutoguider.Abort();Out=0;";

        /// <summary>
        /// Décale la monture via <c>sky6RASCOMTele.Jog</c> de
        /// <paramref name="arcsec"/> arc-secondes en E puis N. Les guides
        /// reprennent automatiquement sur la nouvelle position.
        /// </summary>
        public static string BuildDither(double arcsec) {
            var v = arcsec.ToString("R", CultureInfo.InvariantCulture);
            return $"sky6RASCOMTele.Jog({v}, 'East');sky6RASCOMTele.Jog({v}, 'North');Out=0;";
        }

        /// <summary>Réinitialise la calibration (forcera une nouvelle au prochain guidage).</summary>
        public static string BuildClearCalibration()
            => "ccdsoftAutoguider.Calibration = 0;Out=0;";

        /// <summary>
        /// Lit les coordonnées pixel de l'étoile guide sélectionnée dans la fenêtre
        /// Autoguider de TheSkyX (GuideStarX, GuideStarY) et les retourne dans <c>Out</c>
        /// au format <c>"x,y"</c> (séparateur virgule, point décimal ECMAScript).
        ///
        /// Retourne <c>"0,0"</c> si aucune étoile n'a encore été sélectionnée (premier
        /// lancement ou sélection non effectuée). La valeur (0,0) correspond au coin
        /// supérieur gauche du capteur (pixel chaud typique) — à vérifier côté appelant.
        /// </summary>
        public static string BuildReadGuideStarPosition()
            => "Out=ccdsoftAutoguider.GuideStarX+\",\"+ccdsoftAutoguider.GuideStarY;";

        // ---- Calibration du guidage (wizard) ---------------------------------

        /// <summary>
        /// Prend une seule image plein capteur avec la caméra guide. L'image est
        /// affichée dans la fenêtre Autoguider de TheSkyX — l'utilisateur peut y
        /// cliquer sur l'étoile guide pour la sélectionner.
        ///
        /// Force <c>SubFrame = false</c> avant la capture pour s'assurer que
        /// l'image de prévisualisation montre le champ complet, indépendamment
        /// d'un subframe qui aurait été activé lors d'une calibration précédente.
        ///
        /// Après la prise de vue, applique l'algorithme d'auto-contraste
        /// <b>Heuristic (Bjorn)</b> via <c>ccdsoftCameraImage.AutoContrast()</c>
        /// pour que les étoiles soient visibles dans la fenêtre Autoguider.
        ///
        /// TheSkyX utilise par défaut l'algorithme SBIG (<c>cdAutoContrastSBIG=0</c>)
        /// qui produit un affichage trop sombre et rend les étoiles invisibles avec
        /// les cameras non-SBIG. L'algorithme Bjorn (<c>cdAutoContrastBjorn=1</c>)
        /// est générique et adapté à toutes les cameras.
        ///
        /// Paramètres d'auto-contraste (confirmés depuis la doc TheSkyX officielle) :
        /// <list type="bullet">
        ///   <item><c>Method = 1</c> = <c>cdAutoContrastBjorn</c> (Heuristic)</item>
        ///   <item><c>Background = 1</c> = <c>cdBgWeak</c> (suppression fond faible)</item>
        ///   <item><c>Highlight = 3</c> = <c>cdHLStrong</c> (étoiles bien visibles)</item>
        /// </list>
        /// </summary>
        public static string BuildTakeGuiderImage(double exposureSeconds) {
            var exp = exposureSeconds.ToString("R", CultureInfo.InvariantCulture);
            // Subframe (lowercase 'f') est la vraie propriété ccdsoftCamera — confirmé
            // par la doc officielle TheSkyX et AtFocus3.js. 'SubFrame' (F majuscule)
            // est silencieusement ignoré par le moteur JS (crée une propriété fantôme).
            //
            // ccdsoftCameraImage est l'objet global image TheSkyX (cf. ShowInventory.js).
            // AttachToActiveAutoguider() l'attache à la dernière image de l'autoguider.
            // AutoContrast(Method, Background, Highlight) :
            //   cdAutoContrastBjorn=1, cdBgWeak=1, cdHLStrong=3
            return $"ccdsoftAutoguider.Subframe=false;" +
                   $"ccdsoftAutoguider.ExposureTime={exp};" +
                   $"ccdsoftAutoguider.TakeImage();" +
                   $"ccdsoftCameraImage.AttachToActiveAutoguider();" +
                   $"ccdsoftCameraImage.AutoContrast(1,1,3);" +
                   $"Out=0;";
        }

        /// <summary>
        /// Prend une image avec la caméra guide, analyse le champ via
        /// <c>ccdsoftAutoguiderImage.ShowInventory()</c> et sélectionne automatiquement
        /// l'étoile guide la plus brillante dont le FWHM est dans la plage
        /// [<paramref name="minFwhmPx"/>, <paramref name="maxFwhmPx"/>] pixels.
        ///
        /// Si une étoile valide est trouvée, ses coordonnées pixel sont écrites dans
        /// <c>ccdsoftAutoguider.GuideStarX</c> et <c>ccdsoftAutoguider.GuideStarY</c>
        /// (propriétés read/write confirmées dans la documentation officielle TheSkyX).
        ///
        /// Gestion de l'autosave : <c>AutoSaveOn</c> est lu, activé temporairement avant
        /// <c>TakeImage()</c> puis restauré — <c>ShowInventory()</c> requiert que l'image
        /// ait été sauvegardée sur disque (le champ <c>Path</c> de
        /// <c>ccdsoftAutoguiderImage</c> doit être non vide).
        /// Le dossier cible est celui déjà configuré dans TheSkyX (AutoSave Setup).
        ///
        /// Format de retour (variable <c>Out</c>) :
        /// <list type="bullet">
        ///   <item><c>"X,Y,N"</c> — étoile trouvée aux coordonnées (X, Y) pixels ;
        ///     N = nombre total d'étoiles détectées dans le champ.</item>
        ///   <item><c>"0,0,0"</c> — champ vide (N == 0, aucune étoile détectée).</item>
        ///   <item><c>"0,0,N"</c> — N étoiles détectées, aucune dans la plage FWHM ;
        ///     l'utilisateur doit sélectionner manuellement.</item>
        /// </list>
        ///
        /// Utilise <c>ccdsoftAutoguiderImage.InventoryArray(index)</c> avec les constantes
        /// de la documentation officielle TheSkyX :
        ///   cdInventoryX = 0, cdInventoryY = 1, cdInventoryMagnitude = 2, cdInventoryFWHM = 4.
        ///
        /// <b>⚠ Fix 2026-05-30</b> : utilise <c>ccdsoftAutoguiderImage</c> (objet image dédié
        /// autoguider) au lieu de <c>ccdsoftCameraImage</c> (imageur), qui renvoyait 0 source via
        /// ShowInventory(). Détection + écriture GuideStarX/Y confirmées sur ciel réel (2026-05-31).
        /// </summary>
        /// <param name="exposureSeconds">Durée d'exposition en secondes.</param>
        /// <param name="minFwhmPx">FWHM minimum acceptable en pixels (défaut : 1.5).</param>
        /// <param name="maxFwhmPx">FWHM maximum acceptable en pixels (défaut : 20.0).</param>
        /// <param name="maxFwhmMedianFactor">Rejette une source si sa FWHM dépasse ce facteur × la médiane du champ (défaut : 2.5).</param>
        /// <param name="maxEllipticityMedianFactor">Rejette une source si son ellipticité dépasse ce facteur × la médiane du champ (défaut : 2.5).</param>
        /// <param name="saturationFraction">Fraction de la pleine échelle (2^BITPIX-1) au-delà de laquelle un pixel est saturé (défaut : 0.9).</param>
        /// <param name="edgeMarginPx">Marge de bord minimale en pixels : une source à moins de cette distance d'un bord est écartée — évite qu'elle sorte du champ pendant la course de calibration. 0 = dérive de la track box. Plancher effectif = max(edgeMarginPx, TrackBox/2+5).</param>
        /// <param name="minADU">Pic ADU minimum acceptable (échelle brute capteur, 0..2^BITPIX-1). 0 (avec maxADU=0) = critère ADU désactivé.</param>
        /// <param name="maxADU">Pic ADU maximum acceptable (sous la saturation). <b>0 = mode hérité</b> (plus brillante non saturée). &gt;0 = active la sélection par critère ADU.</param>
        /// <param name="optimumADU">Pic ADU cible : parmi les candidats dont le pic ∈ [minADU, maxADU], on retient celui dont |pic − optimumADU| est minimal (meilleur compromis SNR/saturation pour le guidage).</param>
        /// <remarks>
        /// <b>Deux modes de sélection</b> :
        /// <list type="bullet">
        ///   <item><c>maxADU == 0</c> (défaut) : comportement hérité — l'étoile la plus brillante
        ///     (magnitude minimale) parmi les candidats non saturés. Utilisé par le wizard de calibration.</item>
        ///   <item><c>maxADU &gt; 0</c> : sélection par <b>pic ADU</b> — on calcule le pixel maximum
        ///     (<c>peakADU</c>) dans une boîte ~2×FWHM autour de chaque candidat, on ne garde que ceux
        ///     dont le pic ∈ [<paramref name="minADU"/>, <paramref name="maxADU"/>] (et &lt; seuil de
        ///     saturation BITPIX), puis on choisit le pic le plus proche de <paramref name="optimumADU"/>.
        ///     Utilisé par le flux de guidage (étoile bien exposée mais NON saturée).</item>
        /// </list>
        /// Le pic ADU est sur l'échelle brute du capteur (0..2^BITPIX−1) : régler les bornes selon
        /// la profondeur de bits réelle de la caméra guide (⚠ caméras 12 bits encapsulées en 16 bits).
        /// </remarks>
        public static string BuildAutoSelectGuideStar(
            double exposureSeconds,
            double minFwhmPx = 1.5,
            double maxFwhmPx = 20.0,
            double maxFwhmMedianFactor = 2.5,
            double maxEllipticityMedianFactor = 2.5,
            double saturationFraction = 0.9,
            int edgeMarginPx = 0,
            int minADU = 0,
            int maxADU = 0,
            int optimumADU = 0) {

            var exp      = exposureSeconds.ToString("R", CultureInfo.InvariantCulture);
            var minFwhm  = minFwhmPx.ToString("R", CultureInfo.InvariantCulture);
            var maxFwhm  = maxFwhmPx.ToString("R", CultureInfo.InvariantCulture);
            var fwhmFac  = maxFwhmMedianFactor.ToString("R", CultureInfo.InvariantCulture);
            var ellipFac = maxEllipticityMedianFactor.ToString("R", CultureInfo.InvariantCulture);
            var satFrac  = saturationFraction.ToString("R", CultureInfo.InvariantCulture);
            var margin   = edgeMarginPx.ToString(CultureInfo.InvariantCulture);
            var minAdu   = minADU.ToString(CultureInfo.InvariantCulture);
            var maxAdu   = maxADU.ToString(CultureInfo.InvariantCulture);
            var optAdu   = optimumADU.ToString(CultureInfo.InvariantCulture);

            // ECMAScript 3 (moteur Qt Script de TheSkyX) : var, for traditionnel.
            // Constantes InventoryArray (doc officielle TheSkyX) :
            //   cdInventoryX=0, cdInventoryY=1, cdInventoryMagnitude=2, cdInventoryFWHM=4
            //
            // AutoSaveOn : sauvegarde l'état, l'active pour que TakeImage() écrive le
            // fichier sur disque (Path non vide) — requis par ShowInventory(). Restauré
            // après lecture des tableaux pour ne pas modifier durablement les réglages TSX.
            //
            // AttachToActiveAutoguider() : méthode sémantiquement correcte pour l'automatisation.
            // Attache ccdsoftAutoguiderImage à la DERNIÈRE IMAGE ACQUISE PAR L'AUTOGUIDER, quelle que
            // soit la fenêtre active dans TheSkyX — indépendant du focus clavier/souris.
            //
            // NE PAS utiliser AttachToActive() en contexte automatisé :
            //   AttachToActive() attache à la fenêtre frontmost de TheSkyX (carte du ciel,
            //   fenêtre imager principal, etc.) — résultat non déterministe depuis NINA.
            //   ShowInventory.js officiel l'utilise parce que c'est un script MANUEL où
            //   l'utilisateur a cliqué sur la bonne fenêtre avant d'exécuter.
            //
            // AutoSaveOn=1 garantit que Path est non vide après TakeImage() — requis par
            // ShowInventory(). Restauré après lecture des tableaux.
            //
            // ⚠ AutoContrast(1,1,3) délibérément ABSENT : Error 11000 confirmé sur ciel réel
            // (2026-05-24) — "This command is not supported" après AttachToActiveAutoguider().
            // ShowInventory() n'en a pas besoin (travaille sur les pixels bruts).
            //
            // Vérification par relecture : après écriture GuideStarX/Y, on relit pour confirmer.
            // Si la propriété n'est pas réellement writable en contexte scripting, rbX ≠ Xa[bi]
            // et on retourne "0,0,N" honnêtement (évite d'afficher faussement "⭐ sélectionnée").
            //
            // Sélection : candidats = FWHM ∈ [minFwhmPx, maxFwhmPx] + filtres bord/médiane.
            // maxADU==0 → la plus brillante non saturée (calibration) ;
            // maxADU>0  → pic ADU le plus proche de optimumADU dans [minADU,maxADU] (guidage).
            //
            // Retour :
            //   "X,Y,N"  → étoile valide trouvée ET GuideStarX/Y mis à jour (X,Y > 1)
            //   "0,0,0"  → champ vide (ShowInventory n'a détecté aucune source)
            //   "0,0,N"  → N étoiles trouvées mais aucune valide (FWHM hors plage OU
            //               écriture GuideStarX/Y sans effet)
            return
                "var cdX=0;var cdY=1;var cdM=2;var cdF=4;var cdE=8;" +
                // median() : médiane d'un tableau (copie + tri) → seuils relatifs au champ.
                "function median(a){var b=[];for(var i=0;i<a.length;i++)b.push(a[i]);" +
                "b.sort(function(p,q){return p-q;});var h=Math.floor(b.length/2);" +
                "return (b.length%2)?b[h]:(b[h-1]+b[h])/2.0;}" +
                // peakADU(ls) : pic (pixel max) dans une boîte ~2*FWHM autour de l'étoile ls.
                // Lecture des pixels bruts via scanLine() → sert au rejet de saturation ET au
                // critère ADU (étoile bien exposée mais non saturée pour le guidage).
                "function peakADU(ls){var r=Math.max(2,Math.floor(Fa[ls]*2+0.5));" +
                "var x0=Math.max(0,Math.floor(Xa[ls]-r)),x1=Math.min(W-1,Math.floor(Xa[ls]+r));" +
                "var y0=Math.max(0,Math.floor(Ya[ls]-r)),y1=Math.min(H-1,Math.floor(Ya[ls]+r));" +
                "var mx=0;for(var yy=y0;yy<=y1;yy++){var row=ccdsoftAutoguiderImage.scanLine(yy);" +
                "for(var xx=x0;xx<=x1;xx++){if(row[xx]>mx)mx=row[xx];}}return mx;}" +
                // notSat(ls) : vrai si le pic ne dépasse pas le seuil de saturation satMax.
                "function notSat(ls){return peakADU(ls)<=satMax;}" +
                "var oldSave=ccdsoftAutoguider.AutoSaveOn;" +
                "ccdsoftAutoguider.AutoSaveOn=1;" +
                // Asynchronous=0 : rend TakeImage() SYNCHRONE — bloque jusqu'à fin d'exposition.
                // Sans ça, AttachToActiveAutoguider() s'exécute AVANT que la nouvelle image soit
                // disponible et récupère l'ancienne image encore affichée dans la fenêtre.
                // Confirmé sur ciel réel (2026-05-24) : path "May 23 2026" retourné sans ce flag.
                "ccdsoftAutoguider.Asynchronous=0;" +
                "ccdsoftAutoguider.Subframe=false;" +
                $"ccdsoftAutoguider.ExposureTime={exp};" +
                "ccdsoftAutoguider.TakeImage();" +
                // AttachToActiveAutoguider() : attache à la dernière image autoguider acquise.
                // Déterministe en contexte automatisé — indépendant de la fenêtre active dans TSX.
                // NB : AutoContrast() n'est PAS appelé ici — Error 11000 ("This command is not
                // supported") confirmé sur ciel réel (2026-05-24). ShowInventory() travaille sur
                // les pixels bruts et n'a pas besoin d'AutoContrast (contrairement à BuildTakeGuiderImage
                // qui l'utilise uniquement pour l'affichage à l'utilisateur).
                // ccdsoftAutoguiderImage = objet image DÉDIÉ à l'autoguider (≠ ccdsoftCameraImage = imageur).
                // ccdsoftCameraImage.AttachToActiveAutoguider() renvoyait 0 source via ShowInventory()
                // (le script de prod ScriptSkyX / Cline Observatory utilise ccdsoftAutoguiderImage). Fix 2026-05-30.
                "ccdsoftAutoguiderImage.AttachToActiveAutoguider();" +
                "ccdsoftAutoguiderImage.ShowInventory();" +
                "var Xa=ccdsoftAutoguiderImage.InventoryArray(cdX);" +
                "var Ya=ccdsoftAutoguiderImage.InventoryArray(cdY);" +
                "var Ma=ccdsoftAutoguiderImage.InventoryArray(cdM);" +
                "var Fa=ccdsoftAutoguiderImage.InventoryArray(cdF);" +
                "var Ea=ccdsoftAutoguiderImage.InventoryArray(cdE);" +
                "var n=Xa.length;" +
                "ccdsoftAutoguider.AutoSaveOn=oldSave;" +
                "if(n==0){Out=\"0,0,0\";}" +
                "else{" +
                // Dimensions image + marge de bord + seuil saturation.
                "var W=ccdsoftAutoguiderImage.WidthInPixels,H=ccdsoftAutoguiderImage.HeightInPixels;" +
                // Marge de bord = max(edgeMarginPx demandé, demi track box +5). edgeMarginPx doit couvrir
                // la course de calibration (TheSkyX déplace l'étoile) sinon elle sort du champ et la calib échoue.
                $"var mrg={margin};" +
                "var mX=ccdsoftAutoguider.TrackBoxX/2+5;if(!(mX>0))mX=16;if(mrg>mX)mX=mrg;" +
                "var mY=ccdsoftAutoguider.TrackBoxY/2+5;if(!(mY>0))mY=16;if(mrg>mY)mY=mrg;" +
                "var bits=ccdsoftAutoguiderImage.FITSKeyword(\"BITPIX\");" +
                $"var satMax=(Math.pow(2,bits)-1)*{satFrac};if(!(satMax>0))satMax=1e30;" +
                // Seuils relatifs au champ : facteur × médiane (FWHM et ellipticité).
                $"var maxF=median(Fa)*{fwhmFac},maxEl=median(Ea)*{ellipFac};" +
                // Bornes ADU : maxA==0 → mode hérité (plus brillante non saturée) ;
                // maxA>0 → sélection du pic le plus proche de optA dans [minA,maxA].
                $"var minA={minAdu},maxA={maxAdu},optA={optAdu};" +
                // Candidats : pas trop près du bord, FWHM dans plage absolue ET < facteur*médiane,
                // ellipticité < facteur*médiane (rejette blobs, doubles, traînées, hot pixels).
                "var cand=[];for(var i=0;i<n;i++){" +
                "if(Xa[i]<mX||Xa[i]>W-mX||Ya[i]<mY||Ya[i]>H-mY)continue;" +
                $"if(Fa[i]<{minFwhm}||Fa[i]>{maxFwhm}||Fa[i]>maxF)continue;" +
                "if(Ea[i]>maxEl)continue;cand.push(i);}" +
                "var bi=-1;" +
                "if(maxA>0){" +
                // Mode ADU (guidage) : garder les candidats dont le pic ∈ [minA,maxA] et < satMax,
                // puis retenir celui dont le pic est le plus proche de optA (|pic−optA| minimal).
                "var bestD=-1;" +
                "for(var c=0;c<cand.length;c++){var pk=peakADU(cand[c]);" +
                "if(pk<minA||pk>maxA||pk>satMax)continue;" +
                "var dd=Math.abs(pk-optA);" +
                "if(bestD<0||dd<bestD){bestD=dd;bi=cand[c];}}" +
                "}else{" +
                // Mode hérité (calibration) : plus brillant (magnitude mini) d'abord, 1er NON saturé.
                "cand.sort(function(a,b){return Ma[a]-Ma[b];});" +
                "for(var c=0;c<cand.length;c++){if(notSat(cand[c])){bi=cand[c];break;}}" +
                "}" +
                "if(bi<0){Out=\"0,0,\"+n;}" +
                "else{" +
                // Unscale binning : GuideStarX/Y en coords capteur (TheSkyX rescale ensuite).
                "var bx=ccdsoftAutoguider.BinX;if(!(bx>0))bx=1;" +
                "var by=ccdsoftAutoguider.BinY;if(!(by>0))by=1;" +
                "var wX=Xa[bi]*bx,wY=Ya[bi]*by;" +
                "ccdsoftAutoguider.GuideStarX=wX;ccdsoftAutoguider.GuideStarY=wY;" +
                // Relecture : confirme que l'écriture a pris effet (sinon "0,0,N").
                "var rbX=ccdsoftAutoguider.GuideStarX;" +
                "var rbY=ccdsoftAutoguider.GuideStarY;" +
                "if(Math.abs(rbX-wX)<1.0&&Math.abs(rbY-wY)<1.0){" +
                "Out=Xa[bi]+\",\"+Ya[bi]+\",\"+n;}" +
                "else{Out=\"0,0,\"+n;}" +
                "}" +
                "}";
        }

        /// <summary>
        /// Script de diagnostic pour valider le flux AutoSelectGuideStar sur ciel réel.
        /// À exécuter directement depuis la console de scripting TheSkyX (pas depuis NINA).
        ///
        /// Retourne une chaîne multi-ligne avec :
        ///   - Path de l'image attachée (confirme qu'AttachToActiveAutoguider fonctionne)
        ///   - Nombre d'étoiles détectées par ShowInventory()
        ///   - Propriétés des 3 premières étoiles (X, Y, Magnitude, FWHM)
        ///   - GuideStarX/Y avant et après écriture (confirme si write prend effet)
        ///
        /// Utilisation : exécuter dans TSX après une prise de vue autoguider manuelle,
        /// ou laisser le script prendre sa propre image (exposureSeconds > 0).
        /// </summary>
        public static string BuildDiagnoseAutoSelect(double exposureSeconds = 5.0) {
            var exp = exposureSeconds.ToString("R", CultureInfo.InvariantCulture);
            return
                // Prise de vue avec AutoSaveOn forcé
                "var oldSave=ccdsoftAutoguider.AutoSaveOn;" +
                "ccdsoftAutoguider.AutoSaveOn=1;" +
                "ccdsoftAutoguider.Subframe=false;" +
                $"ccdsoftAutoguider.ExposureTime={exp};" +
                "ccdsoftAutoguider.TakeImage();" +
                // Diagnostic : AttachToActiveAutoguider vs chemin image
                "ccdsoftAutoguiderImage.AttachToActiveAutoguider();" +
                "var imgPath=ccdsoftAutoguiderImage.Path;" +
                // AutoContrast retiré : Error 11000 ("command not supported") + inutile pour ShowInventory().
                "ccdsoftAutoguiderImage.ShowInventory();" +
                "var Xa=ccdsoftAutoguiderImage.InventoryArray(0);" +
                "var Ya=ccdsoftAutoguiderImage.InventoryArray(1);" +
                "var Ma=ccdsoftAutoguiderImage.InventoryArray(2);" +
                "var Fa=ccdsoftAutoguiderImage.InventoryArray(4);" +
                "var n=Xa.length;" +
                "ccdsoftAutoguider.AutoSaveOn=oldSave;" +
                // Lecture GuideStarX/Y avant écriture
                "var gsXBefore=ccdsoftAutoguider.GuideStarX;" +
                "var gsYBefore=ccdsoftAutoguider.GuideStarY;" +
                // Écriture sur la première étoile (si disponible)
                "var gsXAfter=gsXBefore;var gsYAfter=gsYBefore;" +
                "if(n>0){" +
                "ccdsoftAutoguider.GuideStarX=Xa[0];" +
                "ccdsoftAutoguider.GuideStarY=Ya[0];" +
                "gsXAfter=ccdsoftAutoguider.GuideStarX;" +
                "gsYAfter=ccdsoftAutoguider.GuideStarY;" +
                "}" +
                // Rapport complet
                "var cr=\"\\n\";" +
                "Out=\"=== DiagnoseAutoSelect ===\"+cr;" +
                "Out+=\"Path=\"+imgPath+cr;" +
                "Out+=\"Stars=\"+n+cr;" +
                "Out+=\"GuideStarX avant=\"+gsXBefore+\" apres=\"+gsXAfter+cr;" +
                "Out+=\"GuideStarY avant=\"+gsYBefore+\" apres=\"+gsYAfter+cr;" +
                "Out+=\"WriteOK=\"+(Math.abs(gsXAfter-Xa[0])<1.0&&n>0)+cr;" +
                "for(var i=0;i<Math.min(n,3);i++){" +
                "Out+=\"Star\"+i+\": X=\"+Xa[i]+\" Y=\"+Ya[i]+\" Mag=\"+Ma[i]+\" FWHM=\"+Fa[i]+cr;" +
                "}";
        }

        /// <summary>
        /// ⚠ NON DISPONIBLE dans l'API de scripting TheSkyX (confirmé 2026-05-10).
        ///
        /// <c>ccdsoftAutoguider.AutoFindGuideStar()</c> lève une TypeError JS
        /// ("Result of expression ... [undefined] is not a function") — cette méthode
        /// n'est pas exposée dans le serveur de scripting TheSkyX.
        ///
        /// Recherche exhaustive effectuée (2026-05-22) dans la documentation officielle
        /// <c>classccdsoft_camera-members.html</c> et dans tous les scripts d'exemple
        /// (.js) fournis avec TheSkyX : aucun équivalent trouvé sous les noms
        /// AutoFind, FindStar, FindGuideStar, SelectGuideStar, SelectStar.
        ///
        /// Alternatives identifiées dans la documentation officielle :
        /// <list type="bullet">
        ///   <item>
        ///     <b><c>CenterBrightestObject()</c></b> — méthode sur <c>ccdsoftCamera</c> /
        ///     <c>ccdsoftAutoguider</c>. Doc : "Center the brightest object on the last
        ///     acquired photo by using the autoguider." Sémantique probable : correction
        ///     physique de centrage via l'autoguider (différent de "sélectionner une
        ///     étoile guide"). À confirmer par test réel — voir
        ///     <see cref="BuildCenterBrightestObject"/>.
        ///   </item>
        ///   <item>
        ///     <b>Propriétés <c>GuideStarX</c> / <c>GuideStarY</c></b> — read/write.
        ///     Doc : "destination guide star position". Pourrait permettre de positionner
        ///     l'étoile guide par script, mais nécessite de connaître les coordonnées
        ///     pixel au préalable (ex: via <c>ccdsoftCameraImage.InventoryArray</c>
        ///     sur une image sauvegardée).
        ///   </item>
        /// </list>
        ///
        /// Méthode de sélection actuelle (confirmée fonctionnelle sur ciel réel 2026-05-20) :
        /// l'utilisateur clique l'étoile dans la fenêtre Autoguider de TheSkyX,
        /// puis valide dans le wizard du plugin.
        ///
        /// Méthode conservée pour mémoire — NE PAS APPELER via TCP.
        /// </summary>
        [Obsolete("AutoFindGuideStar() n'existe pas dans l'API scripting TheSkyX. Aucun équivalent scripting trouvé (confirmé 2026-05-22 : doc exhaustive + CenterBrightestObject() testé → centrage physique, pas sélection). Sélection manuelle obligatoire.")]
        public static string BuildAutoFindGuideStar()
            => "ccdsoftAutoguider.AutoFindGuideStar();Out=0;";

        /// <summary>
        /// ⚠ NON UTILE pour la sélection d'étoile guide (confirmé sur ciel réel 2026-05-22).
        ///
        /// Centre l'objet le plus brillant de la dernière photo acquise en utilisant
        /// l'autoguider (<c>ccdsoftAutoguider.CenterBrightestObject()</c>).
        ///
        /// Méthode documentée officiellement dans <c>classccdsoft_camera.html</c> :
        /// "Center the brightest object on the last acquired photo by using the autoguider."
        ///
        /// Test réel (2026-05-22) avec la séquence :
        /// <code>
        ///   ccdsoftAutoguider.Subframe=false;
        ///   ccdsoftAutoguider.ExposureTime=5;
        ///   ccdsoftAutoguider.TakeImage();
        ///   ccdsoftAutoguider.CenterBrightestObject();
        ///   Out = ccdsoftAutoguider.GuideStarX + "," + ccdsoftAutoguider.GuideStarY;
        /// </code>
        /// Résultat : <b>"0,0"</b> — <c>GuideStarX/Y</c> restent à zéro après l'appel.
        ///
        /// Conclusion : <c>CenterBrightestObject()</c> NE sélectionne PAS l'étoile guide.
        /// Il s'agit d'une correction physique de la monture pour centrer l'objet
        /// sur le capteur (utilise les impulsions d'autoguidage pour déplacer la monture).
        /// Ne met pas à jour <c>GuideStarX/Y</c>, donc inutilisable pour notre cas
        /// d'usage (sélection d'étoile guide avant calibration).
        ///
        /// Conclusion finale sur "Auto-Find Guide Star" (2026-05-22) : aucun équivalent
        /// scripting n'existe dans l'API TheSkyX. La sélection manuelle (clic dans la
        /// fenêtre Autoguider de TheSkyX) reste la seule méthode disponible.
        ///
        /// Méthode conservée pour mémoire — NE PAS UTILISER pour la sélection d'étoile guide.
        /// </summary>
        [Obsolete("CenterBrightestObject() fait un centrage physique de monture, pas une sélection d'étoile guide. Confirmé sur ciel réel 2026-05-22 : GuideStarX/Y reste 0,0 après l'appel.")]
        public static string BuildCenterBrightestObject()
            => "ccdsoftAutoguider.CenterBrightestObject();Out=0;";

        /// <summary>
        /// Lance la calibration de l'autoguider via
        /// <c>ccdsoftAutoguider.Calibrate(int CalibrateAO)</c>.
        ///
        /// Confirmé 2026-05-14 : Calibrate() EXIGE un paramètre int.
        /// Sans paramètre → SyntaxError "too few arguments; candidates are Calibrate(int)".
        ///
        /// Paramètre <c>CalibrateAO</c> :
        ///   0 = calibration standard (monture, via impulsions RA/Dec)
        ///   1 = calibration Adaptive Optics (AO, miroir inclinable)
        /// On utilise toujours 0 (non-AO) sauf matériel AO explicite.
        ///
        /// ⚠ NE PAS appeler TakeImage() ni écraser GuideStarX/GuideStarY ici.
        /// L'image de prévisualisation a déjà été prise via
        /// <see cref="BuildTakeGuiderImage"/> et l'utilisateur a sélectionné
        /// manuellement l'étoile guide dans la fenêtre Autoguider de TheSkyX
        /// (clic → TheSkyX mémorise les coordonnées GuideStarX/Y).
        ///
        /// <b>Subframe anti-hijacking (confirmé 2026-05-17)</b> :
        /// Si <paramref name="subframeSize"/> &gt; 0, active un subframe carré
        /// de <paramref name="subframeSize"/> × <paramref name="subframeSize"/> px
        /// centré sur <c>GuideStarX/Y</c> (position cliquée par l'utilisateur).
        /// Ceci empêche TheSkyX de basculer sur une étoile plus brillante entrant
        /// dans le champ lors du slew ou de la calibration elle-même.
        ///
        /// API TheSkyX officielle (doc + AtFocus3.js) : <c>ccdsoftAutoguider.Subframe</c>
        /// (booléen, 'f' minuscule), <c>SubframeLeft</c>, <c>SubframeTop</c>,
        /// <c>SubframeRight</c>, <c>SubframeBottom</c> (coordonnées absolues du bord droit
        /// et bas — PAS de SubframeWidth/Height, ces propriétés n'existent pas).
        ///
        /// Contrairement à <c>Autoguide()</c>, <c>Calibrate()</c> est bloquant
        /// mais NE démarre PAS le guidage — il retourne après mesure des vecteurs
        /// RA/Dec, sans passer en mode guidage. TheSkyX sauvegarde la calibration.
        ///
        /// Timeout TCP recommandé : ≥ 10 min (typiquement 2–4 min en pratique).
        /// </summary>
        /// <param name="exposureSeconds">Durée d'exposition de chaque image de calibration.</param>
        /// <param name="subframeSize">
        /// Taille du subframe carré en pixels. 0 = désactivé (plein capteur).
        /// Valeur recommandée : 300 pour FSQ106 + ATIK 314L+.
        /// </param>
        public static string BuildCalibrateAutoguider(double exposureSeconds, int subframeSize = 0) {
            var exp = exposureSeconds.ToString("R", CultureInfo.InvariantCulture);

            string subframeScript = string.Empty;
            if (subframeSize > 0) {
                // Lit les coordonnées pixel de l'étoile sélectionnée par l'utilisateur
                // dans la fenêtre Autoguider de TheSkyX (mémorisées au dernier clic).
                //
                // API officielle ccdsoftCamera (doc TheSkyX + AtFocus3.js) :
                //   Subframe    = true/false  (booléen, 'f' MINUSCULE — 'SubFrame' est ignoré)
                //   SubframeLeft, SubframeTop : coin supérieur gauche (pixels)
                //   SubframeRight             : coordonnée x du bord DROIT (x + half)
                //   SubframeBottom            : coordonnée y du bord BAS  (y + half)
                //   → PAS de SubframeWidth / SubframeHeight (propriétés inexistantes,
                //     silencieusement ignorées par le moteur JS TheSkyX)
                //
                // Math.max(0, ...) : clamp pour éviter des coordonnées négatives si
                // l'étoile est trop proche du bord haut/gauche du capteur.
                int half = subframeSize / 2;
                subframeScript =
                    $"var x=ccdsoftAutoguider.GuideStarX;" +
                    $"var y=ccdsoftAutoguider.GuideStarY;" +
                    $"ccdsoftAutoguider.Subframe=true;" +
                    $"ccdsoftAutoguider.SubframeLeft=Math.max(0,x-{half});" +
                    $"ccdsoftAutoguider.SubframeTop=Math.max(0,y-{half});" +
                    $"ccdsoftAutoguider.SubframeRight=x+{half};" +
                    $"ccdsoftAutoguider.SubframeBottom=y+{half};";
            }

            // Subframe=false après Calibrate() : remet le capteur en mode plein champ
            // pour que le guidage normal (Autoguide()) retrouve son comportement par
            // défaut TheSkyX (trackbox 100×100 px interne). Sans ce reset, TheSkyX
            // continuerait à lire uniquement la zone 300×300 configurée pour la
            // calibration, ce qui perturbe la sélection d'étoile guide en guidage normal.
            return subframeScript +
                   $"ccdsoftAutoguider.ExposureTime={exp};" +
                   $"ccdsoftAutoguider.Calibrate(0);" +
                   $"ccdsoftAutoguider.Subframe=false;" +
                   $"Out=0;";
        }

        // ---- Sauvegarde / restauration de calibration par côté (EXPÉRIMENTAL) -------
        //
        // ⚠⚠ NON VÉRIFIÉ SUR CIEL (2026-05-31). TheSkyX ne garde qu'UNE calibration active à
        // la fois ; l'objectif est de mémoriser les paramètres de calibration de chaque côté du
        // méridien (Est/Ouest) après le wizard, puis de les réécrire au Start Guiding selon la
        // position de l'objet — pour éviter une recalibration après un meridian flip.
        //
        // Les noms de propriétés ci-dessous proviennent de la doc officielle Software Bisque
        // (classccdsoft_camera-members.html, vérifiée 2026-05-31). CE QUI RESTE À CONFIRMER :
        //   1. Que ces propriétés sont accessibles sur l'objet `ccdsoftAutoguider` (et non
        //      uniquement sur `ccdsoftCamera` avec la propriété `Autoguider=1`).
        //   2. Surtout : qu'elles sont **inscriptibles** par script (beaucoup de résultats de
        //      calibration sont en lecture seule). La relecture après écriture tranchera.
        // Utiliser `BuildDiagnoseCalibration()` depuis la console TheSkyX pour trancher ces points.

        /// <summary>
        /// Noms officiels (doc Bisque) des propriétés de calibration de l'autoguider à mémoriser.
        /// Les 8 composantes du vecteur de calibration + temps de calibration X/Y + backlash X/Y +
        /// temps de calibration sauvegardés + déclinaison à la calibration (la calibration dépend
        /// de la déclinaison, d'où l'intérêt de la mémoriser par côté).
        /// </summary>
        internal static readonly string[] CalibrationPropertyNames = {
            "CalibrationVectorXPositiveXComponent",
            "CalibrationVectorXPositiveYComponent",
            "CalibrationVectorXNegativeXComponent",
            "CalibrationVectorXNegativeYComponent",
            "CalibrationVectorYPositiveXComponent",
            "CalibrationVectorYPositiveYComponent",
            "CalibrationVectorYNegativeXComponent",
            "CalibrationVectorYNegativeYComponent",
            "AutoguiderCalibrationTimeXAxis",
            "AutoguiderCalibrationTimeYAxis",
            "AutoguiderBacklashXAxis",
            "AutoguiderBacklashYAxis",
            "SavedCalibrationTimeX",
            "SavedCalibrationTimeY",
            "DeclinationAtCalibration",
        };

        /// <summary>
        /// Génère le corps JS qui lit toutes les propriétés de <see cref="CalibrationPropertyNames"/>
        /// sur <c>ccdsoftAutoguider</c> et concatène le résultat sous forme <c>"clé=valeur;clé=valeur;…"</c>.
        ///
        /// <c>g(v)</c> protège des propriétés absentes (undefined / NaN → 0). Accès en notation
        /// pointée (pas crochets) pour rester compatible avec le binding Qt Script de TheSkyX.
        /// </summary>
        private static string ReadCalibrationJsBody(bool includeCalibrationFlag) {
            var sb = new StringBuilder();
            sb.Append("function g(v){return (v==undefined||isNaN(v))?0:v;}var s=\"\";");
            foreach (var p in CalibrationPropertyNames) {
                sb.Append($"s+=\"{p}=\"+g(ccdsoftAutoguider.{p})+\";\";");
            }
            if (includeCalibrationFlag) {
                sb.Append("s+=\"Calibration=\"+ccdsoftAutoguider.Calibration+\";\";");
            }
            return sb.ToString();
        }

        /// <summary>
        /// ⚠ EXPÉRIMENTAL — Lit les paramètres de calibration courants de l'autoguider TheSkyX.
        /// À appeler juste après une calibration réussie du wizard (côté Est ou Ouest) pour
        /// mémoriser le blob retourné dans <c>PluginOptions.East/WestCalibrationData</c>.
        ///
        /// Retour (<c>Out</c>) : <c>"clé=valeur;clé=valeur;…"</c> (point décimal ECMAScript).
        /// </summary>
        public static string BuildReadCalibration()
            => ReadCalibrationJsBody(includeCalibrationFlag: true) + "Out=s;";

        /// <summary>
        /// ⚠ EXPÉRIMENTAL (non vérifié sur ciel) — Réécrit les paramètres de calibration mémorisés
        /// (<paramref name="calibrationBlob"/> produit par <see cref="BuildReadCalibration"/>) dans
        /// <c>ccdsoftAutoguider</c>, force <c>Calibration=1</c>, puis <b>relit</b> toutes les
        /// propriétés pour que l'appelant vérifie en C# que l'écriture a pris effet.
        ///
        /// Si TheSkyX ignore l'écriture (propriétés en lecture seule), la relecture ne correspondra
        /// pas aux valeurs écrites → l'appelant doit alors recalibrer plutôt que guider à l'aveugle.
        ///
        /// Seules les clés de <see cref="CalibrationPropertyNames"/> sont réécrites (liste blanche) ;
        /// toute clé inconnue ou valeur non numérique est ignorée.
        /// </summary>
        /// <param name="calibrationBlob">Blob <c>"clé=valeur;…"</c> mémorisé pour le côté courant.</param>
        public static string BuildRestoreCalibration(string calibrationBlob) {
            var sb = new StringBuilder();
            foreach (var kv in ParseCalibrationBlob(calibrationBlob)) {
                var v = kv.Value.ToString("R", CultureInfo.InvariantCulture);
                sb.Append($"ccdsoftAutoguider.{kv.Key}={v};");
            }
            sb.Append("ccdsoftAutoguider.Calibration=1;");
            // Relecture (même format que BuildReadCalibration) pour vérification C#.
            sb.Append(ReadCalibrationJsBody(includeCalibrationFlag: true));
            sb.Append("Out=s;");
            return sb.ToString();
        }

        /// <summary>
        /// ⚠ DIAGNOSTIC — À exécuter depuis la console de scripting TheSkyX (pas depuis NINA),
        /// juste APRÈS une calibration réussie. Affiche la valeur de chaque propriété de calibration
        /// puis teste leur <b>inscriptibilité</b> (écrit valeur+1, relit, restaure) sur 3 propriétés
        /// représentatives. Permet de trancher si la stratégie save/restore par script est viable.
        ///
        /// Retour : rapport multi-ligne (valeurs + "writable=true/false" par propriété testée).
        /// </summary>
        public static string BuildDiagnoseCalibration() {
            var sb = new StringBuilder();
            sb.Append("var cr=\"\\n\";");
            sb.Append("function g(v){return (v==undefined||isNaN(v))?0:v;}");
            sb.Append("Out=\"=== DiagnoseCalibration (EXPERIMENTAL) ===\"+cr;");
            sb.Append("Out+=\"Calibration=\"+ccdsoftAutoguider.Calibration+cr;");
            // Dump : typeof (number = propriété lisible / function = méthode à appeler) + valeur.
            foreach (var p in CalibrationPropertyNames) {
                sb.Append($"Out+=\"{p}: type=\"+(typeof ccdsoftAutoguider.{p})+\" val=\"+g(ccdsoftAutoguider.{p})+cr;");
            }
            // Tests d'inscriptibilité (try/catch : une écriture qui lève est reportée, pas fatale).
            sb.Append("try{var o1=g(ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis);" +
                      "ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis=o1+1;" +
                      "var r1=g(ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis);" +
                      "ccdsoftAutoguider.AutoguiderCalibrationTimeXAxis=o1;" +
                      "Out+=\"AutoguiderCalibrationTimeXAxis writable=\"+(Math.abs(r1-(o1+1))<0.5)+cr;}" +
                      "catch(e1){Out+=\"AutoguiderCalibrationTimeXAxis writable=ERROR(\"+e1+\")\"+cr;}");
            sb.Append("try{var o2=g(ccdsoftAutoguider.CalibrationVectorXPositiveXComponent);" +
                      "ccdsoftAutoguider.CalibrationVectorXPositiveXComponent=o2+1;" +
                      "var r2=g(ccdsoftAutoguider.CalibrationVectorXPositiveXComponent);" +
                      "ccdsoftAutoguider.CalibrationVectorXPositiveXComponent=o2;" +
                      "Out+=\"CalibrationVectorXPositiveXComponent writable=\"+(Math.abs(r2-(o2+1))<0.5)+cr;}" +
                      "catch(e2){Out+=\"CalibrationVectorXPositiveXComponent writable=ERROR(\"+e2+\")\"+cr;}");
            sb.Append("try{var o3=g(ccdsoftAutoguider.DeclinationAtCalibration);" +
                      "ccdsoftAutoguider.DeclinationAtCalibration=o3+1;" +
                      "var r3=g(ccdsoftAutoguider.DeclinationAtCalibration);" +
                      "ccdsoftAutoguider.DeclinationAtCalibration=o3;" +
                      "Out+=\"DeclinationAtCalibration writable=\"+(Math.abs(r3-(o3+1))<0.5)+cr;}" +
                      "catch(e3){Out+=\"DeclinationAtCalibration writable=ERROR(\"+e3+\")\"+cr;}");
            return sb.ToString();
        }

        /// <summary>
        /// Parse un blob <c>"clé=valeur;clé=valeur;…"</c> produit par <see cref="BuildReadCalibration"/>.
        /// Ne retourne que les clés présentes dans <see cref="CalibrationPropertyNames"/> (liste blanche)
        /// dont la valeur est un double valide (point décimal, <see cref="CultureInfo.InvariantCulture"/>).
        /// Robuste aux blobs nuls/vides/partiels (retourne ce qui est valide).
        /// </summary>
        internal static IEnumerable<KeyValuePair<string, double>> ParseCalibrationBlob(string blob) {
            var result = new List<KeyValuePair<string, double>>();
            if (string.IsNullOrWhiteSpace(blob)) {
                return result;
            }
            var known = new HashSet<string>(CalibrationPropertyNames, StringComparer.Ordinal);
            foreach (var pair in blob.Split(';')) {
                if (string.IsNullOrWhiteSpace(pair)) {
                    continue;
                }
                var eq = pair.IndexOf('=');
                if (eq <= 0) {
                    continue;
                }
                var key = pair.Substring(0, eq).Trim();
                var val = pair.Substring(eq + 1).Trim();
                if (!known.Contains(key)) {
                    continue;
                }
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) {
                    result.Add(new KeyValuePair<string, double>(key, d));
                }
            }
            return result;
        }

        /// <summary>
        /// Démarre la calibration puis le guidage en un seul appel bloquant.
        ///
        /// <b>Conservé pour compatibilité / tests.</b> Dans le wizard de calibration
        /// guidage, préférer la séquence
        /// <see cref="BuildAutoFindGuideStar"/> → <see cref="BuildCalibrateAutoguider"/>
        /// qui sépare proprement la calibration du démarrage du guidage.
        ///
        /// L'appel à <c>ccdsoftAutoguider.Autoguide()</c> est <b>bloquant</b>
        /// dans le serveur de scripting TheSkyX : il retourne uniquement après
        /// calibration ET démarrage du guidage. Il faut ensuite envoyer
        /// <see cref="BuildStopGuiding"/> pour arrêter le guidage.
        /// </summary>
        public static string BuildStartCalibrationAndGuide(double exposureSeconds) {
            var exp = exposureSeconds.ToString("R", CultureInfo.InvariantCulture);
            return $"ccdsoftAutoguider.ExposureTime = {exp};" +
                   $"ccdsoftAutoguider.Calibration = 1;" +
                   $"ccdsoftAutoguider.Autoguide();" +
                   $"Out=0;";
        }

        /// <summary>
        /// Déplace la monture en Ascension Droite via
        /// <c>sky6RASCOMTele.Jog</c>. Utilisé pour repositionner l'étoile
        /// guide quand l'image n'est pas satisfaisante.
        ///
        /// <paramref name="arcsec"/> positif → Est ; négatif → Ouest.
        /// Amplitude recommandée : 60–300 arc-secondes (1–5 arcminutes).
        /// </summary>
        public static string BuildJogRA(double arcsec) {
            var direction = arcsec >= 0 ? "East" : "West";
            var magnitude = Math.Abs(arcsec).ToString("R", CultureInfo.InvariantCulture);
            return $"sky6RASCOMTele.Jog({magnitude}, '{direction}');Out=0;";
        }
    }
}
