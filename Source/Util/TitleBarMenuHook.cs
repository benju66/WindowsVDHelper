using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Util {

	/// <summary>
	/// Raises <see cref="Requested"/> when the user Ctrl + right-clicks the title bar of any window (instead of
	/// Windows' own title bar menu). Uses a low level mouse hook; the callback must be very fast, so it only looks
	/// further at right-clicks with Ctrl held, and asks the window with WM_NCHITTEST (with a short timeout) whether
	/// the click is on its title bar. Both the button down and up of such a click are swallowed.
	/// </summary>
	public class TitleBarMenuHook : IDisposable {

		// The window (top level) and the screen position of the click
		public event Action<IntPtr, System.Drawing.Point> Requested;

		private readonly Control _uiThread;
		private IntPtr _hook = IntPtr.Zero;
		private LowLevelMouseProc _proc; // must be kept alive while hooked
		private bool _swallowNextUp = false;

		public TitleBarMenuHook(Control uiThread) {
			_uiThread = uiThread;
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
				if (nCode >= 0) {
					var message = (int)wParam;
					if (message == WM_RBUTTONUP && _swallowNextUp) {
						_swallowNextUp = false;
						return (IntPtr)1;
					}
					if (message == WM_RBUTTONDOWN && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) {
						var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
						var window = GetAncestor(WindowFromPoint(data.pt), GA_ROOT);
						if (window != IntPtr.Zero && _isTitleBar(window, data.pt)) {
							_swallowNextUp = true;
							var point = new System.Drawing.Point(data.pt.x, data.pt.y);
							_uiThread.BeginInvoke((Action)(() => Requested?.Invoke(window, point)));
							return (IntPtr)1;
						}
					}
				}
			} catch (Exception) {
				// never let the hook fail
			}
			return CallNextHookEx(_hook, nCode, wParam, lParam);
		}

		private static bool _isTitleBar(IntPtr window, POINT pt) {
			IntPtr result;
			var lParam = (IntPtr)((pt.y << 16) | (pt.x & 0xFFFF));
			if (SendMessageTimeout(window, WM_NCHITTEST, IntPtr.Zero, lParam, SMTO_ABORTIFHUNG, 50, out result) == IntPtr.Zero) return false;
			return (int)result == HTCAPTION;
		}

		#region Native

		private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

		private const int WH_MOUSE_LL = 14;
		private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
		private const uint WM_NCHITTEST = 0x0084;
		private const int HTCAPTION = 2;
		private const uint SMTO_ABORTIFHUNG = 0x0002;
		private const uint GA_ROOT = 2;
		private const int VK_CONTROL = 0x11;

		[StructLayout(LayoutKind.Sequential)]
		private struct POINT { public int x; public int y; }

		[StructLayout(LayoutKind.Sequential)]
		private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll")]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr GetModuleHandle(string lpModuleName);

		[DllImport("user32.dll")]
		private static extern IntPtr WindowFromPoint(POINT point);

		[DllImport("user32.dll")]
		private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

		[DllImport("user32.dll")]
		private static extern short GetAsyncKeyState(int vKey);

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

		#endregion
	}
}
