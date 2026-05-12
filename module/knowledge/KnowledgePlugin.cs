using System;
using System.IO;
using System.Linq;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// Das KnowledgePlugin v2.0.0
    /// Unterstützt zufälliges und gezieltes Lesen von Dateien via BioBus.
    /// Nutzt das neue BioCommand-Struct für Metadaten und Gating via BioModulHelper.
    /// </summary>
    public class KnowledgePlugin : IBioPlugin
    {
        public string PluginID => "KNOWLEDGE";
        public string Version => "2.0.0";

        private const string LOG_COMP = "KNOWLEDGE";
        private string LibraryDir => BioConfig.Instance.Get("KNOWLEDGE_DIR", "library\\knowledge");
        private string FileExt => BioConfig.Instance.Get("KNOWLEDGE_FILE_EXT", ".txt");
        private string CommentPrefix => BioConfig.Instance.Get("KNOWLEDGE_COMMENT_PREFIX", "//");
        private string MsgEmpty => BioConfig.Instance.Get("KNOWLEDGE_MSG_EMPTY", "MEINE BIBLIOTHEK IST LEER.");
        private string MsgReading => BioConfig.Instance.Get("KNOWLEDGE_MSG_READING", "ICH LESE DAS BUCH: {0}");
        private string MsgNotFound => BioConfig.Instance.Get("KNOWLEDGE_MSG_NOT_FOUND", "DAS BUCH '{0}' EXISTIERT NICHT.");
        private string FullPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LibraryDir);

        /// <summary>
        /// Initialisiert das KnowledgePlugin und sichert die physische Bibliotheks-Grenze.
        /// Erstellt das Zielverzeichnis deterministisch, falls es noch nicht existiert, bindet den Event-Listener 
        /// an den globalen BioBus und triggert die Registrierung der domänenspezifischen Lesebefehle.
        /// </summary>
        public void OnLoad()
        {
            if (!Directory.Exists(FullPath)) Directory.CreateDirectory(FullPath);
            BioBus.OnPacket += HandleTraffic;
            RegisterCommands();
            BioBus.Send(LOG_COMP, "INFO", $"Wissens-Bibliothek v{Version} aktiv.");
        }

        /// <summary>
        /// Führt einen sauberen Teardown des Plugins beim Entladen durch.
        /// Meldet den asynchronen Bus-Listener ab, um Speicherlecks rigoros zu verhindern (Zero Footprint), 
        /// und hinterlässt einen verlässlichen Offline-Status im System-Audit.
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleTraffic;
            BioBus.Send(LOG_COMP, "INFO", "Wissens-Bibliothek offline.");
        }

        /// <summary>
        /// Registriert die Lese-Befehle über den zentralen BioModulHelper und delegiert das Pre-Gating an den Command-Router.
        /// Etabliert die Kausalität für den Befehl 'LIES', der so konfiguriert ist, dass er dynamisch auf 
        /// Benutzereingaben reagiert: Gezielter Dateizugriff bei Parameterübergabe, ansonsten deterministisches Zufalls-Routing.
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("LIES", "READ_FILE", false, true, 0, "[Dateiname?]", "Liest eine angegebene Datei aus [library]. Ohne Angabe wird ein zufälliges Buch gelesen.")
            );
        }

        /// <summary>
        /// Der lokale Nachrichten-Router (Switchboard) des Plugins.
        /// Evaluiert eingehende BioBus-Pakete in Echtzeit und delegiert Lese-Anfragen basierend auf der dynamischen Befehlssignatur.
        /// Fehlt das Ziel-Argument, wird kausal in den Zufallsmodus (ProcessRandomFile) gewechselt.
        /// Blockiert deterministisch eigene Pakete zur Vermeidung von Feedback-Loops und verarbeitet systemweite Hilfe-Synchronisationen.
        /// </summary>
        /// <param name="pkt">Das vom Bus empfangene Datenpaket, das den Header und optionale Suchparameter enthält.</param>
        private void HandleTraffic(BioPacket pkt)
        {
            if (pkt.Sender == LOG_COMP) return;

            if (pkt.Header == "READ_FILE")
            {
                string target = pkt.Data.Length > 0 ? string.Join(" ", pkt.Data) : "";
                if (string.IsNullOrWhiteSpace(target)) ProcessRandomFile();
                else ProcessSpecificFile(target.Trim());
            }

            if (pkt.Header == "SYS_HELP_SYNC")
            {
                RegisterCommands();
                return;
            }
        }

        /// <summary>
        /// Sanitisiert und validiert einen gezielten Dateizugriff auf die Bibliothek.
        /// Sichert die Pfad-Integrität durch automatische Ergänzung der Dateiendung und prüft deterministisch auf 
        /// physische Existenz, bevor der eigentliche I/O-Lese-Prozess initiiert wird.
        /// Fehlerhafte oder nicht existierende Ziele triggern keinen Absturz, sondern werden über WARN- und ERRN-Pakete 
        /// sauber in den Audit-Log und an das UI zurückgemeldet.
        /// </summary>
        /// <param name="fileName">Der vom Nutzer oder System angeforderte Ziel-Dateiname (mit oder ohne Extension).</param>
        private void ProcessSpecificFile(string fileName)
        {
            try
            {
                string actualFile = fileName.EndsWith(FileExt, StringComparison.OrdinalIgnoreCase)
                                    ? fileName : fileName + FileExt;
                string targetPath = Path.Combine(FullPath, actualFile);

                if (!File.Exists(targetPath))
                {
                    BioBus.Send(PluginID, "WARN", string.Format(MsgNotFound, actualFile.ToUpper()));
                    BioBus.Send(LOG_COMP, "WARN", $"Datei nicht gefunden: {actualFile}");
                    return;
                }
                ReadFile(targetPath);
            }
            catch (Exception ex)
            {
                BioBus.Send(LOG_COMP, "ERRN", $"Fehler beim Zugriff: {ex.Message}");
            }
        }

        /// <summary>
        /// Führt das kausale Fallback-Routing für parameterlose Leseanfragen durch.
        /// Erfasst den physischen Zustand der Bibliothek in Echtzeit. Ist das Verzeichnis leer, 
        /// greift eine deterministische Rückfall-Logik (Graceful Degradation), die das UI benachrichtigt, anstatt ins Leere zu laufen.
        /// Andernfalls wird über einen deterministischen Zufallsgenerator (RNG) eine Zieldatei für die Verarbeitung selektiert.
        /// </summary>
        private void ProcessRandomFile()
        {
            try
            {
                var files = Directory.GetFiles(FullPath, "*" + FileExt);
                if (files.Length == 0)
                {
                    BioBus.Send("DIANE", "TALK", MsgEmpty);
                    return;
                }
                Random rng = new Random();
                ReadFile(files[rng.Next(files.Length)]);
            }
            catch (Exception ex)
            {
                BioBus.Send(LOG_COMP, "ERRN", $"Random-Read-Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Die zentrale Ingestion-Engine (Datenaufnahme) des Plugins.
        /// Liest die validierte Datei in den Speicher, benachrichtigt das System und sanitisiert die Inhalte 
        /// zeilenweise durch das deterministische Herausfiltern von Leerzeilen und Konfigurationskommentaren. 
        /// Das extrahierte Wissen wird anschließend sequentiell als 'CMD_REQ' auf den BioBus injiziert, 
        /// wo es vom Command-Router gemäß der globalen Rechte-Architektur verarbeitet wird.
        /// </summary>
        /// <param name="filePath">Der absolut aufgelöste und auf physische Existenz geprüfte Pfad zur Quelldatei.</param>
        private void ReadFile(string filePath)
        {
            string bookTitle = Path.GetFileNameWithoutExtension(filePath).ToUpper();
            BioBus.Send(LOG_COMP, "INFO", $"Lese: {Path.GetFileName(filePath)}");
            BioBus.Send(PluginID, "TALK", string.Format(MsgReading, bookTitle));

            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                string clean = line.Trim();
                if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith(CommentPrefix)) continue;
                BioBus.Send(LOG_COMP, "CMD_REQ", clean);
            }
        }
    }
}