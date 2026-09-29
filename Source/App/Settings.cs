using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using WindowsInput.Native;

namespace WindowsVirtualDesktopHelper {

	/// <summary>
	/// Settings class to manage settings and configuration.
	/// Settings can be loaded from launch arguments, config file, or defaults, and are internally stored as object (which can be string, int, bool or float).
	/// Each key for a setting can be given a namespace, e.g. "icons.automaticallyHidePrevNextOnBounds" or "general.language".
	/// The config file is a text file in the following format:
	/// ```txt
	/// general.language: "en"
	/// icons.automaticallyHidePrevNextOnBounds: true
	/// feature.showSplashScreen.text: "Virtual \nDesktop Helper"
	/// ```
	/// </summary>
	class Settings {

		#region Defaults

		public static void LoadDefaults() {
			// Register any known default settings here

			// Debug
			RegisterDefault("debug.singleInstance", true, "true - only allow a single instance of the application to run (default); false - allow multiple instances (expect errors registering hotkeys)");

			// General
			RegisterDefault("general.startupWithWindows", false, "If true, the app will register itself with Windows to startup when Windows starts (via the registry).");
			RegisterDefault("general.theme", "auto", "Can be either auto, dark or light. If set to auto, the theme is derived from the current windows theme (dark or light).");

			// Theme
			RegisterDefault("theme.icons.disabledOpacity", 0.5, "Defines the opacity to use for icons which are disabled.");
			RegisterDefault("theme.icons.font", "Segoe UI", "Defines the font name to use for the icons (for regular numbers, characters). If a specific style is to be used, then one can append 'Bold', 'Italic', 'Regular' after a comma and the font name - for example 'Arial, Bold'.");
			RegisterDefault("theme.icons.emojiFont", "Segoe UI Symbol", "Defines the font name to use for emoji icons.");
			RegisterDefault("theme.icons.symbolsFont", "Segoe UI Symbol", "Defines the font name to use for symbol icons.");
			RegisterDefault("theme.icons.iconBG.dark", "black");
			RegisterDefault("theme.icons.iconFG.dark", "white");
			RegisterDefault("theme.icons.iconBG.light", "white");
			RegisterDefault("theme.icons.iconFG.light", "black");
			RegisterDefault("theme.overlay.width", 900, "With width in pixels of the switch overlay.");
			RegisterDefault("theme.overlay.height", 430, "With height in pixels of the switch overlay.");
			RegisterDefault("theme.overlay.font", "Segoe UI Light", "Defines the font name to use for the switch overlay.");
			RegisterDefault("theme.overlay.fontSize", 30, "Defines the font size to use for the switch overlay.");
			RegisterDefault("theme.overlay.overlayBG.dark", "black");
			RegisterDefault("theme.overlay.overlayFG.dark", "white");
			RegisterDefault("theme.overlay.overlayBG.light", "black");
			RegisterDefault("theme.overlay.overlayFG.light", "white");
			RegisterDefault("theme.status.width", 250, "With width in pixels of the status overlay.");
			RegisterDefault("theme.status.height", 40, "With height in pixels of the status overlay.");
			RegisterDefault("theme.status.offset", 0, "With height in pixels of the status overlay.");
			RegisterDefault("theme.status.font", "Segoe UI Light", "Defines the font name to use for the status overlay.");
			RegisterDefault("theme.status.fontSize", 12, "Defines the font size to use for the status overlay.");
			RegisterDefault("theme.status.overlayBG.dark", "black");
			RegisterDefault("theme.status.overlayFG.dark", "white");
			RegisterDefault("theme.status.overlayBG.light", "black");
			RegisterDefault("theme.status.overlayFG.light", "white");

			// Feature: splash
			RegisterDefault("feature.showSplashScreen", true, "If enabled, a splash screen is shown on startup of the app. Overlays must be enabled.");
			RegisterDefault("feature.showSplashScreen.duration", 2000, "Splash duration in milliseconds.");
			RegisterDefault("feature.showSplashScreen.text", "Virtual Desktop Helper", "The splash text to show.");

			// Feature: showPrevNextIcons
			RegisterDefault("feature.showPrevNextIcons", true, "If enabled, a previous and next arrow will appear in the icons tray of Windows to allow easy switching between desktops.");
			RegisterDefault("feature.showPrevNextIcons.automaticallyHidePrevNextOnBounds", false, "If enabled, the prev/next icon will automatically hide if there is no prev/next desktop.");
			RegisterDefault("feature.showPrevNextIcons.nextChar", "\u203A", "Defines the character to use for next desktop icon (typically a unicode character like the chevron, for example \\xE101 = skip forward (player style), \xE111 = next (arrow style), \\xe26b = next (chevron style), \\u02C3 = next (chevron style), \\u203A = next (chevron style))");
			RegisterDefault("feature.showPrevNextIcons.prevChar", "\u2039", "Defines the character to use for prev desktop icon (typically a unicode character like the chevron, for example \\xE100 = skip back (player style), \\xE112 = previous (arrow style), \\xe26c = previous (chevron style), \\u02C2 = previous (chevron style), \\u2039 = previous (chevron style))");

			// Feature: showDesktopSwitchOverlay
			RegisterDefault("feature.showDesktopSwitchOverlay", true);
			RegisterDefault("feature.showDesktopSwitchOverlay.duration", 2000, "Defines the duration in milliseconds for a switch overlay to show. If set to zero, then the overlay is shown indefinately.");
			RegisterDefault("feature.showDesktopSwitchOverlay.animate", true);
			RegisterDefault("feature.showDesktopSwitchOverlay.translucent", true);
			RegisterDefault("feature.showDesktopSwitchOverlay.showOnAllMonitors", true);
			RegisterDefault("feature.showDesktopSwitchOverlay.position", "middlecenter");

			// Feature: showDesktopStatusOverlay
			RegisterDefault("feature.showDesktopStatusOverlay", false);
			RegisterDefault("feature.showDesktopStatusOverlay.animate", true);
			RegisterDefault("feature.showDesktopStatusOverlay.translucent", true);
			RegisterDefault("feature.showDesktopStatusOverlay.showOnAllMonitors", true);
			RegisterDefault("feature.showDesktopStatusOverlay.position", "topcenter");

			// Feature: useHotKeyToJumpToDesktopNumber
			RegisterDefault("feature.useHotKeyToJumpToDesktopNumber", true, "Jump to desktop 1..9 with the hotkey + number");
			RegisterDefault("feature.useHotKeyToJumpToDesktopNumber.hotkey", "Ctrl + Alt");

			// Feature: useHotKeyToJumpToPreviousDesktop
			RegisterDefault("feature.useHotKeyToJumpToPreviousDesktop", true, "Jump back to the previously used desktop");
			RegisterDefault("feature.useHotKeyToJumpToPreviousDesktop.hotkey", "Ctrl + Alt + Tilde");

			// Feature: useHotKeyToSwitchDesktopForward
			RegisterDefault("feature.useHotKeyToSwitchDesktopForward", false);
			RegisterDefault("feature.useHotKeyToSwitchDesktopForward.hotkey", "Ctrl + Alt + Right");

			// Feature: useHotKeyToSwitchDesktopForward
			RegisterDefault("feature.useHotKeyToSwitchDesktopBackward", false);
			RegisterDefault("feature.useHotKeyToSwitchDesktopBackward.hotkey", "Ctrl + Alt + Left");

			// Feature: move the active window to another desktop (Windows 11 24H2+)
			RegisterDefault("feature.useHotKeyToMoveWindowForward", true, "Move the active window to the next desktop", "v2.2");
			RegisterDefault("feature.useHotKeyToMoveWindowForward.hotkey", "Ctrl + Shift + Win + Right");
			RegisterDefault("feature.useHotKeyToMoveWindowBackward", true, "Move the active window to the previous desktop", "v2.2");
			RegisterDefault("feature.useHotKeyToMoveWindowBackward.hotkey", "Ctrl + Shift + Win + Left");
			RegisterDefault("feature.useHotKeyToMoveWindowToDesktopNumber", true, "Move the active window to desktop 1..9 with the hotkey + number", "v2.2");
			RegisterDefault("feature.useHotKeyToMoveWindowToDesktopNumber.hotkey", "Ctrl + Alt + Shift");
			RegisterDefault("feature.moveWindow.follow", true, "true - after moving a window to another desktop, switch to that desktop too; false - stay", "v2.2");

			// Feature: pin the active window to all desktops (Windows 11 24H2+)
			RegisterDefault("feature.useHotKeyToTogglePinWindow", true, "Pin/unpin the active window to all desktops", "v2.2");
			RegisterDefault("feature.useHotKeyToTogglePinWindow.hotkey", "Ctrl + Shift + Win + P");
			RegisterDefault("feature.useHotKeyToTogglePinApp", true, "Pin/unpin all windows of the active app to all desktops (also windows it opens later)", "v2.2");
			RegisterDefault("feature.useHotKeyToTogglePinApp.hotkey", "Ctrl + Shift + Win + A");

			// Feature: take the active window to a new desktop
			RegisterDefault("feature.useHotKeyToMoveWindowToNewDesktop", true, "Create a new desktop and move the active window there", "v2.2");
			RegisterDefault("feature.useHotKeyToMoveWindowToNewDesktop.hotkey", "Ctrl + Shift + Win + N");

			// Feature: bring all windows of the active app to the current desktop
			RegisterDefault("feature.useHotKeyToGatherAppWindows", true, "Move all windows of the active app from other desktops to the current desktop", "v2.2");
			RegisterDefault("feature.useHotKeyToGatherAppWindows.hotkey", "Ctrl + Shift + Win + G");

			// Feature: apps which are always shown on all desktops
			RegisterDefault("feature.autoPin.apps", "", "Comma separated process names (e.g. \"Spotify, ms-teams\") of apps which are automatically shown on all desktops", "v2.2");

			// Feature: tray panel (right-click on the desktop number) instead of the classic menu
			RegisterDefault("feature.trayFlyout", true, "true - right-click on the tray icon opens the panel (desktops, current window, pinned windows); false - the classic menu", "v2.2");

			// Feature: window menu (desktop actions for a window, at the mouse cursor)
			RegisterDefault("feature.useHotKeyToShowWindowMenu", true, "Open the window menu (move, pin, ...) for the active window", "v2.2");
			RegisterDefault("feature.useHotKeyToShowWindowMenu.hotkey", "Ctrl + Shift + Win + M");
			RegisterDefault("feature.windowMenu.titleBarCtrlRightClick", true, "Ctrl + right-click on a window's title bar opens the window menu", "v2.2");

			// Feature: mouse wheel over the tray icons switches desktops
			RegisterDefault("feature.mouseWheelOnTrayIcons", true, "Scroll the mouse wheel over the tray icons to switch desktops", "v2.2");

			// Feature: wrap around when switching forward/backward past the last/first desktop
			RegisterDefault("feature.wrapAround", false, "If enabled, switching forward on the last desktop goes to the first (and backward on the first to the last)", "v2.2");

			// Feature: notify about hotkeys which could not be registered (e.g. already used by another app)
			RegisterDefault("feature.notifyHotKeyConflicts", true, "If enabled, a notification lists the hotkeys which could not be registered", "v2.2");

			// Feature: color the tray desktop number per desktop
			RegisterDefault("feature.colorIconsPerDesktop", false, "If enabled, the desktop number in the tray is colored per desktop (see theme.icons.desktopColors.*)", "v2.2");
			RegisterDefault("theme.icons.desktopColors.dark", "#4FC3F7, #81C784, #FFB74D, #F06292, #BA68C8, #FFF176, #4DB6AC, #FF8A65, #90A4AE", "Comma separated colors for desktops 1, 2, 3... (repeating) on a dark taskbar", "v2.2");
			RegisterDefault("theme.icons.desktopColors.light", "#0277BD, #2E7D32, #E65100, #AD1457, #6A1B9A, #9E7C00, #00695C, #BF360C, #455A64", "Comma separated colors for desktops 1, 2, 3... (repeating) on a light taskbar", "v2.2");

			// Feature: reload the config file(s) automatically when they are edited
			RegisterDefault("feature.autoReloadConfig", true, "If enabled, changes to the config file are applied immediately without restarting", "v2.2");

			// Feature: showDesktopNumberInIconTray
			RegisterDefault("feature.showDesktopNumberInIconTray", true);
			RegisterDefault("feature.showDesktopNumberInIconTray.clickToOpenTaskView", true);

			// Feature: showDesktopNameInIconTray
			RegisterDefault("feature.showDesktopNameInIconTray", false);

			// Feature: restorePreviousWindowFocus
			RegisterDefault("feature.useShellNotifications", true, "If enabled (and supported by the Windows version), desktop switches are detected instantly via shell notifications instead of by polling. Disable if you experience issues.", "v2.2");
			RegisterDefault("feature.restorePreviousWindowFocus", false,"If enabled, when switching desktop the previously focused window on that desktop will be refocused", "v2.1");

		}

