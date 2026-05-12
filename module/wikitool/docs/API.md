# API-Dokumentation: WikiToolPlugin v2.0.0

Das **WikiToolPlugin** (ID: `WIKI_TOOL`) dient als semantische Brücke zur externen Wissens-Ingestion. Es extrahiert unstrukturierte Informationen aus Wikipedia-Artikeln und transformiert diese mittels Regex-basierter Analyse in deterministische logische Tripel (Subjekt-Prädikat-Objekt). Das Modul implementiert einen Validierungsschritt (Human-in-the-loop), um die Integrität der lokalen Wissensdatenbank sicherzustellen.

## Metadaten

* **Plugin-ID:** `WIKI_TOOL`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Abhängigkeiten:** `Diane.Core`, `HtmlAgilityPack`

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Initialisiert die semantische Pipeline und bereitet die Bibliothek vor.

* **Kausalität:** Wird beim Systemstart aufgerufen.
* **Aktionen:** 1. Erstellt bei Bedarf die Verzeichnisstruktur für die Wissensbasis (`library/knowledge/`).
2. Registriert den asynchronen Listener am `BioBus`.
3. Etabliert die semantischen Befehle via `RegisterCommands()`.

### `OnUnload()`

Führt den deterministischen Teardown durch.

* **Aktionen:** Meldet den Event-Listener ab, um Speicherlecks während laufender HTTP-Anfragen zu verhindern (Zero Footprint).

---

## Interne Logik & Extraktion

### `HandleTraffic(BioPacket pkt)`

Der asynchrone Nachrichten-Router des Moduls.

* **Architektur-Feature:** Nutzt O(1)-Switch-Routing zur effizienten Auflösung von System-Synchronisationen, Extraktions-Anfragen (`WIKI_REQ`) und Nutzer-Entscheidungen (`WIKI_DECISION`).

### `ProcessWikipediaArticle(string url)`

Der Kern des Web-Scrapings und der Analyse.

* **Pipeline:** 1. Lädt den HTML-Content asynchron via `HttpClient`.
2. Isoliert relevante Text-Absätze (`<p>`) unter Ausschluss von Navigationselementen.
3. Triggert die semantische Analyse und präsentiert die Ergebnisse via `DIANE.TALK` zur menschlichen Überprüfung.
* **Fehlerbehandlung:** Protokolliert Netzwerk- oder Parsing-Fehler als `ERRN`-Pakete am Systembus.

### `ProcessDecision(string input)`

Realisierung der „Human-in-the-loop“-Validierung.

* **Logik:** Erlaubt die Bestätigung aller Fakten (`JA`) oder das selektive Löschen fehlerhafter Zeilen durch Angabe der Indizes.
* **Persistenz:** Speichert die validierten Tripel in der physischen Wissensbibliothek.

### `ExtractSemanticRelations(string text)`

Der deterministische Extraktions-Algorithmus.

* **Verfahren:** Nutzt optimierte Regex-Patterns zur Identifikation von Subjekt-Prädikat-Objekt-Strukturen (z.B. „Berlin IST Hauptstadt“).
* **Integritäts-Filter:** Verwirft Fragmente mit Pronomen (Er, Sie, Es) oder unzureichender Länge, um die Qualität der Wissensbasis hochzuhalten.

### `CleanText(string input)`

Die linguistische Vorreinigung.

* **Aktionen:** Entfernt Wikipedia-Artefakte wie Einzelnachweise (`[1]`), phonetische Umschreibungen in Klammern und überschüssige Whitespaces.

---

## Registrierte Befehle

| Befehl | Header | Admin? | Parameter | Beschreibung |
| --- | --- | --- | --- | --- |
| **WIKI** | `WIKI_REQ` | Nein | `<URL>` | Startet die Extraktion aus dem angegebenen Wikipedia-Link. |
| **WIKI_CONFIRM** | `WIKI_DECISION` | Nein | `<Ja|Nummern>` | Bestätigt den Import oder filtert extrahierte Zeilen. |

---

## Architektur-Pfad

* **Wissensbasis:** `library/knowledge/99_wikipedia_knowledge.txt`
* **Strategie:** Das Plugin nutzt asynchrone Tasks (`Task.Run`), um den `BioBus` während langwieriger Netzwerk-I/O-Operationen nicht zu blockieren.