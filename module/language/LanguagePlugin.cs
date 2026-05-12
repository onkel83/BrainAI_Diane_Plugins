using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Collections.Concurrent;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// LanguagePlugin v2.1.0 - Contextual Memory Edition
    /// Linguistisches Verarbeitungszentrum mit Kurzzeitgedächtnis.
    /// Löst Pronomen (er, sie, es) automatisch anhand des letzten Gesprächskontextes auf.
    /// </summary>
    public class LanguagePlugin : IBioPlugin
    {
        public string PluginID => "LANG-MOD";
        public string Version => "2.1.0";
        private const string LOG_COMP = "LANG";

        private readonly ConcurrentDictionary<ulong, string> _vocabCache = new ConcurrentDictionary<ulong, string>();
        private float _currentEnergy = 100f;

        private string _lastSubjectContext = "";
        private DateTime _lastSubjectTime = DateTime.MinValue;

        private float EnergyThreshold => float.Parse(BioConfig.Instance.Get("LANG_ENERGY_THRESHOLD", "5.0"), CultureInfo.InvariantCulture);
        private string[] StopWords => BioConfig.Instance.Get("LANG_STOPWORDS", "DER,DIE,DAS,EIN,EINE,EINEN").Split(',');
        private string[] QuestionWords => BioConfig.Instance.Get("LANG_QUESTION_WORDS", "WER,WAS,WO,WIE,WANN").Split(',');

        private string[] Pronouns => BioConfig.Instance.Get("LANG_PRONOUNS", "ER,SIE,ES,IHN,IHM,IHR,DIESE,DIESER,DIESES").Split(',');

        /// <summary>
        /// Initialisiert den linguistischen Cortex und aktiviert das kontextuelle Kurzzeitgedächtnis.
        /// Abonniert den globalen BioBus für den asynchronen Nachrichtenverkehr und triggert 
        /// die Etablierung der syntaktischen und semantischen Systembefehle.
        /// </summary>
        public void OnLoad()
        {
            BioBus.OnPacket += HandleTraffic;
            RegisterAllCommands();
            BioBus.Send(LOG_COMP, "INFO", $"Linguistischer Cortex v{Version} (Contextual Memory) online.");
        }

        /// <summary>
        /// Registriert die linguistischen Kernbefehle über den zentralen BioModulHelper.
        /// Definiert die kausalen Schnittstellen für die dynamische Wortschatzerweiterung (NOM, VERB, ADJ) 
        /// sowie für das deterministische Erlernen von relationalen Verknüpfungen (ISTR, HATTR, NOTR).
        /// Delegiert das Pre-Gating vollständig an den Command-Router, da Sprachverarbeitung per se keine 
        /// Systemprivilegien erfordert.
        /// </summary>
        private void RegisterAllCommands()
        {
            BioModulHelper.RegisterCommands(PluginID,
                new BioCommand("NOM", "LANG_DEF_NOM", false, false, 1, "<Wort>", "Lehrt das System ein neues Nomen (Objekt)."),
                new BioCommand("VERB", "LANG_DEF_VERB", false, false, 1, "<Wort>", "Lehrt das System ein neues Verb (Aktion)."),
                new BioCommand("ADJ", "LANG_DEF_ADJ", false, false, 1, "<Wort>", "Lehrt das System ein neues Adjektiv (Eigenschaft)."),
                new BioCommand("SELF", "LANG_DEF_SELF", false, false, 1, "<Wort>", "Lehrt das System ein neues Identitäts-Token (Self)."),
                new BioCommand("TIME", "LANG_DEF_TIME", false, false, 1, "<Wort>", "Lehrt das System ein neues Zeit-Konzept."),
                new BioCommand("LINK", "LANG_TEACH_LINK", false, false, 2, "<WortA> <WortB> [Gewicht]", "Erstellt eine einfache Assoziation zwischen zwei Wörtern."),
                new BioCommand("ISTR", "LANG_TEACH_IST", false, false, 2, "<Subjekt> <Objekt> [Gewicht]", "Definiert eine feste IST-Relation (A ist B)."),
                new BioCommand("HATTR", "LANG_TEACH_HAT", false, false, 2, "<Subjekt> <Objekt> [Gewicht]", "Definiert eine feste HAT-Relation (A hat B)."),
                new BioCommand("NOTR", "LANG_TEACH_NOT", false, false, 2, "<Subjekt> <Objekt> [Gewicht]", "Definiert eine feste NOT-Relation (A ist nicht B)."),
                new BioCommand("PARSE", "LANG_REQ", false, false, 1, "<Satz/Frage>", "Parst eine Benutzereingabe oder beantwortet eine Frage.")
            );
        }

        /// <summary>
        /// Führt den kausalen Teardown des Plugins durch.
        /// Meldet den Event-Listener vom Systembus ab, um Speicherlecks und hängende Referenzen 
        /// beim Entladen deterministisch auszuschließen (Zero Footprint).
        /// </summary>
        public void OnUnload() => BioBus.OnPacket -= HandleTraffic;

        /// <summary>
        /// Der lokale Nachrichten-Router (Switchboard) des linguistischen Cortex.
        /// Nutzt eine konsequente Switch-Architektur (O(1) Jump-Table) für latenzfreies Routing von BioBus-Paketen.
        /// Verarbeitet neben den expliziten Sprach- und Lernbefehlen auch systemweite Status-Updates:
        /// Aktualisiert das metabolische Energieniveau (STATS_UPDATE) und synchronisiert das lokale 
        /// Kurzzeitgedächtnis über eingehende Vokabel-Snapshots (VOCAB_SNAPSHOT). Eigene Pakete werden deterministisch blockiert.
        /// </summary>
        /// <param name="pkt">Das vom BioBus empfangene Datenpaket, das den auszuführenden Header und die Payload enthält.</param>
        private void HandleTraffic(BioPacket pkt)
        {
            switch (pkt.Header)
            {
                case PluginID: break;
                case "SYS_HELP_SYNC": RegisterAllCommands(); break;
                case "STATS_UPDATE":
                    string ePart = pkt.Get(0).Split(',')[0].Substring(2);
                    float.TryParse(ePart, NumberStyles.Any, CultureInfo.InvariantCulture, out _currentEnergy);
                    break;
                case "VOCAB_SNAPSHOT": UpdateLocalVocabCache(pkt.Get(0)); break;
                case "LANG_DEF_NOM": ProcessDefinition(pkt.Get(0), "OBJECT"); break;
                case "LANG_DEF_VERB": ProcessDefinition(pkt.Get(0), "ACTION"); break;
                case "LANG_DEF_ADJ": ProcessDefinition(pkt.Get(0), "LOGIC"); break;
                case "LANG_DEF_SELF": ProcessDefinition(pkt.Get(0), "SELF"); break;
                case "LANG_DEF_TIME": ProcessDefinition(pkt.Get(0), "TIME"); break;
                case "LANG_TEACH_LINK": ProcessRelation(pkt.Data, 0); break;
                case "LANG_TEACH_IST": ProcessRelation(pkt.Data, 0x1000000000000000); break;
                case "LANG_TEACH_HAT": ProcessRelation(pkt.Data, 0x2000000000000000); break;
                case "LANG_TEACH_NOT": ProcessRelation(pkt.Data, 0x4000000000000000); break;
                case "LANG_REQ": ProcessInput(pkt); break;
            }
        }

        /// <summary>
        /// Evaluiert und delegiert die Neudefinition von semantischen Knotenpunkten (Wörtern) in spezifischen Clustern.
        /// Implementiert ein striktes metabolisches Gating: Ist das Energieniveau des Systems zu niedrig, 
        /// wird das Erlernen neuer Konzepte blockiert (Schutz vor CPU-Überlastung bei Erschöpfung).
        /// Erfolgreiche Eingaben primen den lokalen Satzkontext (für die spätere Pronomen-Auflösung) 
        /// und werden als CORE_TEACH_NAME an das Langzeitgedächtnis des Kerns weitergereicht.
        /// </summary>
        /// <param name="name">Das zu erlernende Wort (wird deterministisch in Großbuchstaben konvertiert).</param>
        /// <param name="cluster">Die semantische Gruppierung (z.B. OBJECT, ACTION, LOGIC) für die Netz-Architektur.</param>
        private void ProcessDefinition(string name, string cluster)
        {
            if (_currentEnergy < EnergyThreshold) return;
            UpdateContext(name.ToUpper());
            BioBus.Send(LOG_COMP, "CORE_TEACH_NAME", name.ToUpper(), cluster);
        }

        /// <summary>
        /// Evaluiert und verarbeitet deterministisch gewichtete semantische Relationen (z.B. IST, HAT, NICHT) zwischen zwei Konzepten.
        /// Nutzt Bitmasken zur exakten Klassifizierung der Bindungsart und blockiert den Lernvorgang (Metabolisches Gating), 
        /// falls die Systemenergie zu gering ist. Aktualisiert den lokalen Pronomen-Kontext mit dem Subjekt und 
        /// routet die Assoziation (CORE_ASSOC_NAME) an das Langzeitgedächtnis des Kerns.
        /// </summary>
        /// <param name="data">Die Payload vom Bus, bestehend aus [Subjekt, Objekt, optionales Gewicht].</param>
        /// <param name="mask">Die hexadezimale Bitmaske, die die Art der Relation (z.B. 0x10... für IST) kausal definiert.</param>
        private void ProcessRelation(string[] data, ulong mask)
        {
            if (_currentEnergy < EnergyThreshold) return;
            float weight = data.Length > 2 ? float.Parse(data[2], CultureInfo.InvariantCulture) : 1.0f;
            UpdateContext(data[0].ToUpper());
            BioBus.Send(LOG_COMP, "CORE_ASSOC_NAME", data[0].ToUpper(), data[1].ToUpper(), mask.ToString(), weight.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Die primäre Parsing-Engine für natürliche oder strukturierte Benutzereingaben.
        /// Führt initial eine Pre-Processing-Sanitisierung durch, bei der Pronomen (z.B. 'er', 'sie') 
        /// anhand des aktiven Kurzzeitgedächtnisses kausal aufgelöst werden (ResolvePronouns). 
        /// Routet die bereinigte Eingabe anschließend deterministisch weiter: Interrogativsätze (Fragen) gehen an ProcessQuestion, 
        /// Deklarativsätze generieren Standard-Assoziationen.
        /// </summary>
        /// <param name="pkt">Das eingehende Datenpaket mit der unstrukturierten Satz-Payload.</param>
        private void ProcessInput(BioPacket pkt)
        {
            string clean = string.Join(" ", pkt.Data).ToUpper();
            clean = ResolvePronouns(clean);
            if (QuestionWords.Any(q => clean.StartsWith(q))) ProcessQuestion(clean);
            else
            {
                var parts = clean.Split(' ');
                string subj = parts[0];
                string obj = parts.Last();
                UpdateContext(subj);
                BioBus.Send(LOG_COMP, "CORE_ASSOC_NAME", subj, obj, "0", "0.8");
            }
        }

        /// <summary>
        /// Behandelt interrogative Eingaben durch Abgleich mit dem lokalen O(1)-Vokabel-Cache.
        /// Wenn das Zielobjekt der Frage bekannt ist, wird eine Faktenabfrage (FACT_REQ) an den Kern gesendet.
        /// Fehlt das Konzept im lokalen Cache, triggert die Methode deterministisch einen Active-Learning-Prozess 
        /// und bittet den User direkt um eine Definition ("Was ist X?").
        /// </summary>
        /// <param name="cleanInput">Der bereits von Pronomen bereinigte und in Großbuchstaben konvertierte Fragesatz.</param>
        private void ProcessQuestion(string cleanInput)
        {
            string targetName = cleanInput.Split(' ').Last().Replace("?", "");
            UpdateContext(targetName);

            var entry = _vocabCache.FirstOrDefault(x => x.Value.Equals(targetName, StringComparison.OrdinalIgnoreCase));
            if (entry.Key == 0) BioBus.Send("DIANE", "TALK", $"Was ist {targetName}?");
            else BioBus.Send(LOG_COMP, "FACT_REQ", entry.Key.ToString(), targetName);
        }

        /// <summary>
        /// Evaluiert und ersetzt kausal Pronomen (z.B. 'er', 'sie', 'es') im Eingabestream durch das aktive Subjekt des Kurzzeitgedächtnisses.
        /// Implementiert ein striktes temporales Gating (60 Sekunden TTL - Time to Live), um Kontext-Verfälschungen 
        /// bei Themenwechseln oder längeren Pausen deterministisch zu verhindern.
        /// Erfolgreiche Ersetzungen werden für das Audit-Log auf dem BioBus ("INFO") transparent gemacht.
        /// </summary>
        /// <param name="input">Der rohe, unstrukturierte Eingabesatz.</param>
        /// <returns>Der deterministisch bereinigte Satz mit explizit aufgelösten Subjekten.</returns>
        private string ResolvePronouns(string input)
        {
            if (string.IsNullOrEmpty(_lastSubjectContext)) return input;
            if ((DateTime.Now - _lastSubjectTime).TotalSeconds > 60) return input;

            var words = input.Split(' ').ToList();
            bool resolved = false;
            for (int i = 0; i < words.Count; i++)
            {
                string wordClean = words[i].Replace("?", "").Replace("!", "");
                if (Pronouns.Contains(wordClean))
                {
                    words[i] = words[i].Replace(wordClean, _lastSubjectContext);
                    resolved = true;
                }
            }
            if (resolved) BioBus.Send(LOG_COMP, "INFO", $"Kontext aufgelöst: Ersetze Pronomen durch '{_lastSubjectContext}'.");
            return string.Join(" ", words);
        }

        /// <summary>
        /// Aktualisiert den linguistischen Zustandsraum (Kurzzeitgedächtnis) mit einem neuen potenziellen Subjekt.
        /// Sanitisiert die Eingabe und blockiert deterministisch ungültige Kontext-Anker wie Fragewörter, 
        /// Pronomen oder Stopwörter, um eine semantische Verschmutzung des Caches ("Context Pollution") auszuschließen.
        /// </summary>
        /// <param name="newSubject">Das extrahierte Subjekt aus der letzten erfolgreichen Interaktion.</param>
        private void UpdateContext(string newSubject)
        {
            newSubject = newSubject.Replace("?", "").Replace("!", "").Trim();
            if (QuestionWords.Contains(newSubject) || Pronouns.Contains(newSubject) || StopWords.Contains(newSubject)) return;

            _lastSubjectContext = newSubject;
            _lastSubjectTime = DateTime.Now;
        }

        /// <summary>
        /// Synchronisiert den lokalen High-Speed-Cache (O(1)-Lookup) für das Vokabular mit dem Langzeitgedächtnis des Systemkerns.
        /// Parst die komprimierte Payload (ID:Wort) deterministisch und überschreibt thread-safe die lokalen Referenzen, 
        /// um latenzfreie Active-Learning-Rückfragen zu ermöglichen.
        /// </summary>
        /// <param name="data">Die kommagetrennte Liste der Vokabel-Snapshots vom BioBus.</param>
        private void UpdateLocalVocabCache(string data)
        {
            foreach (var p in data.Split(','))
            {
                var kv = p.Split(':');
                if (kv.Length == 2 && ulong.TryParse(kv[0], out ulong id)) _vocabCache[id] = kv[1];
            }
        }
    }
}