		#endregion

		#region Settings API

		public static List<string> GetUsedConfigFiles() {
			return _settingsConfigFilesUsed;
		}

		public static string GetSettingsAsString() {
			return _compileSettingsDocumentation(_createMergedSettingsDictionary(true, false), false, false);
		}

		public static string GetDocumentationAsString() {
			return _compileSettingsDocumentation(_settingsDefaults, true, false);
		}

		public static string GetDocumentationAsMarkdown() {
			return _compileSettingsDocumentation(_settingsDefaults, true, true);
		}

		public static void RegisterDefault(string key, object value, string documentation = null, string version = null) {
			_settingsDefaults[key] = value;
			if(documentation != null) _settingsDocumentations[key] = documentation;
			if(version != null) _settingsVersions[key] = version;
		}

		public static void LoadConfig() {
			// Get the config file path
			var path = _getConfigPath();
			// Get the direcotry of the config file
			var dir = System.IO.Path.GetDirectoryName(path);
			// Load
			_loadConfigPath(dir, _settingsConfig, _settingsConfigFilesUsed);
		}

		// True when a setting was changed at runtime and not yet written to the config file
		public static bool HasUnsavedChanges { get; private set; }

		// Re-reads the config file(s), replacing the current config values (e.g. after the user edited the file).
		// The new values are loaded into a new dictionary which is then swapped in, so readers on other
		// threads never see a half loaded config
		public static void ReloadConfig() {
			var path = _getConfigPath();
			var dir = System.IO.Path.GetDirectoryName(path);
			var config = new ConcurrentDictionary<string, object>();
			var filesUsed = new List<string>();
			_loadConfigPath(dir, config, filesUsed);
			_settingsConfig = config;
			_settingsConfigFilesUsed = filesUsed;
			HasUnsavedChanges = false;
		}

