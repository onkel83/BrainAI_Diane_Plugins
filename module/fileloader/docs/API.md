# API-Dokumentation: FileLoaderPlugin v2.0.0

Das **FileLoaderPlugin** (ID: `FILE_LOADER`) verwaltet die physische Dateisystem-Schnittstelle (Training-Inbox) der Diane-Architektur. In der Secure Pattern Edition (v2.0.0) verzichtet das Modul auf harte Auto-Logins (Backdoors) und delegiert die Rechteprüfung vollständig an das deterministische Session-Gating des `CommandPlugin`.

## Metadaten

* **Plugin-ID:** `FILE_LOADER`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Abhängigkeiten:** `Diane.Core`

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Initialisiert das Plugin und etabliert die Dateisystem-Grenze.

* **Kausalität:** Wird beim Modulstart aufgerufen.
* **Aktionen:**
1. Löst den absoluten Pfad zur `training_data`-Inbox auf.
2. Erstellt das Verzeichnis deterministisch, falls es nicht existiert.
3. Abonniert den lokalen Traffic-Handler auf dem `BioBus`.
4. Triggert `RegisterCommands()`.



### `OnUnload()`

Setzt die Zero-Footprint-Policy beim Beenden um.

* **Kausalität:** Wird beim Entladen des Moduls aufgerufen.
* **Aktionen:** Meldet den Event-Listener (`HandleTraffic`) asynchron vom Bus ab, um Speicherlecks und Ghost-Calls zu verhindern.

---

## Interne Logik (Private Methoden)

### `HandleTraffic(BioPacket pkt)`

Das lokale Switchboard für asynchrone Dateioperationen.

* **Parameter:** `BioPacket pkt`
* **Funktionsweise:** * Blockiert eigene Pakete (Rekursionsschutz).
* Routet auf Basis des Headers (`FILE_LOAD_REQ`, `FILE_LIST_REQ`, `FILE_PURGE_REQ`) in O(1)-Zeit an die spezifischen Dateisystem-Methoden.
* Verarbeitet `SYS_HELP_SYNC` zur Laufzeit-Synchronisation der Befehle.



### `HandleLoadRequest(string target)`

Evaluiert und routet Ladeanfragen.

* **Parameter:** `string target` (Dateiname oder "ALL")
* **Aktionen:** Löst Batch-Verarbeitungen auf oder validiert Einzelpfade gegen die physische Inbox. Leitet validierte Pfade an `ProcessFile` weiter.

### `ProcessFile(string path)`

Die konsumptive Verarbeitungs-Engine.

* **Parameter:** `string path` (Absoluter Dateipfad)
* **Kausalität:**
1. Liest die Datei zeilenweise ein.
2. Ignoriert Leerzeilen und Kommentare (`//`).
3. Injiziert Befehle als rohe `CMD_REQ` auf den Bus (mit `Task.Delay(30)` als Taktgeber, um den Core nicht zu überfluten).
4. **Zero Footprint:** Löscht die Quelldatei nach erfolgreicher Verarbeitung physisch, um Replay-Ausführungen auszuschließen.



### `ListFiles()` & `PurgeInbox()`

* **`ListFiles`:** Iteriert über die Inbox und feuert zustandsfreie `DATA`-Pakete für jede gefundene Datei auf den Bus (kein RAM-Overhead durch große Listen).
* **`PurgeInbox`:** Führt einen unumkehrbaren State Wipe der Inbox durch. Nutzt isolierte `try-catch`-Blöcke, um System-Locks einzelner Dateien zu umgehen.

---

## Registrierte Befehle (Via BioModulHelper)

| Befehl | Header | Admin? | Parameter | Beschreibung |
| --- | --- | --- | --- | --- |
| **LOAD** | `FILE_LOAD_REQ` | Dynamisch* | `[Dateiname|ALL]` | Lädt Trainingsdateien und verarbeitet sie. |
| **FILES** | `FILE_LIST_REQ` | Nein | - | Zeigt alle Dateien in der Inbox an. |
| **PURGE** | `FILE_PURGE_REQ` | Ja | - | Löscht unwiderruflich alle Dateien der Inbox. |

> **Hinweis zu LOAD:* Der Befehl erfordert per Definition *keine* Admin-Rechte (`false`), aber er führt die in der Datei enthaltenen Befehle im aktuellen Rechte-Kontext des aufrufenden Senders aus. Wenn ein normaler User eine Datei lädt, die Admin-Befehle enthält, werden diese vom Command-Router blockiert.

---

## Architektur & Sicherheit

* **Pre-Gating Delegation:** Das Modul zwingt den User nicht in eine Admin-Rolle. Es injiziert Befehle lediglich als `CMD_REQ`. Die Autorisierung übernimmt der Kern.
* **Konsumptive Architektur:** Daten, die verarbeitet wurden, existieren nicht mehr. Das physische Löschen durch `ProcessFile` garantiert, dass ein Systemzustand nicht versehentlich doppelt überschrieben wird.
* **Asynchrone Isolation:** Alle File-I/O-Operationen laufen asynchron, wodurch der Haupt-Thread des XAGI-Grids (UI) niemals durch langsame Festplattenzugriffe blockiert wird.
