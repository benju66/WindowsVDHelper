using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// Raises <see cref="Scrolled"/> when the mouse wheel is turned over one of the given tray icons.
	/// NotifyIcon has no wheel events, so this uses a low level mouse hook and compares the cursor position
	/// with the icons' screen rectangles (Shell_NotifyIconGetRect). The hook callback must be very fast
	/// (Windows removes slow low level hooks), so it only looks at wheel messages and the handler is posted.
	/// </summary>
	public class TrayMouseWheel : IDisposable {

		// +1 = wheel down (next desktop), -1 = wheel up (previous desktop)
		public event Action<int> Scrolled;

		private readonly Control _uiThread;
		private readonly NotifyIcon[] _icons;
		private IntPtr _hook = IntPtr.Zero;
		private LowLevelMouseProc _proc; // must be kept alive while hooked
		private List<RECT> _rects = new List<RECT>();
		private int _rectsTick = 0;

		public TrayMouseWheel(Control uiThread, params NotifyIcon[] icons) {
			_uiThread = uiThread;
			_icons = icons;
		}

		public bool Start() {
			if (_hook != IntPtr.Zero) return true;
			_proc = HookCallback;
			_hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
			return _hook != IntPtr.Zero;
		}

		public void Stop() {
			if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
			_hook = IntPtr.Zero;
		}

		public void Dispose() {
			Stop();
		}

		private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
			try {
				if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL) {
					var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
					if (_isOverIcon(data.pt)) {
						var delta = (short)((data.mouseData >> 16) & 0xFFFF);
						var direction = delta < 0 ? +1 : -1;
						_uiThread.BeginInvoke((Action)(() => Scrolled?.Invoke(direction)));
						return (IntPtr)1; // handled
					}
				}
			} catch (Exception) {
				// never let the hook fail
			}
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}

		private bool _isOverIcon(POINT pt) {
			// The icon positions rarely change, so they are cached for a few seconds
			if (unchecked(Environment.TickCount - _rectsTick) > 3000 || _rects.Count == 0) {
				_rects = _getIconRects();
				_rectsTick = Environment.TickCount;
			}
			foreach (var r in _rects) {
				if (pt.x >= r.left && pt.x < r.right && pt.y >= r.top && pt.y < r.bottom) return true;
			}
			return false;
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