		// The directory which contains the config file(s)
		public static string GetConfigDirectory() {
			return System.IO.Path.GetDirectoryName(_getConfigPath());
		}

		// When the config file was last written by SaveConfig (to ignore our own writes when watching the file)
		public static DateTime LastSavedUtc { get; private set; }

		public static void SaveConfig() {
			// Get the config file path
			var path = _getConfigPath();

			// Create a merged dictionary of all config and defaults settings
			var allSettings = _createMergedSettingsDictionary(false, true);

			// Serialize _settingsConfig to text, each setting on a line split by colon, sorting each line by trimming # comments out (ie sorting with comments inline)
			var lines = new List<string>();
			foreach(var kvp in allSettings) {
				var key = kvp.Key;
				var val = _serializeValAsType(kvp.Value);
				lines.Add($"{key}: {val}");
			}
			// Sort the lines by key but so that parent keys appear first (ie feature.showSplashScreen appears before feature.showSplashScreen.duration)
			// Note: we sort on the key part of each line only (never the value, which can contain dots), and
			// keys without a dot are their own parent, which keeps the comparison total and transitive
			lines.Sort((a, b) => {
				var aKey = _getSortKey(a);
				var bKey = _getSortKey(b);
				var aParentKey = _getParentKey(aKey);
				var bParentKey = _getParentKey(bKey);
				var byParent = string.Compare(aParentKey, bParentKey, StringComparison.Ordinal);
				if(byParent != 0) return byParent;
				return string.Compare(aKey, bKey, StringComparison.Ordinal);
			});
			// Write the lines to a temp file first and then swap it in, so that a crash or power loss
			// mid-write can never leave the user with a truncated/empty config file
			var tempPath = path + ".tmp";
			System.IO.File.WriteAllLines(tempPath, lines);
			if(System.IO.File.Exists(path)) {
				System.IO.File.Replace(tempPath, path, null);
			} else {
				System.IO.File.Move(tempPath, path);
			}
			HasUnsavedChanges = false;
			LastSavedUtc = DateTime.UtcNow;
		}

