using System;
using System.Collections.Generic;
using System.Linq;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// CommandPlugin v2.0.0
    /// Nutzt BioCommand Structs für Pre-Gating, Arg-Checking und Auto-Help Generierung.
    /// </summary>
    public class CommandPlugin : IBioPlugin
    {
        public string PluginID => "CMD-MOD";
        public string Version => "2.0.0";

        private const string LOG_COMP = "CMD-MOD";
        private bool _isAdminLocally = false;

        private readonly Dictionary<string, BioCommand> _commands = new Dictionary<string, BioCommand>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initialisiert das Plugin beim Laden durch den PluginManager.
        /// Abonniert den globalen BioBus für eingehenden Traffic, registriert die essenziellen 
        /// Kern-Befehle im internen Dictionary und meldet den aktiven Status an das System.
        /// </summary>
        public void OnLoad()
        {
            BioBus.OnPacket += HandleBusTraffic;
            RegisterCoreCommands();
            BioBus.Send(PluginID, "INFO", "Dynamischer Command-Router (Pattern Edition) v2.0.0 online.");
        }

        /// <summary>
        /// Führt die saubere Dekonstruktion (Teardown) des Plugins durch, wenn es entladen wird.
        /// Meldet den Event-Listener vom BioBus ab und leert das Command-Dictionary, 
        /// um hängende Referenzen und Speicherlecks zu verhindern (Zero Footprint).
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleBusTraffic;
            _commands.Clear();
        }

        /// <summary>
        /// Registriert die essenziellen Kernbefehle (Core Commands) des Systems im lokalen Dictionary 
        /// und veröffentlicht diese anschließend über den BioModulHelper für die globale Systemhilfe.
        /// Definiert für jeden Befehl exakte Kausalitätsregeln, darunter erforderliche Admin-Rechte (Pre-Gating), 
        /// die minimale Argumenten-Anzahl und die korrekten Routing-Header für den BioBus.
        /// </summary>
        private void RegisterCoreCommands()
        {
            Register(new BioCommand("LOGIN", "AUTH_REQ", false, 1, "[Passwort]", "Loggt den Administrator ins System ein."));
            Register(new BioCommand("LOGOUT", "LOGOUT_REQ", false, 0, "", "Beendet die Admin-Sitzung."));
            Register(new BioCommand("HELP", "HELP_REQ", false, 0, "[Befehl]", "Öffnet die Systemhilfe."));
            Register(new BioCommand("HELPUI", "HELPUI_REQ", false, 0, "", "Öffnet das visuelle Hilfe-Interface."));
            Register(new BioCommand("PLUGINS", "PLUGIN_LIST_REQ", false, 0, "", "Listet alle aktiven Plugins auf."));
            Register(new BioCommand("DISPLAY", "SIDEBAR_REQ", false, 0, "", "Steuert die Anzeige der Seitenleiste."));
            Register(new BioCommand("LOAD", "FILE_LOAD_REQ", false, true, 1, "<Dateipfad>", "Lädt eine Datei in den Cortex."));
            Register(new BioCommand("SAVE", "SWARM_SAVE", true, true, 0, "", "Speichert den neuronalen Zustand des Kerns."));
            Register(new BioCommand("RELOAD", "CONFIG_LOAD", true, 0, "", "Lädt die System-Konfiguration neu."));
            Register(new BioCommand("INSTALL", "PLUGIN_LOAD", true, 1, "[DLL-Name]", "Lädt ein Plugin zur Laufzeit."));
            Register(new BioCommand("REMOVE", "PLUGIN_UNLOAD", true, 1, "[PluginID]", "Entfernt ein Plugin aus dem Speicher."));

            BioModulHelper.RegisterCommands(PluginID, _commands.Values.ToArray());
        }

        /// <summary>
        /// Fügt einen vorkonfigurierten Systembefehl (BioCommand) in das interne Routing-Dictionary ein.
        /// Etabliert die O(1)-Lookup-Basis, um eingehende Bus-Pakete deterministisch und ohne Such-Overhead 
        /// dem richtigen Command-Objekt zuzuordnen.
        /// </summary>
        /// <param name="cmd">Das vollständig definierte Befehlsobjekt, inklusive aller Metadaten für das Pre-Gating, Routing und die Hilfe-Generierung.</param>
        private void Register(BioCommand cmd)
        {
            _commands[cmd.Name] = cmd;
        }

        /// <summary>
        /// Der zentrale Event-Handler (Switchboard) für den eingehenden BioBus-Traffic. 
        /// Analysiert Pakete in Echtzeit, fängt rohe Command-Requests ('CMD_REQ') ab und erzwingt das 
        /// deterministische Pre-Gating (Prüfung von Admin-Rechten und Argumenten-Länge). 
        /// Validierte Befehle werden an die korrekten System-Header geroutet, unbekannte als Chat-Input ('CHAT_REQ') deklariert.
        /// Verarbeitet zudem dynamische Befehlsregistrierungen zur Laufzeit ('CMD_REGISTER') und 
        /// synchronisiert den lokalen Berechtigungs-State anhand von Auth-Responses ('AUTH_RES').
        /// </summary>
        /// <param name="pkt">Das vom Bus empfangene BioPacket, das den Header (Routing-Typ) und die Rohdaten (Payload) enthält.</param>
        private void HandleBusTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;
            if (pkt.Header == "CMD_REQ")
            {
                string raw = pkt.Get(0);
                if (string.IsNullOrWhiteSpace(raw)) return;

                string[] parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string cmdName = parts[0].ToUpper();
                string[] args = parts.Skip(1).ToArray();

                if (_commands.TryGetValue(cmdName, out BioCommand cmd))
                {
                    if (cmd.RequiresAdmin && !_isAdminLocally)
                    {
                        BioBus.Send(PluginID, "DENIED", $"Zugriff verweigert. Für '{cmd.Name}' werden Admin-Rechte benötigt.");
                        return;
                    }
                    if (args.Length < cmd.MinArgs)
                    {
                        BioBus.Send(PluginID, "WARN", $"Fehlende Argumente für '{cmd.Name}'. Format: {cmd.Name} {cmd.ArgFormat}");
                        return;
                    }
                    if (cmdName == "LOGIN")
                    {
                        BioBus.Send(PluginID, "AUTH_REQ", string.Join(" ", args));
                        return;
                    }
                    if (cmdName == "LOGOUT")
                    {
                        _isAdminLocally = false;
                        BioBus.Send(PluginID, "SYS", "LOGOUT");
                        BioBus.Send(PluginID, "AUTH_RES", "LOGOUT");
                    }
                    if (cmd.TargetHeader == "METAB_REQ")
                    {
                        BioBus.Send(PluginID, cmd.TargetHeader, cmdName);
                    }
                    else
                    {
                        BioBus.Send(PluginID, cmd.TargetHeader, args);
                    }
                }
                else if (cmdName != "LOGOUT" && cmdName != "LOGIN")
                {
                    BioBus.Send(PluginID, "CHAT_REQ", raw);
                }
            }
            if (pkt.Header == "CMD_REGISTER")
            {
                try
                {
                    if (pkt.Data.Length >= 7)
                    {
                        var newCmd = new BioCommand(
                            pkt.Get(0),
                            pkt.Get(1),
                            bool.Parse(pkt.Get(2)),
                            bool.Parse(pkt.Get(3)),
                            int.Parse(pkt.Get(4)),
                            pkt.Get(5),
                            pkt.Get(6)
                        );
                        Register(newCmd);
                    }
                    else if (pkt.Data.Length == 6)
                    {
                        var newCmd = new BioCommand(
                            pkt.Get(0),
                            pkt.Get(1),
                            bool.Parse(pkt.Get(2)),
                            false,
                            int.Parse(pkt.Get(3)),
                            pkt.Get(4),
                            pkt.Get(5)
                        );
                        Register(newCmd);
                    }
                }
                catch { }
                return;
            }
            if (pkt.Header == "SYS_HELP_SYNC")
            {
                BioModulHelper.RegisterCommands(PluginID, _commands.Values.ToArray());
                return;
            }
            if (pkt.Header == "AUTH_RES")
            {
                if (pkt.Get(0) == "SUCCESS")
                {
                    _isAdminLocally = true;
                    BioBus.Send(PluginID, "INFO", "ADMIN-RECHTE GEWÄHRT.");
                }
                else if (pkt.Get(0) != "LOGOUT")
                {
                    _isAdminLocally = false;
                    BioBus.Send(PluginID, "INFO", "ADMIN-LOGIN FEHLGESCHLAGEN.");
                }
            }
        }
    }
}