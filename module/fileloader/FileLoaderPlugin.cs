using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// FileLoaderPlugin v2.0.0
    /// Lädt Trainingsdateien. Verlässt sich nun auf das globale Session-Gating des Command-Routers,
    /// anstatt eigene Admin-Rechte zu erzwingen (Keine Backdoor mehr).
    /// </summary>
    public class FileLoaderPlugin : IBioPlugin
    {
        public string PluginID => "FILE_LOADER";
        public string Version => "2.0.0";

        private const string LOG_COMP = "FILE-LDR";
        private string _inboxPath = "";

        /// <summary>
        /// Initialisiert das FileLoader-Plugin und etabliert die physikalische Dateisystem-Grenze.
        /// Löst den Pfad für die Training-Inbox auf (erstellt diese bei Bedarf deterministisch), 
        /// abonniert den BioBus für den Nachrichtenverkehr und triggert die Registrierung der lokalen Befehle.
        /// </summary>
        public void OnLoad()
        {
            _inboxPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                 BioConfig.Instance.Get("TRAINING_INBOX_DIR", "training_data"));

            if (!Directory.Exists(_inboxPath)) Directory.CreateDirectory(_inboxPath);
            BioBus.OnPacket += HandleTraffic;
            RegisterCommands();
            BioBus.Send(PluginID, "INFO", "Secure File-Loader v2.0.0 online.");
        }

        /// <summary>
        /// Definiert und registriert die dateibasierten Systembefehle (LOAD, FILES, PURGE) über den zentralen BioModulHelper.
        /// Nutzt das strikte Pre-Gating des Command-Routers: Befehle wie LOAD erben den aktuellen Rechte-Kontext des Senders, 
        /// während kritische Befehle wie PURGE hart auf Admin-Rechte (true) limitiert werden.
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("LOAD", "FILE_LOAD_REQ", false, true, 0, "[Dateiname|ALL]", "Lädt Trainingsdateien und führt sie im aktuellen Rechte-Kontext aus."),
                new BioCommand("FILES", "FILE_LIST_REQ", false, false, 0, "", "Zeigt alle verfügbaren Trainingsdateien in der Inbox an."),
                new BioCommand("PURGE", "FILE_PURGE_REQ", true, false, 0, "", "Löscht unwiderruflich alle Trainingsdateien in der Inbox.")
            );
        }

        /// <summary>
        /// Führt die saubere Dekonstruktion des Moduls beim Entladen durch.
        /// Meldet den asynchronen Event-Listener vom globalen BioBus ab, 
        /// um hängende Referenzen und Speicherlecks konsequent zu verhindern (Zero Footprint).
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleTraffic;
        }

        /// <summary>
        /// Der lokale Nachrichten-Router (Switchboard) des Plugins.
        /// Evaluiert eingehende BioBus-Pakete nach O(1)-Logik und delegiert sie an die asynchronen Dateioperationen.
        /// Blockiert deterministisch selbstgesendete Pakete, um rekursive Feedback-Loops zu verhindern, 
        /// und verarbeitet globale Sync-Events (wie SYS_HELP_SYNC) zur Laufzeit.
        /// </summary>
        /// <param name="pkt">Das empfangene Datenpaket vom BioBus, das den auszuführenden Header und die Argumente enthält.</param>
        private async void HandleTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;

            switch (pkt.Header)
            {
                case "SYS_HELP_SYNC":
                    RegisterCommands();
                    break;
                case "FILE_LOAD_REQ":
                    await HandleLoadRequest(pkt.Get(0, "ALL"));
                    break;
                case "FILE_LIST_REQ":
                    ListFiles();
                    break;
                case "FILE_PURGE_REQ":
                    PurgeInbox();
                    break;
            }
        }

        /// <summary>
        /// Liest den aktuellen Zustand der physischen Training-Inbox aus und iteriert verzweigungsfrei über die vorhandenen Dateien.
        /// Feuert für jeden Treffer ein asynchrones DATA-Paket auf den BioBus, ohne den Speicher durch große Listen-Objekte zu belasten.
        /// </summary>
        private void ListFiles()
        {
            var files = Directory.GetFiles(_inboxPath, "*.txt");
            BioBus.Send(PluginID, "INFO", $"Inbox enthält {files.Length} Dateien.");
            foreach (var f in files)
            {
                BioBus.Send(PluginID, "DATA", Path.GetFileName(f));
            }
        }

        /// <summary>
        /// Führt eine kompromisslose, irreversible Bereinigung (State Wipe) der physischen Inbox durch.
        /// Nutzt isolierte Try-Catch-Blöcke pro Datei, um die Ausführung auch dann deterministisch fortzusetzen, 
        /// wenn einzelne Handles vom Betriebssystem gelockt sind. Sendet abschließend einen WARN-Header zur Auditierung.
        /// </summary>
        private void PurgeInbox()
        {
            var files = Directory.GetFiles(_inboxPath, "*.txt");
            foreach (var f in files)
            {
                try { File.Delete(f); } catch { }
            }
            BioBus.Send(PluginID, "WARN", $"Inbox geleert. {files.Length} Dateien entfernt.");
        }

        /// <summary>
        /// Evaluiert und routet Ladeanfragen deterministisch anhand des übergebenen Ziels.
        /// Löst Batch-Verarbeitungen ("ALL") auf oder validiert spezifische Dateipfade gegen die physische Inbox, 
        /// bevor die asynchrone Datenaufnahme (Ingestion) gestartet wird. Unbekannte Ziele werden direkt auf dem Bus abgewiesen.
        /// </summary>
        /// <param name="target">Der angeforderte Dateiname oder das "ALL"-Flag für eine vollständige Batch-Verarbeitung.</param>
        private async Task HandleLoadRequest(string target)
        {
            if (target.ToUpper() == "ALL")
            {
                var files = Directory.GetFiles(_inboxPath, "*.txt");
                foreach (var file in files) await ProcessFile(file);
            }
            else
            {
                string path = Path.Combine(_inboxPath, target.ToLower().EndsWith(".txt") ? target : target + ".txt");
                if (File.Exists(path)) await ProcessFile(path);
                else BioBus.Send(PluginID, "ERRN", $"Datei nicht gefunden: {target}");
            }
        }

        /// <summary>
        /// Führt die zeilenweise, konsumptive Verarbeitung einer Trainingsdatei durch.
        /// Parst den Inhalt, filtert Kommentare heraus und injiziert die rohen Befehle mit einem definierten Hardware-Takt (Delay) 
        /// als 'CMD_REQ' auf den BioBus. Da der harte Auto-Login entfernt wurde, erben diese Befehle nun strikt den aktuellen Rechte-Kontext des Senders.
        /// Nach erfolgreicher Verarbeitung wird die Quelldatei deterministisch gelöscht (Zero Footprint), 
        /// um redundante Statusänderungen physisch auszuschließen.
        /// </summary>
        /// <param name="path">Der absolute, validierte Dateipfad zur Quelldatei im System.</param>
        private async Task ProcessFile(string path)
        {
            string fileName = Path.GetFileName(path);
            try
            {
                string[] lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                    BioBus.Send(PluginID, "CMD_REQ", line.Trim());
                    await Task.Delay(30);
                }
                BioBus.Send(PluginID, "SUCCESS", $"Datei verarbeitet: {fileName}");
                await Task.Delay(200);
                if (File.Exists(path)) File.Delete(path);
                BioBus.Send(PluginID, "INFO", $"Datei gelöscht: {fileName}");
            }
            catch (Exception ex) { BioBus.Send(PluginID, "ERRN", $"Fehler {fileName}: {ex.Message}"); }
        }
    }
}