		private static string _getSortKey(string line) {
			// The key of a config line "key: value", where commented defaults ("#key: value") sort with their key
			var key = line.Split(new[] { ':' }, 2)[0].Trim();
			if(key.StartsWith("#")) key = key.Substring(1).Trim();
			return key;
		}

		private static string _getParentKey(string key) {
			var lastDot = key.LastIndexOf('.');
			return lastDot < 0 ? key : key.Substring(0, lastDot);
		}

		public static void RegisterLaunchArgs(string[] args) {
			// Parse the launch arguments, e.g. "--key value"
			for(int i = 0; i < args.Length; i++) {
				var arg = args[i];
				if(arg.StartsWith("--")) {
					var key = arg.Substring(2);
					if(i + 1 < args.Length) {
						var val = args[i + 1];
						// Support args with no val as assumed to be true, e.g. "--key" is the same as "--key true"
						if(val.StartsWith("--")) {
							_settingsLaunchArgs[key] = true;
						} else {
							_settingsLaunchArgs[key] = _parseValAsType(val);
						}
					} else if(i + 1 == args.Length) {
						// Support args with no val as assumed to be true, e.g. "--key" is the same as "--key true"
						// In this case there is no proceeding value, so we assume it is true
						_settingsLaunchArgs[key] = true;
					}
				}
			}
		}

