using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper {

	/// <summary>
	/// The tray panel (right-click on the desktop number), in the style of the PowerToys / PC Manager flyouts:
	/// search windows, desktop tiles, actions for the current window, the windows shown on all desktops, and a
	/// footer. Closes when it loses the focus or with Esc. Built in code, all sizes scaled for the DPI.
	/// </summary>
	public class TrayFlyout : Form {

		private enum View { Home, Search, PinList }

		private readonly IntPtr _window; // the window the user was working in (actions target)
		private readonly Rectangle? _anchor; // the tray icon, to position next to it
		private View _view = View.Home;
		private bool _movePicker = false;
		private bool _closing = false;

		private List<string> _names = new List<string>();
		private int _current;
		private List<App.WindowEntry> _windows = new List<App.WindowEntry>();

		private readonly float _scale;
		private readonly int _width;
		private FlowLayoutPanel _root;
		private Panel _content;
		private TextBox _search;
		private readonly ToolTip _toolTip = new ToolTip();

		public TrayFlyout(IntPtr window, Rectangle? anchor) {
			_window = window;
			_anchor = anchor;
			_scale = (App.Instance != null ? App.Instance.TrayDpi : 96) / 96f;
			_width = S(360);

			FormBorderStyle = FormBorderStyle.None;
			ShowInTaskbar = false;
			TopMost = true;
			StartPosition = FormStartPosition.Manual;
			KeyPreview = true;
			AutoScaleMode = AutoScaleMode.None;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			BackColor = FlyoutColors.Back;
			Font = new Font("Segoe UI", 9f * _scale, FontStyle.Regular, GraphicsUnit.Point);
			Text = "Virtual desktops";

			_root = new FlowLayoutPanel {
				FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Padding = new Padding(S(16), S(14), S(16), S(10)), Margin = new Padding(0), BackColor = FlyoutColors.Back
			};
			Controls.Add(_root);
			LoadData();
			BuildChrome();
			Render();
		}

		private int S(int px) { return (int)Math.Round(px * _scale); }
		private int Inner { get { return _width - S(32); } }

		#region Window setup

		protected override CreateParams CreateParams {
			get {
				var cp = base.CreateParams;
				cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
				cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW (not in Alt+Tab)
				return cp;
			}
		}

		protected override void OnHandleCreated(EventArgs e) {
			base.OnHandleCreated(e);
			try {
				int round = 2; // rounded corners (Windows 11)
				DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
				int border = ColorTranslator.ToWin32(FlyoutColors.Border);
				DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
				int dark = FlyoutColors.IsDark ? 1 : 0;
				DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); // dark scrollbars/frame
			} catch (Exception) { }
		}

		public void ShowFlyout() {
			PerformLayout();
			Position();
			Show();
			Activate();
			Util.OS.SetForegroundWindow(Handle);
			_search?.Focus();
		}

		// Next to the tray icon, on the taskbar's side of the screen, inside the working area
		private void Position() {
			var size = GetPreferredSize(Size.Empty);
			var anchor = _anchor ?? new Rectangle(Cursor.Position, new Size(1, 1));
			var screen = Screen.FromPoint(new Point(anchor.X + anchor.Width / 2, anchor.Y + anchor.Height / 2));
			var work = screen.WorkingArea;
			var bounds = screen.Bounds;
			var margin = S(12);
			int x = anchor.X + anchor.Width / 2 - size.Width / 2;
			int y;
			if (work.Bottom < bounds.Bottom) y = work.Bottom - size.Height - margin;      // taskbar at the bottom
			else if (work.Top > bounds.Top) y = work.Top + margin;                          // top
			else y = anchor.Y + anchor.Height / 2 - size.Height / 2;                        // left/right
			if (work.Left > bounds.Left) x = work.Left + margin;
			else if (work.Right < bounds.Right) x = work.Right - size.Width - margin;
			x = Math.Max(work.Left + margin, Math.Min(x, work.Right - size.Width - margin));
			y = Math.Max(work.Top + margin, Math.Min(y, work.Bottom - size.Height - margin));
			Location = new Point(x, y);
		}

		protected override void OnDeactivate(EventArgs e) {
			base.OnDeactivate(e);
			if (!_keepOpen) CloseFlyout();
		}

		private bool _keepOpen = false; // while a modal child (e.g. a menu of this panel) is open

		private void CloseFlyout() {
			if (_closing) return;
			_closing = true;
			BeginInvoke((Action)Close);
		}

		protected override void OnFormClosed(FormClosedEventArgs e) {
			_toolTip.Dispose();
			base.OnFormClosed(e);
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
			if (keyData == Keys.Escape) {
				if (_view == View.Search && _search != null && _search.Text != "") { _search.Text = ""; return true; }
				if (_view == View.PinList) { _view = View.Home; Render(); return true; }
				CloseFlyout();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		// Runs an action after the panel closed (so the focus goes back to where the action wants it)
		private void CloseThen(Action action) {
			CloseFlyout();
			App.Instance.AppForm.BeginInvoke((Action)(() => {
				try { action(); } catch (Exception e) { Util.Logging.WriteLine("TrayFlyout: Error: " + e.Message); }
			}));
		}

		#endregion

		#region Data

		private void LoadData() {
			var app = App.Instance;
			var ext = app.VDAPIExtended;
			_current = (int)app.CurrentVDDisplayNumber;
			try { _names = ext != null ? ext.GetDesktopNames() : null; } catch (Exception) { _names = null; }
			if (_names == null) _names = Enumerable.Range(1, app.CurrentVDDisplayCount).Select(i => i - 1 == _current ? app.CurrentVDDisplayName : "Desktop " + i).ToList();
			try { _windows = ext != null ? app.GetWindowEntries() : new List<App.WindowEntry>(); } catch (Exception e) { Util.Logging.WriteLine("TrayFlyout: Error: windows: " + e.Message); }
		}

		private App.WindowEntry CurrentWindow {
			get { return _windows.FirstOrDefault(w => w.Hwnd == _window); }
		}

		private string DesktopName(int index) {
			return index >= 0 && index < _names.Count ? _names[index] : "Desktop " + (index + 1);
		}

		#endregion

		#region Layout

		// Header, search box, content area (rebuilt per view), footer
		private void BuildChrome() {
			// Header: title and the "more" menu
			var header = new Panel { Width = Inner, Height = S(32), Margin = new Padding(0, 0, 0, S(8)), BackColor = FlyoutColors.Back };
			header.Controls.Add(new Label { Text = "Virtual desktops", AutoSize = true, ForeColor = FlyoutColors.Text, Font = new Font("Segoe UI Semibold", 11f * _scale, GraphicsUnit.Point), Location = new Point(0, S(4)) });
			var more = new FlyoutButton(_scale, "", null) { Size = new Size(S(32), S(32)), Location = new Point(Inner - S(32), 0) };
			_toolTip.SetToolTip(more, "More");
			more.Click += (s, e) => ShowMoreMenu(more);
			header.Controls.Add(more);
			_root.Controls.Add(header);

			// Search
			var searchBox = new RoundedPanel(_scale) { Width = Inner, Height = S(36), Margin = new Padding(0, 0, 0, S(12)), Fill = FlyoutColors.Card, Radius = S(6) };
			searchBox.Controls.Add(new PictureBox { Image = Util.MenuIcons.Glyph("", FlyoutColors.TextSecondary, S(16)), Size = new Size(S(16), S(16)), Location = new Point(S(10), S(10)), BackColor = FlyoutColors.Card });
			_search = new TextBox { BorderStyle = BorderStyle.None, BackColor = FlyoutColors.Card, ForeColor = FlyoutColors.Text, Width = Inner - S(44), Location = new Point(S(34), S(9)) };
			searchBox.Controls.Add(_search);
			searchBox.Click += (s, e) => _search.Focus();
			_search.HandleCreated += (s, e) => SendMessage(_search.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Search windows on all desktops");
			_search.TextChanged += (s, e) => { _view = _search.Text.Trim() == "" ? View.Home : View.Search; Render(); };
			_search.KeyDown += (s, e) => {
				if (e.KeyCode == Keys.Enter && _view == View.Search) {
					var first = SearchResults().FirstOrDefault();
					if (first != null) CloseThen(() => App.Instance.FocusWindow(first.Hwnd));
					e.SuppressKeyPress = true;
				}
			};
			if (App.Instance.VDAPIExtended != null) _root.Controls.Add(searchBox);

			_content = new Panel { Width = Inner, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), BackColor = FlyoutColors.Back };
			_root.Controls.Add(_content);

			// Footer: position, shortcuts, settings, exit
			var footer = new Panel { Width = Inner, Height = S(40), Margin = new Padding(0, S(8), 0, 0), BackColor = FlyoutColors.Back };
			footer.Paint += (s, e) => { using (var pen = new Pen(FlyoutColors.Divider)) e.Graphics.DrawLine(pen, 0, 0, Inner, 0); };
			footer.Controls.Add(new Label { Text = $"Desktop {_current + 1} of {_names.Count}", AutoSize = true, ForeColor = FlyoutColors.TextMuted, Location = new Point(0, S(12)) });
			var buttons = new[] {
				Tuple.Create("", "Keyboard shortcuts", (Action)(() => CloseThen(App.Instance.ShowKeyboardShortcuts))),
				Tuple.Create("", "Settings", (Action)(() => CloseThen(() => App.Instance.ShowSettings()))),
				Tuple.Create("", "Exit", (Action)(() => CloseThen(App.Instance.Exit))),
			};
			for (var i = 0; i < buttons.Length; i++) {
				var b = buttons[i];
				var button = new FlyoutButton(_scale, b.Item1, null) { Size = new Size(S(32), S(32)), Location = new Point(Inner - S(32) * (buttons.Length - i), S(6)) };
				_toolTip.SetToolTip(button, b.Item2);
				button.Click += (s, e) => b.Item3();
				footer.Controls.Add(button);
			}
			_root.Controls.Add(footer);
		}

		private void Render() {
			SuspendLayout();
			_content.SuspendLayout();
			foreach (Control c in _content.Controls.Cast<Control>().ToList()) { _content.Controls.Remove(c); c.Dispose(); }
			var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0), BackColor = FlyoutColors.Back };
			if (_view == View.Home) RenderHome(flow);
			else if (_view == View.Search) RenderSearch(flow);
			else RenderPinList(flow);
			_content.Controls.Add(flow);
			_content.ResumeLayout(true);
			ResumeLayout(true);
			if (Visible) Position();
		}

		private void RenderHome(FlowLayoutPanel flow) {
			// Desktop tiles, 3 per row, plus "new desktop"
			// (an auto sized FlowLayoutPanel only wraps when it has a maximum width)
			var tiles = new FlowLayoutPanel { Width = Inner, MaximumSize = new Size(Inner, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, S(12)), Padding = new Padding(0), BackColor = FlyoutColors.Back };
			var gap = S(8);
			var tileWidth = (Inner - gap * 2) / 3;
			var ext = App.Instance.VDAPIExtended;
			for (var i = 0; i <= _names.Count; i++) {
				var isAdd = i == _names.Count;
				if (isAdd && ext == null) break;
				var index = i;
				var count = isAdd ? 0 : _windows.Count(w => w.Desktop == index);
				var tile = new FlyoutTile(_scale) {
					Size = new Size(tileWidth, S(68)), Margin = new Padding(0, 0, (i % 3 == 2) ? 0 : gap, gap),
					Number = isAdd ? null : (i + 1).ToString(), Title = isAdd ? "New desktop" : _names[i],
					Subtitle = isAdd ? "" : (count == 1 ? "1 window" : count + " windows"),
					IsCurrent = !isAdd && i == _current, IsAdd = isAdd
				};
				if (_movePicker) {
					tile.Enabled = isAdd || i != (CurrentWindow?.Desktop ?? -1);
					_toolTip.SetToolTip(tile, isAdd ? "Move the window to a new desktop" : "Move the window to " + _names[i]);
					tile.Click += (s, e) => CloseThen(() => {
						if (isAdd) App.Instance.MoveWindowToNewDesktop(_window);
						else App.Instance.MoveWindowToDesktop(_window, index);
					});
				} else if (isAdd) {
					_toolTip.SetToolTip(tile, "New desktop (Ctrl + Win + D)");
					tile.Click += (s, e) => CloseThen(App.Instance.CreateDesktopAndSwitch);
				} else {
					var key = App.Instance.GetHotKeyForAction("Desktop" + (i + 1));
					_toolTip.SetToolTip(tile, "Switch to " + _names[i] + (key != null ? " (" + key + ")" : "") + "\nRight-click to rename or close");
					tile.MouseUp += (s, e) => {
						if (e.Button == MouseButtons.Left) CloseThen(() => App.Instance.SwitchToDesktop(index));
						else if (e.Button == MouseButtons.Right) ShowTileMenu(tile, index);
					};
				}
				tiles.Controls.Add(tile);
			}
			flow.Controls.Add(tiles);
			if (ext == null) return;

			// Current window
			var window = CurrentWindow;
			flow.Controls.Add(SectionLabel(_movePicker ? "Move the window to... (pick a desktop above)" : "Current window"));
			if (window == null) {
				flow.Controls.Add(new Label { Text = "Switch to a window first, then open this panel", AutoSize = true, ForeColor = FlyoutColors.TextMuted, Margin = new Padding(0, 0, 0, S(12)) });
			} else {
				var row = new FlyoutRow(_scale) { Width = Inner, Height = S(36), Icon = Util.MenuIcons.ForWindow(window.Hwnd), Title = window.Title, Margin = new Padding(0, 0, 0, S(6)) };
				_toolTip.SetToolTip(row, "Bring this window to the front");
				row.RowClick += () => CloseThen(() => Util.OS.ActivateWindow(window.Hwnd));
				flow.Controls.Add(row);

				var actions = new FlowLayoutPanel { Width = Inner, MaximumSize = new Size(Inner, 0), AutoSize = true, Margin = new Padding(0, 0, 0, S(12)), Padding = new Padding(0), BackColor = FlyoutColors.Back };
				var aGap = S(6);
				var aWidth = (Inner - aGap * 4) / 5;
				var defs = new[] {
					new ActionDef("", "Move", _movePicker, "Move to another desktop (pick a desktop above)", "MoveWindowForward", () => { _movePicker = !_movePicker; Render(); }),
					new ActionDef("", "New desk", false, "Move to a new desktop", "MoveWindowToNewDesktop", () => CloseThen(() => App.Instance.MoveWindowToNewDesktop(window.Hwnd))),
					new ActionDef("", window.Pinned ? "Pinned" : "Pin", window.Pinned, window.Pinned ? "Pinned to all desktops (click to unpin)" : "Pin window to all desktops", "TogglePinWindow", () => { App.Instance.TogglePinWindow(window.Hwnd); Refresh(); }),
					new ActionDef("", window.AppPinned ? "App pinned" : "Pin app", window.AppPinned, (window.AppPinned ? window.AppName + " is pinned to all desktops (click to unpin)" : "Pin " + window.AppName + " to all desktops (all its windows)"), "TogglePinApp", () => { App.Instance.TogglePinApp(window.Hwnd); Refresh(); }),
					new ActionDef("", "Bring here", false, "Bring all " + window.AppName + " windows to this desktop", "GatherAppWindows", () => CloseThen(() => App.Instance.GatherAppWindows(window.Hwnd))),
				};
				for (var i = 0; i < defs.Length; i++) {
					var d = defs[i];
					var button = new FlyoutButton(_scale, d.Glyph, d.Label) { Size = new Size(aWidth, S(58)), Margin = new Padding(0, 0, i == defs.Length - 1 ? 0 : aGap, 0), Active = d.Active, Card = true };
					var key = App.Instance.GetHotKeyForAction(d.Action);
					_toolTip.SetToolTip(button, d.Tip + (key != null && d.Action != "MoveWindowForward" ? " (" + key + ")" : ""));
					button.Click += (s, e) => d.Run();
					actions.Controls.Add(button);
				}
				flow.Controls.Add(actions);
			}

			// Windows on all desktops (max 4 here, the rest in the pin list)
			var pinned = _windows.Where(w => w.Pinned).ToList();
			var pinnedApps = _windows.Where(w => w.AppPinned && !w.Pinned).GroupBy(w => w.Process).Select(g => g.First()).ToList();
			flow.Controls.Add(SectionLabel("Pinned to all desktops"));
			var shown = 0;
			foreach (var w in pinned) {
				if (shown++ >= 4) break;
				var entry = w;
				flow.Controls.Add(PinnedRow(Util.MenuIcons.ForWindow(entry.Hwnd), entry.Title, "Unpin", () => { App.Instance.VDAPIExtended.SetWindowPinned(entry.Hwnd, false); Refresh(); }));
			}
			foreach (var w in pinnedApps) {
				if (shown++ >= 4) break;
				var entry = w;
				flow.Controls.Add(PinnedRow(Util.MenuIcons.ForWindow(entry.Hwnd), "All " + entry.AppName + " windows", "Unpin app", () => { App.Instance.VDAPIExtended.SetAppPinned(entry.Hwnd, false); Refresh(); }));
			}
			var total = pinned.Count + pinnedApps.Count;
			if (total == 0) flow.Controls.Add(new Label { Text = "Nothing pinned yet", AutoSize = true, ForeColor = FlyoutColors.TextMuted, Margin = new Padding(S(2), 0, 0, S(4)) });
			var link = new FlyoutRow(_scale) { Width = Inner, Height = S(32), Title = total > 4 ? $"+ {total - 4} more, pin other windows..." : "Pin windows...", TitleColor = FlyoutColors.Accent, Margin = new Padding(0) };
			link.RowClick += () => { _view = View.PinList; Render(); };
			flow.Controls.Add(link);
		}

		private IEnumerable<App.WindowEntry> SearchResults() {
			var words = (_search?.Text ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			return _windows.Where(w => words.All(word => (w.Title + " " + w.AppName + " " + DesktopName(w.Desktop)).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0));
		}

		private void RenderSearch(FlowLayoutPanel flow) {
			var results = SearchResults().ToList();
			if (results.Count == 0) {
				flow.Controls.Add(new Label { Text = "No window matches", AutoSize = true, ForeColor = FlyoutColors.TextMuted, Margin = new Padding(S(2), 0, 0, S(8)) });
				return;
			}
			var list = ListContainer(results.Count);
			foreach (var w in results) {
				var entry = w;
				var row = new FlyoutRow(_scale) { Width = list.ClientSize.Width, Height = S(40), Icon = Util.MenuIcons.ForWindow(entry.Hwnd), Title = entry.Title, Subtitle = entry.Pinned || entry.AppPinned ? "All desktops" : DesktopName(entry.Desktop), Margin = new Padding(0) };
				row.RowClick += () => CloseThen(() => App.Instance.FocusWindow(entry.Hwnd));
				list.Controls.Add(row);
			}
			flow.Controls.Add(list);
			flow.Controls.Add(new Label { Text = "Enter opens the first one", AutoSize = true, ForeColor = FlyoutColors.TextMuted, Margin = new Padding(S(2), S(6), 0, 0) });
		}

		private void RenderPinList(FlowLayoutPanel flow) {
			var back = new FlyoutRow(_scale) { Width = Inner, Height = S(32), Title = "Back", Icon = Util.MenuIcons.Glyph("", FlyoutColors.Text, S(16)), Margin = new Padding(0, 0, 0, S(4)) };
			back.RowClick += () => { _view = View.Home; Render(); };
			flow.Controls.Add(back);
			flow.Controls.Add(SectionLabel("Pin windows to all desktops"));
			var ordered = _windows.OrderBy(w => w.Pinned ? -2 : w.Desktop == _current ? -1 : w.Desktop < 0 ? int.MaxValue : w.Desktop).ToList();
			var list = ListContainer(ordered.Count);
			foreach (var w in ordered) {
				var entry = w;
				var row = new FlyoutRow(_scale) {
					Width = list.ClientSize.Width, Height = S(40), Icon = Util.MenuIcons.ForWindow(entry.Hwnd), Title = entry.Title,
					Subtitle = entry.Pinned ? "" : entry.AppPinned ? "App pinned" : DesktopName(entry.Desktop),
					TrailingGlyph = entry.Pinned ? "" : "", TrailingActive = entry.Pinned, Margin = new Padding(0)
				};
				_toolTip.SetToolTip(row, entry.Pinned ? "Pinned to all desktops, click to unpin" : "Click to pin to all desktops");
				Action toggle = () => {
					try { App.Instance.VDAPIExtended.SetWindowPinned(entry.Hwnd, !entry.Pinned); } catch (Exception e) { Util.Logging.WriteLine("TrayFlyout: Error: pin: " + e.Message); }
					Refresh();
				};
				row.RowClick += toggle;
				row.TrailingClick += toggle;
				list.Controls.Add(row);
			}
			flow.Controls.Add(list);
		}

		// A list of rows which scrolls when it is long
		private Panel ListContainer(int rows) {
			var rowHeight = S(40);
			var maxRows = 8;
			var list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = Inner, Margin = new Padding(0), Padding = new Padding(0), BackColor = FlyoutColors.Back };
			if (rows > maxRows) {
				list.AutoScroll = true;
				list.Height = rowHeight * maxRows;
				list.HandleCreated += (s, e) => { if (FlyoutColors.IsDark) SetWindowTheme(list.Handle, "DarkMode_Explorer", null); };
				list.Width = Inner;
				// Rows leave room for the scroll bar
				list.ClientSize = new Size(Inner - SystemInformation.VerticalScrollBarWidth, list.Height);
				list.Width = Inner;
			} else {
				list.AutoSize = true;
				list.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			}
			return list;
		}

		private Control PinnedRow(Image icon, string title, string tip, Action unpin) {
			var row = new FlyoutRow(_scale) { Width = Inner, Height = S(36), Icon = icon, Title = title, TrailingGlyph = "", Margin = new Padding(0) };
			_toolTip.SetToolTip(row, tip);
			row.TrailingClick += () => {
				try { unpin(); } catch (Exception e) { Util.Logging.WriteLine("TrayFlyout: Error: unpin: " + e.Message); }
			};
			return row;
		}

		private Label SectionLabel(string text) {
			return new Label { Text = text, AutoSize = true, ForeColor = FlyoutColors.TextSecondary, Font = new Font("Segoe UI Semibold", 8.5f * _scale, GraphicsUnit.Point), Margin = new Padding(S(2), 0, 0, S(6)) };
		}

		private new void Refresh() {
			LoadData();
			Render();
		}

		private class ActionDef {
			public string Glyph, Label, Tip, Action; public bool Active; public Action Run;
			public ActionDef(string glyph, string label, bool active, string tip, string action, Action run) { Glyph = glyph; Label = label; Active = active; Tip = tip; Action = action; Run = run; }
		}

		#endregion

		#region Menus of the panel

		private void ShowTileMenu(Control tile, int index) {
			var menu = NewMenu();
			var rename = new ToolStripMenuItem("Rename...") { Image = Util.MenuIcons.Icon(Util.MenuIcons.Rename) };
			rename.Click += (s, e) => CloseThen(() => {
				App.Instance.SwitchToDesktop(index);
				App.Instance.RenameCurrentDesktop();
			});
			menu.Items.Add(rename);
			if (CurrentWindow != null && CurrentWindow.Desktop != index) {
				var move = new ToolStripMenuItem("Move the current window here") { Image = Util.MenuIcons.Icon(Util.MenuIcons.Move) };
				move.Click += (s, e) => CloseThen(() => App.Instance.MoveWindowToDesktop(_window, index));
				menu.Items.Add(move);
			}
			var close = new ToolStripMenuItem("Close desktop") { Image = Util.MenuIcons.Icon(Util.MenuIcons.Close), Enabled = _names.Count > 1 };
			close.Click += (s, e) => CloseThen(() => App.Instance.VDAPIExtended?.RemoveDesktop(index));
			menu.Items.Add(close);
			ShowMenu(menu, tile, new Point(0, tile.Height));
		}

		private void ShowMoreMenu(Control anchor) {
			var menu = NewMenu();
			foreach (var item in App.Instance.BuildMoreMenuItems(CloseThen)) menu.Items.Add(item);
			ShowMenu(menu, anchor, new Point(anchor.Width - menu.GetPreferredSize(Size.Empty).Width, anchor.Height));
		}

		private ContextMenuStrip NewMenu() {
			var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.ManagerRenderMode, ShowCheckMargin = false, ShowImageMargin = true };
			var size = S(16);
			menu.ImageScalingSize = new Size(size, size);
			return menu;
		}

		private void ShowMenu(ContextMenuStrip menu, Control anchor, Point at) {
			_keepOpen = true;
			menu.Closed += (s, e) => {
				_keepOpen = false;
				BeginInvoke((Action)menu.Dispose);
				// Clicking outside the menu and the panel should close the panel too
				if (!_closing && !ContainsFocus && Form.ActiveForm != this) CloseFlyout();
			};
			menu.Show(anchor, at);
		}

		#endregion

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);
	}

	#region Colors and controls of the panel

	internal static class FlyoutColors {
		public static bool IsDark { get { return Util.ModernMenuRenderer.IsDark; } }
		public static Color Back { get { return IsDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
		public static Color Card { get { return IsDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(251, 251, 251); } }
		public static Color CardHover { get { return IsDark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(240, 240, 240); } }
		public static Color CardBorder { get { return IsDark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(229, 229, 229); } }
		public static Color Hover { get { return IsDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(233, 233, 233); } }
		public static Color Text { get { return IsDark ? Color.White : Color.FromArgb(27, 27, 27); } }
		public static Color TextSecondary { get { return IsDark ? Color.FromArgb(200, 200, 200) : Color.FromArgb(96, 96, 96); } }
		public static Color TextMuted { get { return IsDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(130, 130, 130); } }
		public static Color Accent { get { return IsDark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 95, 184); } }
		public static Color AccentBack { get { return IsDark ? Color.FromArgb(30, 58, 74) : Color.FromArgb(221, 236, 249); } }
		public static Color Border { get { return IsDark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(214, 214, 214); } }
		public static Color Divider { get { return IsDark ? Color.FromArgb(52, 52, 52) : Color.FromArgb(225, 225, 225); } }

		public static GraphicsPath Rounded(Rectangle r, int radius) {
			var path = new GraphicsPath();
			var d = Math.Max(1, radius * 2);
			path.AddArc(r.X, r.Y, d, d, 180, 90);
			path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
			path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
			path.CloseFigure();
			return path;
		}
	}

	// Base for the owner drawn controls: double buffered, hover tracking, hand cursor
	internal class FlyoutControl : Control {
		protected readonly float Scale;
		protected bool Hovered;
		public FlyoutControl(float scale) {
			Scale = scale;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
			BackColor = FlyoutColors.Back;
			Cursor = Cursors.Hand;
		}
		protected int S(int px) { return (int)Math.Round(px * Scale); }
		protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { Hovered = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
		protected void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags) {
			TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
		}
	}

	// A desktop tile: number, name, window count; the current desktop is highlighted
	internal class FlyoutTile : FlyoutControl {
		public string Number, Title, Subtitle;
		public bool IsCurrent, IsAdd;
		public FlyoutTile(float scale) : base(scale) { }
		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(FlyoutColors.Back);
			var rect = new Rectangle(0, 0, Width - 1, Height - 1);
			var fill = IsCurrent ? FlyoutColors.AccentBack : (Hovered && Enabled ? FlyoutColors.CardHover : FlyoutColors.Card);
			using (var path = FlyoutColors.Rounded(rect, S(6)))
			using (var brush = new SolidBrush(fill))
			using (var pen = new Pen(IsCurrent ? FlyoutColors.Accent : FlyoutColors.CardBorder, IsCurrent ? S(2) : 1) { DashStyle = IsAdd ? DashStyle.Dash : DashStyle.Solid }) {
				g.FillPath(brush, path);
				g.DrawPath(pen, path);
			}
			var text = Enabled ? (IsCurrent ? FlyoutColors.Accent : FlyoutColors.Text) : FlyoutColors.TextMuted;
			var pad = S(8);
			if (IsAdd) {
				var plus = Util.MenuIcons.Glyph("", Enabled ? FlyoutColors.Text : FlyoutColors.TextMuted, S(18));
				g.DrawImage(plus, (Width - plus.Width) / 2, S(12));
				using (var font = new Font(Font.FontFamily, 8f * Scale, GraphicsUnit.Point)) DrawText(g, Title, font, FlyoutColors.TextSecondary, new Rectangle(pad, S(38), Width - pad * 2, S(20)), TextFormatFlags.HorizontalCenter);
				return;
			}
			using (var big = new Font("Segoe UI Semibold", 13f * Scale, GraphicsUnit.Point))
				DrawText(g, Number, big, text, new Rectangle(pad, S(4), Width - pad * 2, S(28)), TextFormatFlags.Left);
			using (var font = new Font(Font.FontFamily, 8.5f * Scale, GraphicsUnit.Point))
				DrawText(g, Title, font, text, new Rectangle(pad, S(30), Width - pad * 2, S(18)), TextFormatFlags.Left);
			using (var small = new Font(Font.FontFamily, 7.5f * Scale, GraphicsUnit.Point))
				DrawText(g, Subtitle, small, FlyoutColors.TextMuted, new Rectangle(pad, S(47), Width - pad * 2, S(16)), TextFormatFlags.Left);
		}
	}

	// An icon button (optionally with a label under the icon, optionally on a card, optionally "active")
	internal class FlyoutButton : FlyoutControl {
		public string Glyph, Label;
		public bool Active, Card;
		public FlyoutButton(float scale, string glyph, string label) : base(scale) { Glyph = glyph; Label = label; }
		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(FlyoutColors.Back);
			var rect = new Rectangle(0, 0, Width - 1, Height - 1);
			Color? fill = Active ? FlyoutColors.AccentBack : (Card ? (Hovered ? FlyoutColors.CardHover : FlyoutColors.Card) : (Hovered ? FlyoutColors.Hover : (Color?)null));
			if (fill != null) {
				using (var path = FlyoutColors.Rounded(rect, S(6)))
				using (var brush = new SolidBrush(fill.Value)) g.FillPath(brush, path);
			}
			var color = Active ? FlyoutColors.Accent : FlyoutColors.Text;
			var iconSize = S(Label == null ? 16 : 18);
			var icon = Util.MenuIcons.Glyph(Glyph, color, iconSize);
			var iconY = Label == null ? (Height - iconSize) / 2 : S(10);
			g.DrawImage(icon, (Width - iconSize) / 2, iconY);
			if (Label != null) {
				using (var font = new Font(Font.FontFamily, 7.5f * Scale, GraphicsUnit.Point))
					DrawText(g, Label, font, Active ? FlyoutColors.Accent : FlyoutColors.TextSecondary, new Rectangle(S(2), iconY + iconSize + S(6), Width - S(4), S(16)), TextFormatFlags.HorizontalCenter);
			}
		}
	}

	// A list row: icon, title, optional subtitle on the right, optional trailing button (e.g. unpin)
	internal class FlyoutRow : FlyoutControl {
		public Image Icon;
		public string Title, Subtitle, TrailingGlyph;
		public bool TrailingActive;
		public Color? TitleColor;
		public event Action RowClick;
		public event Action TrailingClick;
		private bool _trailingHovered;
		public FlyoutRow(float scale) : base(scale) { }
		private Rectangle TrailingRect { get { var size = S(28); return new Rectangle(Width - size - S(4), (Height - size) / 2, size, size); } }
		protected override void OnMouseMove(MouseEventArgs e) {
			var over = TrailingGlyph != null && TrailingRect.Contains(e.Location);
			if (over != _trailingHovered) { _trailingHovered = over; Invalidate(); }
			base.OnMouseMove(e);
		}
		protected override void OnMouseLeave(EventArgs e) { _trailingHovered = false; base.OnMouseLeave(e); }
		protected override void OnMouseUp(MouseEventArgs e) {
			base.OnMouseUp(e);
			if (e.Button != MouseButtons.Left) return;
			if (TrailingGlyph != null && TrailingRect.Contains(e.Location)) TrailingClick?.Invoke();
			else RowClick?.Invoke();
		}
		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.Clear(FlyoutColors.Back);
			if (Hovered) {
				using (var path = FlyoutColors.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), S(6)))
				using (var brush = new SolidBrush(FlyoutColors.Hover)) g.FillPath(brush, path);
			}
			var x = S(8);
			if (Icon != null) {
				var size = S(20);
				g.DrawImage(Icon, new Rectangle(x, (Height - size) / 2, size, size));
				x += size + S(10);
			}
			var right = Width - S(8) - (TrailingGlyph != null ? S(32) : 0);
			var subtitleWidth = 0;
			if (!string.IsNullOrEmpty(Subtitle)) {
				using (var small = new Font(Font.FontFamily, 8f * Scale, GraphicsUnit.Point)) {
					subtitleWidth = Math.Min(S(110), TextRenderer.MeasureText(Subtitle, small).Width);
					DrawText(g, Subtitle, small, FlyoutColors.TextMuted, new Rectangle(right - subtitleWidth, 0, subtitleWidth, Height), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
				}
			}
			DrawText(g, Title ?? "", Font, TitleColor ?? FlyoutColors.Text, new Rectangle(x, 0, right - x - subtitleWidth - S(8), Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
			if (TrailingGlyph != null) {
				var r = TrailingRect;
				if (_trailingHovered || TrailingActive) {
					using (var path = FlyoutColors.Rounded(r, S(4)))
					using (var brush = new SolidBrush(TrailingActive ? FlyoutColors.AccentBack : FlyoutColors.CardHover)) g.FillPath(brush, path);
				}
				var glyph = Util.MenuIcons.Glyph(TrailingGlyph, TrailingActive ? FlyoutColors.Accent : FlyoutColors.TextSecondary, S(14));
				g.DrawImage(glyph, r.X + (r.Width - glyph.Width) / 2, r.Y + (r.Height - glyph.Height) / 2);
			}
		}
	}

	// A panel with a rounded fill (the search box)
	internal class RoundedPanel : Panel {
		public Color Fill;
		public int Radius;
		public RoundedPanel(float scale) {
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = FlyoutColors.Back;
		}
		protected override void OnPaint(PaintEventArgs e) {
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			e.Graphics.Clear(FlyoutColors.Back);
			using (var path = FlyoutColors.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
			using (var brush = new SolidBrush(Fill))
			using (var pen = new Pen(FlyoutColors.CardBorder)) {
				e.Graphics.FillPath(brush, path);
				e.Graphics.DrawPath(pen, path);
			}
		}
	}

	#endregion
}
