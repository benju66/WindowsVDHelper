using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper {

	// The "Desktops & windows" and "Keyboard shortcuts" tabs of the settings window. They are built in code
	// (not in the designer), and the designer's original content becomes the "General" tab.
	public partial class SettingsForm {

		private TabControl _tabs;
		private readonly List<Tuple<CheckBox, string>> _optionCheckBoxes = new List<Tuple<CheckBox, string>>();
		private readonly List<ShortcutRow> _shortcutRows = new List<ShortcutRow>();
		private ListBox _autoPinList;

		private class ShortcutRow {
			public string Feature;
			public bool ModifiersOnly;
			public CheckBox Enabled;
			public HotKeyBox Box;
			public Label Status;
		}

		private void BuildTabs() {
			SuspendLayout();

			// Move the designer's content into the "General" tab
			var originalControls = this.Controls.Cast<Control>().ToList();
			var bounds = originalControls.Select(c => c.Bounds).Aggregate(Rectangle.Union);
			var generalPage = new TabPage("General") { UseVisualStyleBackColor = true };
			foreach (var control in originalControls) {
				this.Controls.Remove(control);
				generalPage.Controls.Add(control);
			}

			_tabs = new TabControl { Dock = DockStyle.Fill };
			_tabs.TabPages.Add(generalPage);
			_tabs.TabPages.Add(BuildOptionsPage());
			_tabs.TabPages.Add(BuildShortcutsPage());
			_tabs.SelectedIndexChanged += (s, e) => LoadTabs();
			this.Controls.Add(_tabs);

			// Grow the window by the tab header and borders, so the General tab fits as before
			// (the new tabs need more width than the original content, for the shortcut status column)
			var header = TextRenderer.MeasureText("Ag", this.Font).Height + 16;
			this.ClientSize = new Size(
				Math.Max(bounds.Right + bounds.Left + 12, Scale(780)),
				Math.Max(bounds.Bottom + bounds.Top + header + 12, Scale(640)));
			ResumeLayout();
		}

		#region Desktops & windows

		private TabPage BuildOptionsPage() {
			var page = new TabPage("Desktops & windows") { UseVisualStyleBackColor = true, AutoScroll = true, Padding = new Padding(16) };
			var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };

			layout.Controls.Add(Heading("Switching"));
			layout.Controls.Add(Option("Wrap around (next on the last desktop goes to the first)", "feature.wrapAround"));
			layout.Controls.Add(Option("Mouse wheel over the tray icons switches desktops", "feature.mouseWheelOnTrayIcons"));
			layout.Controls.Add(Option("Restore the previously focused window when switching", "feature.restorePreviousWindowFocus"));
			layout.Controls.Add(Option("Detect switches instantly (Windows notifications instead of polling)", "feature.useShellNotifications"));

			layout.Controls.Add(Heading("Moving windows"));
			layout.Controls.Add(Option("Switch along when moving a window to another desktop", "feature.moveWindow.follow"));
			layout.Controls.Add(Option("Ctrl + right-click a window's title bar opens its window menu", "feature.windowMenu.titleBarCtrlRightClick"));

			layout.Controls.Add(Heading("Tray icon"));
			layout.Controls.Add(Option("Right-click opens the panel (off: the classic menu)", "feature.trayFlyout"));
			layout.Controls.Add(Option("Color the desktop number per desktop", "feature.colorIconsPerDesktop"));
			layout.Controls.Add(Option("Notify when a shortcut can't be used because another app uses it", "feature.notifyHotKeyConflicts"));

			layout.Controls.Add(Heading("Always show on all desktops"));
			layout.Controls.Add(new Label { Text = "These apps are shown on every desktop automatically, also after restarting:", AutoSize = true, Margin = new Padding(3, 0, 3, 6) });
			var listRow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
			_autoPinList = new ListBox { Width = Scale(320), Height = Scale(130), IntegralHeight = false };
			var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
			var addButton = new Button { Text = "Add app...", AutoSize = true };
			var removeButton = new Button { Text = "Remove", AutoSize = true };
			addButton.Click += (s, e) => ShowAddAutoPinMenu(addButton);
			removeButton.Click += (s, e) => {
				var app = _autoPinList.SelectedItem as string;
				if (app == null) return;
				App.Instance.SetAutoPinnedProcess(app, false);
				LoadTabs();
			};
			buttons.Controls.Add(addButton);
			buttons.Controls.Add(removeButton);
			listRow.Controls.Add(_autoPinList);
			listRow.Controls.Add(buttons);
			layout.Controls.Add(listRow);

			layout.Controls.Add(Heading("Config"));
			var reload = Option("Apply changes to the config file immediately", "feature.autoReloadConfig");
			layout.Controls.Add(reload);
			var openConfig = new LinkLabel { Text = "Open config folder", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
			openConfig.LinkClicked += (s, e) => App.Instance.OpenURL(Settings.GetConfigDirectory());
			layout.Controls.Add(openConfig);

			page.Controls.Add(layout);
			return page;
		}

		private void ShowAddAutoPinMenu(Control anchor) {
			var menu = new ContextMenuStrip();
			var existing = new HashSet<string>(App.Instance.GetAutoPinApps(), StringComparer.OrdinalIgnoreCase);
			var apps = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase); // process name -> display name
			foreach (var window in Util.OS.GetAppWindows()) {
				var process = Util.OS.GetWindowProcessName(window);
				if (process == "" || existing.Contains(process) || apps.ContainsKey(process)) continue;
				uint pid;
				Util.OS.GetWindowThreadProcessId(window, out pid);
				if (pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) continue;
				var name = Util.OS.GetWindowAppName(window);
				apps[process] = string.Equals(name, process, StringComparison.OrdinalIgnoreCase) ? process : $"{name}  ({process})";
			}
			if (apps.Count == 0) menu.Items.Add(new ToolStripMenuItem("(no other apps open)") { Enabled = false });
			foreach (var app in apps.OrderBy(a => a.Value, StringComparer.OrdinalIgnoreCase)) {
				var process = app.Key;
				var item = new ToolStripMenuItem(app.Value.Replace("&", "&&"));
				item.Click += (s, e) => { App.Instance.SetAutoPinnedProcess(process, true); LoadTabs(); };
				menu.Items.Add(item);
			}
			menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
			menu.Show(anchor, new Point(0, anchor.Height));
		}

		private int Scale(int logicalPixels) {
			return (int)Math.Round(logicalPixels * this.DeviceDpi / 96.0);
		}

		private Label Heading(string text) {
			return new Label { Text = text, AutoSize = true, Font = new Font(this.Font, FontStyle.Bold), Margin = new Padding(3, 14, 3, 4) };
		}

		private CheckBox Option(string text, string setting) {
			var checkBox = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 2, 3, 2) };
			checkBox.CheckedChanged += (s, e) => {
				if (IsLoading) return;
				Settings.SetBool(setting, checkBox.Checked);
				App.Instance.ApplySettings();
			};
			_optionCheckBoxes.Add(Tuple.Create(checkBox, setting));
			return checkBox;
		}

		#endregion

		#region Keyboard shortcuts

		private TabPage BuildShortcutsPage() {
			var page = new TabPage("Keyboard shortcuts") { UseVisualStyleBackColor = true, AutoScroll = true, Padding = new Padding(16) };
			var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
			layout.Controls.Add(new Label {
				Text = "Click a shortcut and press the keys you want (at least one of Ctrl, Alt, Shift, Win plus a key). "
					+ "Backspace clears it, Esc cancels. For the 1..9 shortcuts, press the modifiers together with any number.",
				AutoSize = true, MaximumSize = new Size(Scale(700), 0), Margin = new Padding(3, 0, 3, 10)
			});

			var table = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Margin = new Padding(0) };
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			AddShortcutSection(table, "Desktops");
			AddShortcutRow(table, "feature.useHotKeyToJumpToDesktopNumber", "Jump to desktop 1..9", true);
			AddShortcutRow(table, "feature.useHotKeyToJumpToPreviousDesktop", "Back to the last used desktop", false);
			AddShortcutRow(table, "feature.useHotKeyToSwitchDesktopForward", "Next desktop", false);
			AddShortcutRow(table, "feature.useHotKeyToSwitchDesktopBackward", "Previous desktop", false);
			AddShortcutSection(table, "Active window");
			AddShortcutRow(table, "feature.useHotKeyToMoveWindowToDesktopNumber", "Take it to desktop 1..9", true);
			AddShortcutRow(table, "feature.useHotKeyToMoveWindowForward", "Move it to the next desktop", false);
			AddShortcutRow(table, "feature.useHotKeyToMoveWindowBackward", "Move it to the previous desktop", false);
			AddShortcutRow(table, "feature.useHotKeyToMoveWindowToNewDesktop", "Move it to a new desktop", false);
			AddShortcutRow(table, "feature.useHotKeyToTogglePinWindow", "Show it on all desktops", false);
			AddShortcutRow(table, "feature.useHotKeyToTogglePinApp", "Show all windows of its app on all desktops", false);
			AddShortcutRow(table, "feature.useHotKeyToGatherAppWindows", "Bring all windows of its app here", false);
			AddShortcutRow(table, "feature.useHotKeyToShowWindowMenu", "Open its window menu (move, pin, ...)", false);
			layout.Controls.Add(table);

			var reset = new Button { Text = "Reset all shortcuts to defaults", AutoSize = true, Margin = new Padding(3, 14, 3, 3) };
			reset.Click += (s, e) => {
				foreach (var row in _shortcutRows) {
					Settings.ResetToDefault(row.Feature);
					Settings.ResetToDefault(row.Feature + ".hotkey");
				}
				App.Instance.ApplySettings();
				LoadTabs();
			};
			layout.Controls.Add(reset);
			var note = new Label { Text = "Built into Windows: Ctrl + Win + Left/Right switch desktops, Ctrl + Win + D new desktop, Ctrl + Win + F4 close desktop.", AutoSize = true, MaximumSize = new Size(Scale(700), 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 12, 3, 3) };
			layout.Controls.Add(note);

			page.Controls.Add(layout);
			return page;
		}

		private void AddShortcutSection(TableLayoutPanel table, string text) {
			var label = Heading(text);
			table.Controls.Add(label);
			table.SetColumnSpan(label, 3);
		}

		private void AddShortcutRow(TableLayoutPanel table, string feature, string text, bool modifiersOnly) {
			var row = new ShortcutRow { Feature = feature, ModifiersOnly = modifiersOnly };
			row.Enabled = new CheckBox { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 4, 12, 4) };
			row.Box = new HotKeyBox { Width = Scale(230), ModifiersOnly = modifiersOnly, Anchor = AnchorStyles.Left };
			row.Status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 4, 3, 4) };
			row.Enabled.CheckedChanged += (s, e) => {
				if (IsLoading) return;
				Settings.SetBool(feature, row.Enabled.Checked);
				App.Instance.SetupHotKeys();
				LoadTabs();
			};
			row.Box.Recorded += value => {
				Settings.SetString(feature + ".hotkey", value);
				if (value != "" && !row.Enabled.Checked) Settings.SetBool(feature, true); // recording a shortcut turns it on
				row.Enabled.Focus(); // leaving the box re-registers the hotkeys (see HotKeyBox)
				LoadTabs();
			};
			row.Box.RecordingEnded += () => LoadTabs();
			table.Controls.Add(row.Enabled);
			table.Controls.Add(row.Box);
			table.Controls.Add(row.Status);
			_shortcutRows.Add(row);
		}

		#endregion

		// Loads the settings into the tabs and updates the shortcut status
		private void LoadTabs() {
			if (_tabs == null) return;
			var wasLoading = IsLoading;
			IsLoading = true;
			try {
				foreach (var option in _optionCheckBoxes) option.Item1.Checked = Settings.GetBool(option.Item2);
				if (_autoPinList != null) {
					_autoPinList.Items.Clear();
					foreach (var app in App.Instance.GetAutoPinApps()) _autoPinList.Items.Add(app);
				}
				foreach (var row in _shortcutRows) {
					var enabled = Settings.GetBool(row.Feature);
					var hotkey = Settings.GetString(row.Feature + ".hotkey") ?? "";
					row.Enabled.Checked = enabled;
					if (!row.Box.IsRecording) row.Box.SetValue(hotkey);
					if (!enabled || hotkey.Trim() == "") {
						row.Status.Text = "Off";
						row.Status.ForeColor = SystemColors.GrayText;
					} else {
						var problem = App.Instance.GetHotKeyProblem(hotkey);
						row.Status.Text = problem ?? "OK";
						row.Status.ForeColor = problem == null ? Color.SeaGreen : Color.Firebrick;
					}
				}
			} finally {
				IsLoading = wasLoading;
			}
		}
	}

	/// <summary>
	/// A box which records a keyboard shortcut: click it and press the keys. While it has the focus, the app's
	/// hotkeys are suspended, so pressing an existing shortcut records it instead of running it.
	/// </summary>
	public class HotKeyBox : TextBox {

		// The new value in the config format, e.g. "Ctrl + Shift + Win + N" (or "Ctrl + Alt" for ModifiersOnly), "" = cleared
		public event Action<string> Recorded;
		public event Action RecordingEnded;

		public bool ModifiersOnly { get; set; }
		public bool IsRecording { get; private set; }
		private string _value = "";

		[DllImport("user32.dll")]
		private static extern short GetKeyState(int nVirtKey);
		private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

		public HotKeyBox() {
			this.ReadOnly = true;
			this.ShortcutsEnabled = false;
			this.Cursor = Cursors.Hand;
			this.BackColor = SystemColors.Window;
		}

		public void SetValue(string value) {
			_value = value ?? "";
			this.Text = Display(_value);
		}

		private string Display(string value) {
			if (value.Trim() == "") return "(none)";
			return ModifiersOnly ? value + " + 1..9" : value;
		}

		protected override void OnGotFocus(EventArgs e) {
			base.OnGotFocus(e);
			IsRecording = true;
			App.Instance.SuspendHotKeys();
			this.Text = "Press the keys...";
			this.BackColor = SystemColors.Info;
		}

		protected override void OnLostFocus(EventArgs e) {
			base.OnLostFocus(e);
			IsRecording = false;
			this.BackColor = SystemColors.Window;
			this.Text = Display(_value);
			App.Instance.ResumeHotKeys();
			RecordingEnded?.Invoke();
		}

		protected override bool IsInputKey(Keys keyData) {
			return true; // also receive arrows, Tab, Enter, ...
		}

		protected override bool ProcessDialogKey(Keys keyData) {
			return false; // don't let the form handle Enter/Esc/Tab while recording
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			e.Handled = true;
			e.SuppressKeyPress = true;
			var key = e.KeyCode;
			var win = GetKeyState(VK_LWIN) < 0 || GetKeyState(VK_RWIN) < 0;
			var modifiers = new List<string>();
			if (e.Control) modifiers.Add("Ctrl");
			if (e.Alt) modifiers.Add("Alt");
			if (e.Shift) modifiers.Add("Shift");
			if (win) modifiers.Add("Win");

			if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) {
				// Only modifiers so far: live preview
				this.Text = modifiers.Count > 0 ? string.Join(" + ", modifiers) + " + ..." : "Press the keys...";
				return;
			}
			if (modifiers.Count == 0) {
				if (key == Keys.Escape) { EndRecording(); return; }
				if (key == Keys.Back || key == Keys.Delete) { Commit(""); return; }
				if (key == Keys.Tab) { this.Parent.SelectNextControl(this, true, true, true, true); return; }
				this.Text = "Add Ctrl, Alt, Shift or Win";
				return;
			}
			if (ModifiersOnly) {
				var isNumber = (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9);
				if (!isNumber) { this.Text = "Press the modifiers with a number"; return; }
				Commit(string.Join(" + ", modifiers));
				return;
			}
			Commit(string.Join(" + ", modifiers) + " + " + KeyName(key));
		}

		private void Commit(string value) {
			SetValue(value);
			Recorded?.Invoke(value);
		}

		private void EndRecording() {
			this.Parent.SelectNextControl(this, false, true, true, true);
		}

		// Key names as understood by the hotkey parser (App.SetupHotKeys)
		private static string KeyName(Keys key) {
			switch (key) {
				case Keys.Oemtilde: return "Tilde";
				case Keys.OemMinus: return "Minus";
				case Keys.Oemplus: return "Plus";
				case Keys.Oemcomma: return "Comma";
				case Keys.OemPeriod: return "Period";
				case Keys.OemQuestion: return "Question";
				case Keys.OemSemicolon: return "Semicolon";
				case Keys.OemOpenBrackets: return "OpenBrackets";
				case Keys.OemCloseBrackets: return "CloseBrackets";
				case Keys.OemPipe: return "Pipe";
				case Keys.OemQuotes: return "Quotes";
				case Keys.OemBackslash: return "Backslash";
				default: return key.ToString();
			}
		}
	}
}