		public static List<string> GetKeys() {
			// Note: the merged dictionary contains the defaults under "#key" (as commented lines for
			// SaveConfig), which are not real settings and must not be exposed to callers
			return _createMergedSettingsDictionary(true, true).Keys.Where(k => !k.StartsWith("#")).ToList();
		}

		public static string GetString(string key, string defaultValue = null) {
			var ret = _get(key, defaultValue);
			if(ret == null) return null;
			if(ret is string) return (string)ret;
			// The parser types unquoted values eagerly (5 becomes an int), so a text setting such as
			// `feature.showSplashScreen.text: 5` must still read back as the string "5"
			if(ret is bool) return (bool)ret ? "true" : "false";
			return Convert.ToString(ret, CultureInfo.InvariantCulture);
		}

		public static string GetFontName(string key, string defaultValue = null) {
			var val = GetString(key, defaultValue); // can include style, like "Segoe UI, Bold"
			if(val == null) return defaultValue;
			if(val.Contains(",")) {
				var segs = val.Split(',');
				return segs.First().Trim(); // Return the first segment as the font name
			} else {
				return val.Trim();
			}
		}

		public static FontStyle GetFontStyle(string key, FontStyle defaultValue = FontStyle.Regular) {
			var val = GetString(key); // can include style, like "Segoe UI, Bold"
			if(val == null) return defaultValue;
			if(val.Contains(",")) {
				var segs = val.Split(',');
				var styleStr = segs.Last().Trim().ToLower();
				if(styleStr == "bold") return FontStyle.Bold;
				else if(styleStr == "italic") return FontStyle.Italic;
				else if(styleStr == "underline") return FontStyle.Underline;
				else if(styleStr == "strikeout") return FontStyle.Strikeout;
				else return FontStyle.Regular; // Default case
			} else {
				return defaultValue;
			}
		}

		// Removes a setting from the config, so the default applies again
		public static void ResetToDefault(string key) {
			object removed;
			if(_settingsConfig.TryRemove(key, out removed)) HasUnsavedChanges = true;
		}

		public static void SetString(string key, string value) {
			_set(key, value);
		}

