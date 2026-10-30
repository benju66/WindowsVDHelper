using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// Raises <see cref="Scrolled"/> when the mouse wheel is turned over one of the given tray icons (if
	/// <see cref="OverIcons"/>), or anywhere while exactly the <see cref="Modifiers"/> keys are held (e.g. Win + Alt).
	/// NotifyIcon has no wheel events, so this uses a low level mouse hook and compares the cursor position
	/// with the icons' screen rectangles (Shell_NotifyIconGetRect). The hook callback must be very fast
	/// (Windows removes slow low level hooks), so it runs on the hook thread, only looks at wheel messages and the
	/// handler is posted to the UI thread. The wheel events it handles are swallowed (the window under the cursor
	/// doesn't scroll).
	/// </summary>
	public class TrayMouseWheel : IDisposable {

		// +1 = wheel down (next desktop), -1 = wheel up (previous desktop)
		public event Action<int> Scrolled;

		// Scrolling over the tray icons raises Scrolled
		public volatile bool OverIcons = true;
		// Scrolling anywhere with exactly these modifier keys held raises Scrolled; 0 = off
		public volatile ModifierKey Modifiers = 0;

		[Flags]
		public enum ModifierKey { Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

		private readonly Control _uiThread;
		private readonly NotifyIcon[] _icons;
		private IntPtr _hook = IntPtr.Zero;
		private LowLevelMouseProc _proc; // must be kept alive while hooked
		private volatile List<RECT> _rects = new List<RECT>(); // replaced as a whole, read by the hook thread
		private int _rectsTick = 0;

		public TrayMouseWheel(Control uiThread, params NotifyIcon[] icons) {
			_uiThread = uiThread;
			_icons = icons;
		}

		public bool Start() {
			if (_hook != IntPtr.Zero) return true;
			_proc = HookCallback;
			// Installed on the hook thread, which then receives the callbacks (see HookThread)
			HookThread.Invoke(() => _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0));
			_refreshRectsAsync();
			return _hook != IntPtr.Zero;
		}

		public void Stop() {
			var hook = _hook;
			_hook = IntPtr.Zero;
			if (hook != IntPtr.Zero) HookThread.Invoke(() => UnhookWindowsHookEx(hook));
		}

		public void Dispose() {
			Stop();
		}

		private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
			try {
				if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL) {
					var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
					var modifiers = Modifiers;
					var withModifiers = modifiers != 0 && _heldModifiers() == modifiers;
					if (withModifiers || (OverIcons && _isOverIcon(data.pt))) {
						var delta = (short)((data.mouseData >> 16) & 0xFFFF);
						var direction = delta < 0 ? +1 : -1;
						_uiThread.BeginInvoke((Action)(() => {
							// Releasing Win alone opens the Start menu, Alt alone the window's menu bar: the scroll isn't a key
							// press, so Windows still thinks the modifier was pressed alone. A key press in between prevents that
							if (withModifiers) _pressMaskKey();
							Scrolled?.Invoke(direction);
						}));
						return (IntPtr)1; // handled
					}
				}
			} catch (Exception) {
				// never let the hook fail
			}
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}

		private bool _isOverIcon(POINT pt) {
			// The icon positions rarely change, so they are cached for a few seconds. Finding them asks explorer, which
			// can be busy, so an outdated list is refreshed in the background instead of here (the mouse waits for us)
			if (unchecked(Environment.TickCount - _rectsTick) > 3000) _refreshRectsAsync();
			foreach (var r in _rects) {
				if (pt.x >= r.left && pt.x < r.right && pt.y >= r.top && pt.y < r.bottom) return true;
			}
			return false;
		}

		private static bool _isDown(int vk) {
			return (GetAsyncKeyState(vk) & 0x8000) != 0;
		}

		private static ModifierKey _heldModifiers() {
			ModifierKey held = 0;
			if (_isDown(VK_CONTROL)) held |= ModifierKey.Ctrl;
			if (_isDown(VK_MENU)) held |= ModifierKey.Alt;
			if (_isDown(VK_SHIFT)) held |= ModifierKey.Shift;
			if (_isDown(VK_LWIN) || _isDown(VK_RWIN)) held |= ModifierKey.Win;
			return held;
		}

		// Presses and releases an unassigned key (0xE8, which AutoHotkey uses for the same purpose)
		private static void _pressMaskKey() {
			keybd_event(VK_MASK, 0, 0, UIntPtr.Zero);
			keybd_event(VK_MASK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
		}

		// Parses "Win + Alt" (the order and case don't matter); 0 if empty or not only modifier keys
		public static ModifierKey ParseModifiers(string keys) {
			ModifierKey result = 0;
			foreach (var part in (keys ?? "").Split('+')) {
				var key = part.Trim().ToLowerInvariant();
				if (key == "") continue;
				if (key == "ctrl" || key == "control") result |= ModifierKey.Ctrl;
				else if (key == "alt") result |= ModifierKey.Alt;
				else if (key == "shift") result |= ModifierKey.Shift;
				else if (key == "win" || key == "windows") result |= ModifierKey.Win;
				else return 0;
			}
			return result;
		}

		// The screen rectangle of a tray icon, or null if it isn't shown (e.g. in the overflow area)
		public static System.Drawing.Rectangle? GetIconRect(NotifyIcon icon) {
			try {
				var id = (int)typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(icon);
				var window = (NativeWindow)typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(icon);
				var identifier = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = window.Handle, uID = (uint)id };
				RECT rect;
				if (Shell_NotifyIconGetRect(ref identifier, out rect) != 0) return null;
				return System.Drawing.Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
			} catch (Exception) {
				return null;
			}
		}

		private int _refreshing = 0;

		private void _refreshRectsAsync() {
			if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;
			ThreadPool.QueueUserWorkItem(_ => {
				try {
					_rects = _getIconRects();
					_rectsTick = Environment.TickCount;
				} catch (Exception) {
					// keep the previous positions
				} finally {
					_refreshing = 0;
				}
			});
		}

		private List<RECT> _getIconRects() {
			var rects = new List<RECT>();
			foreach (var icon in _icons) {
				if (!icon.Visible) continue;
				try {
					// NotifyIcon keeps its window and icon id private
					var id = (int)typeof(NotifyIcon).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(icon);
					var window = (NativeWindow)typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(icon);
					var identifier = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = window.Handle, uID = (uint)id };
					RECT rect;
					if (Shell_NotifyIconGetRect(ref identifier, out rect) == 0) rects.Add(rect);
				} catch (Exception) {
					// icon not (yet) shown
				}
			}
			return rects;
		}

		#region Native

		private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

		private const int WH_MOUSE_LL = 14;
		private const int WM_MOUSEWHEEL = 0x020A;
		private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
		private const byte VK_MASK = 0xE8;
		private const uint KEYEVENTF_KEYUP = 0x2;

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		[DllImport("user32.dll")]
		private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT { public int x; public int y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct RECT { public int left; public int top; public int right; public int bottom; }

		[StructLayout(LayoutKind.Sequential)]
		private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

		[StructLayout(LayoutKind.Sequential)]
		private struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll")]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("shell32.dll")]
		private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

		#endregion
	}
}
