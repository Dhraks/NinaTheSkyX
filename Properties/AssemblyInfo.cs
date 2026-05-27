using System.Reflection;
using System.Runtime.InteropServices;

// GUID stable du plugin — DOIT correspondre à PluginOptions.cs.
[assembly: Guid("c3d4e5f6-a7b8-9012-cdef-012345678912")]
[assembly: ComVisible(false)]

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NinaTheSkyX.Tests")]

[assembly: AssemblyTitle("TheSkyX Guider")]
[assembly: AssemblyDescription("Autoguidage via TheSkyX 64 (ccdsoftAutoguider over TCP scripting).")]
[assembly: AssemblyCompany("Alexandre")]
[assembly: AssemblyProduct("TheSkyX Guider")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Alexandre")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1000")]
[assembly: AssemblyMetadata("Homepage",    "https://example.com/nina-theskyx")]
[assembly: AssemblyMetadata("Repository",  "https://github.com/example/nina-theskyx")]
[assembly: AssemblyMetadata("License",     "MIT")]
[assembly: AssemblyMetadata("LicenseURL",  "https://opensource.org/licenses/MIT")]
[assembly: AssemblyMetadata("ChangelogURL","https://example.com/nina-theskyx/CHANGELOG")]
[assembly: AssemblyMetadata("Tags",        "TheSkyX,Guider,Autoguide,TCP,Calibration")]

[assembly: AssemblyMetadata("FeaturedImageURL", "")]
[assembly: AssemblyMetadata("ScreenshotURL",    "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]

[assembly: AssemblyMetadata("LongDescription", @"
Ce plugin connecte NINA à l'autoguider de TheSkyX 64 via le serveur de
scripting JavaScript intégré (Tools → Run Java Script, port TCP 3040).

**TheSkyX Guider** — pilote ccdsoftAutoguider en TCP pur, sans DLL ni
dépendance binaire à TheSkyX. Implémente IGuider (connexion, démarrage/arrêt
guidage, dithering, clear calibration). Inclut un wizard de calibration
guidage en deux passes (Est + Ouest, 0° Dec) avec pointage automatique via
NINA, prise de vue guide, sélection manuelle de l'étoile dans TheSkyX,
et validation Calibrate(0) — conforme à l'API scripting TheSkyX officielle.
")]
