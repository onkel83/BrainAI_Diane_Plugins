using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// ShutdownManagerPlugin v2.0.0 - Pattern Edition
    /// Handhabt das sichere Beenden mit asynchroner Speicher-Bestätigung.
    /// Nutzt das BioCommand-Struct für das Pre-Gating (Admin-Schutz) und die Auto-Hilfe.
    /// </summary>
    public class ShutdownManagerPlugin : IBioPlugin
    {
        public string PluginID => "SHUTDOWN-MOD";
        public string Version => "2.0.0";

        private const string LOG_COMP = "SHUTDOWN";
        private bool _isWaitingForConfirmation = false;
        private bool _isPerformingSave = false;

        /// <summary>
        /// Initialisiert den Shutdown-Manager und etabliert die Sicherheits-Ebene für den System-Exit.
        /// Bindet den asynchronen Nachrichten-Router an den BioBus, registriert die administrativen 
        /// Abschaltbefehle sowie die Nutzer-Bestätigungsaliase und sendet eine Bereitschaftsmeldung 
        /// an den System-Audit zur Überwachung der Shutdown-Integrität.
        /// </summary>
        public void OnLoad()
        {
            BioBus.OnPacket += HandleTraffic;
            RegisterCommands();
            BioBus.Send(LOG_COMP, "INFO", $"Shutdown-Manager v{Version} online.");
        }

        /// <summary>
        /// Registriert die kausalen Systembefehle für das Herunterfahren sowie die dynamischen 
        /// Bestätigungs-Synonyme (Ja/Nein) über den zentralen BioModulHelper.
        /// Implementiert ein striktes Gating: Kernbefehle wie 'QUIT' oder 'QQUIT' sind an Admin-Rechte 
        /// gebunden (true), während die einfachen Bestätigungs-Aliase für laufende Abfragen 
        /// für jeden Nutzer zugänglich bleiben (false), um die Systeminteraktion nicht zu blockieren.
        /// </summary>
        private void RegisterCommands()
        {
            var commands = new List<BioCommand>
            {
                new BioCommand("QUIT", "SYS_QUIT_REQ", true, false, 0, "", "Leitet das sichere Herunterfahren des Systems ein."),
                new BioCommand("EXIT", "SYS_QUIT_REQ", true, false, 0, "", "Alternativer Befehl für das Herunterfahren des Systems."),
                new BioCommand("QQUIT", "SYS_QUIT_FORCE_PROMPT", true, false, 0, "", "Erzwingt die Speicher-Abfrage vor dem Herunterfahren (unabhängig von der Policy).")
            };

            string[] y = { "JA", "J", "YES", "Y" };
            string[] n = { "NEIN", "N", "NO" };

            foreach (var cmd in y)
                commands.Add(new BioCommand(cmd, "SYS_QUIT_CONFIRM_YES", false, false, 0, "", "Bestätigt eine laufende Systemabfrage mit JA."));

            foreach (var cmd in n)
                commands.Add(new BioCommand(cmd, "SYS_QUIT_CONFIRM_NO", false, false, 0, "", "Lehnt eine laufende Systemabfrage ab (NEIN)."));

            BioModulHelper.RegisterCommands(PluginID, commands.ToArray());
        }

        /// <summary>
        /// Führt den deterministischen Teardown des Shutdown-Managers durch.
        /// Meldet den asynchronen Event-Listener vom BioBus ab, um Speicherlecks und 
        /// Ghost-Prozesse während der finalen System-Terminierung physisch auszuschließen (Zero Footprint).
        /// </summary>
        public void OnUnload() => BioBus.OnPacket -= HandleTraffic;

        /// <summary>
        /// Der asynchrone Nachrichten-Router und Zustands-Wächter für den System-Exit.
        /// Evaluiert eingehende Pakete nach einer strikten O(1)-Logik (Switch-Statement) und steuert die 
        /// Kausalitäts-Kette des Shutdowns. Überwacht im aktiven Speichermodus (_isPerformingSave) 
        /// gezielt die Rückmeldungen der Persistenz-Layer (SWARM/BIOSWARM). 
        /// Erhält zudem die Systemintegrität, indem bei einer ausstehenden Bestätigung (_isWaitingForConfirmation) 
        /// jede nicht-valide Eingabe deterministisch als Abbruch des Shutdown-Prozesses gewertet wird.
        /// </summary>
        /// <param name="pkt">Das abgefangene Datenpaket vom BioBus.</param>
        private void HandleTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;

            if (pkt.Header == "SYS_HELP_SYNC")
            {
                RegisterCommands();
                return;
            }

            // Asynchrone Rückkopplung vom Speicher-Subsystem (Indikator für Industrial Sovereignty)
            if (_isPerformingSave && pkt.Header == "SUCCESS" && (pkt.Sender == "SWARM" || pkt.Sender == "BIOSWARM"))
            {
                BioBus.Send(LOG_COMP, "SUCCESS", "Speichervorgang bestätigt. Beende System...");
                FinalHardExit();
                return;
            }

            switch (pkt.Header)
            {
                case "SYS_QUIT_REQ":
                    ProcessShutdownLogic();
                    break;
                case "SYS_QUIT_FORCE_PROMPT":
                    AskForSave();
                    break;
                case "SYS_QUIT_CONFIRM_YES":
                    if (_isWaitingForConfirmation) ExecuteShutdown(true);
                    break;
                case "SYS_QUIT_CONFIRM_NO":
                    if (_isWaitingForConfirmation) ExecuteShutdown(false);
                    break;
                case "CMD_REQ":
                    // Validierung des Nutzer-Kontexts während der kritischen Shutdown-Abfrage
                    if (_isWaitingForConfirmation && !IsAllowedInput(pkt.Get(0)))
                    {
                        _isWaitingForConfirmation = false;
                        BioBus.Send(LOG_COMP, "INFO", "Shutdown abgebrochen.");
                    }
                    break;
            }
        }

        /// <summary>
        /// Realisiert eine deterministische White-List-Prüfung für Nutzerinteraktionen während des Shutdown-Prompts.
        /// Verhindert, dass das System in einem undefinierten Wartezustand verharrt, indem es Eingaben 
        /// strikt gegen die Menge der erlaubten Bestätigungs-Aliase und System-Exit-Befehle prüft.
        /// Arbeitet Case-Insensitive und Normalisiert die Eingaben (Trim), um maximale Robustheit zu garantieren.
        /// </summary>
        /// <param name="inp">Der zu prüfende Befehls-String des Nutzers.</param>
        /// <returns>True, wenn die Eingabe Teil des validen Interaktions-Kontexts ist.</returns>
        private bool IsAllowedInput(string inp)
        {
            string[] allowed = { "JA", "J", "YES", "Y", "NEIN", "N", "NO", "QUIT", "EXIT", "QQUIT" };
            return allowed.Contains(inp.ToUpper().Trim());
        }

        /// <summary>
        /// Evaluiert die zentrale Abschalt-Policy der Architektur und leitet die entsprechende Kausalitätskette ein.
        /// Löst die Konfiguration 'SHUTDOWN_POLICY' deterministisch auf:
        /// - 'SAVE': Erzwingt die asynchrone Persistenzierung ohne Rückfrage.
        /// - 'NOSAVE': Initiert den sofortigen System-Exit ohne Datensicherung.
        /// - 'PROMPT' (Default): Übergibt die Entscheidungsebene an den Nutzer (HMI-Interaktion).
        /// </summary>
        private void ProcessShutdownLogic()
        {
            string pol = BioConfig.Instance.Get("SHUTDOWN_POLICY", "PROMPT").ToUpper();
            if (pol == "SAVE") ExecuteShutdown(true);
            else if (pol == "NOSAVE") ExecuteShutdown(false);
            else AskForSave();
        }

        /// <summary>
        /// Etabliert einen interaktiven Wartezustand für die Nutzer-Bestätigung.
        /// Setzt das interne Status-Flag '_isWaitingForConfirmation', sendet eine Sprachausgabe-Anforderung 
        /// an den DIANE-Kern (HMI) und protokolliert die Warnung im System-Audit.
        /// Das System verharrt in diesem Zustand, bis ein validierter Befehl über den BioBus empfangen wird.
        /// </summary>
        private void AskForSave()
        {
            _isWaitingForConfirmation = true;
            BioBus.Send("DIANE", "TALK", "Soll der neuronale Zustand vor dem Beenden gespeichert werden? (Y/N)");
            BioBus.Send(LOG_COMP, "WARN", "Warte auf Bestätigung...");
        }

        /// <summary>
        /// Orchestriert den asynchronen Herunterfahr-Prozess und das Persistenz-Gating.
        /// Bei angefordertem Speichervorgang wird ein 'SWARM_SAVE'-Signal emittiert und ein paralleler 
        /// 5-Sekunden-Watchdog (Task.Delay) gestartet. Sollte das Speicher-Subsystem innerhalb dieses 
        /// Fensters keine Erfolgsmeldung liefern, greift der Fail-Safe und erzwingt den FinalHardExit, 
        /// um die System-Terminierung unter allen Umständen zu garantieren.
        /// </summary>
        /// <param name="shouldSave">Indikator, ob die neuronalen Gewichte vor der Terminierung gesichert werden sollen.</param>
        private async void ExecuteShutdown(bool shouldSave)
        {
            _isWaitingForConfirmation = false;

            if (shouldSave)
            {
                BioBus.Send(LOG_COMP, "INFO", "Initiiere Speichervorgang...");
                _isPerformingSave = true;

                BioBus.Send(PluginID, "SWARM_SAVE", "SHUTDOWN_AUTO");

                await Task.Delay(5000);
                if (_isPerformingSave)
                {
                    BioBus.Send(LOG_COMP, "ERRN", "Timeout beim Speichern! Beende erzwungen.");
                    FinalHardExit();
                }
            }
            else
            {
                BioBus.Send(LOG_COMP, "WARN", "Beenden ohne Speichern.");
                FinalHardExit();
            }
        }

        /// <summary>
        /// Der definitive 'Point of No Return' der Diane-Architektur.
        /// Signalisiert dem Systembus den finalen Benutzer-Exit und erzwingt nach einer 
        /// kurzen Latenzzeit (250ms Grace Period), um die vollständige Paket-Auslieferung 
        /// auf dem Bus sicherzustellen, die physische Terminierung des Betriebssystem-Prozesses (Environment.Exit).
        /// </summary>
        private void FinalHardExit()
        {
            BioBus.Send("SYS", "SHUTDOWN", "USER_EXIT");
            System.Threading.Thread.Sleep(250);
            Environment.Exit(0);
        }
    }
}