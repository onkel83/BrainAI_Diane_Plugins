# API-Dokumentation: KnowledgePlugin v2.0.0

Das **KnowledgePlugin** (ID: `KNOWLEDGE`) fungiert als deterministische Wissensquelle innerhalb der Diane-Architektur. Es verwaltet eine physische Bibliothek (`library/knowledge`) und ermöglicht es dem System, gespeichertes Wissen (in Form von Befehlsketten) entweder gezielt oder per Zufallsprinzip zu "lesen" und sequentiell in den Systembus zu injizieren.

## Metadaten

* **Plugin-ID:** `KNOWLEDGE`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Abhängigkeiten:** `Diane.Core`

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Initialisiert die Wissensbibliothek und stellt die physische Integrität sicher.

* **Kausalität:** Wird beim Laden des Moduls aufgerufen.
* **Aktionen:**
1. Prüft die Existenz des Bibliotheksverzeichnisses und erstellt dieses bei Bedarf deterministisch.
2. Abonniert den globalen `BioBus` für eingehende Steuerbefehle.
3. Registriert den Befehl `LIES` im System.



### `OnUnload()`

Führt den sauberen Teardown durch.

* **Kausalität:** Wird beim Entladen des Moduls aufgerufen.
* **Aktionen:** Meldet den Listener vom `BioBus` ab, um Speicherlecks zu vermeiden (Zero Footprint).

---

## Interne Logik (Private Methoden)

### `HandleTraffic(BioPacket pkt)`

Zentraler Router für bibliotheksspezifische Anfragen.

* **Header-Verarbeitung:**
* `READ_FILE`: Wertet Argumente aus. Wenn leer, wird `ProcessRandomFile` aufgerufen; andernfalls `ProcessSpecificFile` mit dem entsprechenden Pfad.
* `SYS_HELP_SYNC`: Triggert die Neu-Registrierung der Befehls-Metadaten.



### `ProcessSpecificFile(string fileName)`

Validiert und bereitet den Zugriff auf ein spezifisches "Buch" vor.

* **Aktionen:** Ergänzt automatisch die konfigurierte Dateiendung (z.B. `.txt`), prüft die physische Existenz im `library`-Pfad und leitet bei Erfolg an `ReadFile` weiter.
* **Fehlerbehandlung:** Sendet `WARN`- oder `ERRN`-Pakete bei fehlenden Dateien oder Zugriffsfehlern.

### `ProcessRandomFile()`

Implementiert die kausale Fallback-Logik.

* **Kausalität:** Wählt per Zufallsgenerator (RNG) eine Datei aus dem Verzeichnis aus, sofern dieses nicht leer ist. Bei leerer Bibliothek erfolgt eine Rückmeldung via `TALK`-Header.

### `ReadFile(string filePath)`

Die Ingestion-Engine des Moduls.

* **Funktionsweise:**
1. Liest die Datei zeilenweise ein.
2. Bereinigt die Daten (Trim) und filtert Kommentare (`//`) sowie Leerzeilen heraus.
3. Injiziert die extrahierten Zeilen sequentiell als `CMD_REQ` auf den Bus.
4. **Architektur-Hinweis:** Die Ausführung unterliegt dem Pre-Gating des Command-Routers; das Plugin selbst erzwingt keine Privilegien.



---

## Registrierte Befehle (Via BioModulHelper)

| Befehl | Header | Admin? | Parameter | Beschreibung |
| --- | --- | --- | --- | --- |
| **LIES** | `READ_FILE` | Nein | `[Dateiname?]` | Liest eine Datei aus der Bibliothek. Ohne Angabe wird ein zufälliges Element gewählt. |

---

## Architektur-Konfiguration

Das Plugin nutzt die `BioConfig`, um Pfade und Präfixe zur Laufzeit zu steuern:

* `KNOWLEDGE_DIR`: Basisverzeichnis der Bibliothek (Standard: `library\knowledge`).
* `KNOWLEDGE_FILE_EXT`: Erwartete Dateiendung (Standard: `.txt`).
* `KNOWLEDGE_COMMENT_PREFIX`: Zeichenfolge für zu ignorierende Zeilen (Standard: `//`).
