using System;
using System.Linq;
using System.Collections.Generic;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// Das VisionPlugin v2.0.0 - Pattern Edition.
    /// Verarbeitet visuelle Matrizen zu neuronalen Hashes.
    /// Nutzt das BioCommand-Struct für Metadaten und Pre-Gating.
    /// </summary>
    public class VisionPlugin : IBioPlugin
    {
        public string PluginID => "VISION-MOD";
        public string Version => "2.0.0";

        private const string LOG_COMP = "VISION";
        private int _matrixWidth;
        private int _matrixHeight;
        private int _totalBits;

        /// <summary>
        /// Initialisiert den visuellen Cortex und etabliert das sensorische Grid.
        /// Löst die Grid-Dimensionen (Breite und Höhe) deterministisch über die BioConfig auf, 
        /// berechnet die resultierende Bit-Kapazität und bindet den asynchronen Handler an den BioBus. 
        /// Durch die Vorberechnung der '_totalBits' wird eine effiziente O(1)-Validierung eingehender 
        /// Bildmatrizen ermöglicht, bevor diese in den neuronalen Raum überführt werden.
        /// </summary>
        public void OnLoad()
        {
            _matrixWidth = int.Parse(BioConfig.Instance.Get("VISION_WIDTH", "8"));
            _matrixHeight = int.Parse(BioConfig.Instance.Get("VISION_HEIGHT", "6"));
            _totalBits = _matrixWidth * _matrixHeight;
            BioBus.OnPacket += HandleVisionTraffic;
            RegisterCommands();
            BioBus.Send(LOG_COMP, "INFO", $"Visueller Cortex v{Version} aktiv. Sensor-Grid: {_matrixWidth}x{_matrixHeight} ({_totalBits} Bits)");
        }

        /// <summary>
        /// Registriert den sensorischen Befehl 'SEE' über den zentralen BioModulHelper.
        /// Etabliert die Schnittstelle für externe Bildquellen (z.B. Kamera-Plugins oder manuelle Eingaben). 
        /// Der Befehl ist nicht an Admin-Rechte gebunden (false), da sensorische Eingaben als passive 
        /// Wahrnehmung gewertet werden, die den Systemstatus nicht administrativ gefährden. 
        /// Die Metadaten definieren zudem die Erwartung an den Binär-String für eine korrekte Kausalitätsprüfung.
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("SEE", "VISION_REQ", false, false, 1, "<Binär-String> [Label]", $"Füttert den visuellen Sensor mit einem Binär-Muster ({_totalBits} Bits). Optional kann ein Label für den Lernmodus angehängt werden.")
            );
        }

        /// <summary>
        /// Führt den deterministischen Teardown des visuellen Cortex durch.
        /// Meldet den asynchronen Event-Listener vom BioBus ab, um Ghost-Events während der 
        /// Modul-Entladung zu verhindern, und signalisiert den Offline-Status im System-Audit (Zero Footprint).
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleVisionTraffic;
            BioBus.Send(LOG_COMP, "INFO", "Visueller Cortex offline.");
        }

        /// <summary>
        /// Der asynchrone Nachrichten-Router für den visuellen Cortex.
        /// Evaluiert eingehende BioBus-Pakete nach einer strikten O(1)-Logik (Switch-Statement).
        /// Verarbeitet System-Synchronisationen, Live-Konfigurations-Updates für das Sensor-Grid 
        /// sowie explizite visuelle Wahrnehmungs-Anfragen (VISION_REQ). 
        /// Extrahiert deterministisch Binärmuster und optionale Lern-Labels aus dem Datenstrom 
        /// und erzwingt eine harte Dimensions-Prüfung gegen das aktuelle Sensor-Grid, 
        /// um die Datenintegrität des neuronalen Raums sicherzustellen.
        /// </summary>
        /// <param name="pkt">Das abgefangene Datenpaket vom BioBus.</param>
        private void HandleVisionTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;
            switch (pkt.Header)
            {
                case "SYS_HELP_SYNC":
                    RegisterCommands();
                    break;

                case "CONFIG_VAL":
                    string cfgKey = pkt.Get(0);
                    if (cfgKey == "VISION_WIDTH") _matrixWidth = int.Parse(pkt.Get(1));
                    else if (cfgKey == "VISION_HEIGHT") _matrixHeight = int.Parse(pkt.Get(1));
                    _totalBits = _matrixWidth * _matrixHeight;
                    break;

                case "VISION_REQ":
                    string payload = string.Join(" ", pkt.Data);
                    if (string.IsNullOrWhiteSpace(payload)) return;
                    var args = payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    string? label = null;
                    if (args.Count > 1 && !args.Last().All(c => c == '0' || c == '1' || c == ','))
                    {
                        label = args.Last().ToUpper();
                        args.RemoveAt(args.Count - 1);
                    }
                    string bits = string.Join("", args).Replace(",", "").Replace(" ", "");
                    if (bits.Length != _totalBits)
                    {
                        BioBus.Send(LOG_COMP, "ERRN", $"Dimension-Fehler! Erwartet {_totalBits} Bits, empfangen {bits.Length}.");
                        return;
                    }
                    bool[] matrix = bits.Select(c => c == '1').ToArray();
                    ProcessMatrix(matrix, label);
                    break;
            }
        }

        /// <summary>
        /// Orchestriert die Transformation einer binären Sensormatrix in den assoziativen neuronalen Raum.
        /// Generiert einen deterministischen 64-Bit-Token (VisionHash) und leitet die Kausalitätskette ein:
        /// 1. Supervised Learning: Bei Vorhandensein eines Labels wird eine 'CORE_ASSOC_NAME'-Anforderung emittiert, 
        ///    um das Muster im Langzeitgedächtnis begrifflich zu verankern.
        /// 2. Mustererkennung: Sendet ein 'SWARM_SIM'-Signal, um eine Ähnlichkeitsanalyse gegen bestehende 
        ///    Erinnerungen im SWARM-Speicher zu triggern.
        /// 3. Telemetrie: Bestätigt die erfolgreiche Extraktion des visuellen Tokens über den BioBus.
        /// </summary>
        /// <param name="matrix">Die validierte binäre Bildmatrix des Sensors.</param>
        /// <param name="label">Optionaler Identifikator für die begriffliche Klassifizierung (Lernmodus).</param>
        private void ProcessMatrix(bool[] matrix, string? label = null)
        {
            ulong visionToken = GenerateVisionHash(matrix);
            if (visionToken == 0) return;
            if (!string.IsNullOrEmpty(label))
            {
                BioBus.Send(LOG_COMP, "CORE_ASSOC_NAME", visionToken.ToString(), label.ToUpper());
                BioBus.Send(LOG_COMP, "INFO", $"Assoziation angefordert: 0x{visionToken:X16} <-> {label}");
            }
            BioBus.Send(LOG_COMP, "SWARM_SIM", visionToken.ToString());
            BioBus.Send(LOG_COMP, "VISION_SUCCESS", $"Hash:0x{visionToken:X16}");
        }

        /// <summary>
        /// Realisiert den deterministischen Hash-Algorithmus zur Reduktion der Sensormatrix auf einen 64-Bit-Bezeichner.
        /// Nutzt Bit-Shifting zur raumsparenden Kodierung der Bild-Bits und verankert das Ergebnis 
        /// über den statischen Präfix 'BioAI.CLUSTER_OBJECT' im spezifischen Objekt-Cluster des Kerns.
        /// Dies garantiert die Typ-Integrität innerhalb der Diane-Architektur: Visuelle Muster bleiben 
        /// im assoziativen Raum deterministisch von metabolischen Werten oder sprachlichen Tokens unterscheidbar.
        /// </summary>
        /// <param name="binaryMatrix">Das flache Array der Sensordaten.</param>
        /// <returns>Ein eindeutiger ulong-Hash, der das visuelle Muster im neuronalen Grid repräsentiert.</returns>
        public ulong GenerateVisionHash(bool[] binaryMatrix)
        {
            ulong hash = 0;
            for (int i = 0; i < binaryMatrix.Length && i < 60; i++)
                if (binaryMatrix[i]) hash |= (1UL << i);
            return BioAI.CLUSTER_OBJECT | hash;
        }
    }
}