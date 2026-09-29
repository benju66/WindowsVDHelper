using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// Windows 11 style for the app's menus (tray menu, window menu): follows the dark/light system theme,
	/// rounded corners (DWM), roomier items, rounded hover highlight, accent colored checks.
	/// Installed once for the whole app via ToolStripManager.Renderer (see AppForm).
	/// </summary>
	public class ModernMenuRenderer : ToolStripRenderer {

		private class Palette {
			public Color Back, Hover, Text, TextDisabled, Separator, Border, Accent, CheckedBack;
		}

		private static readonly Palette Dark = new Palette {
			Back = Color.FromArgb(44, 44, 44), Hover = Color.FromArgb(61, 61, 61), Text = Color.White,
			TextDisabled = Color.FromArgb(125, 125, 125), Separator = Color.FromArgb(70, 70, 70),
			Border = Color.FromArgb(70, 70, 70), Accent = Color.FromArgb(96, 205, 255), CheckedBack = Color.FromArgb(55, 70, 80)
		};

		private static readonly Palette Light = new Palette {
			Back = Color.FromArgb(249, 249, 249), Hover = Color.FromArgb(234, 234, 234), Text = Color.FromArgb(27, 27, 27),
			TextDisabled = Color.FromArgb(160, 160, 160), Separator = Color.FromArgb(225, 225, 225),
			Border = Color.FromArgb(214, 214, 214), Accent = Color.FromArgb(0, 95, 184), CheckedBack = Color.FromArgb(221, 235, 247)
		};

		public static bool IsDark {
			get { return App.Instance == null || App.Instance.CurrentSystemThemeName != "light"; }
		}

		private static Palette Colors { get { return IsDark ? Dark : Light; } }

		public static Color TextColor { get { return Colors.Text; } }

		public static Color AccentColor { get { return Colors.Accent; } }

		private static int Scale(ToolStrip strip, int px) {
			var dpi = strip != null && strip.IsHandleCreated ? strip.DeviceDpi : (App.Instance != null ? App.Instance.TrayDpi : 96);
			return (int)Math.Round(px * dpi / 96.0);
		}

		protected override void Initialize(ToolStrip toolStrip) {
			base.Initialize(toolStrip);
			toolStrip.Font = SystemFonts.MenuFont;
			var dropDown = toolStrip as ToolStripDropDown;
			if (dropDown != null) {
				dropDown.Padding = new Padding(Scale(toolStrip, 4), Scale(toolStrip, 4), Scale(toolStrip, 4), Scale(toolStrip, 4));
				if (dropDown.IsHandleCreated) RoundCorners(dropDown.Handle);
				else dropDown.HandleCreated += (s, e) => RoundCorners(((Control)s).Handle);
			}
		}

		protected override void InitializeItem(ToolStripItem item) {
			base.InitializeItem(item);
			if (item is ToolStripMenuItem) item.Padding = new Padding(0, Scale(item.Owner, 5), 0, Scale(item.Owner, 5));
		}

		// Windows 11: rounded corners and a theme colored border for the popup (ignored on Windows 10)
		private static void RoundCorners(IntPtr hwnd) {
			try {
				int round = 2; // DWMWCP_ROUND
				DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
				int border = ColorTranslator.ToWin32(Colors.Border);
				DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR
			} catch (Exception) {
				// older Windows
			}
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
			using (var brush = new SolidBrush(Colors.Back)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
			// DWM draws the (rounded) border on Windows 11; on Windows 10 draw a plain one
			if (Environment.OSVersion.Version.Build >= 22000) return;
			using (var pen = new Pen(Colors.Border)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
		}

		protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {
			using (var brush = new SolidBrush(Colors.Back)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
			if (!e.Item.Selected || !e.Item.Enabled) return;
			var inset = Scale(e.ToolStrip, 4);
			var rect = new Rectangle(inset, 1, e.Item.Width - inset * 2, e.Item.Height - 2);
			FillRounded(e.Graphics, rect, Scale(e.ToolStrip, 4), Colors.Hover);
		}

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
			e.TextColor = e.Item.Enabled ? Colors.Text : Colors.TextDisabled;
			base.OnRenderItemText(e);
		}

		protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
			e.ArrowColor = e.Item.Enabled ? Colors.Text : Colors.TextDisabled;
			base.OnRenderArrow(e);
		}

		protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
			var y = e.Item.Height / 2;
			var inset = Scale(e.ToolStrip, 8);
			using (var pen = new Pen(Colors.Separator)) e.Graphics.DrawLine(pen, inset, y, e.Item.Width - inset, y);
		}

		protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
			// Checked items: an accent tinted square behind the item's icon, or an accent check mark
			var rect = e.ImageRectangle;
			rect.Inflate(Scale(e.ToolStrip, 2), Scale(e.ToolStrip, 2));
			var item = e.Item as ToolStripMenuItem;
			if (item != null && item.Image != null) {
				FillRounded(e.Graphics, rect, Scale(e.ToolStrip, 3), Colors.CheckedBack);
				e.Graphics.DrawImage(item.Image, e.ImageRectangle);
			} else {
				var check = MenuIcons.Glyph(MenuIcons.CheckMark, Colors.Accent, e.ImageRectangle.Height);
				if (check != null) e.Graphics.DrawImage(check, e.ImageRectangle);
			}
		}

		protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) {
			if (e.Image == null) return;
			if (!e.Item.Enabled) {
				ControlPaint.DrawImageDisabled(e.Graphics, e.Image, e.ImageRectangle.X, e.ImageRectangle.Y, Colors.Back);
				return;
			}
			e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			e.Graphics.DrawImage(e.Image, e.ImageRectangle);
		}

		private static void FillRounded(Graphics g, Rectangle rect, int radius, Color color) {
			var oldMode = g.SmoothingMode;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			using (var path = new GraphicsPath())
			using (var brush = new SolidBrush(color)) {
				var d = radius * 2;
				path.AddArc(rect.X, rect.Y, d, d, 180, 90);
				path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
				path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
				path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
				path.CloseFigure();
				g.FillPath(brush, path);
			}
			g.SmoothingMode = oldMode;
		}

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
	}

	/// <summary>Menu icons drawn from the Windows icon font (Segoe Fluent Icons on Windows 11), in the theme color.</summary>
	public static class MenuIcons {

		public const string Desktop = "";
		public const string Add = "";
		public const string Rename = "";
		public const string Close = "";
		public const string Move = "";
		public const string Pin = "";
		public const string Gather = "";
		public const string Settings = "";
		public const string Info = "";
		public const string Exit = "";
		public const string Keyboard = "";
		public const string Options = "";
		public const string CheckMark = "";
		public const string Window = "";
		public const string Folder = "";
		public const string Log = "";

		private static readonly ConcurrentDictionary<string, Bitmap> _cache = new ConcurrentDictionary<string, Bitmap>();
		private static string _fontName;

		private static string FontName {
			get {
				if (_fontName == null) {
					using (var fonts = new InstalledFontCollection()) {
						_fontName = "Segoe MDL2 Assets";
						foreach (var family in fonts.Families) {
							if (family.Name == "Segoe Fluent Icons") { _fontName = family.Name; break; }
						}
					}
				}
				return _fontName;
			}
		}

		// The glyph in the menu text color, sized for the menus at the tray DPI
		public static Image Icon(string glyph) {
			var size = (int)Math.Round(16 * (App.Instance != null ? App.Instance.TrayDpi : 96) / 96.0);
			return Glyph(glyph, ModernMenuRenderer.TextColor, size);
		}

		public static Bitmap Glyph(string glyph, Color color, int size) {
			if (size <= 0) return null;
			var key = glyph + "_" + color.ToArgb() + "_" + size;
			return _cache.GetOrAdd(key, k => {
				var bitmap = new Bitmap(size, size);
				using (var g = Graphics.FromImage(bitmap))
				using (var font = new Font(FontName, size * 0.75f, FontStyle.Regular, GraphicsUnit.Pixel))
				using (var brush = new SolidBrush(color))
				using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) {
					g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
					g.Clear(Color.Transparent);
					g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
				}
				return bitmap;
			});
		}

		// A window's own icon (see WindowIcons) as a menu image, or the generic window glyph
		public static Image ForWindow(IntPtr hwnd) {
			try {
				var path = WindowIcons.GetIconPath(hwnd);
				if (path != null) {
					var key = "file:" + path;
					return _cache.GetOrAdd(key, k => {
						using (var file = Image.FromFile(path)) return new Bitmap(file);
					});
				}
			} catch (Exception) {
			}
			return Icon(Window);
		}
	}
}
