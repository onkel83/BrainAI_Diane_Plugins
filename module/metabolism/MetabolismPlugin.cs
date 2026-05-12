using System;
using System.Timers;
using System.Globalization;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// Das MetabolismPlugin v2.0.0 - Pattern Edition.
    /// Simuliert den biologischen Rhythmus (Energie, Stress, Neugier).
    /// Nutzt das BioCommand-Struct, um Vital-Befehle sicher über das Switchboard zu routen.
    /// </summary>
    public class MetabolismPlugin : IBioPlugin
    {
        public string PluginID => "METABOL";
        public string Version => "2.0.0";

        private const string LOG_COMP = "METABOL";

        private int _tickIntervalMs;
        private float _rateEnergyDecay;
        private float _rateEnergyRegen;
        private float _rateStressRegen;
        private float _thresholdExhaustion;
        private float _targetEnergyWake;
        private float _targetStressWake;
        private ulong _idSelfEnergy;
        private ulong _idSelfStress;
        private System.Timers.Timer? _ticker;
        
        public float Energy = 100f;
        public float Stress = 0f;
        public float Curiosity = 0f;
        public bool IsSleeping = false;

        private DateTime _lastTickTime;
        private DateTime _lastInteractionTime;

        /// <summary>
        /// Initialisiert den autonomen Stoffwechsel und etabliert den biologischen Rhythmus der Architektur.
        /// Setzt die temporalen Anker (Timestamps) für die Delta-Time-Berechnung, bindet den globalen Systembus an, 
        /// lädt die Schwellenwerte deterministisch in den Speicher und startet den asynchronen Heartbeat-Ticker.
        /// </summary>
        public void OnLoad()
        {
            _lastTickTime = DateTime.Now;
            _lastInteractionTime = DateTime.Now;

            BioBus.OnPacket += HandleMetabolismTraffic;
            LoadConfigSynchronously();
            RegisterCommands();
            StartHeartbeat();

            BioBus.Send(PluginID, "INFO", $"Autonomer Stoffwechsel {Version} stabilisiert.");
        }

        /// <summary>
        /// Registriert die metabolischen Systembefehle über den zentralen BioModulHelper und delegiert das Pre-Gating an den Command-Router.
        /// Definiert die Kausalität für 'FEED' (für jeden Nutzer frei zugänglich zur reinen Energiezufuhr) 
        /// und 'RESCUE' (hart an Admin-Rechte gekoppelt, um Missbrauch der System-Notfall-Stabilisierung auszuschließen).
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("FEED", "METAB_REQ", false, false, 0, "", "Füllt die Energie der KI wieder vollständig auf 100% auf."),
                new BioCommand("RESCUE", "METAB_REQ", true, false, 0, "", "Führt eine Notfall-Stabilisierung durch (Setzt Stress auf 0% und rettet Energie auf 50%).")
            );
        }

        /// <summary>
        /// Führt einen sauberen und deterministischen Teardown des Stoffwechsels durch.
        /// Meldet den asynchronen Bus-Listener ab und erzwingt das sofortige Stoppen sowie die physische 
        /// Freigabe (Dispose) des Heartbeat-Tickers, um Speicherlecks und Ghost-Ticks kompromisslos zu verhindern (Zero Footprint).
        /// </summary>
        public void OnUnload()
        {
            BioBus.OnPacket -= HandleMetabolismTraffic;
            _ticker?.Stop();
            _ticker?.Dispose();
        }

        /// <summary>
        /// Lädt die biologischen Systemparameter synchron und deterministisch aus der Systemkonfiguration.
        /// Etabliert die Grundregeln des Stoffwechsels (Verfall, Regeneration, Schwellenwerte) und erzwingt beim 
        /// Parsen von Gleitkommazahlen die 'InvariantCulture', um Abstürze oder Fehlberechnungen durch 
        /// unterschiedliche OS-Spracheinstellungen (Komma vs. Punkt) kompromisslos zu verhindern.
        /// Übersetzt zudem die logischen Strings für Energie und Stress in die exakten, hexadezimalen 
        /// Hardware-IDs des neuronalen Kerns.
        /// </summary>
        private void LoadConfigSynchronously()
        {
            var cfg = BioConfig.Instance;
            _tickIntervalMs = int.Parse(cfg.Get("METAB_TICK_MS", "1000"));
            _rateEnergyDecay = float.Parse(cfg.Get("METAB_RATE_ENERGY_DECAY", "-0.05"), CultureInfo.InvariantCulture);
            _rateEnergyRegen = float.Parse(cfg.Get("METAB_RATE_ENERGY_REGEN", "2.5"), CultureInfo.InvariantCulture);
            _rateStressRegen = float.Parse(cfg.Get("METAB_RATE_STRESS_REGEN", "-1.5"), CultureInfo.InvariantCulture);
            _thresholdExhaustion = float.Parse(cfg.Get("METAB_THRESHOLD_EXHAUST", "5.0"), CultureInfo.InvariantCulture);
            _targetEnergyWake = float.Parse(cfg.Get("METAB_TARGET_WAKE_ENG", "100.0"), CultureInfo.InvariantCulture);
            _targetStressWake = float.Parse(cfg.Get("METAB_TARGET_WAKE_STR", "10.0"), CultureInfo.InvariantCulture);

            _idSelfEnergy = ulong.Parse(cfg.Get("ID_SELF_ENERGY", "0x5000000000001000").Replace("0x", ""), NumberStyles.HexNumber);
            _idSelfStress = ulong.Parse(cfg.Get("ID_SELF_STRESS", "0x5000000000001001").Replace("0x", ""), NumberStyles.HexNumber);
        }

        /// <summary>
        /// Initialisiert und startet den asynchronen biologischen Taktgeber (Heartbeat).
        /// Stoppt präventiv alle eventuell noch laufenden Timer-Instanzen, um speicherfressende Überlappungen 
        /// oder doppelte Ticks (Ghost-Ticks) beim Neuladen der Konfiguration physisch auszuschließen.
        /// Koppelt das Ticker-Event an den zentralen 'OnHeartbeat'-Prozesslauf.
        /// </summary>
        private void StartHeartbeat()
        {
            _ticker?.Stop();
            _ticker = new System.Timers.Timer(_tickIntervalMs);
            _ticker.Elapsed += (s, e) => OnHeartbeat();
            _ticker.AutoReset = true;
            _ticker.Start();
        }

        /// <summary>
        /// Der zentrale asynchrone Nachrichten-Router des Stoffwechsel-Systems.
        /// Evaluiert eingehende System-Events nach strikter O(1)-Logik (Switch-Statement).
        /// Reagiert auf System-Synchronisationen (SYS_HELP_SYNC) und Live-Konfigurations-Updates (CONFIG_VAL).
        /// Fängt zudem passive Interaktions-Events (CMD_REQ, LANG_REQ, CHAT_REQ) via Switch-Fall-Through ab, 
        /// um deterministisch den mentalen Stress der KI bei aktiver Nutzung zu reduzieren (Simulation von 'Fokus').
        /// Explicit deklarierte Vital-Befehle (METAB_REQ) werden über ein verschachteltes O(1)-Routing kausal aufgelöst.
        /// </summary>
        /// <param name="pkt">Das vom BioBus empfangene Datenpaket.</param>
        private void HandleMetabolismTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;
            switch (pkt.Header)
            {
                case "SYS_HELP_SYNC":
                    RegisterCommands();
                    break;
                case "CONFIG_VAL":
                    UpdateLocalParameterLive(pkt.Get(0), pkt.Get(1));
                    break;
                case "CMD_REQ":
                case "LANG_REQ":
                case "CHAT_REQ":
                    _lastInteractionTime = DateTime.Now;
                    Curiosity = 0f;
                    if (!IsSleeping) ModifyStat(ref Stress, -2.0f);
                    break;
                case "METAB_REQ":
                    string action = pkt.Get(0).ToUpper();
                    switch (action)
                    {
                        case "FEED":
                            Energy = 100f;
                            BioBus.Send(PluginID, "SUCCESS", "Energie auf 100% regeneriert.");
                            break;
                        case "RESCUE":
                            Stress = 0f;
                            Energy = 50f;
                            BioBus.Send(PluginID, "SUCCESS", "Notfall-Stabilisierung durchgeführt.");
                            break;
                        case "EXERTION":
                            float cost = float.TryParse(pkt.Get(1, "0.1"), NumberStyles.Any, CultureInfo.InvariantCulture, out float c) ? c : 0.1f;
                            ModifyStat(ref Energy, -cost);
                            break;
                    }
                    break;
            }
        }

        /// <summary>
        /// Ermöglicht das deterministische Hot-Swapping (Live-Updates) von Stoffwechsel-Parametern zur Laufzeit, 
        /// ohne das Modul entladen oder neu starten zu müssen.
        /// Abgefangene Konfigurationsänderungen (z.B. das Takt-Intervall METAB_TICK_MS) triggern sofort 
        /// einen sauberen Neustart des asynchronen Heartbeats. 
        /// Nutzt eine harte Try-Catch-Isolation, um zu verhindern, dass fehlerhafte oder böswillige 
        /// Cast-Versuche (z.B. Buchstaben anstelle von Zahlen) den biologischen Rhythmus zum Absturz bringen.
        /// </summary>
        /// <param name="key">Der Identifikator des zu ändernden Parameters (z.B. METAB_TICK_MS).</param>
        /// <param name="val">Der neue Wert als roher String, der deterministisch geparst wird.</param>
        private void UpdateLocalParameterLive(string key, string val)
        {
            try
            {
                if (key.ToUpper() == "METAB_TICK_MS")
                {
                    _tickIntervalMs = int.Parse(val);
                    StartHeartbeat();
                }
            }
            catch { }
        }

        /// <summary>
        /// Der primäre Ausführungszyklus (Tick) der biologischen Simulation.
        /// Berechnet die präzise Delta-Zeit (dt) in Sekunden seit dem letzten Aufruf, um ratenunabhängige 
        /// (frame-rate independent) Modifikationen zu garantieren. Implementiert einen harten Fail-Safe: 
        /// War das System länger als 10 Sekunden eingefroren (z.B. durch Standby des Betriebssystems), 
        /// wird das Delta auf 1 Sekunde gekappt, um massive, zerstörerische Wertesprünge physisch auszuschließen.
        /// Orchestriert anschließend die kausale Pipeline: Werteberechnung -> Bus-Broadcast -> Neuronale Synchronisation.
        /// </summary>
        private void OnHeartbeat()
        {
            float dt = (float)(DateTime.Now - _lastTickTime).TotalSeconds;
            _lastTickTime = DateTime.Now;
            if (dt > 10f) dt = 1.0f;

            UpdateVitals(dt);
            BroadcastStatus();
            SyncToNeuralCore();
        }

        /// <summary>
        /// Die deterministische State-Machine des Stoffwechsels (Schlaf-/Wach-Zyklus).
        /// Wendet die konfigurierten Verfalls- und Regenerationsraten präzise auf Basis der Delta-Zeit an.
        /// Überwacht kausal die biologischen Schwellenwerte: Erreicht die Energie das Exhaustion-Limit, 
        /// erzwingt das Modul den Schlafmodus und feuert ein 'SLEEP_START'-Paket. Sind im Schlaf die 
        /// Target-Werte für Erholung erreicht, wird das System über 'WAKE_UP' wieder aktiviert.
        /// </summary>
        /// <param name="dt">Die vergangene, normalisierte Zeit in Sekunden seit dem letzten Tick.</param>
        private void UpdateVitals(float dt)
        {
            if (IsSleeping)
            {
                ModifyStat(ref Energy, _rateEnergyRegen * dt);
                ModifyStat(ref Stress, _rateStressRegen * dt);

                if (Energy >= _targetEnergyWake && Stress <= _targetStressWake)
                {
                    IsSleeping = false;
                    BioBus.Send(PluginID, "STATE", "WAKE_UP");
                }
            }
            else
            {
                ModifyStat(ref Energy, _rateEnergyDecay * dt);
                if (Energy <= _thresholdExhaustion)
                {
                    IsSleeping = true;
                    BioBus.Send(PluginID, "STATE", "SLEEP_START", "EXHAUSTED");
                }
            }
        }

        /// <summary>
        /// Formatiert und sendet die aktuellen biologischen Vitalwerte (Energie, Stress, Neugier) als kompakten 
        /// Telemetrie-String an das UI und abhängige Plugins (Fast-Path-Sync).
        /// Nutzt zwingend die InvariantCulture für die Gleitkomma-Formatierung, um UI-Abstürze durch 
        /// abweichende Ländereinstellungen (Komma statt Punkt) bei den Empfängern deterministisch auszuschließen.
        /// </summary>
        private void BroadcastStatus()
        {
            string stats = string.Format(CultureInfo.InvariantCulture, "E:{0:F1},S:{1:F1},C:{2:F1}", Energy, Stress, Curiosity);
            BioBus.Send(PluginID, "STATS_UPDATE", stats);
        }

        /// <summary>
        /// Der Brückenschlag zur Neuro-Symbolik. Übersetzt die deterministischen Fließkommazahlen (0-100) der 
        /// Simulation in normalisierte neuronale Aktivierungsgewichte (0.0 - 1.0) und injiziert diese 
        /// direkt in das assoziative Langzeitgedächtnis des Systemkerns.
        /// Die Werte werden fest an die konfigurierten hexadezimalen Hardware-IDs für 'SELF_ENERGY' 
        /// und 'SELF_STRESS' gekoppelt, wodurch die KI ihren eigenen Erschöpfungszustand kausal "fühlen" kann.
        /// </summary>
        private void SyncToNeuralCore()
        {
            BioBus.Send(PluginID, "CORE_TEACH", "SELF", _idSelfEnergy.ToString(), (Energy / 100f).ToString(CultureInfo.InvariantCulture));
            BioBus.Send(PluginID, "CORE_TEACH", "SELF", _idSelfStress.ToString(), (Stress / 100f).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Ein hochoptimierter O(1)-Clamper für die sichere In-Place-Modifikation von Vitalwerten.
        /// Arbeitet direkt auf der Speicherreferenz (ref) der Zielvariable, um unnötige Zuweisungen zu vermeiden, 
        /// und erzwingt harte mathematische Grenzen (0.0 bis 100.0), um Buffer-Overflows oder 
        /// logische Fehler in der Zustandsmaschine deterministisch zu verhindern.
        /// </summary>
        /// <param name="stat">Harte Speicherreferenz auf den zu modifizierenden Vitalwert (z.B. Energy oder Stress).</param>
        /// <param name="delta">Der positive (Regeneration) oder negative (Verfall) Modifikator.</param>
        private void ModifyStat(ref float stat, float delta) => stat = Math.Max(0f, Math.Min(100f, stat + delta));
    }
}