		public static bool GetBool(string key, bool? defaultValue = null) {
			string defaultValueStr = null;
			if(defaultValue != null) defaultValueStr = defaultValue.ToString().ToLower();
			var ret = _get(key, defaultValueStr);
			// A hand-edited config should degrade gracefully, so we coerce where the intent is clear
			// and otherwise fall back to the registered default instead of crashing the app
			if(ret is bool) return (bool)ret;
			if(ret is int) return (int)ret != 0;
			if(ret is string && bool.TryParse(((string)ret).Trim(), out bool parsedBool)) return parsedBool;
			var fallback = _getRegisteredDefault(key);
			if(fallback is bool) {
				_logInvalidValue(key, "a bool", ret);
				return (bool)fallback;
			}
			throw new Exception($"Setting {key} is not a bool (value is {ret})");
		}

		public static void SetBool(string key, bool value) {
			_set(key, value);
		}

		public static int GetInt(string key, int? defaultValue = null) {
			string defaultValueStr = null;
			if(defaultValue != null) defaultValueStr = defaultValue.Value.ToString(CultureInfo.InvariantCulture);
			var ret = _get(key, defaultValueStr);
			if(ret is int) return (int)ret;
			if(ret is float) return (int)Math.Round((float)ret);
			if(ret is double) return (int)Math.Round((double)ret);
			if(ret is string && int.TryParse(((string)ret).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt)) return parsedInt;
			var fallback = _getRegisteredDefault(key);
			if(fallback is int) {
				_logInvalidValue(key, "an int", ret);
				return (int)fallback;
			}
			throw new Exception($"Setting {key} is not a int (value is {ret})");
		}

		public static void SetInt(string key, int value) {
			_set(key, value);
		}

		public static double GetDouble(string key, double? defaultValue = null) {
			string defaultValueStr = null;
			if(defaultValue != null) defaultValueStr = defaultValue.Value.ToString(CultureInfo.InvariantCulture);
			var ret = _get(key, defaultValueStr);
			if(ret is double) return (double)ret;
			if(ret is float) return (float)ret; // note: must unbox as float first, unboxing directly to double throws
			if(ret is int) return (int)ret;
			if(ret is string && double.TryParse(((string)ret).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble)) return parsedDouble;
			var fallback = _getRegisteredDefault(key);
			if(fallback is double || fallback is float || fallback is int) {
				_logInvalidValue(key, "a number", ret);
				return Convert.ToDouble(fallback, CultureInfo.InvariantCulture);
			}
			throw new Exception($"Setting {key} is not a double (value is {ret})");
		}

		public static void SetDouble(string key, double value) {
			_set(key, value);
		}

		public static float GetFloat(string key, float? defaultValue = null) {
			string defaultValueStr = null;
			if(defaultValue != null) defaultValueStr = defaultValue.Value.ToString(CultureInfo.InvariantCulture);
			var ret = _get(key, defaultValueStr);
			if(ret is double) return (float)(double)ret; // note: must unbox as double first, unboxing directly to float throws
			if(ret is float) return (float)ret;
			if(ret is int) return (int)ret;
			if(ret is string && float.TryParse(((string)ret).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFloat)) return parsedFloat;
			var fallback = _getRegisteredDefault(key);
			if(fallback is double || fallback is float || fallback is int) {
				_logInvalidValue(key, "a number", ret);
				return Convert.ToSingle(fallback, CultureInfo.InvariantCulture);
			}
			throw new Exception($"Setting {key} is not a float (value is {ret})");
		}

		private static object _getRegisteredDefault(string key) {
			object val;
			return _settingsDefaults.TryGetValue(key, out val) ? val : null;
		}

		private static void _logInvalidValue(string key, string expected, object actual) {
			Util.Logging.WriteLine($"Settings: value of {key} is not {expected} (value is \"{actual}\"), using the default instead");
		}

		#endregion

		#region Internal Methods and Properties

		// The launch arguments 
		static private Dictionary<string, string> _settingsDocumentations = new Dictionary<string, string>();
		static private Dictionary<string, string> _settingsVersions = new Dictionary<string, string>();
		// Note: config values are read from several threads, so these are concurrent dictionaries
		static private ConcurrentDictionary<string, object> _settingsLaunchArgs = new ConcurrentDictionary<string, object>();
		static private ConcurrentDictionary<string, object> _settingsConfig = new ConcurrentDictionary<string, object>();
		static private Dictionary<string, object> _settingsDefaults = new Dictionary<string, object>();
		static private List<string> _settingsConfigFilesUsed = new List<string>();

