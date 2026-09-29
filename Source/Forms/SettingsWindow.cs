using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Forms {

	/// <summary>
	/// The settings window, in the style of Windows 11 Settings / PowerToys: navigation on the left, cards on the
	/// right. Changes apply immediately (and are saved). Pages: General, Desktops and windows, Overlays, Keyboard
	/// shortcuts, About. Built in code, all sizes scaled for the DPI.
	/// </summary>
	public class SettingsWindow : Form {

		public const string PageGeneral = "general", PageDesktops = "desktops", PageOverlays = "overlays", PageShortcuts = "shortcuts", PageAbout = "about";

		private readonly float _scale;
		private readonly bool _builtDark;
		private string _page = PageGeneral;
		private readonly Dictionary<string, NavItem> _nav = new Dictionary<string, NavItem>();
		private StackPanel _content;
		private bool _applying = false; // our own change is being applied: don't re-render from ReloadFromSettings

		public SettingsWindow() {
			_scale = DpiScale();
			_builtDark = Theme.IsDark;
			Text = "Settings - Windows VD Helper";
			Font = new Font("Segoe UI", 9f * _scale, GraphicsUnit.Point);
			AutoScaleMode = AutoScaleMode.None;
			StartPosition = FormStartPosition.CenterScreen;
			Size = new Size(S(1000), S(720));
			MinimumSize = new Size(S(760), S(520));
			BackColor = Theme.WindowBack;
			ForeColor = Theme.Text;
			KeyPreview = true;
			try {
				using (var glyph = new Bitmap(Theme.Glyph("", Theme.Accent, 32))) Icon = Icon.FromHandle(glyph.GetHicon());
			} catch (Exception) { }
			BuildNavigation();
			_content = new StackPanel(_scale) { Dock = DockStyle.Fill, ContentPadding = new Padding(S(36), S(24), S(36), S(36)) };
			Controls.Add(_content);
			_content.BringToFront();
			ShowPage(PageGeneral);
		}

		private static float DpiScale() {
			var dpi = App.Instance != null ? App.Instance.TrayDpi : 96;
			return Math.Max(1f, dpi / 96f);
		}

		private int S(float px) { return (int)Math.Round(px * _scale); }

		protected override void OnHandleCreated(EventArgs e) {
			base.OnHandleCreated(e);
			Theme.ApplyWindowTheme(Handle);
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
			if (keyData == Keys.Escape && !(ActiveControl is ShortcutRecorder)) { Close(); return true; }
			return base.ProcessCmdKey(ref msg, keyData);
		}

		// True when the window was built for another theme than the current one (then it's re-created)
		public bool IsOutdated { get { return _builtDark != Theme.IsDark; } }

		public void ReloadFromSettings() {
			if (_applying || !Visible) return;
			Render();
		}

		#region Navigation

		private void BuildNavigation() {
			var nav = new Panel { Dock = DockStyle.Left, Width = S(270), BackColor = Theme.WindowBack, Padding = new Padding(S(12), S(16), S(12), S(12)) };
			var title = new Label { Text = "Windows VD Helper", AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 11f * _scale, GraphicsUnit.Point), Location = new Point(S(16), S(18)) };
			nav.Controls.Add(title);
			var pages = new[] {
				Tuple.Create(PageGeneral, "", "General"),
				Tuple.Create(PageDesktops, "", "Desktops and windows"),
				Tuple.Create(PageOverlays, "", "Overlays"),
				Tuple.Create(PageShortcuts, "", "Keyboard shortcuts"),
				Tuple.Create(PageAbout, "", "About"),
			};
			var y = S(64);
			foreach (var page in pages) {
				var item = new NavItem(_scale, page.Item2, page.Item3) { Location = new Point(S(8), y), Width = S(250) };
				var id = page.Item1;
				item.Click += (s, e) => ShowPage(id);
				nav.Controls.Add(item);
				_nav[id] = item;
				y += item.Height + S(2);
			}
			Controls.Add(nav);
		}

		public void ShowPage(string page) {
			if (!_nav.ContainsKey(page ?? "")) page = PageGeneral;
			_page = page;
			foreach (var kvp in _nav) { kvp.Value.Selected = kvp.Key == page; kvp.Value.Invalidate(); }
			_content.AutoScrollPosition = new Point(0, 0);
			Render();
		}

		// Re-builds the current page, keeping the scroll position (used after every change, so dependent
		// settings are enabled/disabled right away)
		private void Render() {
			var scroll = -_content.AutoScrollPosition.Y;
			_content.SuspendLayout();
			foreach (Control c in _content.Items.Cast<Control>().ToList()) { _content.Items.Remove(c); c.Dispose(); }
			var controls = new List<Control>();
			switch (_page) {
				case PageDesktops: BuildDesktopsPage(controls); break;
				case PageOverlays: BuildOverlaysPage(controls); break;
				case PageShortcuts: BuildShortcutsPage(controls); break;
				case PageAbout: BuildAboutPage(controls); break;
				default: BuildGeneralPage(controls); break;
			}
			_content.Items.AddRange(controls.ToArray());
			_content.ResumeLayout(true);
			_content.PerformLayout(); // the items are in the inner panel, so re-measure the page explicitly
			_content.AutoScrollPosition = new Point(0, scroll);
		}

		#endregion

		#region Building blocks

		private Label PageTitle(string text) {
			return new Label { Text = text, AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 20f * _scale, GraphicsUnit.Point), Margin = new Padding(0, 0, 0, S(12)) };
		}

		private Label Section(string text) {
			return new Label { Text = text, AutoSize = true, ForeColor = Theme.Text, Font = new Font("Segoe UI Semibold", 10.5f * _scale, GraphicsUnit.Point), Margin = new Padding(S(2), S(18), 0, S(8)) };
		}

		private Label Note(string text) {
			return new Label { Text = text, AutoSize = true, ForeColor = Theme.TextSecondary, Margin = new Padding(S(2), 0, 0, S(8)) };
		}

		private SettingsCard Card(string glyph, string title, string description, Control action, bool indent = false, bool enabled = true) {
			var card = new SettingsCard(_scale, glyph, title, description, action) { Margin = new Padding(indent ? S(44) : 0, 0, 0, S(4)), Enabled = enabled };
			return card;
		}

		// A card with a toggle bound to a bool setting
		private SettingsCard ToggleCard(string glyph, string title, string description, string setting, bool indent = false, bool enabled = true) {
			var toggle = new ModernToggle(_scale) { Checked = Settings.GetBool(setting) };
			toggle.CheckedChanged += (s, e) => Apply(() => Settings.SetBool(setting, toggle.Checked));
			return Card(glyph, title, description, toggle, indent, enabled);
		}

		// A card with a drop-down bound to a setting
		private SettingsCard DropDownCard(string glyph, string title, string description, IEnumerable<KeyValuePair<string, string>> options, string value, Action<string> set, bool indent = false, bool enabled = true) {
			var dropDown = new ModernDropDown(_scale, options, value);
			dropDown.ValueChanged += v => Apply(() => set(v));
			return Card(glyph, title, description, dropDown, indent, enabled);
		}

		private static KeyValuePair<string, string> O(string value, string label) { return new KeyValuePair<string, string>(value, label); }

		private static readonly KeyValuePair<string, string>[] Positions = {
			O("topleft", "Top left"), O("topcenter", "Top center"), O("topright", "Top right"),
			O("middleleft", "Middle left"), O("middlecenter", "Center"), O("middleright", "Middle right"),
			O("bottomleft", "Bottom left"), O("bottomcenter", "Bottom center"), O("bottomright", "Bottom right"),
		};

		// Applies a change: store it, save, let the app apply it, and re-render (dependent settings)
		private void Apply(Action change) {
			_applying = true;
			try {
				change();
				try { Settings.SaveConfig(); } catch (Exception e) { Util.Logging.WriteLine("Settings: Error: saving: " + e.Message); }
				App.Instance.ApplySettings();
			} catch (Exception e) {
				Util.Logging.WriteLine("Settings: Error: " + e.Message);
				MessageBox.Show(this, e.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			} finally {
				_applying = false;
			}
			BeginInvoke((Action)Render);
		}

		private ModernButton Button(string text, string glyph, Action click, bool accent = false) {
			var button = new ModernButton(_scale, text, glyph) { AccentStyle = accent };
			button.Click += (s, e) => click();
			return button;
		}

		private static void Open(string target) {
			try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception e) { Util.Logging.WriteLine("Settings: Error: open " + target + ": " + e.Message); }
		}

		private static string LogFile { get { return System.IO.Path.Combine(Settings.GetConfigDirectory(), "WindowsVirtualDesktopHelper.log"); } }

		#endregion

		#region Pages

		private void BuildGeneralPage(List<Control> c) {
			c.Add(PageTitle("General"));

			c.Add(Section("Startup"));
			var startup = new ModernToggle(_scale) { Checked = App.Instance.IsStartupWithWindowsEnabled() };
			startup.CheckedChanged += (s, e) => Apply(() => {
				Settings.SetBool("general.startupWithWindows", startup.Checked);
				if (startup.Checked) App.Instance.EnableStartupWithWindows(); else App.Instance.DisableStartupWithWindows();
			});
			c.Add(Card("", "Start with Windows", "Start the app when you sign in", startup));

			c.Add(Section("Appearance"));
			c.Add(DropDownCard("", "Theme", "The colors of the tray icons, overlays, menus and windows",
				new[] { O("auto", "Use the Windows setting"), O("light", "Light"), O("dark", "Dark") },
				Settings.GetString("general.theme"), v => Settings.SetString("general.theme", v)));

			c.Add(Section("Tray icon"));
			c.Add(ToggleCard("", "Show the desktop number", "The number of the current desktop in the notification area", "feature.showDesktopNumberInIconTray"));
			c.Add(ToggleCard("", "Show the first letter of the desktop name", "An extra tray icon with the desktop name's initial", "feature.showDesktopNameInIconTray"));
			c.Add(ToggleCard("", "Color the number per desktop", "Each desktop gets its own color, so you recognize it at a glance", "feature.colorIconsPerDesktop"));
			var arrows = Settings.GetBool("feature.showPrevNextIcons");
			c.Add(ToggleCard("", "Previous and next arrows", "Two tray icons to switch to the neighbouring desktop", "feature.showPrevNextIcons"));
			c.Add(ToggleCard(null, "Hide the arrows at the first and last desktop", null, "feature.showPrevNextIcons.automaticallyHidePrevNextOnBounds", indent: true, enabled: arrows));
			c.Add(ToggleCard("", "Right-click opens the panel", "Off: the classic menu", "feature.trayFlyout"));
			c.Add(ToggleCard("", "Left-click opens Task View", null, "feature.showDesktopNumberInIconTray.clickToOpenTaskView"));
			c.Add(ToggleCard("", "Mouse wheel switches desktops", "Scroll over the tray icons", "feature.mouseWheelOnTrayIcons"));
		}

		private void BuildDesktopsPage(List<Control> c) {
			c.Add(PageTitle("Desktops and windows"));
			var extended = App.Instance.VDAPIExtended != null;

			c.Add(Section("Switching"));
			c.Add(ToggleCard("", "Wrap around", "Next on the last desktop goes to the first, and previous on the first to the last", "feature.wrapAround"));
			c.Add(ToggleCard("", "Detect switches instantly", "Uses Windows' own notifications instead of checking regularly. Turn off if the tray number stops updating", "feature.useShellNotifications"));
			c.Add(ToggleCard("", "Restore the focused window", "When switching back to a desktop, focus the window you used there last", "feature.restorePreviousWindowFocus"));

			if (extended) {
				c.Add(Section("Windows"));
				c.Add(ToggleCard("", "Switch along when moving a window", "After moving a window to another desktop, go to that desktop too", "feature.moveWindow.follow"));
				c.Add(ToggleCard("", "Window menu on Ctrl + right-click", "Ctrl + right-click a window's title bar for its desktop menu (move, pin, ...)", "feature.windowMenu.titleBarCtrlRightClick"));

				c.Add(Section("Always show on all desktops"));
				c.Add(Note("These apps are shown on every desktop automatically, also after restarting."));
				var apps = App.Instance.GetAutoPinApps();
				foreach (var app in apps) {
					var process = app;
					var remove = Button("Remove", "", () => Apply(() => App.Instance.SetAutoPinnedProcess(process, false)));
					c.Add(Card("", process, null, remove));
				}
				var add = Button("Add an app", "", () => { });
				add.Click += (s, e) => ShowAddAppMenu(add);
				c.Add(Card(apps.Count == 0 ? "" : null, apps.Count == 0 ? "No apps yet" : "Add another app", "Pick one of the apps you have open", add));
			}

			c.Add(Section("Config file"));
			c.Add(ToggleCard("", "Apply changes to the config file immediately", "For editing the settings file by hand", "feature.autoReloadConfig"));
			c.Add(Card("", "Settings file", Settings.GetConfigDirectory(), Button("Open folder", null, () => Open(Settings.GetConfigDirectory()))));
		}

		private void ShowAddAppMenu(Control anchor) {
			var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.ManagerRenderMode, ShowImageMargin = true, ShowCheckMargin = false, ImageScalingSize = new Size(S(16), S(16)) };
			var existing = new HashSet<string>(App.Instance.GetAutoPinApps(), StringComparer.OrdinalIgnoreCase);
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var window in Util.OS.GetAppWindows()) {
				if (!App.Instance.IsUserWindow(window)) continue;
				var process = Util.OS.GetWindowProcessName(window);
				if (process == "" || existing.Contains(process) || !seen.Add(process)) continue;
				var name = Util.OS.GetWindowAppName(window);
				var item = new ToolStripMenuItem(string.Equals(name, process, StringComparison.OrdinalIgnoreCase) ? process : $"{name}  ({process})") { Image = Util.MenuIcons.ForWindow(window) };
				item.Click += (s, e) => Apply(() => App.Instance.SetAutoPinnedProcess(process, true));
				menu.Items.Add(item);
			}
			if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem("No other apps are open") { Enabled = false });
			menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
			menu.Show(anchor, new Point(0, anchor.Height));
		}

		private void BuildOverlaysPage(List<Control> c) {
			c.Add(PageTitle("Overlays"));

			c.Add(Section("When switching desktops"));
			var switchOverlay = Settings.GetBool("feature.showDesktopSwitchOverlay");
			c.Add(ToggleCard("", "Show the desktop name", "A short overlay with the new desktop's name. Windows 11 also shows the name itself", "feature.showDesktopSwitchOverlay"));
			c.Add(DropDownCard(null, "Duration", null,
				new[] { O("500", "0.5 seconds"), O("1000", "1 second"), O("2000", "2 seconds"), O("3000", "3 seconds"), O("0", "Until the next switch") },
				Settings.GetInt("feature.showDesktopSwitchOverlay.duration").ToString(), v => Settings.SetInt("feature.showDesktopSwitchOverlay.duration", int.Parse(v)), indent: true, enabled: switchOverlay));
			c.Add(DropDownCard(null, "Position", null, Positions, Settings.GetString("feature.showDesktopSwitchOverlay.position"), v => Settings.SetString("feature.showDesktopSwitchOverlay.position", v), indent: true, enabled: switchOverlay));
			c.Add(ToggleCard(null, "Fade in and out", null, "feature.showDesktopSwitchOverlay.animate", indent: true, enabled: switchOverlay));
			c.Add(ToggleCard(null, "Translucent", null, "feature.showDesktopSwitchOverlay.translucent", indent: true, enabled: switchOverlay));
			c.Add(ToggleCard(null, "On all monitors", null, "feature.showDesktopSwitchOverlay.showOnAllMonitors", indent: true, enabled: switchOverlay));
			c.Add(ToggleCard("", "Splash screen at startup", "Briefly show the app name when it starts (uses the overlay above)", "feature.showSplashScreen", enabled: switchOverlay));

			c.Add(Section("Always visible"));
			var status = Settings.GetBool("feature.showDesktopStatusOverlay");
			c.Add(ToggleCard("", "Show the desktop name permanently", "A small label with the current desktop's name, always on screen", "feature.showDesktopStatusOverlay"));
			c.Add(DropDownCard(null, "Position", null, Positions, Settings.GetString("feature.showDesktopStatusOverlay.position"), v => Settings.SetString("feature.showDesktopStatusOverlay.position", v), indent: true, enabled: status));
			c.Add(ToggleCard(null, "Fade in", null, "feature.showDesktopStatusOverlay.animate", indent: true, enabled: status));
			c.Add(ToggleCard(null, "Translucent", null, "feature.showDesktopStatusOverlay.translucent", indent: true, enabled: status));
			c.Add(ToggleCard(null, "On all monitors", null, "feature.showDesktopStatusOverlay.showOnAllMonitors", indent: true, enabled: status));
		}

		private class ShortcutDef {
			public string Feature, Title, Description; public bool ModifiersOnly, NeedsExtended;
		}

		private static readonly ShortcutDef[] DesktopShortcuts = {
			new ShortcutDef { Feature = "feature.useHotKeyToJumpToDesktopNumber", Title = "Jump to desktop 1–9", Description = "Hold the keys and press the desktop's number", ModifiersOnly = true },
			new ShortcutDef { Feature = "feature.useHotKeyToJumpToPreviousDesktop", Title = "Back to the last used desktop", Description = "Like Alt + Tab, but for desktops" },
			new ShortcutDef { Feature = "feature.useHotKeyToSwitchDesktopForward", Title = "Next desktop", Description = "Windows' own: Ctrl + Win + Right" },
			new ShortcutDef { Feature = "feature.useHotKeyToSwitchDesktopBackward", Title = "Previous desktop", Description = "Windows' own: Ctrl + Win + Left" },
		};

		private static readonly ShortcutDef[] WindowShortcuts = {
			new ShortcutDef { Feature = "feature.useHotKeyToMoveWindowToDesktopNumber", Title = "Take the window to desktop 1–9", Description = "Hold the keys and press the desktop's number", ModifiersOnly = true, NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToMoveWindowForward", Title = "Move the window to the next desktop", NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToMoveWindowBackward", Title = "Move the window to the previous desktop", NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToMoveWindowToNewDesktop", Title = "Move the window to a new desktop", NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToTogglePinWindow", Title = "Show the window on all desktops", Description = "Press again to undo", NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToTogglePinApp", Title = "Show all windows of the app on all desktops", Description = "Also windows it opens later. Press again to undo", NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToGatherAppWindows", Title = "Bring all windows of the app here", Description = "From the other desktops", NeedsExtended = true },
			new ShortcutDef { Feature = "feature.useHotKeyToShowWindowMenu", Title = "Window menu", Description = "Move, pin and more for the active window, at the mouse cursor", NeedsExtended = true },
		};

		private void BuildShortcutsPage(List<Control> c) {
			c.Add(PageTitle("Keyboard shortcuts"));
			c.Add(Note("Click a shortcut and press the keys you want. Changes work right away."));
			var extended = App.Instance.VDAPIExtended != null;

			c.Add(Section("Desktops"));
			foreach (var def in DesktopShortcuts) c.Add(ShortcutCard(def));
			if (extended) {
				c.Add(Section("Active window"));
				foreach (var def in WindowShortcuts) c.Add(ShortcutCard(def));
			}

			c.Add(Section("Mouse"));
			c.Add(Card("", "Right-click the tray number", "Opens the panel: desktops, the current window, pinned windows", null));
			c.Add(Card("", "Scroll over the tray number", "Switches desktops", null));
			if (extended) c.Add(Card("", "Ctrl + right-click a title bar", "The window menu for that window", null));

			c.Add(Section("Built into Windows"));
			c.Add(KeyCard("Next or previous desktop", "Ctrl + Win + Right"));
			c.Add(KeyCard("New desktop", "Ctrl + Win + D"));
			c.Add(KeyCard("Close this desktop", "Ctrl + Win + F4"));
			c.Add(KeyCard("Task View", "Win + Tab"));

			c.Add(Section("Other"));
			c.Add(ToggleCard("", "Tell me when a shortcut can't be used", "A notification when another app already uses one of the shortcuts", "feature.notifyHotKeyConflicts"));
			var reset = new StackRow(_scale, S(40)) { Margin = new Padding(0, S(12), 0, 0) };
			reset.Controls.Add(Button("Reset all shortcuts to the defaults", "", () => Apply(() => {
				foreach (var def in DesktopShortcuts.Concat(WindowShortcuts)) {
					Settings.ResetToDefault(def.Feature);
					Settings.ResetToDefault(def.Feature + ".hotkey");
				}
			})));
			c.Add(reset);
		}

		private SettingsCard ShortcutCard(ShortcutDef def) {
			var enabled = Settings.GetBool(def.Feature);
			var hotkey = Settings.GetString(def.Feature + ".hotkey") ?? "";
			var problem = enabled && hotkey.Trim() != "" ? App.Instance.GetHotKeyProblem(hotkey) : null;

			var actions = new StackRow(_scale, S(36)) { Width = S(250) + S(8) + S(40) };
			var recorder = new ShortcutRecorder(_scale) { Value = hotkey, ModifiersOnly = def.ModifiersOnly, Enabled = enabled };
			recorder.Recorded += value => Apply(() => {
				Settings.SetString(def.Feature + ".hotkey", value);
				if (value != "") Settings.SetBool(def.Feature, true);
			});
			var toggle = new ModernToggle(_scale) { Checked = enabled };
			toggle.CheckedChanged += (s, e) => Apply(() => Settings.SetBool(def.Feature, toggle.Checked));
			actions.Controls.Add(recorder);
			actions.Controls.Add(toggle);
			actions.BackColor = Theme.Card;

			var card = Card(null, def.Title, problem != null ? problem + " – pick other keys" : def.Description, actions);
			if (problem != null) card.DescriptionColor = Theme.Danger;
			return card;
		}

		// A card showing a fixed shortcut as key caps
		private SettingsCard KeyCard(string title, string keys) {
			var caps = new KeyCapsView(_scale, keys) { Height = S(36), Width = S(250) };
			return Card(null, title, null, caps);
		}

		private void BuildAboutPage(List<Control> c) {
			c.Add(PageTitle("About"));
			var version = Assembly.GetExecutingAssembly().GetName().Version;
			c.Add(Card("", "Windows VD Helper", $"Version {version.Major}.{version.Minor}.{version.Build}  ·  Virtual desktop number in the tray, switching, moving and pinning windows", null));

			c.Add(Section("Files"));
			c.Add(Card("", "Settings file", Settings.GetConfigDirectory(), Button("Open folder", null, () => Open(Settings.GetConfigDirectory()))));
			c.Add(Card("", "Log", "What the app did, for when something doesn't work", Button("Open log", null, () => { if (System.IO.File.Exists(LogFile)) Open(LogFile); })));

			c.Add(Section("Project"));
			c.Add(Card("", "Source code and updates", "github.com/benju66/WindowsVDHelper", Button("Open", "", () => Open("https://github.com/benju66/WindowsVDHelper"))));
			c.Add(Card("", "License", "Free software under the GNU General Public License v3", Button("View", "", () => Open("https://github.com/benju66/WindowsVDHelper/blob/main/LICENSE.md"))));
		}

		#endregion
	}

	/// <summary>A fixed shortcut shown as key caps.</summary>
	internal class KeyCapsView : ModernControl {
		private readonly string _keys;
		public KeyCapsView(float scale, string keys) : base(scale) { _keys = keys; }
		protected override void OnPaint(PaintEventArgs e) {
			e.Graphics.Clear(Parent?.BackColor ?? Theme.Card);
			KeyCaps.Draw(e.Graphics, KeyCaps.Split(_keys, false), new Rectangle(0, 0, Width - 1, Height), Scale, false, true);
		}
	}
}
