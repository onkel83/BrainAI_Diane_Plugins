# API-Dokumentation: WorldPlugin v2.0.0

Das **WorldPlugin** (ID: `WORLD-MOD`) realisiert die physikalische Simulations-Ebene der Diane-Architektur. Es stellt eine Gitter-basierte Umgebung bereit, in der die Entität agieren kann. Das Modul fungiert als primärer Datenlieferant für den visuellen Cortex und verknüpft physikalische Aktionen (Bewegung) deterministisch mit metabolischen Kosten.

## Metadaten

* **Plugin-ID:** `WORLD-MOD`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Abhängigkeiten:** `Diane.Core` (Bus & Config)

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Initialisiert die physikalische Welt und startet den asynchronen Simulationszyklus.

* **Kausalität:** Wird beim Modul-Start aufgerufen.
* **Aktionen:**
1. Lädt Grid-Dimensionen (`WORLD_WIDTH`, `WORLD_HEIGHT`) und Taktfrequenzen aus der Konfiguration.
2. Initialisiert die Welt-Matrix und platziert Start-Objekte via `InitializeWorld()`.
3. Abonniert den `BioBus` und registriert das physikalische Befehlssatz-Gating.
4. Startet den asynchronen `WorldLoop` als Fire-and-Forget-Task.

### `OnUnload()`

Führt den deterministischen Teardown der Simulation durch.

* **Aktionen:**
1. Setzt das Laufzeit-Flag auf `false`, um den Simulations-Loop kontrolliert zu beenden.
2. Meldet den asynchronen Listener vom `BioBus` ab.
3. Hinterlässt einen sauberen Offline-Status im System-Audit (Zero Footprint).

---

## Die Simulations-Engine

### `WorldLoop()`

Der primäre Hintergrund-Task der physikalischen Simulation.

* **Intervall:** Gesteuert durch `WORLD_TICK_MS` (Standard: 500ms).
* **Ablauf:**
1. Extrahiert periodisch das 8x6 Sichtfeld um die Drohne.
2. Prüft auf visuelle Veränderungen (`Delta-Check`). Nur bei Abweichungen wird ein neues `VISION_REQ` an den Bus emittiert, um die Last auf dem visuellen Cortex zu minimieren.
3. Triggert den `BroadcastFullMap()`-Prozess für das System-Monitoring.

### `ExtractVision(int w, int h)`

Berechnet die lokale Wahrnehmung der Entität.

* **Binär-Extraktion:** Wandelt das Gitter-Umfeld in ein binäres Muster um (Leerzeichen = `0`, Objekt/Wand = `1`). Dieses Muster ist die Basis für das neuronale Hashing im `VisionPlugin`.
* **UI-Repräsentation:** Erzeugt parallel ein visuelles Gitter für das Dashboard, wobei Begrenzungen außerhalb der Karte als `|` markiert werden.

---

## Interaktion & Befehlssatz

### `HandleTraffic(BioPacket pkt)`

Der asynchrone Nachrichten-Router der Welt-Simulation.

* **Routing-Strategie**: Nutzt verschachtelte O(1)-Switch-Statements zur hocheffizienten Auflösung von Bewegungsvektoren (`WORLD_CMD`) und administrativen Anfragen (`SPAWN_REQ`).
* **Befehls-Gating**: Trennt strikt zwischen unprivilegierten Bewegungsbefehlen und administrativen Eingriffen in die Welt-Topologie.

### `Move(int dx, int dy)`

Berechnet die räumliche Translation der Entität innerhalb der Gitter-Grenzen.

* **Metabolisches Gating**: Jede erfolgreiche Bewegung emittiert ein `METAB_REQ`-Paket mit dem Header `EXERTION` und einem festen Last-Wert (0.2), um die physische Anstrengung im Stoffwechsel-Plugin zu simulieren.
* **Auto-Interaktion**: Sollte das Ziel-Feld ein Objekt enthalten, wird unmittelbar nach der Bewegung die Interaktions-Logik (`TryInteract`) ausgelöst.

### `TryInteract()`

Das deterministische Regelwerk für Objekt-Interaktionen.

* **Statisches Regelwerk**:
* **'O' (Nahrung)**: Triggert `FEED` im Stoffwechsel-Plugin.
* **'X' (Rettung)**: Triggert `RESCUE` (Stress-Reset und Energie-Partial-Refill).
* **'?' (Unbekannt)**: Löst eine Sprachausgabe via `DIANE.TALK` aus und wird anschließend als Warnung protokolliert.


* **Dynamische Registry**: Prüft bei unbekannten Icons in der `_customRegistry`, ob spezifische metabolische Effekte oder System-Aktionen (z. B. `SWARM_SIM`) hinterlegt sind.

### `HandleSpawn(BioPacket pkt)`

Ermöglicht die dynamische Injektion von Objekten mit erweiterten Metadaten.

* **Metadaten-Parsing**: Erwartet Energie-, Stress- und Neugier-Modifikatoren sowie eine Ziel-Aktion (z. B. `AUTO` für neuronale Hashes).

---

## Registrierte Befehle

Das Plugin registriert die physikalische Schnittstelle zur Steuerung der Drohne.

| Befehl | Header | Admin? | Parameter | Beschreibung |
| --- | --- | --- | --- | --- |
| **AUF** | `WORLD_CMD` | Nein | - | Bewegt die Entität einen Schritt nach oben (Y-1). |
| **AB** | `WORLD_CMD` | Nein | - | Bewegt die Entität einen Schritt nach unten (Y+1). |
| **LINKS** | `WORLD_CMD` | Nein | - | Bewegt die Entität einen Schritt nach links (X-1). |
| **RECHTS** | `WORLD_CMD` | Nein | - | Bewegt die Entität einen Schritt nach rechts (X+1). |
| **NIMM** | `WORLD_CMD` | Nein | - | Interagiert mit dem Objekt auf dem aktuellen Feld. |
| **SPAWN** | `SPAWN_REQ` | **Ja** | `<X> <Y> <Icon> <E> <S> <C> <Action>` | Erschafft ein neues Objekt mit definierten Auswirkungen. |

---

## Architektur-Pfad & Konfiguration

Das Verhalten der Simulation wird über die `BioConfig` gesteuert.

* **`WORLD_WIDTH` / `WORLD_HEIGHT`**: Definiert die Dimensionen der physischen Matrix (Standard: 48x20).
* **`WORLD_TICK_MS`**: Bestimmt die Frequenz der visuellen Extraktion und Map-Broadcasts (Standard: 500ms).
* **`WORLD_BOOT_DELAY_MS`**: Grace-Period vor dem Start des Simulations-Loops (Standard: 2000ms).
* **`WORLD_START_NODES`**: Semikolon-getrennte Liste von Start-Objekten (Format: `Icon:X,Y`).
