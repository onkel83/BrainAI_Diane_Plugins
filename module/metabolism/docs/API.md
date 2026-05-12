# API-Dokumentation: MetabolismPlugin v2.0.0

Das **MetabolismPlugin** (ID: `METABOL`) ist der autonome biologische Taktgeber der Diane-Architektur (Pattern Edition). Es zwingt der KI einen realitätsnahen Rhythmus aus Erschöpfung und Erholung auf. Das Modul schlägt die Brücke zwischen klassischer, deterministischer Fließkomma-Simulation und neuro-symbolischer Gewichtung, indem es die berechneten Vitalwerte in Echtzeit an die neuronalen Hardware-IDs des Kernsystems koppelt.

## Metadaten

* **Plugin-ID:** `METABOL`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Abhängigkeiten:** `Diane.Core`, `System.Timers`

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Initialisiert den Stoffwechsel und startet den asynchronen Taktgeber.

* **Kausalität:** Wird beim Systemstart aufgerufen.
* **Aktionen:** 1. Setzt temporale Anker (`_lastTickTime`) für die Delta-Time-Berechnung.
2. Lädt Konfigurationen deterministisch (via `InvariantCulture`).
3. Startet den isolierten `System.Timers.Timer` für den Heartbeat.

### `OnUnload()`

Führt einen sauberen Teardown des Rhythmus durch.

* **Kausalität:** Wird beim Entladen aufgerufen.
* **Aktionen:** Stoppt den Timer (`Stop()`) und gibt die unmanaged Ressourcen auf OS-Ebene frei (`Dispose()`), um Ghost-Ticks zu verhindern (Zero Footprint).

---

## Interne Logik: Der Motor

### `OnHeartbeat()`

Der asynchrone System-Tick.

* **Architektur-Feature (Delta-Time):** Berechnet die präzise Dauer seit dem letzten Tick, um ratenunabhängige Modifikationen zu erzwingen.
* **Fail-Safe:** Kappt das Delta-T deterministisch auf `1.0f` ab, falls die Dauer 10 Sekunden überschreitet (z.B. nach dem Aufwachen des PCs aus dem Standby). Verhindert fatale Berechnungsfehler.
* **Pipeline:** Triggert `UpdateVitals` -> `BroadcastStatus` -> `SyncToNeuralCore`.

### `UpdateVitals(float dt)`

Die deterministische State-Machine des Schlaf-Wach-Zyklus.

* **Wachzustand:** Energie verfällt kontinuierlich gemäß `_rateEnergyDecay`. Fällt der Wert unter `_thresholdExhaustion`, wechselt das System kausal auf `IsSleeping = true` und feuert ein `SLEEP_START` Event.
* **Schlafzustand:** Energie regeneriert, Stress baut sich ab. Sobald die Zielwerte (`_targetEnergyWake`, `_targetStressWake`) erreicht sind, erwacht das System (`WAKE_UP`).

### `SyncToNeuralCore()`

Der Brückenschlag zur Neuro-Symbolik.

* **Logik:** Normalisiert die Fließkommazahlen (0-100) zu neuronalen Aktivierungsgewichten (0.0 - 1.0).
* **Injektion:** Sendet die Gewichte als `CORE_TEACH`-Befehl fest verdrahtet an die hexadezimalen Knoten-IDs `SELF_ENERGY` und `SELF_STRESS`. Die KI "fühlt" ihren Zustand in der Matrix.

### `ModifyStat(ref float stat, float delta)`

O(1) Memory-Clamper.

* **Sicherheit:** Arbeitet in-place über eine Speicherreferenz (`ref`) und clippt harte mathematische Grenzen via `Math.Max` und `Math.Min` zwischen 0.0 und 100.0, um Buffer-Overflows physisch zu blockieren.

### `LoadConfigSynchronously()`
Der deterministische Konfigurations-Lader.
* **Sicherheit:** Erzwingt beim Parsen von Gleitkommazahlen die `CultureInfo.InvariantCulture`, um Abstürze durch abweichende Ländereinstellungen auf OS-Ebene kompromisslos zu verhindern.
* **Hardware-Mapping:** Übersetzt logische Strings in die exakten hexadezimalen Hardware-IDs des neuronalen Kerns.

### `UpdateLocalParameterLive(string key, string val)`
Die Hot-Swapping-Schnittstelle.
* **Architektur-Feature:** Ermöglicht Live-Updates (z. B. `METAB_TICK_MS`) ohne Modul-Neustart.
* **Isolation:** Nutzt harte Try-Catch-Kapselung, damit fehlerhafte Cast-Versuche über den Bus (z.B. Buchstaben statt Zahlen) den Heartbeat nicht zum Absturz bringen.

### `BroadcastStatus()`
Der Telemetrie-Sender.
* **Aktionen:** Formatiert die aktuellen Vitalwerte (Energie, Stress, Neugier) mit `InvariantCulture` in einen kompakten String (`E:...,S:...,C:...`).
* **Routing:** Sendet das Paket als `STATS_UPDATE` für das schnelle UI-Rendering und andere abhängige Plugins in den Bus.

---

## Routing & Befehle (HandleMetabolismTraffic)

Der zentrale Event-Router nutzt eine performante `switch`-Jump-Table.

### Passive System-Events (Fall-Through Logic)

* **Trigger:** `CMD_REQ`, `LANG_REQ`, `CHAT_REQ` (Aktive Nutzer-Interaktionen).
* **Kausalität:** Setzt `Curiosity` zurück und senkt den mentalen `Stress` um 2.0f pro Interaktion (simulierter "Fokus"), sofern die KI wach ist.

### Registrierte System-Kommandos

| Befehl | Header | Admin? | Parameter | Beschreibung |
| --- | --- | --- | --- | --- |
| **FEED** | `METAB_REQ` | Nein | - | Setzt die Energie kausal auf 100% zurück. Für User frei zugänglich. |
| **RESCUE** | `METAB_REQ` | Ja | - | Notfall-Stabilisierung. Setzt Stress auf 0, Energie auf 50. Streng Admin-gekoppelt. |
| **EXERTION** | `METAB_REQ` | Nein | `<Cost>` | Simuliert punktuelle Anstrengung und zieht den angegebenen Betrag direkt von der Energie ab. |

---

## Architektur-Konfiguration

Das Plugin unterstützt deterministisches Hot-Swapping zur Laufzeit (via `CONFIG_VAL`).

* `METAB_TICK_MS`: Takt-Intervall des Heartbeats in Millisekunden (Standard: `1000`).
* `METAB_RATE_ENERGY_DECAY`: Stetiger Energieverfall (Standard: `-0.05`).
* `METAB_RATE_ENERGY_REGEN`: Regeneration im Schlaf (Standard: `2.5`).
* `METAB_RATE_STRESS_REGEN`: Stressabbau im Schlaf (Standard: `-1.5`).
* `METAB_THRESHOLD_EXHAUST`: Schwelle zum erzwungenen Schlaf (Standard: `5.0`).
* `ID_SELF_ENERGY`: Neuronale Hex-Adresse für Energie (Standard: `0x5000000000001000`).
* `ID_SELF_STRESS`: Neuronale Hex-Adresse für Stress (Standard: `0x5000000000001001`).