		private static Dictionary<string, object> _createMergedSettingsDictionary(bool includeRuntimeSettings = false, bool includeDefaults = false) {
			var allSettings = new Dictionary<string, object>();
			if(includeRuntimeSettings) {
				foreach(var kvp in _settingsLaunchArgs) {
					if(!allSettings.ContainsKey(kvp.Key)) {
						allSettings[kvp.Key] = kvp.Value; // Add this default as a comment
					}
				}
			}
			foreach(var kvp in _settingsConfig) {
				if(!allSettings.ContainsKey(kvp.Key)) {
					allSettings[kvp.Key] = kvp.Value;
				}
			}
			if(includeDefaults) {
				foreach(var kvp in _settingsDefaults) {
					if(!allSettings.ContainsKey(kvp.Key)) {
						allSettings["#" + kvp.Key] = kvp.Value; // Add this default as a comment
					}
				}
			}
			return allSettings;
		}

		private static string _compileSettingsDocumentation(Dictionary<string, object> settingsToUse, bool includeDocumentation, bool asMarkdown) {
			// Compile the defaults and documentation into a string
			var lines = new List<string>();
			if(asMarkdown) lines.Add(includeDocumentation ? "|Config|Default|Description|Version|" : "|Config|Default|");
			if(asMarkdown) lines.Add(includeDocumentation ? "| --- | --- | --- | --- |" : "| --- | --- |");
			foreach(var kvp in settingsToUse) {
				var key = kvp.Key;
				var val = _serializeValAsType(kvp.Value);
				var line = asMarkdown ? $"| {key} | ``{val}`` |" : $"{key}: {val}";
				if(includeDocumentation) {
					var doc = _settingsDocumentations.ContainsKey(key) ? _settingsDocumentations[key] : null;
					if(doc != null || asMarkdown) line += asMarkdown ? $" {doc} |" : $", {doc}";
					var ver = _settingsVersions.ContainsKey(key) ? _settingsVersions[key] : null;
					if(ver != null || asMarkdown) line += asMarkdown ? $" {ver} |" : $", {ver}";
				}
				lines.Add(line);
			}
			return string.Join("\n",lines);
		}

		private static object _get(string key, string defaultValue = null) {
			// Check all our sources in the following priority:
			// 1. Launch arguments
			// 2. Config file
			// 3. Default value
			// 4. Defaults
			if(_settingsLaunchArgs.ContainsKey(key)) {
				return _settingsLaunchArgs[key];
			}
			object configValue;
			if(_settingsConfig.TryGetValue(key, out configValue)) { // (single lookup: the dictionary may be swapped by ReloadConfig)
				return configValue;
			}
			if(defaultValue != null) {
				return defaultValue;
			}
			if(_settingsDefaults.ContainsKey(key)) {
				return _settingsDefaults[key];
			}
			return null;
		}

		private static string _getMainConfigFile() {
			// Get the config file for this exe name, without the directory
			//var exeName = (System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
			var exeName = System.AppDomain.CurrentDomain.FriendlyName;
			return exeName + ".config";
		}

		private static void _set(string key, object value) {
			// Store in the config
			_settingsConfig[key] = value;
			HasUnsavedChanges = true;
		}

		private static string _getConfigPath() {
			// Get the path to the config file
			var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "WindowsVirtualDesktopHelper");
			if(!System.IO.Directory.Exists(path)) {
				System.IO.Directory.CreateDirectory(path);
			}
			path = System.IO.Path.Combine(path, _getMainConfigFile());
			return path;
		}

