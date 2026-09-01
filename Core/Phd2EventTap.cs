using NINA.Core.Utility;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    /// <summary>
    /// A second, read-only connection to PHD2's event server, open only for the duration of the
    /// Guiding Assistant measurement.
    ///
    /// Why a second connection: N.I.N.A.'s own PHD2Guider already holds the primary connection this
    /// plugin uses for every command (get_calibrated, min-move readback, etc.), but it swallows
    /// "Alert" events entirely and does not expose "StarLost" as a public event - it only folds
    /// both into an internal AppState. Detecting a genuinely lost star or an error alert during the
    /// unattended measurement run is a safety property worth keeping faithfully, and PHD2's server
    /// is explicitly designed for multiple simultaneous clients (that's how e.g. EKOS and N.I.N.A.
    /// coexist), so a second passive listener is safe.
    /// </summary>
    internal sealed class Phd2EventTap : IDisposable {
        private readonly TcpClient client = new TcpClient();
        private NetworkStream stream;
        private readonly StringBuilder buffer = new StringBuilder();

        public int StarLostCount { get; private set; }
        public string LastErrorAlert { get; private set; }

        public async Task ConnectAsync(string host, int port, CancellationToken token) {
            using (token.Register(() => { try { client.Close(); } catch { } })) {
                await client.ConnectAsync(host, port);
            }
            stream = client.GetStream();
        }

        /// <summary>Drains whatever is waiting on the socket and updates the counters. Call this roughly once a second; never throws.</summary>
        public void Pump() {
            try {
                if (stream == null || !client.Connected) return;
                while (stream.DataAvailable) {
                    var buf = new byte[8192];
                    var n = stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    buffer.Append(Encoding.UTF8.GetString(buf, 0, n));
                }

                var text = buffer.ToString();
                int newlineIndex;
                while ((newlineIndex = text.IndexOf('\n')) >= 0) {
                    var line = text.Substring(0, newlineIndex).Trim();
                    text = text.Substring(newlineIndex + 1);
                    if (line.Length > 0) HandleLine(line);
                }
                buffer.Clear();
                buffer.Append(text);
            } catch {
                // A dropped tap is not fatal to the run - it only degrades how quickly we notice
                // trouble, and the GA run's own PHD2 command calls (via the primary connection)
                // will surface a real disconnect on their own.
            }
        }

        private void HandleLine(string line) {
            JObject obj;
            try { obj = JObject.Parse(line); } catch { return; }
            var evt = (string)obj["Event"];
            if (evt == null) return;

            if (evt == "StarLost") {
                StarLostCount++;
                Logger.Warning($"StarLost during GA run (#{StarLostCount}): {(string)obj["Status"]}");
            } else if (evt == "Alert" && string.Equals((string)obj["Type"], "error", StringComparison.OrdinalIgnoreCase)) {
                LastErrorAlert = (string)obj["Msg"];
                Logger.Error($"PHD2 alert during GA run: {LastErrorAlert}");
            }
        }

        public void Dispose() {
            try { stream?.Dispose(); } catch { }
            try { client?.Close(); } catch { }
        }
    }
}
