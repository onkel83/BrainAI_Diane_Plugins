# API-Dokumentation: ConsolePlugin v2.0.0

Das **ConsolePlugin** (ID: `VIEW-CONSOLE`) ist der primäre Host-Prozess für die visuelle Darstellung der Diane-Architektur. Es implementiert das `IBioInterface` und übernimmt die Kontrolle über den Haupt-Thread, um das verzweigungsfreie Rendering des `BioUIManager` (Grid OS Edition) zu orchestrieren.

## Metadaten

* **Plugin-ID:** `VIEW-CONSOLE`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Vertrag/Interface:** `IBioInterface` (Erweitert die Standard-Plugin-Spezifikation um Host-Fähigkeiten)

## Eigenschaften (Properties)

### `IsActive`

* **Typ:** `bool` (Public Getter, Private Setter)
* **Kausalität:** Verfolgt den deterministischen Zustand des Interfaces. Wird ausschließlich über die Lebenszyklus-Methoden (`OnLoad`, `OnUnload`) manipuliert.

---

## Lebenszyklus-Methoden

### `OnLoad()`

Initialisiert die Host-Umgebung für das UI-Grid.

* **Kausalität:** Wird vom Systemkern aufgerufen, sobald das Modul in den Speicher geladen wurde.
* **Aktionen:**
1. Veröffentlicht eine "INFO"-Nachricht auf dem `BioBus`, um den Start des XAGI Grid-Interfaces zu signalisieren.
2. Setzt den internen State `IsActive` auf `true`.



### `OnUnload()`

Führt den deterministischen Teardown des Moduls durch.

* **Kausalität:** Wird aufgerufen, wenn das System herunterfährt oder das Modul zur Laufzeit entladen wird.
* **Aktionen:** Setzt `IsActive` auf `false`, wodurch abhängige Rendering-Schleifen signalisiert bekommen, dass der Host-Prozess terminiert wird.

---

## Core-Loop Management

### `StartInterface(ref bool systemRunning)`

Der kritische Pfad des Plugins. Übergibt die Kontrolle des Haupt-Threads an das Grid-OS-Rendering.

* **Parameter:** `ref bool systemRunning` – Eine harte Speicherreferenz auf den Master-Switch des gesamten Systemkerns.
* **Architektur-Fokus (Zero Overhead):** Statt teure Events oder Callbacks zu feuern, übergibt der Kern seinen eigenen Lebenszyklus-Pointer an das UI. Das bedeutet: Wenn der Benutzer im `BioUIManager` einen Shutdown-Befehl ausführt, wird die `systemRunning`-Variable direkt im Kern-Speicher auf `false` gesetzt. Der Loop bricht in O(1)-Zeit ab, ohne Message-Warteschlangen abarbeiten zu müssen.
* **Aktionen:**
1. Führt `BioUIManager.Setup()` aus (Initialisierung von Puffern, Cursor-Sperren, Terminal-Größen).
2. Startet die blockierende UI-Schleife via `BioUIManager.StartLoop(ref systemRunning)`.