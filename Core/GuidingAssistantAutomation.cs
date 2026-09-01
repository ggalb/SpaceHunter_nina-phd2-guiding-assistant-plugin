using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using static NINA.Plugin.Phd2GuidingAssistant.Core.NativeMethods;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    internal readonly struct ControlInfo {
        public IntPtr Handle { get; }
        public string ClassName { get; }
        public string Text { get; }
        public bool Enabled { get; }
        public bool Visible { get; }
        public bool IsCheckbox { get; }

        public ControlInfo(IntPtr handle, string className, string text, bool enabled, bool visible, bool isCheckbox) {
            Handle = handle;
            ClassName = className;
            Text = text;
            Enabled = enabled;
            Visible = visible;
            IsCheckbox = isCheckbox;
        }
    }

    /// <summary>
    /// Everything needed to open PHD2's Guiding Assistant, read and click its controls, and find
    /// them again by structure when a caption goes untranslated. See NativeMethods for why this is
    /// Win32 rather than UI Automation: wxWidgets menus are native HMENUs (not UIA children of the
    /// main window at all) and its dialog controls expose too poorly through the MSAA-to-UIA bridge
    /// for a Descendants search to be worth relying on.
    /// </summary>
    internal static class GuidingAssistantAutomation {

        /// <summary>PowerShell's -like wildcard semantics ('*'/'?' , case-insensitive), applied to a plain button caption.</summary>
        public static bool WildcardMatch(string text, string pattern) {
            if (text == null) return false;
            var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
        }

        private static bool AnyMatch(string text, IEnumerable<string> patterns) => patterns.Any(p => WildcardMatch(text, p));

        // ---- Process / window lookup --------------------------------------------------------

        /// <summary>Polls for PHD2's main window handle by process, not by title - works regardless of language or profile name.</summary>
        public static IntPtr FindPhd2MainWindow(TimeSpan timeout) {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline) {
                try {
                    foreach (var p in Process.GetProcessesByName("phd2")) {
                        p.Refresh();
                        if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
                    }
                } catch { /* transient - process list can change mid-enumeration */ }
                Thread.Sleep(400);
            }
            return IntPtr.Zero;
        }

        /// <summary>Every visible top-level window with a title, as handle -> title. Used to spot a new dialog appearing without knowing its caption.</summary>
        public static Dictionary<IntPtr, string> GetTopLevelWindowMap() {
            var map = new Dictionary<IntPtr, string>();
            var h = IntPtr.Zero;
            for (var n = 0; n < 400; n++) {
                h = FindWindowExW(IntPtr.Zero, h, null, null);
                if (h == IntPtr.Zero) break;
                if (!IsWindowVisible(h)) continue;
                var sb = new StringBuilder(512);
                GetWindowTextW(h, sb, 512);
                var t = sb.ToString().Trim();
                if (t.Length > 0) map[h] = t;
            }
            return map;
        }

        public static IntPtr FindTopWindowByTitle(string title) {
            try { return FindWindowW(null, title); }
            catch { return IntPtr.Zero; }
        }

        // ---- Win32 menu walking --------------------------------------------------------------

        /// <summary>Recursively searches an HMENU for a caption matching pattern. Returns 0 if not found; collects every caption seen into <paramref name="seen"/>.</summary>
        public static uint FindMenuCommandId(IntPtr hMenu, string pattern, List<string> seen, int depth = 0) {
            if (hMenu == IntPtr.Zero || depth > 3) return 0;
            var count = GetMenuItemCount(hMenu);

            for (var i = 0; i < count; i++) {
                var sb = new StringBuilder(512);
                GetMenuStringW(hMenu, (uint)i, sb, 512, MF_BYPOSITION);
                var caption = sb.ToString().Replace("&", "");
                var tab = caption.IndexOf('\t');
                if (tab >= 0) caption = caption.Substring(0, tab);
                caption = caption.Trim();
                if (caption.Length > 0) seen.Add(caption);

                var sub = GetSubMenu(hMenu, i);
                if (sub != IntPtr.Zero) {
                    var id = FindMenuCommandId(sub, pattern, seen, depth + 1);
                    if (id != 0) return id;
                    continue;
                }

                if (WildcardMatch(caption, pattern)) return GetMenuItemID(hMenu, i);
            }
            return 0;
        }

        // ---- Control enumeration --------------------------------------------------------------

        /// <summary>Recursively enumerates child windows. wxWidgets nests controls inside panels and sizers, so a flat scan is not enough.</summary>
        public static List<ControlInfo> GetChildControls(IntPtr parent, int depth = 0) {
            var list = new List<ControlInfo>();
            if (depth > 5) return list;

            var h = IntPtr.Zero;
            for (var i = 0; i < 500; i++) {
                h = FindWindowExW(parent, h, null, null);
                if (h == IntPtr.Zero) break;

                var clsSb = new StringBuilder(256);
                GetClassNameW(h, clsSb, 256);
                var className = clsSb.ToString();

                var txtSb = new StringBuilder(512);
                GetWindowTextW(h, txtSb, 512);
                var text = txtSb.ToString().Replace("&", "").Trim();

                var style = 0;
                try { style = GetWindowLongW(h, GWL_STYLE); } catch { /* not every window answers this */ }
                var btnType = style & BS_TYPEMASK;
                var isCheckbox = className == "Button" &&
                    (btnType == BS_CHECKBOX || btnType == BS_AUTOCHECKBOX || btnType == BS_3STATE || btnType == BS_AUTO3STATE);

                list.Add(new ControlInfo(h, className, text, IsWindowEnabled(h), IsWindowVisible(h), isCheckbox));
                list.AddRange(GetChildControls(h, depth + 1));
            }
            return list;
        }

        public static ControlInfo? GetControlAny(IntPtr hwnd, IEnumerable<string> patterns) {
            var controls = GetChildControls(hwnd);
            foreach (var pattern in patterns) {
                foreach (var c in controls) {
                    if (c.ClassName == "Button" && WildcardMatch(c.Text, pattern)) return c;
                }
            }
            return null;
        }

        public static List<ControlInfo> GetControlsAny(IntPtr hwnd, IEnumerable<string> patterns) {
            var controls = GetChildControls(hwnd);
            foreach (var pattern in patterns) {
                var hits = controls.Where(c => c.ClassName == "Button" && WildcardMatch(c.Text, pattern)).ToList();
                if (hits.Count > 0) return hits;
            }
            return new List<ControlInfo>();
        }

        /// <summary>The backlash checkbox is the only checkbox in the dialog - identified by window style, not caption.</summary>
        public static ControlInfo? GetCheckboxByStyle(IntPtr gaHwnd) {
            foreach (var c in GetChildControls(gaHwnd)) {
                if (c.IsCheckbox) return c;
            }
            return null;
        }

        /// <summary>
        /// Start/Stop by position relative to "OptionsButton" - a wxWidgets internal name that is
        /// NOT localised. Verified against English, German and French dialogs, which all enumerate
        /// their trailing controls in the same order:
        ///   &lt;backlash checkbox&gt; , &lt;status text&gt; , Start , OptionsButton , Stop
        /// </summary>
        public static (ControlInfo? Start, ControlInfo? Stop) GetStartStopByPosition(IntPtr gaHwnd) {
            var controls = GetChildControls(gaHwnd);
            var anchor = controls.FindIndex(c => c.Text == "OptionsButton");
            if (anchor < 0) return (null, null);

            ControlInfo? start = null;
            for (var i = anchor - 1; i >= 0; i--) {
                if (controls[i].ClassName == "Button" && !controls[i].IsCheckbox) { start = controls[i]; break; }
            }
            ControlInfo? stop = null;
            for (var i = anchor + 1; i < controls.Count; i++) {
                if (controls[i].ClassName == "Button" && !controls[i].IsCheckbox) { stop = controls[i]; break; }
            }
            return (start, stop);
        }

        public static void ClickControl(ControlInfo control) {
            PostMessageW(control.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        public static void LogControls(IntPtr hwnd, string heading) {
            Logger.Warning($"--- {heading} ---");
            foreach (var c in GetChildControls(hwnd)) {
                if (c.Text.Length > 0 || c.ClassName == "Button") {
                    Logger.Warning($"   {c.ClassName,-16} enabled={c.Enabled,-5} visible={c.Visible,-5} '{c.Text}'");
                }
            }
            Logger.Warning("---------------------------------------------------");
        }

        // ---- Opening the dialog ----------------------------------------------------------------

        /// <summary>
        /// PHD2 asks "Ok to disable guide output?" before opening the Guiding Assistant, unless
        /// "Don't ask again" is ticked - which is untrue for every fresh profile. Recognised
        /// STRUCTURALLY (a handful of controls, one of them a default push button) rather than by
        /// caption, so it works in any language. Deliberately does not tick "Don't ask again" for
        /// the user - that is their PHD2 setting to change, not ours.
        /// </summary>
        public static bool TryDismissConfirmDialog(IntPtr hwnd) {
            var controls = GetChildControls(hwnd);
            if (controls.Count == 0 || controls.Count > 8) return false;

            ControlInfo? defBtn = null;
            foreach (var c in controls) {
                if (c.ClassName != "Button") continue;
                int style;
                try { style = GetWindowLongW(c.Handle, GWL_STYLE); } catch { continue; }
                if ((style & BS_TYPEMASK) == BS_DEFPUSHBUTTON) { defBtn = c; break; }
            }
            if (defBtn == null) return false;

            Logger.Warning($"A small dialog ({controls.Count} controls) appeared instead of the Guiding Assistant - " +
                "almost certainly PHD2's 'Ok to disable guide output?' confirmation. Clicking its default button " +
                $"('{defBtn.Value.Text}') and continuing to wait for the Guiding Assistant.");

            try { SendMessageW(defBtn.Value.Handle, BM_CLICK, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception ex) {
                Logger.Warning($"Could not click the confirmation's default button: {ex.Message}");
                return false;
            }
            Thread.Sleep(750);
            return true;
        }

        private static HashSet<uint> GetPhd2ProcessIds() {
            var ids = new HashSet<uint>();
            try {
                foreach (var p in Process.GetProcessesByName("phd2")) ids.Add((uint)p.Id);
            } catch { /* transient - process list can change mid-enumeration */ }
            return ids;
        }

        /// <summary>
        /// Defends against title matching latching onto an unrelated window that happens to share
        /// a caption fragment with the Guiding Assistant - observed in testing: a File Explorer
        /// window open to this plugin's own install folder, titled "PHD2 Guiding Assistant - File
        /// Explorer", matched the substring scan below and was treated as the dialog. If PHD2's
        /// process id can't be determined at all, the filter is skipped rather than blocking every
        /// match forever.
        /// </summary>
        private static bool BelongsToPhd2(IntPtr hwnd, HashSet<uint> phd2Pids) {
            if (phd2Pids.Count == 0) return true;
            GetWindowThreadProcessId(hwnd, out var pid);
            return phd2Pids.Contains(pid);
        }

        /// <summary>Locates the GA dialog and returns its window handle. Everything downstream works on the handle directly, not a UIA element.</summary>
        public static IntPtr FindGaWindow(TimeSpan timeout, Dictionary<IntPtr, string> windowsBefore) {
            var deadline = DateTime.UtcNow + timeout;
            var phd2Pids = GetPhd2ProcessIds();

            while (DateTime.UtcNow < deadline) {
                foreach (var title in GuidingAssistantCaptions.GAWindowTitles) {
                    var h = FindWindowW(null, title);
                    if (h != IntPtr.Zero) {
                        if (!BelongsToPhd2(h, phd2Pids)) {
                            Logger.Warning($"A window titled exactly '{title}' exists but does not belong to PHD2's process - ignoring it.");
                            continue;
                        }
                        Logger.Info($"Found GA window by exact title '{title}'.");
                        return h;
                    }
                }

                var handle = IntPtr.Zero;
                for (var n = 0; n < 400; n++) {
                    handle = FindWindowExW(IntPtr.Zero, handle, null, null);
                    if (handle == IntPtr.Zero) break;
                    var sb = new StringBuilder(512);
                    GetWindowTextW(handle, sb, 512);
                    var t = sb.ToString();
                    foreach (var title in GuidingAssistantCaptions.GAWindowTitles) {
                        if (t.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) {
                            if (!BelongsToPhd2(handle, phd2Pids)) {
                                Logger.Warning($"Found a window titled '{t}' matching the Guiding Assistant pattern, but it does not belong to PHD2's process - ignoring it (e.g. a File Explorer window open to a similarly-named folder).");
                                continue;
                            }
                            Logger.Info($"Found GA window by title scan: '{t}'.");
                            return handle;
                        }
                    }
                }

                // Language-independent: whatever NEW top-level window appeared since we invoked
                // the menu command is almost certainly the dialog, whatever it is called - but only
                // if PHD2's own process actually owns it, or this would happily hijack an unrelated
                // window that happened to appear at the same moment.
                var now = GetTopLevelWindowMap();
                foreach (var kv in now) {
                    if (windowsBefore.ContainsKey(kv.Key)) continue;
                    if (kv.Value.StartsWith("PHD2", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!BelongsToPhd2(kv.Key, phd2Pids)) continue;

                    if (TryDismissConfirmDialog(kv.Key)) continue;

                    Logger.Warning($"Found a NEW window after the menu command: '{kv.Value}'. If that is the " +
                        "Guiding Assistant in your language, it is worth reporting so it can be added to GAWindowTitles.");
                    return kv.Key;
                }

                Thread.Sleep(500);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// Opens the Guiding Assistant dialog via, in order: (1) the native Win32 menu command -
        /// posting WM_COMMAND for the matched caption, or the language-independent fallback command
        /// ID if no caption matches; (2) Alt+T, G keyboard navigation, which needs an input desktop
        /// and is therefore the last resort. Returns the dialog's window handle.
        /// </summary>
        public static IntPtr OpenGuidingAssistant(IntPtr phdHwnd) {
            var opened = false;
            var windowsBefore = GetTopLevelWindowMap();

            try {
                var hMenu = GetMenu(phdHwnd);
                if (hMenu == IntPtr.Zero) {
                    Logger.Warning("PHD2 window reports no menu bar.");
                } else {
                    var seen = new List<string>();
                    uint id = 0;
                    string matched = null;
                    foreach (var pattern in GuidingAssistantCaptions.GAMenuPatterns) {
                        seen.Clear();
                        id = FindMenuCommandId(hMenu, pattern, seen);
                        if (id != 0) { matched = pattern; break; }
                    }
                    if (id != 0) {
                        Logger.Info($"Found the Guiding Assistant menu item via pattern '{matched}', command id={id} - posting WM_COMMAND.");
                        SetForegroundWindow(phdHwnd);
                        Thread.Sleep(200);
                        PostMessageW(phdHwnd, WM_COMMAND, (IntPtr)id, IntPtr.Zero);
                        opened = true;
                    } else {
                        Logger.Warning("No menu item matched any known Guiding Assistant caption pattern. Menu items seen: " + string.Join(" | ", seen));
                        if (GuidingAssistantCaptions.GAMenuFallbackCommandId > 0) {
                            Logger.Warning($"Trying the known menu command id {GuidingAssistantCaptions.GAMenuFallbackCommandId} instead - menu IDs do not vary by language.");
                            SetForegroundWindow(phdHwnd);
                            Thread.Sleep(200);
                            PostMessageW(phdHwnd, WM_COMMAND, (IntPtr)GuidingAssistantCaptions.GAMenuFallbackCommandId, IntPtr.Zero);
                            opened = true;
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"Win32 menu navigation failed: {ex.Message}");
            }

            if (!opened) {
                Logger.Warning("Falling back to keyboard menu navigation (Alt+T, G). This needs an input desktop.");
                try {
                    SetForegroundWindow(phdHwnd);
                    Thread.Sleep(400);
                    System.Windows.Forms.SendKeys.SendWait("%t");
                    Thread.Sleep(600);
                    System.Windows.Forms.SendKeys.SendWait("g");
                    Thread.Sleep(600);
                } catch (Exception ex) {
                    Logger.Warning($"Keyboard fallback failed: {ex.Message}");
                }
            }

            var ga = FindGaWindow(TimeSpan.FromSeconds(25), windowsBefore);
            if (ga == IntPtr.Zero) {
                LogWindowDiagnostics(phdHwnd);
                throw new Phd2GuidingAssistantException(FailureStage.GaWindowNotFound, "The Guiding Assistant window did not appear.");
            }
            Logger.Info("Guiding Assistant dialog is open.");
            LogControls(ga, "controls inside the Guiding Assistant dialog");
            return ga;
        }

        public static void LogWindowDiagnostics(IntPtr phdHwnd) {
            Logger.Warning("--- window diagnostics ---");
            Logger.Warning("Top-level windows:");
            foreach (var kv in GetTopLevelWindowMap()) {
                Logger.Warning($"   [{kv.Key}] {kv.Value}");
            }
            Logger.Warning("--------------------------");
        }

        // ---- Behaviour during / after the run --------------------------------------------------

        /// <summary>
        /// Must run before Stop is clicked: with this box ticked, Stop begins a declination
        /// backlash run instead of finishing. Not hazardous (PHD2 measures backlash with
        /// north/south guide pulses - the mount stays put) but it extends the run by several
        /// minutes for a measurement nobody asked for this time.
        /// </summary>
        /// <param name="retry">
        /// How long to keep retrying if the checkbox isn't found yet. Needed right after the dialog
        /// opens: wx creates the window before its children finish being laid out, so an immediate
        /// lookup can race and see only a handful of controls. The pre-Stop call doesn't need this -
        /// by then the dialog has existed for the whole measurement window.
        /// </param>
        public static void EnsureBacklashUnchecked(IntPtr gaHwnd, TimeSpan retry = default) {
            var deadline = DateTime.UtcNow + retry;
            ControlInfo? cb;
            var byStyle = false;
            while (true) {
                cb = GetControlAny(gaHwnd, GuidingAssistantCaptions.GABacklashPatterns);
                byStyle = cb == null;
                if (cb == null) cb = GetCheckboxByStyle(gaHwnd);
                if (cb != null || DateTime.UtcNow >= deadline) break;
                Thread.Sleep(250);
            }
            if (cb == null) {
                Logger.Warning("Could not locate the backlash checkbox, by caption or by style - continuing.");
                return;
            }
            if (byStyle) {
                Logger.Warning($"No backlash caption matched; identified it by control style instead: '{cb.Value.Text}'.");
            }

            var state = SendMessageW(cb.Value.Handle, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (state != 0) {
                Logger.Info($"Backlash checkbox '{cb.Value.Text}' was checked - unchecking it.");
                ClickControl(cb.Value);
                Thread.Sleep(500);
                state = SendMessageW(cb.Value.Handle, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero).ToInt32();
                Logger.Info(state != 0 ? "Backlash checkbox is STILL checked after clicking it." : "Backlash checkbox is now unchecked.");
            }
        }

        /// <summary>
        /// PHD2 enforces a two-minute minimum sampling period; if Stop is clicked earlier it opens
        /// an "Extended Sampling" window and keeps measuring. Deliberately not matched by title
        /// (localised) - callers just keep polling for Apply buttons regardless of what PHD2 is
        /// doing meanwhile, which is language-neutral.
        /// </summary>
        public static void NoteExtendedSampling() {
            Thread.Sleep(2000);
            if (FindTopWindowByTitle("Extended Sampling") != IntPtr.Zero) {
                Logger.Info("PHD2 opened 'Extended Sampling' - topping up to its 2-minute minimum. Waiting for results.");
            }
        }

        /// <summary>
        /// Clears PHD2's History / Target / Guide Stats panes so imaging starts on honest guiding
        /// statistics rather than the Guiding Assistant's unguided wander. Fails soft always -
        /// clearing a display is never worth aborting a night over.
        /// </summary>
        public static void ClearPhd2Displays(IntPtr phdHwnd) {
            try {
                var buttons = GetChildControls(phdHwnd).Where(c => c.ClassName == "Button").ToList();
                var clicked = 0;
                foreach (var b in buttons) {
                    if (!b.Enabled) continue;
                    if (!AnyMatch(b.Text, GuidingAssistantCaptions.PHD2ClearPatterns)) continue;
                    ClickControl(b);
                    clicked++;
                    Thread.Sleep(120);
                }

                if (clicked > 0) {
                    Logger.Info($"Cleared PHD2's displays - clicked {clicked} 'Clear' button(s).");
                    return;
                }

                Logger.Warning("No 'Clear' button matched in PHD2's main window, so its displays were not cleared. Harmless - the History pane's RMS will simply read high for the first minute or two of imaging.");
            } catch (Exception ex) {
                Logger.Warning($"Could not clear PHD2's displays: {ex.Message}");
            }
        }

        public static void CloseWindow(IntPtr hwnd) {
            try { PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
            catch { /* best effort */ }
        }
    }
}
