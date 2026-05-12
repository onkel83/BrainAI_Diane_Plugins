using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// WikiToolPlugin v2.0.0 - Semantic Extraction Edition.
    /// Extrahiert Wissen aus Wikipedia und wandelt es in deterministische logische Tripel (X IST Y) um.
    /// Implementiert eine robuste Bereinigung von Metadaten und ein O(1)-Routing für Systembefehle.
    /// </summary>
    public class WikiToolPlugin : IBioPlugin
    {
        public string PluginID => "WIKI_TOOL";
        public string Version => "2.0.0";

        private const string LOG_COMP = "WIKI";
        private readonly string _knowledgePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "library", "knowledge", "99_wikipedia_knowledge.txt");
        private List<string> _pendingFacts = new List<string>();
        private readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Initialisiert das Wiki-Tool und bereitet die Wissens-Bibliothek vor.
        /// Stellt sicher, dass die Verzeichnisstruktur für die extrahierten Fakten existiert, 
        /// bindet den Handler an den BioBus und registriert die semantischen Befehle.
        /// </summary>
        public void OnLoad()
        {
            if (!Directory.Exists(Path.GetDirectoryName(_knowledgePath)))
                Directory.CreateDirectory(Path.GetDirectoryName(_knowledgePath)!);

            BioBus.OnPacket += HandleTraffic;
            RegisterCommands();
            BioBus.Send(LOG_COMP, "INFO", $"Wiki-Semantik v{Version} einsatzbereit.");
        }

        /// <summary>
        /// Registriert die semantischen Extraktions-Befehle über den BioModulHelper.
        /// 'WIKI' dient der Ingestion von URLs, während 'WIKI_CONFIRM' den menschlichen 
        /// Validierungsschritt (Human-in-the-loop) für die extrahierten Tripel realisiert.
        /// </summary>
        private void RegisterCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("WIKI", "WIKI_REQ", false, true, 1, "<URL>", "Extrahiert semantische Daten aus einer Wikipedia-URL."),
                new BioCommand("WIKI_CONFIRM", "WIKI_DECISION", false, false, 1, "<Ja|Nummern>", "Bestätigt (Ja) oder filtert (Nummern zum Löschen) extrahierte Zeilen.")
            );
        }

        /// <summary>
        /// Führt den deterministischen Teardown des Plugins durch.
        /// Meldet den asynchronen Listener ab, um Speicherlecks bei Web-Requests zu verhindern (Zero Footprint).
        /// </summary>
        public void OnUnload() => BioBus.OnPacket -= HandleTraffic;

        /// <summary>
        /// Der asynchrone Nachrichten-Router des Wiki-Tools.
        /// Nutzt O(1)-Switch-Routing für die Befehlsauflösung und delegiert Web-Anfragen 
        /// sowie Validierungsentscheidungen an die entsprechenden Fachmethoden.
        /// </summary>
        private void HandleTraffic(BioPacket pkt)
        {
            if (pkt.Sender == PluginID) return;

            switch (pkt.Header)
            {
                case "SYS_HELP_SYNC":
                    RegisterCommands();
                    break;
                case "WIKI_REQ":
                    _ = ProcessWikipediaArticle(pkt.Get(0));
                    break;
                case "WIKI_DECISION":
                    ProcessDecision(pkt.Get(0));
                    break;
            }
        }

        /// <summary>
        /// Der asynchrone Kern des Web-Scrapings und der semantischen Analyse.
        /// Lädt den Artikel, isoliert den Content-Bereich via HtmlAgilityPack und 
        /// transformiert Absätze in eine Liste von logischen Tripeln.
        /// </summary>
        private async Task ProcessWikipediaArticle(string url)
        {
            try
            {
                BioBus.Send(LOG_COMP, "INFO", $"Analysiere Artikel: {url}...");
                string html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var paragraphs = doc.DocumentNode.SelectNodes("//div[@id='mw-content-text']//p[not(contains(@class, 'mw-empty-elt'))]");
                _pendingFacts.Clear();
                if (paragraphs != null)
                {
                    foreach (var p in paragraphs)
                    {
                        var extractedRelations = ExtractSemanticRelations(p.InnerText);
                        _pendingFacts.AddRange(extractedRelations);
                    }
                }
                _pendingFacts = _pendingFacts.Distinct().ToList();
                if (_pendingFacts.Count == 0)
                {
                    BioBus.Send(LOG_COMP, "WARN", "Keine logischen Verbindungen extrahiert.");
                    return;
                }
                BioBus.Send("DIANE", "TALK", $"Ich habe {_pendingFacts.Count} logische Verbindungen gefunden.");
                for (int i = 0; i < _pendingFacts.Count; i++) BioBus.Send("DIANE", "TALK", $"[{i + 1}] {_pendingFacts[i]}");
                BioBus.Send(LOG_COMP, "INFO", "Antworte mit 'JA' oder den Nummern der Zeilen, die gelöscht werden sollen.");
            }
            catch (Exception ex)
            {
                BioBus.Send(LOG_COMP, "ERRN", $"Extraktions-Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Verarbeitet die menschliche Entscheidung über die extrahierten Fakten.
        /// Erlaubt das selektive Löschen von Fehlinterpretationen des Parsers, bevor 
        /// die Daten in die permanente Wissensbibliothek geschrieben werden.
        /// </summary>
        private void ProcessDecision(string input)
        {
            if (_pendingFacts.Count == 0) return;
            string cmd = input.Trim().ToLower();
            bool saveAll = new[] { "yes", "y", "ja", "j" }.Contains(cmd);
            if (!saveAll)
            {
                var indicesToRemove = input.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .OrderByDescending(i => i)
                    .ToList();

                foreach (int idx in indicesToRemove) if (idx > 0 && idx <= _pendingFacts.Count) _pendingFacts.RemoveAt(idx - 1);
            }

            if (_pendingFacts.Count > 0)
            {
                File.AppendAllLines(_knowledgePath, _pendingFacts);
                BioBus.Send(LOG_COMP, "SUCCESS", $"{_pendingFacts.Count} Fakten in Bibliothek gesichert.");
                _pendingFacts.Clear();
            }
            else
            {
                BioBus.Send(LOG_COMP, "INFO", "Operation abgebrochen, keine Daten übernommen.");
            }
        }

        /// <summary>
        /// Bereinigt den Rohtext von Wikipedia-Artefakten (Einzelnachweisen, Klammern, Sonderzeichen).
        /// Schafft die Grundlage für eine saubere Regex-Analyse.
        /// </summary>
        private string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string cleaned = Regex.Replace(input, @"\[\d+\]", "");
            cleaned = Regex.Replace(cleaned, @"\([^)]*\)", "");
            cleaned = cleaned.Replace("\n", " ").Replace("\r", " ").Replace("&nbsp;", " ");
            return Regex.Replace(cleaned, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Der semantische Extraktor. Sucht nach Subjekt-Prädikat-Objekt Strukturen.
        /// Optimierter Regex-Ansatz: Erkennt 'IST', 'HAT', 'WAR' etc. und filtert 
        /// Pronomen sowie zu kurze Fragmente deterministisch aus.
        /// </summary>
        private List<string> ExtractSemanticRelations(string text)
        {
            string cleanBody = CleanText(text);
            var facts = new List<string>();
            var sentences = cleanBody.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var s in sentences)
            {
                string sentence = s.Trim();
                if (sentence.Length < 15) continue;
                var match = Regex.Match(sentence, @"^([A-ZÄÖÜ][a-zA-Zäöüß\s\-]{2,30})\s+(ist|sind|war|waren|hat|haben|besitzt)\s+([^.!?,;]{3,100})", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string subj = match.Groups[1].Value.Trim();
                    string pred = match.Groups[2].Value.ToUpper();
                    string obj = match.Groups[3].Value.Trim();
                    string lowSubj = subj.ToLower();
                    if (lowSubj == "es" || lowSubj == "dies" || lowSubj == "er" || lowSubj == "sie") continue;

                    facts.Add($"{subj} {pred} {obj}");
                }
            }
            return facts;
        }
    }
}