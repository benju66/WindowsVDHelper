using System;
using System.Runtime.InteropServices;
using System.Text;
using HWND = System.IntPtr;

namespace WindowsVirtualDesktopHelper.Util {
	public class OS {

		#region user32.dll Imports

		[DllImport("user32.dll")]
		private static extern int GetWindowText(HWND hWnd, StringBuilder lpString, int nMaxCount);

		[DllImport("user32.dll")]
		private static extern int GetWindowTextLength(HWND hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool IsWindowVisible(HWND hWnd);

		[DllImport("user32.dll")]
		private static extern IntPtr GetShellWindow();

		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		public static extern bool SetForegroundWindow(IntPtr hWnd);

		private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern IntPtr FindWindowA(string lpClassName, string lpWindowName);

		[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
		private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

		public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

		[DllImport("user32.dll")]
		public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

		[DllImport("user32.dll")]
		public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

		public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
		public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
		public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

		[DllImport("user32.dll")]
		private static extern uint GetDpiForWindow(IntPtr hwnd);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern uint RegisterWindowMessage(string lpString);

		[DllImport("user32.dll")]
		public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

		#endregion

		#region App Windows

		private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		[DllImport("user32.dll")]
		private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("dwmapi.dll")]
		private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

		private const uint GW_OWNER = 4;
		private const int GWL_EXSTYLE = -20;
		private const int WS_EX_TOOLWINDOW = 0x00000080;
		private const int WS_EX_APPWINDOW = 0x00040000;
		private const int DWMWA_CLOAKED = 14;
		private const int DWM_CLOAKED_APP = 1;

		// The top level windows the user would see in Alt+Tab, on all desktops (windows on other desktops
		// are hidden by the shell, "cloaked", but still listed), in z-order (most recently used first)
		public static System.Collections.Generic.List<IntPtr> GetAppWindows() {
			var windows = new System.Collections.Generic.List<IntPtr>();
			EnumWindows((hWnd, lParam) => {
				if (!IsWindowVisible(hWnd)) return true;
				if (GetWindowTextLength(hWnd) == 0) return true;
				var exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
				var owner = GetWindow(hWnd, GW_OWNER);
				if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0) return true;
				if (owner != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0) return true;
				int cloaked;
				// Cloaked by the app itself (e.g. suspended store app frames) means not really open
				if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked == DWM_CLOAKED_APP) return true;
				windows.Add(hWnd);
				return true;
			}, IntPtr.Zero);
			return windows;
		}

