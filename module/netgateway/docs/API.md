# API-Dokumentation: NetGatewayPlugin v2.0.0

Das **NetGatewayPlugin** (ID: `NET_GATEWAY`) dient als universelle REST-Schnittstelle und Web-Server für die Diane-Architektur. Es ermöglicht die Echtzeit-Überwachung des Systems über ein Browser-Dashboard und die Injektion von Befehlen über HTTP-POST-Requests. Das Modul implementiert einen asynchronen `HttpListener` mit vollständiger CORS-Unterstützung für maximale Interoperabilität.

## Metadaten

* **Plugin-ID:** `NET_GATEWAY`
* **Version:** `2.0.0`
* **Namespace:** `Diane.Plugins`
* **Schnittstelle:** `http://localhost:8080/`

## Öffentliche Schnittstellen (IBioPlugin)

### `OnLoad()`

Aktiviert die Netzwerk-Schnittstelle.

* **Aktionen:** * Abonnieren des `BioBus`.
* Starten des HTTP-Servers via `StartServer()`.
* Meldung des Online-Status an den System-Audit.



### `OnUnload()`

Führt den deterministischen Teardown durch.

* **Aktionen:** * Abmeldung vom `BioBus`.
* Beenden des Servers via `StopServer()` (Freigabe des Ports 8080).
* Meldung des Offline-Status.



---

## Interne Logik & Routing

### `HandleTraffic(BioPacket pkt)`

Passiver Listener für den internen Bus-Verkehr.

* **Zustandssicherung:** Speichert die Daten von `STATS_UPDATE` in einem lokalen Cache für schnelle API-Abfragen.
* **O(1) Filter-Gating:** Verwirft deterministisch Hochfrequenz-Pakete (z.B. `SYS_HEARTBEAT`, `WORLD_MAP`), um den Web-Log-Puffer (`MAX_LOGS = 25`) nicht zu fluten.

### `ProcessRequestAsync(HttpListenerContext ctx)`

Die zentrale REST-Pipeline mit O(1) Pfad-Routing.

* **Sicherheit:** Setzt CORS-Header für `GET`, `POST` und `OPTIONS`.
* **Kausalität:** Behandelt Anfragen basierend auf der URI und der HTTP-Methode.
* **Fehlerbehandlung:** Jede Exception generiert einen `HTTP 500` Status und wird als `ERRN`-Paket an den `BioBus` gemeldet.

### `StopServer()`

Der sichere Teardown-Mechanismus.

* **Logik:** Setzt das `_isRunning` Flag auf `false` und schließt den `HttpListener`.
* **Isolation:** Gekapselt in einem Try-Catch-Block, um Abstürze während der Port-Freigabe zu verhindern.

### `StartServer()`
Der Initialisierungsprozess der Netz-Schnittstelle.
* **Aktionen:** Bindet den `HttpListener` an `localhost:8080` und startet die asynchrone `ServerLoop` in einem separaten Task-Kontext.
* **Fehlertoleranz:** Schlägt die Bindung fehl (z.B. Port belegt), wird ein `ERRN`-Paket an den BioBus gesendet, anstatt das Plugin zum Absturz zu bringen.

### `ServerLoop()`
Der asynchrone Hintergrund-Arbeiter.
* **Logik:** Eine nicht-blockierende Endlosschleife, die auf eingehende Verbindungen wartet und diese via `Task.Run` sofort an den Request-Handler delegiert.

---

## REST-Endpunkte

| Pfad | Methode | Rückgabe | Beschreibung |
| --- | --- | --- | --- |
| `/` | GET | HTML | Liefert das statische `index.html` Dashboard aus `default/web/`. |
| `/stats` | GET | JSON | Liefert die aktuellsten Vitalwerte (E, S, C). |
| `/logs` | GET | JSON | Liefert die letzten 25 Log-Einträge des Systembus. |
| `/command` | POST | JSON | Injiziert den Request-Body als `CMD_REQ` in den `BioBus`. |

---

## Architektur-Hinweis

Das Plugin nutzt eine **Fire-and-Forget-Task** (`Task.Run(ServerLoop)`), um den HTTP-Server-Loop vom Haupt-Thread des Plugins zu entkoppeln. Dies garantiert, dass der `BioBus` auch bei hoher Netzlast oder langsamen HTTP-Clients nicht blockiert wird.

