# API-Dokumentation: VisionPlugin v2.0.0

Das **VisionPlugin** (ID: `VISION-MOD`) fungiert als visueller Cortex der Diane-Architektur. Es übersetzt unstrukturierte binäre Sensordaten in deterministische neuronale Hashes (Vision-Tokens). Durch ein flexibles Sensor-Grid und die Integration von Lern-Labels ermöglicht es sowohl die passive Mustererkennung als auch das aktive Training des assoziativen Gedächtnisses.

## Metadaten

* **Plugin-ID:** `VISION-MOD` 
* **Version:** `2.0.0` 
* **Namespace:** `Diane.Plugins` 
* **Abhängigkeiten:** `Diane.Core` 

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Initialisiert den visuellen Cortex und definiert das sensorische Feld. 
* **Aktionen:** * Lädt Grid-Dimensionen (`VISION_WIDTH`, `VISION_HEIGHT`) aus der Systemkonfiguration. 
* Berechnet die totale Bit-Kapazität für die O(1)-Eingangsvalidierung. 
* Abonniert den globalen `BioBus` und registriert sensorische Befehle. 

### `OnUnload()`

Führt den deterministischen Teardown der sensorischen Schicht durch. 

*  **Aktionen:** * Meldet den Event-Listener ab, um Ghost-Events zu verhindern. 
* Signalisiert den Offline-Status im System-Audit (Zero Footprint). 

---

## Interne Logik & Transformation

### `HandleVisionTraffic(BioPacket pkt)`

Der asynchrone Nachrichten-Router mit O(1)-Routing-Logik. 

*  **Features:** 
* **Hot-Swapping:** Unterstützt die Live-Rekonfiguration der Grid-Dimensionen via `CONFIG_VAL`. 
* **Daten-Normalisierung:** Bereinigt eingehende Binär-Strings von Formatierungshilfen (Kommas, Leerzeichen). 
* **Dimensions-Wächter:** Blockiert fehlerhafte Muster, die nicht exakt der erwarteten Bit-Anzahl entsprechen. 

### `ProcessMatrix(bool[] matrix, string? label)`

Orchestriert die Kausalitätskette der Wahrnehmung. 

* **Supervised Learning:** Wenn ein Label mitgesendet wird, triggert das Modul eine `CORE_ASSOC_NAME`-Anforderung zur begrifflichen Verankerung des Musters. 
* **Mustererkennung:** Initiiert via `SWARM_SIM` eine Ähnlichkeitsanalyse im neuronalen SWARM-Speicher. 

### `GenerateVisionHash(bool[] binaryMatrix)`

Der deterministische Reduktions-Algorithmus. 

* **Logik:** Komprimiert die Sensormatrix mittels Bit-Shifting in einen 64-Bit-Bezeichner. 
* **Typ-Integrität:** Verankert den Hash im spezifischen Objekt-Cluster (`BioAI.CLUSTER_OBJECT`), um visuelle Daten im assoziativen Raum eindeutig von anderen Datentypen unterscheidbar zu machen. 

---

## Registrierte Befehle

| Befehl | Header | Admin? | Parameter | Beschreibung |
| --- | --- | --- | --- | --- |
| **SEE** | `VISION_REQ` | Nein | `<Binär-String> [Label]` | Speist ein Binär-Muster in den Cortex ein. Ein optionales Label aktiviert den Lernmodus. |

---

## Architektur-Konfiguration

Das Plugin nutzt folgende Parameter zur Definition der sensorischen Auflösung: 

* `VISION_WIDTH`: Breite des Sensor-Grids (Standard: `8`).
* `VISION_HEIGHT`: Höhe des Sensor-Grids (Standard: `6`). 
