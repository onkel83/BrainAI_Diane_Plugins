using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// LoggerPlugin v2.0.0 - Secure Pattern Edition.
    /// Asynchroner Disk-Logger. Nutzt das BioCommand-Struct für zentrales Pre-Gating 
    /// und behält ein lokales Admin-Flag als doppelte Sicherheitsschicht.
    /// </summary>
    public class LoggerPlugin : IBioPlugin
    {
        public string PluginID => "LOGGER";
        public string Version => "2.0.0";

        private string _logPath = "";
        private readonly BlockingCollection<string> _fileQueue = new BlockingCollection<string>();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private bool _isAdmin = false;

        public BioLogLevel MinLevel { get; set; } = BioLogLevel.Info;

        /// <summary>
        /// Initialisiert das LoggerPlugin und etabliert die physische Dateisystem-Schnittstelle.
        /// Löst die Konfiguration (Pfad und initiales Loglevel) deterministisch auf, bindet den Listener an den BioBus 
        /// und startet einen dedizierten, langlaufenden Hintergrund-Thread (TaskCreationOptions.LongRunning). 
        /// Dadurch wird garantiert, dass langsame Festplatten-I/O-Operationen niemals den primären XAGI-Hauptthread blockieren.
        /// </summary>
        public void OnLoad()
        {
            string fileName = BioConfig.Instance.Get("LOGGER_FILE", "diane_runtime.log");
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

            if (Enum.TryParse(BioConfig.Instance.Get("LOGGER_LEVEL", "Info"), true, out BioLogLevel lvl)) MinLevel = lvl;

            BioBus.OnPacket += HandleBusPacket;
            RegisterCommands();
            Task.Factory.StartNew(() => ProcessFileQueue(), TaskCreationOptions.LongRunning);
        }

        /// <summary>
        /// Registriert die laufzeitspezifischen Konfigurationsbefehle über den zentralen BioModulHelper.
        /// Etabliert den Befehl 'LOGLEVEL' mit strikter Admin-Anforderung (true). Die Ausführung selbst bleibt 
        /// synchron (false), da hierbei lediglich ein interner Enum-Status im Speicher (O(1)-Zeit) überschrieben wird, 
        /// was keinen asynchronen Overhead rechtfertigt.
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("LOGLEVEL", "SYS_SET_LOGLEVEL", true, false, 1, "<Debug|Info|Warn|Errn>", "Ändert das Detail-Level für das Hintergrund-Dateiarchiv (diane_runtime.log).")
            );
        }

        /// <summary>
        /// Führt den deterministischen und thread-sicheren Teardown des Loggers beim Entladen durch.
        /// Meldet den Event-Listener vom BioBus ab, versiegelt die asynchrone Warteschlange (CompleteAdding), 
        /// damit keine neuen Lognachrichten mehr aufgenommen werden, und triggert den Cancel-Token, 
        /// um den I/O-Thread sauber herunterzufahren (Graceful Shutdown & Zero Footprint).
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleBusPacket;
            _fileQueue.CompleteAdding();
            _cts.Cancel();
        }

        /// <summary>
        /// Der asynchrone Nachrichten-Router und die passive Interzeptions-Schicht des Loggers.
        /// Evaluiert eingehende BioBus-Pakete nach einer strikten O(1)-Logik (Switch-Statement).
        /// Verarbeitet System-Events (Hilfe-Sync, Auth-Status, Loglevel-Änderungen) deterministisch im vorderen Block.
        /// Alle nicht explizit abgefangenen Pakete (Default-Block) fallen in die passive Log-Verarbeitung, 
        /// wo sie anhand ihres Headers klassifiziert und bei Erreichen des MinLevels asynchron auf die Festplatte geschrieben werden.
        /// </summary>
        /// <param name="pkt">Das abgefangene Datenpaket vom Systembus.</param>
        private void HandleBusPacket(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;

            switch (pkt.Header)
            {
                case "SYS_HELP_SYNC":
                    RegisterCommands();
                    break;

                case "AUTH_RES":
                    _isAdmin = (pkt.Get(0) == "SUCCESS");
                    break;

                case "SYS_SET_LOGLEVEL":
                    if (!_isAdmin)
                    {
                        BioBus.Send(PluginID, "DENIED", "Zugriff verweigert. Admin-Rechte erforderlich, um das Disk-Loglevel zu ändern.");
                        return;
                    }
                    string newLvl = pkt.Get(0);
                    if (Enum.TryParse(newLvl, true, out BioLogLevel parsedLevel))
                    {
                        MinLevel = parsedLevel;
                        BioBus.Send(PluginID, "SUCCESS", $"Datei-Loglevel sicher auf {MinLevel} geändert.");
                    }
                    else BioBus.Send(PluginID, "WARN", $"Unbekanntes Loglevel: '{newLvl}'. Erlaubt: Debug, Info, Warn, Errn."); 
                    break;

                default:
                    BioLogLevel level = pkt.Header.ToUpper() switch
                    {
                        "DEBUG" => BioLogLevel.Debug,
                        "WARN" => BioLogLevel.Warn,
                        "ERRN" => BioLogLevel.Errn,
                        "ERROR" => BioLogLevel.Errn,
                        "SUCCESS" => BioLogLevel.Info,
                        "FATAL" => BioLogLevel.Errn,
                        _ => BioLogLevel.Info
                    };

                    if (level >= MinLevel)
                    {
                        string payload = string.Join(" | ", pkt.Data);
                        if (level == BioLogLevel.Info && pkt.Header != "INFO") payload = $"[{pkt.Header}] {payload}";
                        WriteEntry(level, pkt.Sender, payload);
                    }
                    break;
            }
        }

        /// <summary>
        /// Formatiert und injiziert einen neuen Log-Eintrag in die asynchrone Warteschlange.
        /// Nutzt hochpräzise Zeitstempel und deterministisches Padding zur Erzeugung eines strikt tabellarischen 
        /// Formats, das sowohl für Menschen lesbar als auch maschinell optimal parsebar ist.
        /// Die Übergabe an die interne BlockingCollection erfolgt extrem schnell und blockiert den aufrufenden Thread niemals.
        /// </summary>
        /// <param name="level">Die Kritikalität des Eintrags (z.B. INFO, WARN, ERRN).</param>
        /// <param name="component">Die exakte Modul-ID (Sender), die das Event ausgelöst hat.</param>
        /// <param name="message">Die zu protokollierende Kernnachricht (Payload).</param>
        private void WriteEntry(BioLogLevel level, string component, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = $"[{level.ToString().ToUpper()}]".PadRight(7);
            _fileQueue.Add($"[{timestamp}] {levelStr} [{component.PadRight(10)}] {message}");
        }

        /// <summary>
        /// Der isolierte I/O-Worker-Thread für das physische Datei-Logging.
        /// Iteriert über die BlockingCollection via GetConsumingEnumerable(), wodurch der Thread extrem 
        /// CPU-schonend schläft, solange keine neuen Einträge vorhanden sind, und sofort aufwacht, wenn Daten eintreffen.
        /// Implementiert eine harte Try-Catch-Isolation pro Schreibvorgang: Sollte die Logdatei temporär durch das 
        /// Betriebssystem oder einen Administrator gesperrt sein (File Lock), wird die Exception geschluckt, 
        /// um einen Absturz des kritischen Worker-Threads deterministisch zu verhindern.
        /// </summary>
        private void ProcessFileQueue()
        {
            foreach (var entry in _fileQueue.GetConsumingEnumerable())
            {
                try { File.AppendAllText(_logPath, entry + Environment.NewLine); }
                catch { }
            }
        }
    }
}