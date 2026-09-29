using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Forms {

	/// <summary>Colors and helpers for the Windows 11 style windows (dark/light follows the system theme).</summary>
	internal static class Theme {
		public static bool IsDark { get { return Util.ModernMenuRenderer.IsDark; } }
		public static Color WindowBack { get { return IsDark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
		public static Color Card { get { return IsDark ? Color.FromArgb(43, 43, 43) : Color.White; } }
		public static Color CardHover { get { return IsDark ? Color.FromArgb(50, 50, 50) : Color.FromArgb(250, 250, 250); } }
		public static Color CardBorder { get { return IsDark ? Color.FromArgb(29, 29, 29) : Color.FromArgb(229, 229, 229); } }
		public static Color Control { get { return IsDark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(251, 251, 251); } }
		public static Color ControlHover { get { return IsDark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(246, 246, 246); } }
		public static Color ControlBorder { get { return IsDark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(212, 212, 212); } }
		public static Color NavHover { get { return IsDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(234, 234, 234); } }
		public static Color NavSelected { get { return IsDark ? Color.FromArgb(45, 45, 45) : Color.FromArgb(234, 234, 234); } }
		public static Color Text { get { return IsDark ? Color.White : Color.FromArgb(27, 27, 27); } }
		public static Color TextSecondary { get { return IsDark ? Color.FromArgb(206, 206, 206) : Color.FromArgb(96, 96, 96); } }
		public static Color TextMuted { get { return IsDark ? Color.FromArgb(150, 150, 150) : Color.FromArgb(130, 130, 130); } }
		public static Color TextDisabled { get { return IsDark ? Color.FromArgb(105, 105, 105) : Color.FromArgb(170, 170, 170); } }
		public static Color Accent { get { return IsDark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 95, 184); } }
		public static Color OnAccent { get { return IsDark ? Color.Black : Color.White; } }
		public static Color Danger { get { return IsDark ? Color.FromArgb(255, 153, 164) : Color.FromArgb(196, 43, 28); } }
		public static Color Success { get { return IsDark ? Color.FromArgb(108, 203, 95) : Color.FromArgb(15, 123, 15); } }
		public static Color KeyCap { get { return IsDark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(240, 240, 240); } }
		public static Color KeyCapBorder { get { return IsDark ? Color.FromArgb(80, 80, 80) : Color.FromArgb(210, 210, 210); } }

		public static GraphicsPath Rounded(Rectangle r, int radius) {
			var path = new GraphicsPath();
			var d = Math.Max(2, radius * 2);
			path.AddArc(r.X, r.Y, d, d, 180, 90);
			path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
			path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
			path.CloseFigure();
			return path;
		}

		public static void FillRounded(Graphics g, Rectangle r, int radius, Color fill, Color? border = null) {
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var path = Rounded(r, radius)) {
				using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
				if (border != null) using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
			}
		}

		public static Image Glyph(string glyph, Color color, int size) {
			return Util.MenuIcons.Glyph(glyph, color, size);
		}

		// Dark title bar and scroll bars for a window in dark mode
		public static void ApplyWindowTheme(IntPtr hwnd) {
			try {
				int dark = IsDark ? 1 : 0;
				DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
			} catch (Exception) { }
		}

		public static void ApplyScrollBarTheme(IntPtr hwnd) {
			try { SetWindowTheme(hwnd, IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch (Exception) { }
		}

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);
	}

	/// <summary>Base for the owner drawn controls: DPI scale, double buffering, hover tracking.</summary>
	internal class ModernControl : Control {
		protected readonly float Scale;
		protected bool Hovered;

		public ModernControl(float scale) {
			Scale = scale;
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = Theme.WindowBack;
		}

		protected int S(float px) { return (int)Math.Round(px * Scale); }
		protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
		protected override void OnMouseLeave(EventArgs e) { Hovered = false; Invalidate(); base.OnMouseLeave(e); }
		protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

		protected void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags) {
			TextRenderer.DrawText(g, text ?? "", font, bounds, color, flags | TextFormatFlags.NoPrefix);
		}
	}

	/// <summary>A Windows 11 toggle switch.</summary>
	internal class ModernToggle : ModernControl {
		private bool _checked;
		public event EventHandler CheckedChanged;

		public ModernToggle(float scale) : base(scale) {
			Size = new Size(S(40), S(20));
			Cursor = Cursors.Hand;
			SetStyle(ControlStyles.Selectable, true);
			TabStop = true;
		}

		public bool Checked {
			get { return _checked; }
			set { if (_checked == value) return; _checked = value; Invalidate(); }
		}

		public void Toggle() {
			if (!Enabled) return;
			Checked = !Checked;
			CheckedChanged?.Invoke(this, EventArgs.Empty);
		}

		protected override void OnClick(EventArgs e) { Toggle(); base.OnClick(e); }
		protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) Toggle(); base.OnKeyDown(e); }
		protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
		protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? Theme.Card);
			g.SmoothingMode = SmoothingMode.AntiAlias;
			var track = new Rectangle(0, 0, Width - 1, Height - 1);
			var radius = track.Height / 2;
			var on = _checked;
			var enabled = Enabled;
			if (on) {
				Theme.FillRounded(g, track, radius, enabled ? Theme.Accent : Theme.TextDisabled);
			} else {
				using (var path = Theme.Rounded(track, radius))
				using (var pen = new Pen(enabled ? Theme.TextSecondary : Theme.TextDisabled, Math.Max(1, S(1)))) g.DrawPath(pen, path);
			}
			var knob = S(Hovered && enabled ? 14 : 12);
			var x = on ? Width - knob - (Height - knob) / 2 - 1 : (Height - knob) / 2;
			using (var brush = new SolidBrush(on ? Theme.OnAccent : (enabled ? Theme.TextSecondary : Theme.TextDisabled)))
				g.FillEllipse(brush, x, (Height - knob) / 2f, knob, knob);
			if (Focused && ShowFocusCues) {
				using (var path = Theme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), radius))
				using (var pen = new Pen(Theme.Text) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, path);
			}
		}
	}

	/// <summary>A button: standard (subtle) or accent style, optional glyph.</summary>
	internal class ModernButton : ModernControl {
		public bool AccentStyle;
		public string Glyph;

		public ModernButton(float scale, string text, string glyph = null) : base(scale) {
			Text = text;
			Glyph = glyph;
			Cursor = Cursors.Hand;
			SetStyle(ControlStyles.Selectable, true);
			TabStop = true;
			Height = S(32);
			Width = MeasureWidth();
		}

		private int MeasureWidth() {
			using (var font = new Font("Segoe UI", 9.5f * Scale, GraphicsUnit.Point))
				return TextRenderer.MeasureText(Text ?? "", font).Width + S(28) + (Glyph != null ? S(24) : 0);
		}

		protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? Theme.WindowBack);
			var r = new Rectangle(0, 0, Width - 1, Height - 1);
			var fill = AccentStyle ? Theme.Accent : (Hovered ? Theme.ControlHover : Theme.Control);
			Theme.FillRounded(g, r, S(4), fill, AccentStyle ? (Color?)null : Theme.ControlBorder);
			var color = AccentStyle ? Theme.OnAccent : (Enabled ? Theme.Text : Theme.TextDisabled);
			var x = S(14);
			if (Glyph != null) {
				var icon = Theme.Glyph(Glyph, color, S(16));
				g.DrawImage(icon, x, (Height - icon.Height) / 2);
				x += S(24);
			}
			using (var font = new Font("Segoe UI", 9.5f * Scale, GraphicsUnit.Point))
				DrawText(g, Text, font, color, new Rectangle(x, 0, Width - x - S(10), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
		}
	}

	/// <summary>A drop-down: shows the current choice, opens a menu of the options.</summary>
	internal class ModernDropDown : ModernControl {
		private readonly List<KeyValuePair<string, string>> _options; // value, label
		public string Value;
		public event Action<string> ValueChanged;

		public ModernDropDown(float scale, IEnumerable<KeyValuePair<string, string>> options, string value) : base(scale) {
			_options = options.ToList();
			Value = value;
			Cursor = Cursors.Hand;
			SetStyle(ControlStyles.Selectable, true);
			TabStop = true;
			Height = S(32);
			using (var font = new Font("Segoe UI", 9.5f * Scale, GraphicsUnit.Point))
				Width = _options.Select(o => TextRenderer.MeasureText(o.Value, font).Width).DefaultIfEmpty(S(80)).Max() + S(52);
		}

		private string Label {
			get { var o = _options.FirstOrDefault(x => x.Key == Value); return o.Value ?? Value ?? ""; }
		}

		protected override void OnClick(EventArgs e) {
			base.OnClick(e);
			if (!Enabled) return;
			var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.ManagerRenderMode, ShowImageMargin = false, ShowCheckMargin = true, MinimumSize = new Size(Width, 0) };
			foreach (var option in _options) {
				var value = option.Key;
				var item = new ToolStripMenuItem(option.Value) { Checked = value == Value };
				item.Click += (s, a) => { Value = value; Invalidate(); ValueChanged?.Invoke(value); };
				menu.Items.Add(item);
			}
			menu.Closed += (s, a) => BeginInvoke((Action)menu.Dispose);
			menu.Show(this, new Point(0, Height));
		}

		protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? Theme.Card);
			var r = new Rectangle(0, 0, Width - 1, Height - 1);
			Theme.FillRounded(g, r, S(4), Hovered && Enabled ? Theme.ControlHover : Theme.Control, Theme.ControlBorder);
			var color = Enabled ? Theme.Text : Theme.TextDisabled;
			using (var font = new Font("Segoe UI", 9.5f * Scale, GraphicsUnit.Point))
				DrawText(g, Label, font, color, new Rectangle(S(12), 0, Width - S(40), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
			var chevron = Theme.Glyph("", Enabled ? Theme.TextSecondary : Theme.TextDisabled, S(12));
			g.DrawImage(chevron, Width - S(26), (Height - chevron.Height) / 2);
		}
	}

	/// <summary>Draws key caps ("Ctrl" "Alt" "1..9"); used for showing and recording shortcuts.</summary>
	internal static class KeyCaps {
		public static List<string> Split(string hotkey, bool modifiersOnly) {
			var keys = (hotkey ?? "").Split('+').Select(k => Pretty(k.Trim())).Where(k => k != "").ToList();
			if (modifiersOnly && keys.Count > 0) keys.Add("1–9");
			return keys;
		}

		private static string Pretty(string key) {
			switch (key.ToLowerInvariant()) {
				case "control": return "Ctrl";
				case "tilde": case "oemtilde": return "`";
				case "left": return "←";
				case "right": return "→";
				case "up": return "↑";
				case "down": return "↓";
				case "minus": return "-";
				case "plus": return "+";
				default:
					if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) return key.Substring(1);
					return key;
			}
		}

		// Draws the caps right aligned in the bounds, returns their total width
		public static int Draw(Graphics g, List<string> keys, Rectangle bounds, float scale, bool accent, bool enabled) {
			Func<float, int> S = px => (int)Math.Round(px * scale);
			using (var font = new Font("Segoe UI Semibold", 9f * scale, GraphicsUnit.Point)) {
				var widths = keys.Select(k => Math.Max(S(28), TextRenderer.MeasureText(k, font).Width + S(14))).ToList();
				var total = widths.Sum() + S(6) * Math.Max(0, keys.Count - 1);
				var x = bounds.Right - total;
				var h = S(28);
				var y = bounds.Y + (bounds.Height - h) / 2;
				for (var i = 0; i < keys.Count; i++) {
					var r = new Rectangle(x, y, widths[i], h);
					var fill = accent ? Theme.Accent : Theme.KeyCap;
					Theme.FillRounded(g, r, S(4), enabled ? fill : Theme.Control, accent ? (Color?)null : Theme.KeyCapBorder);
					TextRenderer.DrawText(g, keys[i], font, r, accent ? Theme.OnAccent : (enabled ? Theme.Text : Theme.TextDisabled), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
					x += widths[i] + S(6);
				}
				return total;
			}
		}
	}

	/// <summary>
	/// Shows a shortcut as key caps; click it (or focus + Enter) and press new keys to record. While recording, the
	/// app's own hotkeys are paused, so pressing an existing shortcut records it instead of running it.
	/// Backspace clears, Esc cancels.
	/// </summary>
	internal class ShortcutRecorder : ModernControl {
		public bool ModifiersOnly;
		public string Value = "";
		public bool Recording { get; private set; }
		public event Action<string> Recorded;
		private string _preview = null;

		[DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);

		public ShortcutRecorder(float scale) : base(scale) {
			Cursor = Cursors.Hand;
			SetStyle(ControlStyles.Selectable, true);
			TabStop = true;
			Height = S(36);
			Width = S(250);
		}

		protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); }

		protected override void OnGotFocus(EventArgs e) {
			base.OnGotFocus(e);
			Recording = true;
			_preview = null;
			App.Instance.SuspendHotKeys();
			Invalidate();
		}

		protected override void OnLostFocus(EventArgs e) {
			base.OnLostFocus(e);
			Recording = false;
			App.Instance.ResumeHotKeys();
			Invalidate();
		}

		protected override bool IsInputKey(Keys keyData) { return true; }

		protected override void OnKeyDown(KeyEventArgs e) {
			e.Handled = true;
			e.SuppressKeyPress = true;
			if (!Recording) return;
			var key = e.KeyCode;
			var mods = new List<string>();
			if (e.Control) mods.Add("Ctrl");
			if (e.Alt) mods.Add("Alt");
			if (e.Shift) mods.Add("Shift");
			if (GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) mods.Add("Win");
			if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) {
				_preview = string.Join(" + ", mods);
				Invalidate();
				return;
			}
			if (mods.Count == 0) {
				if (key == Keys.Escape) { Parent?.Focus(); return; }
				if (key == Keys.Back || key == Keys.Delete) { Commit(""); return; }
				if (key == Keys.Tab) { Parent?.SelectNextControl(this, true, true, true, true); return; }
				return;
			}
			if (ModifiersOnly) {
				var isNumber = (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9);
				if (!isNumber) return;
				Commit(string.Join(" + ", mods));
				return;
			}
			Commit(string.Join(" + ", mods) + " + " + KeyName(key));
		}

		private void Commit(string value) {
			Value = value;
			Recorded?.Invoke(value);
			Parent?.Focus(); // leaving re-registers the hotkeys
		}

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

		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? Theme.Card);
			var r = new Rectangle(0, 0, Width - 1, Height - 1);
			if (Recording || Hovered) Theme.FillRounded(g, r, S(4), Recording ? Theme.Control : Theme.CardHover, Recording ? Theme.Accent : (Color?)null);
			using (var font = new Font("Segoe UI", 9f * Scale, GraphicsUnit.Point)) {
				if (Recording) {
					var text = string.IsNullOrEmpty(_preview) ? "Press the keys (Esc cancels, Backspace clears)" : _preview + " + ...";
					DrawText(g, text, font, Theme.Accent, new Rectangle(S(10), 0, Width - S(20), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
					return;
				}
				var keys = KeyCaps.Split(Value, ModifiersOnly);
				if (keys.Count == 0) {
					DrawText(g, "Not set (click to set)", font, Theme.TextMuted, new Rectangle(S(10), 0, Width - S(20), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
					return;
				}
				KeyCaps.Draw(g, keys, new Rectangle(S(4), 0, Width - S(8), Height), Scale, false, Enabled);
			}
		}
	}

	/// <summary>A navigation entry of the settings window (glyph and text, accent bar when selected).</summary>
	internal class NavItem : ModernControl {
		public string Glyph;
		public bool Selected;

		public NavItem(float scale, string glyph, string text) : base(scale) {
			Glyph = glyph;
			Text = text;
			Height = S(40);
			Cursor = Cursors.Hand;
		}

		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.Clear(Theme.WindowBack);
			var r = new Rectangle(0, S(2), Width - 1, Height - S(5));
			if (Selected || Hovered) Theme.FillRounded(g, r, S(4), Selected ? Theme.NavSelected : Theme.NavHover);
			if (Selected) Theme.FillRounded(g, new Rectangle(0, r.Y + r.Height / 2 - S(8), S(3), S(16)), S(1), Theme.Accent);
			var icon = Theme.Glyph(Glyph, Theme.Text, S(16));
			g.DrawImage(icon, S(14), (Height - icon.Height) / 2);
			using (var font = new Font("Segoe UI", 10f * Scale, GraphicsUnit.Point))
				DrawText(g, Text, font, Theme.Text, new Rectangle(S(44), 0, Width - S(48), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
		}
	}

	/// <summary>
	/// A settings card: optional glyph, title and description on the left, a control on the right. Clicking the
	/// card toggles its toggle switch. Its height follows the description's line wrapping.
	/// </summary>
	internal class SettingsCard : ModernControl {
		public string Glyph, Title, Description;
		public Color? DescriptionColor;
		public Control Action;
		public int Indent;

		public SettingsCard(float scale, string glyph, string title, string description, Control action) : base(scale) {
			Glyph = glyph;
			Title = title;
			Description = description;
			Action = action;
			BackColor = Theme.Card;
			if (action != null) {
				action.BackColor = Theme.Card;
				Controls.Add(action);
				if (action is ModernToggle) Cursor = Cursors.Hand;
			}
		}

		private int TextLeft { get { return Glyph != null ? S(56) : S(18); } }
		private int TextWidth(int width) { return Math.Max(S(80), width - TextLeft - (Action != null ? Action.Width + S(32) : S(18))); }

		public int HeightFor(int width) {
			var h = S(18);
			using (var title = new Font("Segoe UI", 10f * Scale, GraphicsUnit.Point)) h += TextRenderer.MeasureText(Title ?? "", title).Height;
			if (!string.IsNullOrEmpty(Description)) {
				using (var desc = new Font("Segoe UI", 8.75f * Scale, GraphicsUnit.Point))
					h += TextRenderer.MeasureText(Description, desc, new Size(TextWidth(width), int.MaxValue), TextFormatFlags.WordBreak).Height + S(2);
			}
			h += S(18);
			return Math.Max(h, Action != null ? Action.Height + S(28) : S(56));
		}

		protected override void OnLayout(LayoutEventArgs e) {
			base.OnLayout(e);
			if (Action != null) Action.Location = new Point(Width - Action.Width - S(18), (Height - Action.Height) / 2);
		}

		protected override void OnClick(EventArgs e) {
			base.OnClick(e);
			var toggle = Action as ModernToggle;
			if (toggle != null && Enabled) toggle.Toggle();
		}

		protected override void OnPaint(PaintEventArgs e) {
			var g = e.Graphics;
			g.Clear(Parent?.BackColor ?? Theme.WindowBack);
			var r = new Rectangle(0, 0, Width - 1, Height - 1);
			Theme.FillRounded(g, r, S(6), Hovered && Action is ModernToggle && Enabled ? Theme.CardHover : Theme.Card, Theme.CardBorder);
			var textColor = Enabled ? Theme.Text : Theme.TextDisabled;
			if (Glyph != null) {
				var icon = Theme.Glyph(Glyph, textColor, S(20));
				g.DrawImage(icon, S(20), (Height - icon.Height) / 2);
			}
			var y = S(16);
			using (var title = new Font("Segoe UI", 10f * Scale, GraphicsUnit.Point)) {
				var th = TextRenderer.MeasureText(Title ?? "", title).Height;
				var hasDescription = !string.IsNullOrEmpty(Description);
				if (!hasDescription) y = (Height - th) / 2;
				DrawText(g, Title, title, textColor, new Rectangle(TextLeft, y, TextWidth(Width), th), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
				y += th;
			}
			if (!string.IsNullOrEmpty(Description)) {
				using (var desc = new Font("Segoe UI", 8.75f * Scale, GraphicsUnit.Point))
					DrawText(g, Description, desc, Enabled ? (DescriptionColor ?? Theme.TextSecondary) : Theme.TextDisabled, new Rectangle(TextLeft, y + S(2), TextWidth(Width), Height - y), TextFormatFlags.Left | TextFormatFlags.WordBreak);
			}
		}

		// Keep hover while the mouse is over the child control
		protected override void OnControlAdded(ControlEventArgs e) {
			base.OnControlAdded(e);
			e.Control.MouseEnter += (s, a) => { Hovered = true; Invalidate(); };
		}
	}

	/// <summary>
	/// A scrolling page: its <see cref="Items"/> are laid out top to bottom, full width (minus each item's margins).
	/// The items live in an inner panel sized to fit them; this (outer) panel scrolls it with the standard
	/// scroll bar and mouse wheel handling.
	/// </summary>
	internal class StackPanel : Panel {
		private readonly Panel _inner;
		private bool _laying = false;

		// The page padding (not Padding, which would change the scrolled area)
		public Padding ContentPadding;

		public Control.ControlCollection Items { get { return _inner.Controls; } }

		public StackPanel(float scale) {
			// (the inner panel must exist before AutoScroll is set: setting it lays out)
			_inner = new Panel { BackColor = Theme.WindowBack, Location = new Point(0, 0), Margin = new Padding(0) };
			BackColor = Theme.WindowBack;
			Controls.Add(_inner);
			AutoScroll = true;
		}

		protected override void OnHandleCreated(EventArgs e) {
			base.OnHandleCreated(e);
			Theme.ApplyScrollBarTheme(Handle);
		}

		protected override void OnLayout(LayoutEventArgs levent) {
			if (_laying) { base.OnLayout(levent); return; }
			_laying = true;
			try {
				LayoutItems();
				base.OnLayout(levent); // updates the scroll bar for the inner panel's size
				// The vertical scroll bar appearing (or disappearing) changes the available width: fit again,
				// otherwise a horizontal scroll bar shows up
				if (_inner != null && _inner.Width != ClientSize.Width) {
					LayoutItems();
					base.OnLayout(levent);
				}
			} finally {
				_laying = false;
			}
		}

		protected override void OnResize(EventArgs e) {
			base.OnResize(e);
			PerformLayout();
		}

		private void LayoutItems() {
			if (_inner == null) return;
			var width = ClientSize.Width; // excludes the vertical scroll bar when it is shown
			var inner = width - ContentPadding.Horizontal;
			var y = ContentPadding.Top;
			_inner.SuspendLayout();
			foreach (Control c in _inner.Controls) {
				if (!c.Visible) continue;
				y += c.Margin.Top;
				var w = inner - c.Margin.Left - c.Margin.Right;
				var card = c as SettingsCard;
				var label = c as Label;
				if (label != null) label.MaximumSize = new Size(w, 0);
				var h = card != null ? card.HeightFor(w) : (c.AutoSize ? c.GetPreferredSize(new Size(w, 0)).Height : c.Height);
				var fullWidth = card != null || c is StackRow;
				c.SetBounds(ContentPadding.Left + c.Margin.Left, y, fullWidth ? w : (c.AutoSize ? c.GetPreferredSize(new Size(w, 0)).Width : c.Width), h);
				y += h + c.Margin.Bottom;
			}
			_inner.ResumeLayout(false);
			_inner.Size = new Size(width, y + ContentPadding.Bottom);
		}
	}

	/// <summary>A row of controls (e.g. buttons) laid out left to right inside a StackPanel.</summary>
	internal class StackRow : Panel {
		private readonly int _gap;
		public StackRow(float scale, int height) {
			_gap = (int)Math.Round(8 * scale);
			Height = height;
			BackColor = Theme.WindowBack;
		}
		protected override void OnLayout(LayoutEventArgs levent) {
			var x = 0;
			foreach (Control c in Controls) {
				c.Location = new Point(x, (Height - c.Height) / 2);
				x += c.Width + _gap;
			}
		}
	}
}