		private static string _unescapeString(string str) {
			// Strings are stored using the " character, and can contain the following:
			//  - \n for new line
			//  - \" for quote
			//  - \\ for backslash
			//  - \u2039 for unicode characters (for example)
			// Note: we intentionally do not use Regex.Unescape here: it throws on values with
			// plain backslashes such as "C:\Users", while we want to be lenient and keep any
			// unknown escape sequence as-is
			if(str == null) return null;
			// Strip exactly one leading and one trailing quote (a value may legitimately end with an escaped quote)
			if(str.Length >= 2 && str.StartsWith("\"") && str.EndsWith("\"")) str = str.Substring(1, str.Length - 2);
			var unescaped = new System.Text.StringBuilder(str.Length);
			for(int i = 0; i < str.Length; i++) {
				var c = str[i];
				if(c != '\\' || i + 1 >= str.Length) {
					unescaped.Append(c);
					continue;
				}
				var next = str[i + 1];
				if(next == 'n') { unescaped.Append('\n'); i++; }
				else if(next == 'r') { unescaped.Append('\r'); i++; }
				else if(next == 't') { unescaped.Append('\t'); i++; }
				else if(next == '"') { unescaped.Append('"'); i++; }
				else if(next == '\\') { unescaped.Append('\\'); i++; }
				else if(next == 'u' && i + 5 < str.Length && int.TryParse(str.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) {
					unescaped.Append((char)code);
					i += 5;
				} else {
					unescaped.Append(c); // unknown escape: keep the backslash as-is
				}
			}
			return unescaped.ToString();
		}

		private static string _escapeString(string str) {
			if(str == null) return null;
			str = str.Replace("\\", "\\\\");
			str = str.Replace("\n", "\\n");
			str = str.Replace("\"", "\\\"");
			var escaped = new System.Text.StringBuilder();
			foreach(char c in str) {
				if(c > 127) escaped.AppendFormat("\\u{0:X4}", (int)c);
				else escaped.Append(c);
			}
			return "\"" + escaped + "\"";
		}

		private static object _parseValAsType(string value) {
			// Try to parse val as int, float, bool, or string, returning the appropriate type
			// Note: numbers are always parsed with the invariant culture, so that a config file
			// reads the same regardless of the system locale (e.g. "0.5" must never parse as 5
			// on locales which use . as a group separator)
			if(value.StartsWith("\"") && value.EndsWith("\"")) {
				return _unescapeString(value);
			}
			if(int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) {
				return intValue;
			}
			if(bool.TryParse(value, out bool boolValue)) {
				return boolValue;
			}
			if(float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) {
				return floatValue;
			}
			return value; // Return as string if no other type matches
		}

		private static string _serializeValAsType(object val) {
			// Serialize val as string (numbers always with the invariant culture, see _parseValAsType)
			if(val is bool) return (bool)val ? "true" : "false";
			if(val is int) return ((int)val).ToString(CultureInfo.InvariantCulture);
			if(val is float) return ((float)val).ToString(CultureInfo.InvariantCulture);
			if(val is double) return ((double)val).ToString(CultureInfo.InvariantCulture);
			return _escapeString(val?.ToString()); // string default
		}

		private static void _loadConfigPath(string path, ConcurrentDictionary<string, object> target, List<string> filesUsed) {
			if(System.IO.File.Exists(path)) {
				// Register the config file as used
				filesUsed.Add(path);
				// Load the config file
				var lines = System.IO.File.ReadAllLines(path);
				// Parse the lines, split by colon, adding each to the _settingsConfig
				foreach(var line in lines) {
					var trimmed = line.Trim();
					if(trimmed == "" || trimmed.StartsWith("#")) continue; // Skip blank lines and comments (lines starting with #)
					var parts = line.Split(new[] { ':' }, 2); // Split on the first colon only, values may contain colons
					if(parts.Length >= 2 && parts[0].Trim() != "") {
						var key = parts[0].Trim();
						var val = parts[1].Trim();
						target[key] = _parseValAsType(val);
					} else {
						// Never silently ignore a line: a typo (e.g. "=" instead of ":") would otherwise be invisible
						Util.Logging.WriteLine($"Settings: ignoring line which is not in the format \"key: value\" in {path}: {trimmed}");
					}
				}
			} else if(System.IO.Directory.Exists(path)) {
				// Get all .config files in the directory, in a stable order (the file system order is not
				// guaranteed, and later files overwrite the keys of earlier ones)
				var files = System.IO.Directory.GetFiles(path, "*.config");
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				foreach(var file in files) {
					_loadConfigPath(file, target, filesUsed);
				}
			}
		}

		#endregion
	}
}
