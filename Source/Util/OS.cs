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
