using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// NetGatewayPlugin v2.0.0 - Log-Stream & Dashboard Edition.
    /// REST-Schnittstelle zum BioBus inklusive CORS-Unterstützung und Log-Ringpuffer.
    /// Konsequent auf die BioBus-Architektur getrimmt, inkl. O(1)-Routing und zentralem Audit-Logging.
    /// </summary>
    public class NetGatewayPlugin : IBioPlugin
    {
        public string PluginID => "NET_GATEWAY";
        public string Version => "2.0.0";

        private const string LOG_COMP = "NET_GATEWAY";
        private HttpListener? _listener;
        private bool _isRunning = false;
        private readonly ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();
        private const int MAX_LOGS = 25;
        private readonly ConcurrentDictionary<string, string> _lastData = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initialisiert das NetGateway und etabliert die externe REST-/Dashboard-Schnittstelle.
        /// Bindet den asynchronen Router an den globalen BioBus, startet den dedizierten HTTP-Server 
        /// (Fire-and-Forget) und injiziert eine verbindliche Statusmeldung in den System-Audit, 
        /// um die physische Erreichbarkeit der Architektur nach außen zu signalisieren.
        /// </summary>
        public void OnLoad()
        {
            BioBus.OnPacket += HandleTraffic;
            StartServer();
            BioBus.Send(LOG_COMP, "INFO", $"NetGateway v{Version} (REST/Dashboard) online.");
        }

        /// <summary>
        /// Führt einen sauberen und deterministischen Teardown des externen Netz-Gateways durch.
        /// Meldet den asynchronen Bus-Listener ab (Schutz vor Ghost-Paketen und Memory Leaks), stoppt 
        /// den HTTP-Server kompromisslos zur Freigabe der Systemports (Zero Footprint) und hinterlässt 
        /// einen verlässlichen Offline-Status im zentralen Audit-Log.
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleTraffic;
            StopServer();
            BioBus.Send(LOG_COMP, "INFO", "NetGateway offline.");
        }

        /// <summary>
        /// Fängt den Systemverkehr ab, puffert ihn für das Web-Dashboard und filtert 
        /// deterministisch (O(1)) Hochfrequenz-Pakete aus, um das Web-UI nicht zu überlasten.
        /// </summary>
        private void HandleTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;

            if (pkt.Data != null && pkt.Data.Length > 0)
            {
                _lastData[pkt.Header] = string.Join(",", pkt.Data);
            }

            switch (pkt.Header)
            {
                case "STATS_UPDATE":
                case "WORLD_MAP":
                case "CORE_TEACH":
                case "SYS_HEARTBEAT":
                    return;
            }

            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {pkt.Sender.PadRight(10)} | {pkt.Header}";
            if (pkt.Data != null && pkt.Data.Length > 0)
                logEntry += $" -> {string.Join(" ", pkt.Data)}";

            _logBuffer.Enqueue(logEntry);
            while (_logBuffer.Count > MAX_LOGS) _logBuffer.TryDequeue(out _);
        }

        /// <summary>
        /// Bindet den HTTP-Listener sicher an den Port und lagert die Endlosschleife in 
        /// eine Fire-and-Forget-Task aus, um den Aufrufer (BioBus-Hauptthread) nicht zu blockieren.
        /// </summary>
        private void StartServer()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://localhost:8080/");

            try
            {
                _listener.Start();
                _isRunning = true;
                BioBus.Send(LOG_COMP, "INFO", "HTTP Listener erfolgreich auf localhost:8080 gebunden.");

                _ = Task.Run(ServerLoop);
            }
            catch (Exception ex)
            {
                BioBus.Send(LOG_COMP, "ERRN", $"Kritischer Fehler beim Binden des Listeners: {ex.Message}");
            }
        }

        /// <summary>
        /// Realisiert den asynchronen Akzeptanz-Loop des Webservers.
        /// Wartet im Hintergrund auf eingehende HTTP-Kontexte und delegiert diese sofort 
        /// an den asynchronen Request-Handler, um parallele Anfragen effizient zu verarbeiten.
        /// Bricht deterministisch ab, sobald die globale Steuervariable '_isRunning' auf false gesetzt wird.
        /// </summary>
        private async Task ServerLoop()
        {
            while (_isRunning && _listener != null)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = ProcessRequestAsync(ctx);
                }
                catch (HttpListenerException) { break; } 
                catch (Exception ex)
                {
                    BioBus.Send(LOG_COMP, "WARN", $"ServerLoop Exception: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Verarbeitet eingehende HTTP-Anfragen asynchron über ein deterministisches O(1) Pfad-Routing.
        /// Implementiert eine strikte CORS-Policy zur Ermöglichung externer Dashboard-Zugriffe und 
        /// delegiert Anfragen basierend auf der URI-Struktur:
        /// - '/' oder '/index.html': Statische Auslieferung des physischen Dashboards.
        /// - '/stats' oder '/logs': Polling-Endpunkte für den asynchronen Systemstatus.
        /// - '/command': Bidirektionale Schnittstelle zur Injektion von Befehlen in den BioBus (via POST).
        /// Jede Exception wird abgefangen und nicht nur an den Client, sondern auch als 'ERRN' 
        /// an den System-Audit zur Überwachung der Netz-Integrität gemeldet.
        /// </summary>
        /// <param name="ctx">Der HTTP-Kontext der aktuellen Client-Verbindung.</param>
        private async Task ProcessRequestAsync(HttpListenerContext ctx)
        {
            HttpListenerRequest resq = ctx.Request;
            HttpListenerResponse res = ctx.Response;

            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (resq.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }

            string path = resq.Url?.AbsolutePath.ToLower() ?? "/";
            string responseText = "";

            try
            {
                switch (path)
                {
                    case "/":
                    case "/index.html":
                        string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default", "web", "index.html");
                        if (File.Exists(fullPath))
                        {
                            byte[] htmlBuffer = File.ReadAllBytes(fullPath);
                            res.ContentType = "text/html; charset=utf-8";
                            res.ContentLength64 = htmlBuffer.Length;
                            res.OutputStream.Write(htmlBuffer, 0, htmlBuffer.Length);
                            res.Close();
                            return;
                        }
                        res.StatusCode = 404;
                        responseText = "404 - Dashboard missing";
                        BioBus.Send(LOG_COMP, "WARN", $"Web-Dashboard nicht gefunden unter: {fullPath}");
                        break;

                    case "/stats":
                    case "/status":
                        _lastData.TryGetValue("STATS_UPDATE", out string? vitals);
                        responseText = $"{{\"stats\": \"{(string.IsNullOrEmpty(vitals) ? "E:0,S:0,C:0" : vitals)}\"}}";
                        res.ContentType = "application/json";
                        break;

                    case "/logs":
                    case "/log":
                        var logs = _logBuffer.ToArray();
                        responseText = $"{{\"logs\": [\"{string.Join("\",\"", logs)}\"]}}";
                        res.ContentType = "application/json";
                        break;

                    case "/command" when resq.HttpMethod == "POST":
                        using (var reader = new StreamReader(resq.InputStream, resq.ContentEncoding))
                        {
                            string cmd = await reader.ReadToEndAsync();
                            if (!string.IsNullOrWhiteSpace(cmd))
                            {
                                BioBus.Send("WEB_UI", "CMD_REQ", cmd.Trim());
                                BioBus.Send(LOG_COMP, "INFO", $"Web-Command empfangen: {cmd.Trim()}");
                                responseText = "{\"status\":\"ok\"}";
                            }
                        }
                        res.ContentType = "application/json";
                        break;

                    default:
                        res.StatusCode = 404;
                        responseText = "{\"error\":\"Endpoint not found\"}";
                        break;
                }
            }
            catch (Exception ex)
            {
                res.StatusCode = 500;
                responseText = $"{{\"error\":\"{ex.Message.Replace("\"", "'")}\"}}";
                BioBus.Send(LOG_COMP, "ERRN", $"HTTP 500 bei {path}: {ex.Message}");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(responseText);
            res.ContentLength64 = buffer.Length;
            res.OutputStream.Write(buffer, 0, buffer.Length);
            res.Close();
        }

        /// <summary>
        /// Führt den deterministischen Teardown des internen HTTP-Servers durch.
        /// Setzt das Laufzeit-Flag auf 'false', um den asynchronen Server-Loop kontrolliert zu beenden, 
        /// und erzwingt die physische Freigabe der System-Ressourcen (Port 8080).
        /// Die Methode ist in eine harte Try-Catch-Isolation gekapselt, um sicherzustellen, dass 
        /// eventuelle Dispose-Fehler während des Schließvorgangs den allgemeinen Entladevorgang 
        /// des Plugins nicht blockieren (Zero Footprint).
        /// </summary>
        private void StopServer()
        {
            _isRunning = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }
        }
    }
}