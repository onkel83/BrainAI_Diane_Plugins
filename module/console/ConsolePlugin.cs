using System;
using Diane.Core;

namespace Diane.Plugins
{
    /// <summary>
    /// ConsolePlugin v2.0.0
    /// Startet das neue UIManager-System und fungiert als primärer Host-Prozess für die visuelle Darstellung.
    /// </summary>
    public class ConsolePlugin : IBioInterface
    {
        public string PluginID => "VIEW-CONSOLE";
        public string Version => "2.0.0";

        /// <summary>
        /// Verfolgt den deterministischen Zustand des Interfaces. 
        /// Wird ausschließlich über die Lebenszyklus-Methoden (OnLoad/OnUnload) manipuliert.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Initialisiert die Host-Umgebung für das UI-Grid.
        /// Veröffentlicht den Start auf dem BioBus und setzt den internen Status auf aktiv.
        /// </summary>
        public void OnLoad()
        {
            BioBus.Send(PluginID, "INFO", "XAGI Grid-Interface (BioUIManager) geladen.");
            IsActive = true;
        }

        /// <summary>
        /// Führt den deterministischen Teardown des Moduls durch.
        /// Signalisiert abhängigen Rendering-Schleifen durch das Setzen auf false, dass der Host-Prozess terminiert wird.
        /// </summary>
        public void OnUnload()
        {
            IsActive = false;
        }

        /// <summary>
        /// Der kritische Pfad des Plugins. Übergibt die Kontrolle des Haupt-Threads an das Grid-OS-Rendering.
        /// Nutzt eine harte Speicherreferenz auf den Master-Switch des Kerns, um einen O(1)-Shutdown 
        /// ohne teure Event-Warteschlangen zu gewährleisten.
        /// </summary>
        /// <param name="systemRunning">Direkte Referenz auf die Hauptschleifen-Variable des Systemkerns.</param>
        public void StartInterface(ref bool systemRunning)
        {
            BioUIManager.Setup();
            BioUIManager.StartLoop(ref systemRunning);
        }
    }
}