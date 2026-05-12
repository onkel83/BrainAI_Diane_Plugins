using System;
using System.Text;
using System.Threading.Tasks;
using Diane.Core;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace Diane.Plugins
{
    /// <summary>
    /// WorldPlugin v2.0.0 - Discovery Edition.
    /// Simuliert das physische Gitter-Umfeld der Drohne.
    /// Ermöglicht die Interaktion mit Objekten und speist den visuellen Cortex mit Sensordaten.
    /// </summary>
    public class WorldPlugin : IBioPlugin
    {
        public string PluginID => "WORLD-MOD";
        public string Version => "2.0.0";

        private const string LOG_COMP = "WORLD";
        private int _gridW, _gridH, _bootDelay, _tickRate;

        private struct ObjectMetadata
        {
            public float Energy, Stress, Curiosity;
            public string Action;
        }

        private readonly Dictionary<char, ObjectMetadata> _customRegistry = new Dictionary<char, ObjectMetadata>();
        private char[,]? _grid;
        private int _dianeX = 24, _dianeY = 10;
        private bool _running = true;
        private string _lastSentVision = "";

        /// <summary>
        /// Initialisiert die Gitter-Welt und startet den Simulations-Loop.
        /// Lädt Welt-Dimensionen und Taktfrequenzen deterministisch aus der BioConfig.
        /// </summary>
        public void OnLoad()
        {
            var cfg = BioConfig.Instance;
            _gridW = int.Parse(cfg.Get("WORLD_WIDTH", "48"));
            _gridH = int.Parse(cfg.Get("WORLD_HEIGHT", "20"));
            _bootDelay = int.Parse(cfg.Get("WORLD_BOOT_DELAY_MS", "2000"));
            _tickRate = int.Parse(cfg.Get("WORLD_TICK_MS", "500"));

            _grid = new char[_gridW, _gridH];
            InitializeWorld();

            BioBus.OnPacket += HandleTraffic;
            RegisterCommands();

            BioBus.Send(LOG_COMP, "INFO", $"Gitter-Welt v{Version} online ({_gridW}x{_gridH}).");
            _running = true;
            _ = Task.Run(WorldLoop);
        }

        /// <summary>
        /// Registriert die Bewegungs- und Interaktionsbefehle der Drohne.
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("RECHTS", "WORLD_CMD", false, false, 0, "", "Bewegt die Entität einen Schritt nach rechts."),
                new BioCommand("LINKS", "WORLD_CMD", false, false, 0, "", "Bewegt die Entität einen Schritt nach links."),
                new BioCommand("AUF", "WORLD_CMD", false, false, 0, "", "Bewegt die Entität einen Schritt nach oben."),
                new BioCommand("AB", "WORLD_CMD", false, false, 0, "", "Bewegt die Entität einen Schritt nach unten."),
                new BioCommand("NIMM", "WORLD_CMD", false, false, 0, "", "Interagiert mit einem Objekt auf dem aktuellen Feld."),
                new BioCommand("SPAWN", "SPAWN_REQ", true, false, 7, "<X> <Y> <Icon> <E-Cost> <S-Cost> <C-Cost> <Action>", "Erschafft ein neues Objekt in der Welt. (Admin)")
            );
        }

        /// <summary>
        /// Führt den deterministischen Teardown der Welt-Simulation durch.
        /// </summary>
        public void OnUnload()
        {
            _running = false;
            BioBus.OnPacket -= HandleTraffic;
            BioBus.Send(LOG_COMP, "INFO", "Gitter-Welt offline.");
        }

        /// <summary>
        /// Der asynchrone Nachrichten-Router der Welt-Simulation.
        /// Nutzt verschachteltes O(1) Switching zur Auflösung von Bewegungsvektoren und System-Events.
        /// </summary>
        private void HandleTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;

            switch (pkt.Header)
            {
                case "SYS_HELP_SYNC":
                    RegisterCommands();
                    break;

                case "WORLD_CMD":
                    string cmd = pkt.Get(0).ToUpper();
                    switch (cmd)
                    {
                        case "AUF": Move(0, -1); break;
                        case "AB": Move(0, 1); break;
                        case "LINKS": Move(-1, 0); break;
                        case "RECHTS": Move(1, 0); break;
                        case "NIMM": TryInteract(); break;
                    }
                    break;

                case "SPAWN_REQ":
                    HandleSpawn(pkt);
                    break;
            }
        }

        /// <summary>
        /// Erschafft ein dynamisches Objekt mit metabolischen Auswirkungen.
        /// </summary>
        private void HandleSpawn(BioPacket pkt)
        {
            try
            {
                int x = int.Parse(pkt.Get(0)), y = int.Parse(pkt.Get(1));
                char icon = pkt.Get(2)[0];
                _customRegistry[icon] = new ObjectMetadata
                {
                    Energy = float.Parse(pkt.Get(3), CultureInfo.InvariantCulture),
                    Stress = float.Parse(pkt.Get(4), CultureInfo.InvariantCulture),
                    Curiosity = float.Parse(pkt.Get(5), CultureInfo.InvariantCulture),
                    Action = pkt.Get(6).ToUpper()
                };
                SpawnAt(icon, x, y);
                BioBus.Send(LOG_COMP, "SUCCESS", $"Objekt '{icon}' an [{x},{y}] erschaffen.");
            }
            catch
            {
                BioBus.Send(LOG_COMP, "ERRN", "Spawn-Syntax fehlerhaft.");
            }
        }

        /// <summary>
        /// Berechnet die neue Position der Drohne und erzwingt metabolische 'Anstrengung'.
        /// </summary>
        private void Move(int dx, int dy)
        {
            int nx = _dianeX + dx, ny = _dianeY + dy;
            if (nx >= 0 && nx < _gridW && ny >= 0 && ny < _gridH)
            {
                _dianeX = nx;
                _dianeY = ny;
                BioBus.Send(LOG_COMP, "METAB_REQ", "EXERTION", "0.2");
                if (_grid![_dianeX, _dianeY] != ' ') TryInteract();
            }
        }

        /// <summary>
        /// Löst die Interaktion mit einem Gitter-Objekt aus und emittiert entsprechende System-Signale.
        /// </summary>
        private void TryInteract()
        {
            char cell = _grid![_dianeX, _dianeY];
            if (cell == ' ') return;

            if (_customRegistry.TryGetValue(cell, out ObjectMetadata meta))
            {
                _grid[_dianeX, _dianeY] = ' ';
                if (meta.Energy != 0) BioBus.Send(LOG_COMP, "METAB_REQ", "EXERTION", (-meta.Energy).ToString(CultureInfo.InvariantCulture));

                if (meta.Action == "AUTO") BioBus.Send(LOG_COMP, "SWARM_SIM", cell.ToString());
                else BioBus.Send(LOG_COMP, "CMD_REQ", meta.Action);
                return;
            }

            switch (cell)
            {
                case 'O':
                    BioBus.Send(LOG_COMP, "METAB_REQ", "FEED");
                    break;
                case 'X':
                    BioBus.Send(LOG_COMP, "METAB_REQ", "RESCUE");
                    break;
                case '?':
                    BioBus.Send("DIANE", "TALK", "Ich habe ein unbekanntes Objekt detektiert. Die Analyse steht noch aus.");
                    BioBus.Send(LOG_COMP, "WARN", "Unbekannte Entität '?' entfernt.");
                    break;
            }
            _grid[_dianeX, _dianeY] = ' ';
        }

        /// <summary>
        /// Der Haupt-Simulationsloop. Extrahiert periodisch das visuelle Umfeld 
        /// und sendet es als VISION_REQ an den visuellen Cortex.
        /// </summary>
        private async Task WorldLoop()
        {
            await Task.Delay(_bootDelay);
            while (_running)
            {
                string[] vision = ExtractVision(8, 6);
                if (vision[0] != _lastSentVision)
                {
                    _lastSentVision = vision[0];
                    BioBus.Send(LOG_COMP, "VISION_REQ", vision[0]);
                }
                BroadcastFullMap();
                await Task.Delay(_tickRate);
            }
        }

        /// <summary>
        /// Serialisiert das vollständige physikalische Gitter-Umfeld in einen kompakten Telemetrie-Stream für das System-Monitoring.
        /// Konstruiert mittels StringBuilder eine String-Repräsentation der gesamten Welt-Matrix, injiziert die 
        /// aktuelle Position der Entität ('D') deterministisch in den Datenstrom und emittiert das Paket 
        /// inklusive der Dimensions-Metadaten (Breite und Höhe) als 'WORLD_MAP'-Paket an den BioBus. 
        /// Dies stellt die Datenbasis für die Echtzeit-Visualisierung der Umgebung im Dashboard oder in 
        /// externen Überwachungs-Tools dar.
        /// </summary>
        private void BroadcastFullMap()
        {
            StringBuilder sb = new StringBuilder();
            for (int y = 0; y < _gridH; y++)
                for (int x = 0; x < _gridW; x++)
                    sb.Append((x == _dianeX && y == _dianeY) ? 'D' : _grid![x, y]);

            BioBus.Send(LOG_COMP, "WORLD_MAP", _gridW.ToString(), _gridH.ToString(), sb.ToString());
        }

        /// <summary>
        /// Extrahiert ein lokales Sichtfeld um die aktuelle Position der Drohne.
        /// Erzeugt sowohl ein binäres Muster für den neuronalen Hash als auch eine UI-Repräsentation.
        /// </summary>
        private string[] ExtractVision(int w, int h)
        {
            StringBuilder sbBin = new StringBuilder(), sbUI = new StringBuilder();
            int sx = _dianeX - (w / 2), sy = _dianeY - (h / 2);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int wx = sx + x, wy = sy + y;
                    if (wx >= 0 && wx < _gridW && wy >= 0 && wy < _gridH)
                    {
                        char c = _grid![wx, wy];
                        sbBin.Append(c == ' ' ? '0' : '1');
                        sbUI.Append(c);
                    }
                    else { sbBin.Append('1'); sbUI.Append('|'); }
                }
            return new string[] { sbBin.ToString(), sbUI.ToString() };
        }

        /// <summary>
        /// Etabliert den initialen Zustand der physikalischen Gitter-Welt und bereitet das Simulations-Feld vor.
        /// Bereinigt deterministisch alle Gitter-Zellen (Zero-State-Initialisierung), lädt die statischen 
        /// Welt-Knoten aus der zentralen 'BioConfig' unter Verwendung eines vordefinierten Fallback-Layouts 
        /// und initiiert den Parsing-Prozess zur physischen Manifestation der Start-Objekte.
        /// </summary>
        private void InitializeWorld()
        {
            for (int y = 0; y < _gridH; y++) for (int x = 0; x < _gridW; x++) _grid![x, y] = ' ';
            string nodes = BioConfig.Instance.Get("WORLD_START_NODES", "O:5,5;O:40,15;X:10,18;#:20,10;?:30,5");
            ParseAndSpawnInitialNodes(nodes);
        }

        /// <summary>
        /// Realisiert die deterministische Transformation von Konfigurations-Strings in physische Welt-Entitäten.
        /// Zerlegt die Rohdaten kaskadierend nach dem Trennschema 'Objekt:X,Y', validiert die strukturelle 
        /// Integrität der Fragmente und delegiert die Platzierung an die gesicherte Spawn-Routine. 
        /// Die Methode ist durch eine harte Try-Catch-Isolation geschützt, um sicherzustellen, dass 
        /// Syntax-Fehler in der Welt-Konfiguration nicht zum Abbruch der Modul-Initialisierung führen.
        /// </summary>
        /// <param name="configData">Der rohe Konfigurations-String mit den Objekt-Daten und Koordinaten.</param>
        private void ParseAndSpawnInitialNodes(string configData)
        {
            if (string.IsNullOrWhiteSpace(configData)) return;
            try
            {
                foreach (var node in configData.Split(';'))
                {
                    var parts = node.Split(':'); if (parts.Length != 2) continue;
                    var coords = parts[1].Split(',');
                    SpawnAt(parts[0][0], int.Parse(coords[0]), int.Parse(coords[1]));
                }
            }
            catch { }
        }

        /// <summary>
        /// Führt die physische Platzierung eines Icons auf dem Simulations-Gitter unter Einhaltung 
        /// strikter Begrenzungsprüfungen (Boundary Checks) durch.
        /// Garantiert die Speicherintegrität der Welt-Matrix, indem Schreibzugriffe außerhalb der 
        /// konfigurierten Dimensionen (_gridW, _gridH) unterbunden werden, wodurch potenzielle 
        /// Index-Exceptions deterministisch ausgeschlossen werden.
        /// </summary>
        /// <param name="icon">Das visuelle Repräsentations-Zeichen des Objekts.</param>
        /// <param name="x">Die horizontale Gitter-Koordinate.</param>
        /// <param name="y">Die vertikale Gitter-Koordinate.</param>
        private void SpawnAt(char icon, int x, int y)
        {
            if (x >= 0 && x < _gridW && y >= 0 && y < _gridH) _grid![x, y] = icon;
        }
    }
}