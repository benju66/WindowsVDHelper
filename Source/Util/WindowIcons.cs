using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// Saves the icon of a window (as shown in the taskbar/Alt+Tab) as a PNG file and returns its path, for clients
	/// which show window lists (the Command Palette extension). The files are cached in
	/// %LOCALAPPDATA%\WindowsVirtualDesktopHelper\icons, keyed by the app and the icon handle.
	/// </summary>
	public static class WindowIcons {

		private static readonly string _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsVirtualDesktopHelper", "icons");
		private static readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>(); // key -> png path ("" = no icon)
		private static bool _cleanedUp = false;

		// The PNG path of the window's icon, or null if it has none
		public static string GetIconPath(IntPtr hwnd) {
			try {
				var process = OS.GetWindowProcessName(hwnd);
				var hIcon = _getWindowIcon(hwnd);
				string key;
				Func<Bitmap> render;
				if (hIcon != IntPtr.Zero) {
					key = process + "_" + hIcon.ToInt64().ToString("x");
					render = () => Icon.FromHandle(hIcon).ToBitmap(); // not our handle: never destroyed here
				} else {
					// No window icon (e.g. some store apps): the program's icon
					var exe = OS.GetWindowProcessPath(hwnd);
					if (string.IsNullOrEmpty(exe)) return null;
					key = process + "_exe";
					render = () => {
						using (var icon = Icon.ExtractAssociatedIcon(exe)) return icon?.ToBitmap();
					};
				}
				var path = _cache.GetOrAdd(key, k => _save(k, render));
				return path == "" ? null : path;
			} catch (Exception) {
				return null;
			}
		}

		private static string _save(string key, Func<Bitmap> render) {
			try {
				Directory.CreateDirectory(_dir);
				if (!_cleanedUp) {
					_cleanedUp = true;
					// Icon handles change when apps restart, so old files pile up: keep the folder small
					foreach (var old in new DirectoryInfo(_dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).Skip(300)) {
						try { old.Delete(); } catch (IOException) { }
					}
				}
				var file = Path.Combine(_dir, _safeFileName(key) + ".png");
				if (!File.Exists(file)) {
					using (var bitmap = render()) {
						if (bitmap == null) return "";
						bitmap.Save(file, ImageFormat.Png);
					}
				}
				return file;
			} catch (Exception) {
				return "";
			}
		}

		private static string _safeFileName(string key) {
			var sb = new StringBuilder();
			foreach (var c in key) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
			return sb.ToString();
		}

		// The window's own icon: large first (sharper), via WM_GETICON (with a timeout, a hung window must not
		// block us) and then the window class icon
		private static IntPtr _getWindowIcon(IntPtr hwnd) {
			foreach (var type in new[] { ICON_BIG, ICON_SMALL2, ICON_SMALL }) {
				IntPtr result;
				if (SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)type, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 100, out result) != IntPtr.Zero && result != IntPtr.Zero) return result;
			}
			var classIcon = GetClassLongPtr(hwnd, GCLP_HICON);
			if (classIcon != IntPtr.Zero) return classIcon;
			return GetClassLongPtr(hwnd, GCLP_HICONSM);
		}

		private const uint WM_GETICON = 0x007F;
		private const int ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
		private const uint SMTO_BLOCK = 0x0001, SMTO_ABORTIFHUNG = 0x0002;
		private const int GCLP_HICON = -14, GCLP_HICONSM = -34;

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

		[DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
		private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetClassLongW")]
		private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

		private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex) {
			return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : new IntPtr(GetClassLong32(hWnd, nIndex));
		}
	}
}
