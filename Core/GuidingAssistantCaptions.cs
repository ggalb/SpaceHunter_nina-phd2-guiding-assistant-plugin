namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    /// <summary>
    /// Localised captions for PHD2's Guiding Assistant, carried over from the caption survey done
    /// against a running PHD2 in each language. Patterns use '*'/'?' wildcards (case-insensitive)
    /// exactly like PowerShell's -like operator - '*' stands in for an accented letter so the file
    /// can stay ASCII-only regardless of source encoding.
    ///
    /// Every lookup that uses these also has a caption-independent fallback (a Win32 menu command
    /// ID, control style, or creation order) for a language nobody has mapped yet - see
    /// GuidingAssistantAutomation. Missing a language here is a logged warning, never a failure.
    /// </summary>
    internal static class GuidingAssistantCaptions {

        /// <summary>
        /// Windows menu command IDs are assigned at build time and do not vary by locale - 216 was
        /// observed identically in English, German, French and Spanish builds of PHD2 2.6.14. It
        /// may change between PHD2 versions, so it is only tried after caption matching fails, and
        /// the result is verified by confirming a window actually appeared. 0 disables it.
        /// </summary>
        public const int GAMenuFallbackCommandId = 216;

        public static readonly string[] GAMenuPatterns = {
            "*Guiding*Assistant*",      // English
            "*Nachf*hrassistent*",      // German  - Nachfuehrassistent
            "*Assistant*Guidage*",      // French  - Assistant de Guidage
            "*Asistente*Guiado*",       // Spanish - Asistente de Guiado
            "*Assistente*Guida*",       // Italian - Assistente di guida
            "*Assistente*guiagem*"      // Portuguese - Assistente de guiagem
        };

        // NB: PHD2 is not internally consistent - in German the MENU item is
        // "Nachfuehrassistent" but the WINDOW is titled "Guiding-Assistent".
        public static readonly string[] GAWindowTitles = {
            "Guiding Assistant",        // English
            "Assistant de Guidage",     // French
            "Guiding-Assistent",        // German
            "Asistente de Guiado",      // Spanish
            "Assistente di guida",      // Italian
            "Assistente de guiagem"     // Portuguese
        };

        public static readonly string[] GAStartPatterns = {
            "Start",                    // English
            "Starten",                  // German
            "D*marrer",                 // French  - Demarrer
            "Iniciar",                  // Spanish / Portuguese
            "Inizia"                    // Italian
        };

        public static readonly string[] GAStopPatterns = {
            "Stop",                     // English, and German leaves it as-is
            "Arr*ter",                  // French  - Arreter
            "Parar",                    // Spanish / Portuguese
            "Ferma"                     // Italian
        };

        public static readonly string[] GAApplyPatterns = {
            "Apply",                    // English
            "Anwenden",                 // German
            "Aplicar",                  // Spanish
            "Applica",                  // Italian
            "Appliquer"                 // French - candidate; French may also use "Apply"
        };

        // Must be specific: French also has "Montrer le Graphique du Jeu", which contains "Jeu"
        // but is the "Show Backlash Graph" pushbutton, not the checkbox.
        public static readonly string[] GABacklashPatterns = {
            "Measure*Backlash*",        // English
            "Messung*Backlash*",        // German  - Messung Backlash der Deklination
            "Medida*Backlash*",         // Spanish - Medida del Backlash de Declinacion
            "Misurazione*backlash*",    // Italian - Misurazione del backlash in declinazione
            "Medir*folga*",             // Portuguese - Medir folga de declinacao (no form of "backlash" at all)
            "Mesurer*Jeu*"              // French  - Mesurer le Jeu de Declinaison
        };

        // There are FOUR Clear buttons on PHD2's main window (History, Guide Stats, Target, and
        // one in the AO Position pane hidden without adaptive optics). Every enabled button whose
        // caption matches gets clicked, rather than working out which is which.
        public static readonly string[] PHD2ClearPatterns = {
            "Clear",            // English
            "Zur*cksetzen",     // German     - Zuruecksetzen
            "Effacer",          // French
            "Limpiar",          // Spanish
            "Cancella",         // Italian
            "Limpar",           // Portuguese - NOT the Spanish "Limpiar"
            "?terge"            // Romanian   - Sterge, leading U+0218
        };
    }
}
