using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NinaTheSkyX.TheSkyX {

    /// <summary>
    /// Client TCP pour le serveur de scripting JavaScript de TheSkyX
    /// (port 3040 par défaut, activable via Tools → Run Java Script).
    ///
    /// Format de requête attendu par TheSkyX :
    ///   /* Java Script */
    ///   /* Socket Start Packet */
    ///   &lt;script JS, place le résultat dans la variable globale `Out`&gt;
    ///   /* Socket End Packet */
    ///
    /// Format de réponse :
    ///   &lt;valeur de Out&gt;|No error. Error = 0.
    /// ou
    ///   |Internal error. Error = 123.
    /// </summary>
    public class TheSkyXTcpClient {

        private readonly string _host;
        private readonly int _port;
        private readonly TimeSpan _timeout;

        public TheSkyXTcpClient(string host, int port, TimeSpan? timeout = null) {
            _host    = host;
            _port    = port;
            _timeout = timeout ?? TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// Envoie un snippet JS et renvoie la valeur de <c>Out</c>. Lève
        /// <see cref="InvalidOperationException"/> si TheSkyX rapporte
        /// une erreur (Error != 0) ou si le socket timeout.
        /// </summary>
        public async Task<string> ExecuteAsync(string javascript, CancellationToken ct) {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(_timeout);

            using var client = new TcpClient();
            await client.ConnectAsync(_host, _port, linked.Token);
            using var stream = client.GetStream();

            var script = "/* Java Script */\n" +
                         "/* Socket Start Packet */\n" +
                         javascript + "\n" +
                         "/* Socket End Packet */\n";
            var bytes = Encoding.ASCII.GetBytes(script);
            await stream.WriteAsync(bytes, linked.Token);

            // Lecture jusqu'au délimiteur '|' qui sépare la valeur de la
            // chaîne d'erreur. La réponse peut arriver en plusieurs chunks
            // selon la taille — on accumule jusqu'à voir le pipe.
            var sb  = new StringBuilder();
            var buf = new byte[4096];
            while (true) {
                var read = await stream.ReadAsync(buf, linked.Token);
                if (read == 0) break; // socket fermé
                sb.Append(Encoding.ASCII.GetString(buf, 0, read));
                if (sb.ToString().Contains('|')) break;
            }

            return ParseResponse(sb.ToString());
        }

        /// <summary>Test de connexion : ouvre/ferme un socket sans envoyer de script.</summary>
        public async Task<bool> PingAsync(CancellationToken ct) {
            try {
                using var client = new TcpClient();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(_host, _port, linked.Token);
                return client.Connected;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Parse une réponse brute du serveur TheSkyX et retourne la valeur
        /// de la variable JS <c>Out</c>. Public au niveau internal pour
        /// permettre les tests unitaires sans matériel.
        ///
        /// Conventions :
        ///   - <c>"42|No error. Error = 0."</c> → "42"
        ///   - <c>"|No error. Error = 0."</c>   → "" (réponse vide légitime)
        ///   - <c>"|Internal error. Error = 207."</c> → throw, message
        ///     contient "Error = 207" et reflète le diagnostic TheSkyX.
        ///   - réponse sans pipe → throw avec mention "non parseable".
        /// </summary>
        internal static string ParseResponse(string raw) {
            if (raw == null)
                throw new InvalidOperationException("Réponse TheSkyX nulle (non parseable).");

            var pipe = raw.IndexOf('|');
            if (pipe < 0) {
                throw new InvalidOperationException(
                    $"Réponse TheSkyX non parseable: '{raw}'");
            }

            var output = raw.Substring(0, pipe);
            var status = raw.Substring(pipe + 1);

            if (!status.Contains("Error = 0")) {
                // On garde le statut brut (ex. "Internal error. Error = 207.")
                // pour faciliter le diagnostic côté logs NINA.
                throw new InvalidOperationException(
                    $"TheSkyX a retourné une erreur: {status.Trim()}");
            }

            return output.Trim();
        }
    }
}