		// Process name (without .exe) of the process owning the window, e.g. "Spotify", or "" if unknown.
		// Cached per process, as this is called for every foreground change (auto pin).
		// Note: the caches are keyed by the process id AND its start time: Windows reuses the ids of ended processes,
		// so with the id alone a new process could be taken for an old one (and e.g. be auto pinned as that app)
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<Tuple<uint, long>, Tuple<string, string>> _processInfoCache = new System.Collections.Concurrent.ConcurrentDictionary<Tuple<uint, long>, Tuple<string, string>>();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr handle);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetProcessTimes(IntPtr hProcess, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

		private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

		// Ended processes stay in the caches, so they are emptied now and then
		private const int MaxProcessCacheEntries = 1000;

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<Tuple<uint, long>, string> _processPathCache = new System.Collections.Concurrent.ConcurrentDictionary<Tuple<uint, long>, string>();

		// The cache key of the process owning the window (its id and start time), or null if the window has no process
		private static Tuple<uint, long> _getProcessKey(IntPtr hWnd) {
			uint pid;
			GetWindowThreadProcessId(hWnd, out pid);
			if (pid == 0) return null;
			long created = 0;
			var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
			if (handle != IntPtr.Zero) {
				try {
					long exited, kernel, user;
					if (!GetProcessTimes(handle, out created, out exited, out kernel, out user)) created = 0;
				} finally {
					CloseHandle(handle);
				}
			}
			return Tuple.Create(pid, created);
		}

		// Full path of the exe of the process owning the window, or null. Works for elevated processes too
		// (PROCESS_QUERY_LIMITED_INFORMATION), unlike Process.MainModule
		public static string GetWindowProcessPath(IntPtr hWnd) {
			var key = _getProcessKey(hWnd);
			if (key == null) return null;
			if (_processPathCache.Count > MaxProcessCacheEntries) _processPathCache.Clear();
			return _processPathCache.GetOrAdd(key, k => {
				var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, k.Item1);
				if (handle == IntPtr.Zero) return null;
				try {
					var sb = new StringBuilder(1024);
					uint size = (uint)sb.Capacity;
					return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
				} finally {
					CloseHandle(handle);
				}
			});
		}

		public static string GetWindowProcessName(IntPtr hWnd) {
			return _getWindowProcessInfo(hWnd).Item1;
		}

		// A friendly app name for menus, e.g. "Microsoft Teams" (the exe's description), falling back to the process name
		public static string GetWindowAppName(IntPtr hWnd) {
			var info = _getWindowProcessInfo(hWnd);
			return string.IsNullOrWhiteSpace(info.Item2) ? info.Item1 : info.Item2;
		}

		private static Tuple<string, string> _getWindowProcessInfo(IntPtr hWnd) {
			var key = _getProcessKey(hWnd);
			if (key == null) return Tuple.Create("", "");
			if (_processInfoCache.Count > MaxProcessCacheEntries) _processInfoCache.Clear();
			return _processInfoCache.GetOrAdd(key, k => {
				try {
					using (var process = System.Diagnostics.Process.GetProcessById((int)k.Item1)) {
						var name = process.ProcessName;
						string description = null;
						try { description = process.MainModule.FileVersionInfo.FileDescription; } catch (Exception) { /* e.g. elevated process */ }
						return Tuple.Create(name, description ?? "");
					}
				} catch (Exception) {
					return Tuple.Create("", "");
				}
			});
		}

		#endregion

		#region Shell

		// Waits until explorer's taskbar exists: at logon the app can start before the shell, and the virtual desktop API
		// then fails to load. Returns true if it had to wait, false if the shell was already there (or never came)
		public static bool WaitForShell(int timeoutMs) {
			if (FindWindowA("Shell_TrayWnd", null) != IntPtr.Zero) return false;
			var start = Environment.TickCount;
			while (FindWindowA("Shell_TrayWnd", null) == IntPtr.Zero) {
				if (unchecked(Environment.TickCount - start) > timeoutMs) return false;
				System.Threading.Thread.Sleep(500);
			}
			return true;
		}

		#endregion

		#region Desktop of a Window

		// The documented IVirtualDesktopManager (shobjidl.h), which is the same on all Windows 10/11 builds (unlike the
		// internal API in VirtualDesktopAPI). One instance per thread, so a call never has to wait for another thread.
		[ComImport]
		[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		[Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
		private interface IPublicVirtualDesktopManager {
			[return: MarshalAs(UnmanagedType.Bool)]
			bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
			Guid GetWindowDesktopId(IntPtr topLevelWindow);
			void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
		}

		private static readonly Guid CLSID_VirtualDesktopManager = new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A");

		[ThreadStatic]
		private static IPublicVirtualDesktopManager _publicVDManager;

		// True if the window is on the current desktop (or pinned to all desktops), null if unknown (e.g. the window is gone)
		public static bool? IsWindowOnCurrentDesktop(IntPtr hWnd) {
			if (hWnd == IntPtr.Zero) return null;
			try {
				if (_publicVDManager == null) _publicVDManager = (IPublicVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_VirtualDesktopManager));
				return _publicVDManager.IsWindowOnCurrentVirtualDesktop(hWnd);
			} catch (Exception) {
				_publicVDManager = null; // may be stale after an explorer restart: re-created on the next call
				return null;
			}
		}

		#endregion

		#region DPI

		// The DPI of the (primary) taskbar, which is what the tray icons are rendered for. Returns 0 if unknown.
		public static int GetTaskbarDpi() {
			try {
				var taskbar = FindWindowA("Shell_TrayWnd", null);
				if (taskbar == IntPtr.Zero) return 0;
				return (int)GetDpiForWindow(taskbar); // Windows 10 1607+
			} catch (Exception) {
				return 0;
			}
		}

		#endregion

		#region Manipulating Windows

		public static string GetForegroundWindowName() {
			IntPtr handle = GetForegroundWindow();
			string windowName = GetHandleWndName(handle);
			return windowName;
		}
		
		public static bool SetFocusWindow() {
			IntPtr handle = GetForegroundWindow();
			return SetForegroundWindow(handle);
		}


		public static void SetFocusWindowToDesktop(IntPtr hWnd) {
			if (GetHandleWndName(hWnd) == "Folder View") {
				SetForegroundWindow(hWnd);
				return;
			}
			
			// fallback
			IntPtr desktopHandle = FindWindowA("Progman", "Program Manager");
			if (desktopHandle == IntPtr.Zero) {
				desktopHandle = FindWindowA("WorkerW", null);
			}
			
			SetForegroundWindow(desktopHandle);
		}

		public static string GetHandleWndName(IntPtr hWnd) {
			StringBuilder windowName = new StringBuilder(256);
			GetWindowText(hWnd, windowName, windowName.Capacity);
			return windowName.ToString();
		}

        public static string GetHandleWndType(IntPtr hWnd) {
            // Implement the logic to get the window type based on the handle
            // You can use the GetClassName function from the user32.dll to get the window class name
            StringBuilder className = new StringBuilder(256);
            int result = GetClassName(hWnd, className, className.Capacity);
            if(result != 0) {
                return className.ToString();
            } else {
                return string.Empty;
            }
        }

		public static IntPtr GetFolderViewHandle() {
			IntPtr handle = GetForegroundWindow();
			EnumChildWindows(handle, (hWndChild, lParam) => {
				if (IsWindowVisible(hWndChild)) {
					int length = GetWindowTextLength(hWndChild);
					if (length > 0) {
						StringBuilder sb = new StringBuilder(length + 1);
						GetWindowText(hWndChild, sb, sb.Capacity);
						if (sb.ToString() == "Folder View") {
							handle = hWndChild;
							return false;
						}
					}
				}
				return true;
			}, IntPtr.Zero);

			return handle;
		}

		#endregion

		[DllImport("user32.dll")]
		private static extern bool IsIconic(IntPtr hWnd);

		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

		// Brings a window to the front, restoring it if minimized. Windows only lets the foreground process
		// set the foreground window; a (harmless) Alt key press lifts that restriction for this call
		public static void ActivateWindow(IntPtr hWnd) {
			if (!IsWindow(hWnd)) return;
			if (IsIconic(hWnd)) ShowWindow(hWnd, 9); // SW_RESTORE
			const byte VK_MENU = 0x12;
			const uint KEYEVENTF_KEYUP = 0x2;
			keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
			keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
			SetForegroundWindow(hWnd);
		}

		#region Invoking Windows Features

		public static void OpenTaskView() {
			var simu = new WindowsInput.InputSimulator();
			simu.Keyboard.ModifiedKeyStroke(WindowsInput.Native.VirtualKeyCode.LWIN, WindowsInput.Native.VirtualKeyCode.TAB);
			//System.Diagnostics.Process.Start("explorer.exe", "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}");
		}

		public static void DesktopBackwardBySimulatingShortcutKey() {
			var simu = new WindowsInput.InputSimulator();
			simu.Keyboard.ModifiedKeyStroke(new[] { WindowsInput.Native.VirtualKeyCode.LCONTROL, WindowsInput.Native.VirtualKeyCode.LWIN }, WindowsInput.Native.VirtualKeyCode.LEFT);
		}

		public static void DesktopForwardBySimulatingShortcutKey() {
			var simu = new WindowsInput.InputSimulator();
			simu.Keyboard.ModifiedKeyStroke(new[] { WindowsInput.Native.VirtualKeyCode.LCONTROL, WindowsInput.Native.VirtualKeyCode.LWIN }, WindowsInput.Native.VirtualKeyCode.RIGHT);
		}

		#endregion

		#region Windows Settings and Modes

		public static bool IsSystemLightThemeModeEnabled() {
			// https://learn.microsoft.com/en-us/answers/questions/715081/how-to-detect-windows-dark-mode.html
			// Note: this is polled every second, so the key is opened read-only and always disposed.
			// The value is missing on some builds/SKUs, where we assume the (default) light theme
			try {
				using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", false)) {
					var ret = key == null ? null : key.GetValue("SystemUsesLightTheme");
					if(ret == null) return true;
					return Convert.ToInt32(ret) == 1; // 1 == light
				}
			} catch (Exception e) {
				throw new Exception("IsSystemLightThemeModeEnabled: could not get dark/light theme setting: " + e.Message, e);
			}
		}

		#endregion

		#region Windows Versions and Builds

		public static bool IsWindows11() {
			return GetWindowsBuildVersion() >= 22000;
		}

		public static int GetWindowsBuildVersion() {
			// via https://stackoverflow.com/questions/69038560/detect-windows-11-with-net-framework-or-windows-api
			var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
			var currentBuildStr = (string)reg.GetValue("CurrentBuild");
			var currentBuild = int.Parse(currentBuildStr);
			return currentBuild;
		}

		// Revision
		public static int GetWindowsBuildRevision() {
			var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
			var currentRevision = (int)reg.GetValue("UBR");
			return currentRevision;
		}

		public static string GetWindowsProductName() {
			var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
			var retStr = (string)reg.GetValue("ProductName");
			return retStr;
		}

		public static string GetWindowsDisplayVersion() {
			var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
			var retStr = (string)reg.GetValue("DisplayVersion");
			return retStr;
		}

		public static int GetWindowsReleaseId() {
			var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
			var retStr = (string)reg.GetValue("ReleaseId");
			var retInt = int.Parse(retStr);
			return retInt;
		}

		#endregion
	}
}
