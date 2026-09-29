using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

using WindowsVirtualDesktopHelper.VirtualDesktopAPI;
using WindowsVirtualDesktopHelper.WindowsHotKeyAPI;
using System.Drawing.Text;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Security.Policy;

namespace WindowsVirtualDesktopHelper {

	class App {

		#region Public Properties

		public static App Instance;
		public IVirtualDesktopManager VDAPI = null;
		public string CurrentVDDisplayName = null;
		public uint CurrentVDDisplayNumber = 0;
		public int CurrentVDDisplayCount = 1;
		public SettingsForm SettingsForm;
		public AppForm AppForm;
		public string CurrentSystemThemeName = null;
		public static string DetectedVDImplementation = null;

		#endregion

		#region Internal Structs

		internal class HotKeyAction {
			public string HotKeyAndAction;
			public string HotKey;
			public string Action;
			public Keys Keys;
			public ModifierKeys Modifiers;
		}

		#endregion

		#region Private/Internal Properties

		private KeyboardHook _keyboardHooks = null;
		private List<HotKeyAction> _keyboardHooksHotKeysAndActions = new List<HotKeyAction>(); // the registered hotkey actions
		// Note: the following are written by monitor threads and read by the UI thread, so all access is guarded by locks
		private readonly object _focusLock = new object();
		private Dictionary<int, IntPtr> VDDToLastFocusedWin = new Dictionary<int, IntPtr>();
		public IntPtr LastForegroundhWnd = IntPtr.Zero; //TODO: this should be private
		private readonly object _fgHistoryLock = new object();
		private List<KeyValuePair<int, string>> FGWindowHistory = new List<KeyValuePair<int, string>>(); // (tick when it became foreground, window name), needed to detect if Task View was open
		private List<int> _desktopNumberHistory = new List<int>(); // stores a list of most recent desktop numbers used

		#endregion

		#region Constructor

		public App() {
			// Set the app instance global
			App.Instance = this;

			// Settings
			{
				Util.Logging.WriteLine("Using config file(s):\r\n   "+string.Join("\r\n   ", Settings.GetUsedConfigFiles()));
				Util.Logging.WriteLine("Config: \r\n   "+Settings.GetSettingsAsString().Replace("\n", "\r\n   "));
			}

			// Test global error form:
			//throw new Exception("test exception!");

			// Load the implementation
			this.LoadVDAPI();
			this.LoadVDDisplayInfo();

			// Load theme
			this.CurrentSystemThemeName = this.GetSystemThemeName();

			this.CurrentVDDisplayCount = this.GetVDDisplayCount();

			// Create the app form, which acts as our ui main thread (we need such a main thread form for many of the win api calls)
			this.AppForm = new AppForm();

			// Create settings form
			this.SettingsForm = new SettingsForm();

			// Hot keys
			this.SetupHotKeys();

		}

		#endregion

		#region Virtual Desktop Methods

		public void LoadVDAPI() {
			App.DetectedVDImplementation = VirtualDesktopAPI.Loader.GetImplementationForOS();
			this.VDAPI = VirtualDesktopAPI.Loader.LoadImplementationWithFallback(App.DetectedVDImplementation);
		}

		public void LoadVDDisplayInfo() {
			try {
				this.CurrentVDDisplayNumber = this.GetVDDisplayNumber(true);
			} catch (Exception e) {
				throw new Exception("LoadVDDisplayInfo: could not get current display number: " + e.Message, e);
			}
			try {
				this.CurrentVDDisplayName = this.GetVDDisplayName(true);
			} catch (Exception e) {
				throw new Exception("LoadVDDisplayInfo: could not get current display name: " + e.Message, e);
			}
		}

		public uint GetVDDisplayNumber(bool throwException) {
			try {
				var number = this.VDAPI.Current();
				// The implementations cast an index of -1 (desktop not found, e.g. removed mid-enumeration)
				// to uint, which must never be mistaken for a real desktop number
				if(number == uint.MaxValue) throw new InvalidOperationException("the current desktop could not be found");
				return number;
			} catch (Exception e) {
				if (throwException) throw new Exception("GetVDDisplayNumber: could not get current display number: " + e.Message, e);
				else return 0;
			}
		}

		public int GetVDDisplayCount() {
			return this.VDAPI.DisplayCount();
		}

		public string GetVDDisplayName(bool throwException) {
			try {
				return this.VDAPI.CurrentDisplayName();
			} catch (Exception e) {
				if (throwException) throw new Exception("GetVDDisplayName: could not get current display number: " + e.Message, e);
				else return "Unknown";
			}
		}

		public void MonitorVDisplayCount() {
			var thread = new Thread(new ThreadStart(_MonitorVDDisplayCount));
			thread.IsBackground = true; // never keep the process alive on its own
			thread.Start();
		}
		private void _MonitorVDDisplayCount() {
			while(true) {
				try {
					_checkForDesktopCountChange();
					// With shell notifications active, this poll is only a safety net
					System.Threading.Thread.Sleep(_vdNotificationsActive ? 2000 : 100);
				} catch(Exception e) {
					Util.Logging.WriteLine("App: Error: MonitorVDDisplayCount: " + e.Message);
					System.Threading.Thread.Sleep(1000);
				}
			}
		}

		public void MonitorVDSwitch() {
			var thread = new Thread(new ThreadStart(_MonitorVDSwitch));
			thread.IsBackground = true;
			thread.Start();
		}

		private void _refreshNextPrevIconsSafe() {
			try {
				if(this.AppForm == null || !this.AppForm.IsHandleCreated || this.AppForm.IsDisposed) return;
				this.AppForm.BeginInvoke((Action)(() => {
					try {
						this.UIUpdateNextPrevIconVisibility(this.CurrentSystemThemeName);
						this.UIUpdateTooltips();
					} catch(Exception e) {
						Util.Logging.WriteLine("App: Error: refreshing prev/next icons: " + e.Message);
					}
				}));
			} catch(Exception e) {
				// e.g. the form is being closed while the app exits
				Util.Logging.WriteLine("App: Error: could not refresh prev/next icons: " + e.Message);
			}
		}
		private void _MonitorVDSwitch() {
			while(true) {
				try {
					// Throw on error, so that a failure is not mistaken for desktop number 0 and
					// so that a stale API connection can be detected and recovered in the catch below
					_checkForDesktopSwitch(false);
					_vdApiFailureCount = 0;
					// With shell notifications active, switches are handled instantly and this poll is only a
					// safety net (e.g. for a missed notification), so it can run much less often
					System.Threading.Thread.Sleep(_vdNotificationsActive ? 1000 : 100);
				} catch(Exception e) {
					Util.Logging.WriteLine("App: Error: MonitorVDSwitch: " + e.Message);
					_tryReconnectVDAPI();
					System.Threading.Thread.Sleep(1000);
				}
			}
		}

		private int _vdApiFailureCount = 0;

		private void _tryReconnectVDAPI() {
			// The virtual desktop API lives in the explorer.exe process; when explorer crashes or
			// restarts, the cached COM objects become stale and every call fails from then on.
			// After a few consecutive failures we reconnect, otherwise the app stays broken until
			// it is manually restarted.
			_vdApiFailureCount++;
			if(_vdApiFailureCount < 5) return;
			_vdApiFailureCount = 0;
			try {
				Util.Logging.WriteLine("App: MonitorVDSwitch: reconnecting to the virtual desktop API...");
				this.VDAPI.Reconnect();
				this.VDAPI.Current(); // test the connection
				Util.Logging.WriteLine("App: MonitorVDSwitch: reconnect successful");
				// The notification registration died with the old explorer, so register again
				_postToUI(StartVDNotifications);
			} catch(Exception e) {
				Util.Logging.WriteLine("App: MonitorVDSwitch: could not reconnect to the virtual desktop API: " + e.Message);
			}
		}

		// Checks if the current desktop (or its name) changed, and if so updates the state and UI.
		// Called from the polling thread and (on the UI thread) from shell notifications, so the
		// check-and-set is locked to make sure a switch is only handled once. Throws on API errors.
		// Note: the COM calls are made outside the lock - the COM objects belong to the UI thread, so a
		// call from the polling thread may need the UI thread, which must never be waiting on this lock
		private void _checkForDesktopSwitch(bool checkName) {
			bool switched = false, renamed = false;
			var newVDDisplayNumber = this.GetVDDisplayNumber(true);
			// The name is an extra (enumerating) API call, so only fetch it when it can matter
			var newName = (checkName || newVDDisplayNumber != this.CurrentVDDisplayNumber) ? this.GetVDDisplayName(false) : null;
			lock(_switchLock) {
				if(newVDDisplayNumber != this.CurrentVDDisplayNumber) {
					this.CurrentVDDisplayName = newName;
					this.CurrentVDDisplayNumber = newVDDisplayNumber;
					switched = true;
				} else if(newName != null && newName != this.CurrentVDDisplayName) {
					this.CurrentVDDisplayName = newName;
					renamed = true;
				}
			}
			if(switched && newName == null) this.CurrentVDDisplayName = this.GetVDDisplayName(false); // rare race with the other caller
			if(switched) {
				VDSwitchedSafe();
			} else if(renamed) {
				_postToUI(() => {
					this.UIUpdateIcons();
					this.UpdateStatusOverlayWindows();
				});
			}
		}

		private void _checkForDesktopCountChange() {
			var newCurrentVDDisplayCount = this.GetVDDisplayCount();
			if(newCurrentVDDisplayCount != CurrentVDDisplayCount) {
				CurrentVDDisplayCount = newCurrentVDDisplayCount;
				// The desktop count affects the prev/next icons (enabled state and visibility on bounds)
				_refreshNextPrevIconsSafe();
			}
		}

		#region Shell Notifications

		private readonly object _switchLock = new object();
		private VirtualDesktopAPI.Notifications _vdNotifications = null;
		private volatile bool _vdNotificationsActive = false;

		// Subscribes to the shell's desktop change notifications, so switches are shown instantly instead of
		// on the next poll. Must run on the UI thread. If not supported/failing, polling keeps working as before.
		public void StartVDNotifications() {
			_vdNotificationsActive = false;
			if(_vdNotifications != null) {
				_vdNotifications.Changed -= _onVDNotification;
				_vdNotifications.Dispose();
				_vdNotifications = null;
			}
			if(!Settings.GetBool("feature.useShellNotifications")) {
				Util.Logging.WriteLine("App: shell notifications disabled by setting, using polling");
				return;
			}
			int build = 0;
			try { build = Util.OS.GetWindowsBuildVersion(); } catch(Exception) { }
			if(!VirtualDesktopAPI.Notifications.IsSupportedOnThisBuild(build)) {
				Util.Logging.WriteLine("App: shell notifications not supported on build " + build + ", using polling");
				return;
			}
			try {
				var notifications = new VirtualDesktopAPI.Notifications();
				notifications.Changed += _onVDNotification;
				notifications.Register();
				_vdNotifications = notifications;
				_vdNotificationsActive = true;
				Util.Logging.WriteLine("App: registered for shell virtual desktop notifications (instant updates)");
			} catch(Exception e) {
				Util.Logging.WriteLine("App: could not register for shell notifications, using polling: " + e.Message);
			}
		}

		private void _onVDNotification(object sender, EventArgs e) {
			// We are inside a call from the shell: never call back into it synchronously, post the work instead
			_postToUI(() => {
				try {
					_checkForDesktopSwitch(true);
					_checkForDesktopCountChange();
				} catch(Exception ex) {
					Util.Logging.WriteLine("App: Error: handling shell notification: " + ex.Message);
				}
			});
		}

		// Called when explorer.exe (re)starts, see AppForm.WndProc (TaskbarCreated)
		public void OnExplorerRestarted() {
			Util.Logging.WriteLine("App: explorer restarted, reconnecting to the virtual desktop API");
			try {
				this.VDAPI.Reconnect();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: reconnecting after explorer restart: " + e.Message);
			}
			StartVDNotifications();
			try {
				_checkForDesktopSwitch(true);
				_checkForDesktopCountChange();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: refreshing after explorer restart: " + e.Message);
			}
			UIUpdate();
		}

		private void _postToUI(Action action) {
			try {
				if(this.AppForm == null || !this.AppForm.IsHandleCreated || this.AppForm.IsDisposed) return;
				this.AppForm.BeginInvoke(action);
			} catch(Exception e) {
				// e.g. the form is being closed while the app exits
				Util.Logging.WriteLine("App: Error: could not post to UI thread: " + e.Message);
			}
		}

		#endregion

		public void VDSwitchedSafe() {
			// Make sure we run on the main thread
			if(this.AppForm.InvokeRequired) {
				Action safeAction = delegate { VDSwitchedSafe(); };
				this.AppForm.Invoke(safeAction);
			} else {
				// Update icons
				this.UIUpdateIconForVDDisplayNumber(this.CurrentSystemThemeName, this.CurrentVDDisplayNumber, this.CurrentVDDisplayName);
				this.UIUpdateIconForVDDisplayName(this.CurrentSystemThemeName, this.CurrentVDDisplayName);
				this.UIUpdateNextPrevIconVisibility(this.CurrentSystemThemeName);
				// Show notification overlay
				if(Settings.GetBool("feature.showDesktopSwitchOverlay")) {
					ShowSwitchOverlays();
				}
				// Update permanent overlay
				UpdateStatusOverlayWindows();
				// Restore focus
				if(Settings.GetBool("feature.restorePreviousWindowFocus")) {
					try {
						_restorePrevWinFocus();
					} catch(Exception e) {
						Util.Logging.WriteLine("App: Error: SwitchDesktopForward (restorePrevWinFocus()): " + e.Message);
					}
				}
				// Log this desktop number in _desktopNumberHistory, only keeping the last 20
				if(_desktopNumberHistory.Count == 0 || _desktopNumberHistory[_desktopNumberHistory.Count - 1] != this.CurrentVDDisplayNumber) {
					_desktopNumberHistory.Add((int)this.CurrentVDDisplayNumber);
				}
				if(_desktopNumberHistory.Count > 20) {
					_desktopNumberHistory.RemoveRange(0, _desktopNumberHistory.Count - 20);
				}
			}
		}

		public void SwitchDesktopBackward() {
			if(_wrapAroundTarget(-1, out int wrapTarget)) { SwitchToDesktop(wrapTarget); return; }
			// We try the virtual desktop implementation API, but fallback to shortcut keys if it fails...
			try {
				VDAPI.SwitchBackward();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: SwitchDesktopBackward (VDAPI.SwitchBackward()): " + e.Message);
				Util.OS.DesktopBackwardBySimulatingShortcutKey();
			}
		}

		public void SwitchDesktopForward() {
			if(_wrapAroundTarget(+1, out int wrapTarget)) { SwitchToDesktop(wrapTarget); return; }
			// We try the virtual desktop implementation API, but fallback to shortcut keys if it fails...
			try {
				VDAPI.SwitchForward();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: SwitchDesktopForward (VDAPI.SwitchForward()): " + e.Message);
				Util.OS.DesktopForwardBySimulatingShortcutKey();
			}
		}

		// With feature.wrapAround, going forward on the last desktop goes to the first and backward on the first to the last
		private bool _wrapAroundTarget(int direction, out int target) {
			target = -1;
			try {
				if(!Settings.GetBool("feature.wrapAround")) return false;
				var count = this.GetVDDisplayCount();
				var current = (int)this.GetVDDisplayNumber(true);
				if(count < 2) return false;
				if(direction > 0 && current == count - 1) target = 0;
				else if(direction < 0 && current == 0) target = count - 1;
				return target >= 0;
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: wrap around: " + e.Message);
				return false;
			}
		}

		public void SwitchToDesktop(int number) {
			// Explicitly store the last focused window
			try {
				_storeLastWinFocused();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: SwitchToDesktop (storeLastWinFocused()): " + e.Message);
			}
			// We try the virtual desktop implementation API
			try {
				VDAPI.SwitchToDesktop(number);
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: SwitchToDesktop (VDAPI.SwitchToDesktop(number)): " + e.Message);
				return;
			}
		}

		public void SwitchToPreviousDesktop() {
			// Switch to the previous desktop number from _desktopNumberHistory which is not the current desktop number
			for(var i = _desktopNumberHistory.Count - 1; i >= 0; i--) {
				if(_desktopNumberHistory[i] != this.CurrentVDDisplayNumber) {
					this.SwitchToDesktop(_desktopNumberHistory[i]);
					return;
				}
			}
		}

		// The overlay windows are re-used between switches (only the text is updated), instead of closing
		// and re-creating them on every switch, which flickered when switching quickly. They are only
		// re-created when something affecting their layout changed (settings, theme, monitors).
		private List<SwitchNotificationForm> _switchOverlays = new List<SwitchNotificationForm>();
		private string _switchOverlaysLayoutKey = null;
		private List<OverlayForm> _statusOverlays = new List<OverlayForm>();
		private string _statusOverlaysLayoutKey = null;

		private string _getOverlayLayoutKey(string settingsPrefix, string themePrefix) {
			var screens = Screen.AllScreens;
			var key = new System.Text.StringBuilder();
			key.Append(this.CurrentSystemThemeName);
			foreach(var setting in new[] { settingsPrefix + ".showOnAllMonitors", settingsPrefix + ".position", settingsPrefix + ".animate", settingsPrefix + ".translucent" }) {
				key.Append('|').Append(Settings.GetString(setting));
			}
			foreach(var setting in new[] { ".width", ".height", ".font", ".fontSize", ".overlayFG." + this.CurrentSystemThemeName, ".overlayBG." + this.CurrentSystemThemeName }) {
				key.Append('|').Append(Settings.GetString(themePrefix + setting));
			}
			foreach(var screen in screens) key.Append('|').Append(screen.WorkingArea.ToString());
			return key.ToString();
		}

		public void ShowSwitchOverlays() {
			var text = this.CurrentVDDisplayName;
			var duration = Settings.GetInt("feature.showDesktopSwitchOverlay.duration");
			var layoutKey = _getOverlayLayoutKey("feature.showDesktopSwitchOverlay", "theme.overlay");
			_switchOverlays.RemoveAll(f => f.IsDisposed || f.IsClosingOrClosed);
			var wanted = Settings.GetBool("feature.showDesktopSwitchOverlay.showOnAllMonitors") ? Screen.AllScreens.Length : 1;
			if(layoutKey == _switchOverlaysLayoutKey && _switchOverlays.Count == wanted) {
				// Re-use: close anything else (e.g. the splash screen) but keep ours, and just update the text
				SwitchNotificationForm.CloseAllNotificationsExcept(_switchOverlays);
				foreach(var form in _switchOverlays) form.Restart(text, duration);
				return;
			}
			// (Re)create
			SwitchNotificationForm.CloseAllNotifications(this.AppForm);
			_switchOverlays.Clear();
			_switchOverlaysLayoutKey = layoutKey;
			for(var i = 0; i < wanted; i++) {
				var form = wanted > 1 ? new SwitchNotificationForm(i) : new SwitchNotificationForm();
				form.LabelText = text;
				form.DisplayTimeMS = duration;
				form.Show();
				_switchOverlays.Add(form);
			}
		}

		public void UpdateStatusOverlayWindows() {
			if(this.AppForm.InvokeRequired) {
				this.AppForm.Invoke((Action)UpdateStatusOverlayWindows);
				return;
			}
			if(!Settings.GetBool("feature.showDesktopStatusOverlay")) {
				OverlayForm.CloseAllNotifications(this.AppForm);
				_statusOverlays.Clear();
				_statusOverlaysLayoutKey = null;
				return;
			}
			var text = this.CurrentVDDisplayName;
			var layoutKey = _getOverlayLayoutKey("feature.showDesktopStatusOverlay", "theme.status") + "|" + Settings.GetString("theme.status.offset");
			_statusOverlays.RemoveAll(f => f.IsDisposed);
			var wanted = Settings.GetBool("feature.showDesktopStatusOverlay.showOnAllMonitors") ? Screen.AllScreens.Length : 1;
			if(layoutKey == _statusOverlaysLayoutKey && _statusOverlays.Count == wanted) {
				foreach(var form in _statusOverlays) form.UpdateText(text);
				return;
			}
			OverlayForm.CloseAllNotifications(this.AppForm);
			_statusOverlays.Clear();
			_statusOverlaysLayoutKey = layoutKey;
			for(var i = 0; i < wanted; i++) {
				var form = wanted > 1 ? new OverlayForm(i) : new OverlayForm();
				form.LabelText = text;
				form.Show();
				_statusOverlays.Add(form);
			}
		}

		#endregion

		#region Window Methods

		// Foreground window changes are received as events (SetWinEventHook) instead of polling the
		// foreground window every 20ms. The history records when each window became the foreground, which
		// is needed to know if Task View was open right before a tray icon click took the focus away.
		private Util.OS.WinEventDelegate _fgHookProc = null; // must be kept alive while hooked
		private IntPtr _fgHook = IntPtr.Zero;

		public void MonitorFGWindowName() {
			// Must run on the UI thread (the hook's events are delivered via its message loop)
			_fgHookProc = _onForegroundWindowChanged;
			_fgHook = Util.OS.SetWinEventHook(Util.OS.EVENT_SYSTEM_FOREGROUND, Util.OS.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _fgHookProc, 0, 0, Util.OS.WINEVENT_OUTOFCONTEXT | Util.OS.WINEVENT_SKIPOWNPROCESS);
			if(_fgHook != IntPtr.Zero) {
				Util.Logging.WriteLine("App: using foreground window events");
				_recordForegroundWindow(Util.OS.GetForegroundWindow());
			} else {
				// Fallback: poll as before
				Util.Logging.WriteLine("App: could not hook foreground window events, polling instead");
				var thread = new Thread(new ThreadStart(_MonitorFGWindowName));
				thread.IsBackground = true;
				thread.Start();
			}
		}

		private void _onForegroundWindowChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) {
			try {
				_recordForegroundWindow(hwnd);
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: foreground window event: " + e.Message);
			}
		}

		private void _recordForegroundWindow(IntPtr hwnd) {
			var name = Util.OS.GetHandleWndName(hwnd);
			if(_isUserWindow(hwnd)) {
				_lastUserWindow = hwnd;
				if(_getAutoPinApps().Count > 0) _postToUI(() => _applyAutoPin(hwnd));
			}
			lock(_fgHistoryLock) {
				FGWindowHistory.Add(new KeyValuePair<int, string>(Environment.TickCount, name));
				if(FGWindowHistory.Count > 20) FGWindowHistory.RemoveRange(0, FGWindowHistory.Count - 20);
			}
			if(LastForegroundhWnd == IntPtr.Zero) {
				LastForegroundhWnd = Util.OS.GetFolderViewHandle();
			}
		}

		private void _MonitorFGWindowName() {
			string last = null;
			while(true) {
				try {
					var hwnd = Util.OS.GetForegroundWindow();
					var name = Util.OS.GetHandleWndName(hwnd);
					if(name != last) {
						last = name;
						_recordForegroundWindow(hwnd);
					}
					System.Threading.Thread.Sleep(20);
				} catch(Exception e) {
					Util.Logging.WriteLine("App: Error: MonitorFGWindowName: " + e.Message);
					System.Threading.Thread.Sleep(1000);
				}
			}
		}

		// True if Task View is the foreground window, or was within the last half second (a click on a
		// tray icon moves the focus to the taskbar, so at click time Task View is no longer in front)
		public bool IsTaskViewOpen() {
			if(Util.OS.GetForegroundWindowName() == "Task View") return true;
			var now = Environment.TickCount;
			lock(_fgHistoryLock) {
				for(var i = 0; i < FGWindowHistory.Count; i++) {
					if(FGWindowHistory[i].Value != "Task View") continue;
					var until = i + 1 < FGWindowHistory.Count ? FGWindowHistory[i + 1].Key : now;
					if(unchecked(now - until) <= 500) return true;
				}
			}
			return false;
		}

		public void MonitorFocusedWindow() {
			var thread = new Thread(new ThreadStart(_monitorFocusedWindow));
			thread.IsBackground = true;
			thread.Start();
		}

		private void _monitorFocusedWindow() {
			while(true) {
				try {
					// Only needed for the (optional) restore focus feature, otherwise stay idle
					if(Settings.GetBool("feature.restorePreviousWindowFocus")) {
						_storeLastWinFocused();
						System.Threading.Thread.Sleep(200);
					} else {
						System.Threading.Thread.Sleep(2000);
					}
				} catch(Exception e) {
					Util.Logging.WriteLine("App: Error: _monitorFocusedWindow: " + e.Message);
					System.Threading.Thread.Sleep(1000);
				}
			}
		}

		private void _storeLastWinFocused() {
			IntPtr hWnd = Util.OS.GetForegroundWindow();
			if(hWnd != IntPtr.Zero) {
				var fgWindowName = Util.OS.GetForegroundWindowName();
				var fgWindowType = Util.OS.GetHandleWndType(hWnd);
				if(fgWindowType == "Shell_TrayWnd") return; // we ignore the icon tray, since this takes the focus away when we click the prev/next arrows
				// If the desktop number can't be determined we skip, instead of filing this window under desktop 0
				int displayNumber;
				if(!_tryGetVDDisplayNumber(out displayNumber)) return;
				lock(_focusLock) {
					VDDToLastFocusedWin[displayNumber] = hWnd;
				}
				//Console.WriteLine($"store: display {displayNumber} hwnd {hWnd} ({fgWindowType})");
			}
		}

		private void _restorePrevWinFocus() {
			int displayNumber;
			if(!_tryGetVDDisplayNumber(out displayNumber)) return;
			IntPtr lastWindowHandle;
			lock(_focusLock) {
				if(!VDDToLastFocusedWin.TryGetValue(displayNumber, out lastWindowHandle)) return;
			}
			if(Util.OS.IsWindow(lastWindowHandle)) {
				Util.OS.SetForegroundWindow(lastWindowHandle);
			}
		}

		private bool _tryGetVDDisplayNumber(out int displayNumber) {
			try {
				displayNumber = (int)this.GetVDDisplayNumber(true);
				return true;
			} catch(Exception) {
				displayNumber = -1;
				return false;
			}
		}

		#endregion

		#region Desktop and Window Management (Windows 11 24H2+)

		// The optional extended API, null if the loaded implementation doesn't support it
		public VirtualDesktopAPI.IVirtualDesktopManagerExtended VDAPIExtended {
			get { return this.VDAPI as VirtualDesktopAPI.IVirtualDesktopManagerExtended; }
		}

		// The last foreground window which is a real app window (not the taskbar, desktop, Task View, ...),
		// used for the tray menu actions: when the tray menu opens, the taskbar has the focus
		private IntPtr _lastUserWindow = IntPtr.Zero;

		private static readonly HashSet<string> _shellWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
			"Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "NotifyIconOverflowWindow",
			"TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow",
			"ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd"
		};

		private bool _isUserWindow(IntPtr hwnd) {
			if(hwnd == IntPtr.Zero || !Util.OS.IsWindow(hwnd)) return false;
			if(_shellWindowClasses.Contains(Util.OS.GetHandleWndType(hwnd))) return false;
			if(string.IsNullOrWhiteSpace(Util.OS.GetHandleWndName(hwnd))) return false;
			uint pid;
			Util.OS.GetWindowThreadProcessId(hwnd, out pid);
			if(pid == (uint)Process.GetCurrentProcess().Id) return false; // our own overlays/forms
			return true;
		}

		// The window the user is working in: the foreground window (for hotkeys), or the last app window
		// that had the focus (for the tray menu, where the taskbar has the focus)
		public IntPtr GetActiveUserWindow(bool preferForeground) {
			if(preferForeground) {
				var fg = Util.OS.GetForegroundWindow();
				if(_isUserWindow(fg)) return fg;
			}
			return _isUserWindow(_lastUserWindow) ? _lastUserWindow : IntPtr.Zero;
		}

		public void MoveActiveWindowBy(int direction) {
			var hwnd = GetActiveUserWindow(true);
			if(hwnd == IntPtr.Zero) return;
			var count = this.GetVDDisplayCount();
			var target = (int)this.GetVDDisplayNumber(true) + direction;
			if(target < 0 || target >= count) {
				if(!Settings.GetBool("feature.wrapAround") || count < 2) return;
				target = (target + count) % count;
			}
			MoveWindowToDesktop(hwnd, target);
		}

		public void MoveWindowToDesktop(IntPtr hwnd, int index) {
			MoveWindowToDesktop(hwnd, index, Settings.GetBool("feature.moveWindow.follow"));
		}

		public void MoveWindowToDesktop(IntPtr hwnd, int index, bool follow) {
			var ext = VDAPIExtended;
			if(ext == null) { Util.Logging.WriteLine("App: moving windows is not supported on this Windows version"); return; }
			if(hwnd == IntPtr.Zero) return;
			if(index < 0 || index >= this.GetVDDisplayCount()) return;
			try {
				ext.MoveWindowToDesktop(hwnd, index);
				Util.Logging.WriteLine($"App: moved window \"{Util.OS.GetHandleWndName(hwnd)}\" to desktop {index + 1}");
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not move window: " + e.Message);
				return;
			}
			if(follow) {
				SwitchToDesktop(index);
				// Give the moved window the focus again on the new desktop (Windows activates another window
				// when switching); retried once as the switch animation can still be running
				Util.OS.SetForegroundWindow(hwnd);
				var timer = new System.Windows.Forms.Timer { Interval = 250 };
				timer.Tick += (s, e) => { timer.Stop(); timer.Dispose(); if(Util.OS.IsWindow(hwnd)) Util.OS.SetForegroundWindow(hwnd); };
				timer.Start();
			}
		}

		public void TogglePinWindow(IntPtr hwnd) {
			var ext = VDAPIExtended;
			if(ext == null || hwnd == IntPtr.Zero) return;
			try {
				var pinned = !ext.IsWindowPinned(hwnd);
				ext.SetWindowPinned(hwnd, pinned);
				var title = Util.OS.GetHandleWndName(hwnd);
				Util.Logging.WriteLine($"App: {(pinned ? "pinned" : "unpinned")} window \"{title}\"");
				ShowFeedback(pinned ? "Pinned to all desktops" : "Unpinned");
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not pin/unpin window: " + e.Message);
			}
		}

		// Creates a new desktop and takes the window there (always follows: the point is to continue working with it)
		public void MoveWindowToNewDesktop(IntPtr hwnd) {
			var ext = VDAPIExtended;
			if(ext == null || hwnd == IntPtr.Zero) return;
			try {
				var index = ext.CreateDesktop();
				if(index >= 0) MoveWindowToDesktop(hwnd, index, true);
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not move window to a new desktop: " + e.Message);
			}
		}

		public void TogglePinApp(IntPtr hwnd) {
			var ext = VDAPIExtended;
			if(ext == null || hwnd == IntPtr.Zero) return;
			try {
				var pinned = !ext.IsAppPinned(hwnd);
				ext.SetAppPinned(hwnd, pinned);
				var app = Util.OS.GetWindowAppName(hwnd);
				Util.Logging.WriteLine($"App: {(pinned ? "pinned" : "unpinned")} app \"{app}\"");
				ShowFeedback(pinned ? $"All {app} windows on all desktops" : $"{app} unpinned");
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not pin/unpin app: " + e.Message);
			}
		}

		// Moves all windows of the window's app (same process name) from other desktops to the current desktop
		public void GatherAppWindows(IntPtr hwnd) {
			var ext = VDAPIExtended;
			if(ext == null || hwnd == IntPtr.Zero) return;
			var processName = Util.OS.GetWindowProcessName(hwnd);
			if(processName == "") return;
			var current = (int)this.GetVDDisplayNumber(true);
			var moved = 0;
			foreach(var window in Util.OS.GetAppWindows()) {
				if(!_isUserWindow(window) || !string.Equals(Util.OS.GetWindowProcessName(window), processName, StringComparison.OrdinalIgnoreCase)) continue;
				try {
					if(ext.IsWindowPinned(window)) continue;
					var desktop = ext.GetWindowDesktop(window);
					if(desktop < 0 || desktop == current) continue;
					ext.MoveWindowToDesktop(window, current);
					moved++;
				} catch(Exception e) {
					Util.Logging.WriteLine("App: Error: could not move window: " + e.Message);
				}
			}
			var app = Util.OS.GetWindowAppName(hwnd);
			Util.Logging.WriteLine($"App: gathered {moved} {app} window(s) to desktop {current + 1}");
			ShowFeedback(moved == 0 ? $"All {app} windows are already here" : $"Brought {moved} {app} window{(moved == 1 ? "" : "s")} here");
		}

		// ---- Auto pin: apps listed in feature.autoPin.apps are shown on all desktops automatically

		private List<string> _getAutoPinApps() {
			return (Settings.GetString("feature.autoPin.apps") ?? "").Split(',').Select(a => a.Trim()).Where(a => a != "").ToList();
		}

		public bool IsAutoPinned(string processName) {
			return _getAutoPinApps().Any(a => string.Equals(a, processName, StringComparison.OrdinalIgnoreCase) || string.Equals(a + ".exe", processName, StringComparison.OrdinalIgnoreCase));
		}

		public void SetAutoPinned(IntPtr hwnd, bool autoPin) {
			SetAutoPinnedProcess(Util.OS.GetWindowProcessName(hwnd), autoPin);
		}

		// Adds/removes an app (process name) to/from the auto pin list and pins/unpins its open windows right away
		public void SetAutoPinnedProcess(string processName, bool autoPin) {
			if(string.IsNullOrWhiteSpace(processName)) return;
			var apps = _getAutoPinApps().Where(a => !string.Equals(a, processName, StringComparison.OrdinalIgnoreCase)).ToList();
			if(autoPin) apps.Add(processName);
			Settings.SetString("feature.autoPin.apps", string.Join(", ", apps));
			try { Settings.SaveConfig(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: saving config: " + e.Message); }
			var ext = VDAPIExtended;
			if(ext == null) return;
			foreach(var window in Util.OS.GetAppWindows()) {
				if(!string.Equals(Util.OS.GetWindowProcessName(window), processName, StringComparison.OrdinalIgnoreCase)) continue;
				try {
					ext.SetAppPinned(window, autoPin);
					break; // app level: one window pins/unpins them all
				} catch(Exception) {
					// not a pinnable app window, try the next one
				}
			}
		}

		public List<string> GetAutoPinApps() {
			return _getAutoPinApps();
		}

		// Pins the app of the window if it is in the auto pin list and not pinned yet
		private void _applyAutoPin(IntPtr hwnd) {
			var ext = VDAPIExtended;
			if(ext == null || !_isUserWindow(hwnd)) return;
			var processName = Util.OS.GetWindowProcessName(hwnd);
			if(processName == "" || !IsAutoPinned(processName)) return;
			try {
				if(!ext.IsAppPinned(hwnd)) {
					ext.SetAppPinned(hwnd, true);
					Util.Logging.WriteLine($"App: auto pinned app \"{processName}\"");
				}
			} catch(Exception) {
				// not an app window which can be pinned
			}
		}

		// Applies auto pin to all open windows (at startup and when the list changes)
		public void ApplyAutoPinToAllWindows() {
			if(_getAutoPinApps().Count == 0) return;
			foreach(var window in Util.OS.GetAppWindows()) _applyAutoPin(window);
		}

		// ---- Mouse wheel over the tray icons

		private Util.TrayMouseWheel _trayMouseWheel = null;
		private int _lastWheelSwitchTick = 0;

		public void UpdateTrayMouseWheel() {
			var enabled = Settings.GetBool("feature.mouseWheelOnTrayIcons");
			if(enabled && _trayMouseWheel == null) {
				_trayMouseWheel = new Util.TrayMouseWheel(this.AppForm, this.AppForm.notifyIconNumber, this.AppForm.notifyIconName, this.AppForm.notifyIconPrev, this.AppForm.notifyIconNext);
				_trayMouseWheel.Scrolled += direction => {
					// One desktop per wheel notch, but not faster than the switch animation
					if(unchecked(Environment.TickCount - _lastWheelSwitchTick) < 150) return;
					_lastWheelSwitchTick = Environment.TickCount;
					if(direction > 0) SwitchDesktopForward();
					else SwitchDesktopBackward();
				};
				if(_trayMouseWheel.Start()) Util.Logging.WriteLine("App: mouse wheel over the tray icons switches desktops");
				else Util.Logging.WriteLine("App: Error: could not install the mouse hook for the tray mouse wheel");
			} else if(!enabled && _trayMouseWheel != null) {
				_trayMouseWheel.Dispose();
				_trayMouseWheel = null;
			}
		}

		// ---- Keyboard shortcuts overview

		// The hotkey which runs the given action, if it was registered successfully, for display in menus
		public string GetHotKeyForAction(string action) {
			var hotkey = _keyboardHooksHotKeysAndActions.FirstOrDefault(h => string.Equals(h.Action, action, StringComparison.OrdinalIgnoreCase));
			if(hotkey == null || _hotKeyConflicts.Contains(hotkey.HotKeyAndAction)) return null;
			return hotkey.HotKey;
		}

		public void ShowKeyboardShortcuts() {
			var lines = new List<string>();
			lines.Add("Keyboard shortcuts");
			lines.Add("");
			var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
				{ "DesktopForward", "Next desktop" }, { "DesktopBackward", "Previous desktop (left)" },
				{ "PreviousDesktop", "Back to the last used desktop" },
				{ "MoveWindowForward", "Move the active window to the next desktop" }, { "MoveWindowBackward", "Move the active window to the previous desktop" },
				{ "MoveWindowToNewDesktop", "Move the active window to a new desktop" },
				{ "TogglePinWindow", "Show the active window on all desktops (toggle)" }, { "TogglePinApp", "Show all windows of the active app on all desktops (toggle)" },
				{ "GatherAppWindows", "Bring all windows of the active app to this desktop" }, { "NewDesktop", "New desktop" }
			};
			var shown = new HashSet<string>();
			foreach(var hotkey in _keyboardHooksHotKeysAndActions) {
				var action = hotkey.Action;
				string description;
				var status = _hotKeyConflicts.Contains(hotkey.HotKeyAndAction) ? "   [NOT AVAILABLE: used by another app]" : "";
				if(action.StartsWith("MoveWindowToDesktop", StringComparison.OrdinalIgnoreCase) || (action.StartsWith("Desktop", StringComparison.OrdinalIgnoreCase) && char.IsDigit(action[action.Length - 1]))) {
					// Collapse the 1..9 variants into one line
					var isMove = action.StartsWith("MoveWindow", StringComparison.OrdinalIgnoreCase);
					var keyPrefix = hotkey.HotKey.Substring(0, Math.Max(0, hotkey.HotKey.LastIndexOf('+'))).Trim();
					var key = (isMove ? "move" : "jump") + keyPrefix;
					if(!shown.Add(key)) continue;
					lines.Add($"{keyPrefix} + 1..9".PadRight(34) + (isMove ? "Move the active window to desktop 1..9" : "Jump to desktop 1..9"));
					continue;
				}
				if(!descriptions.TryGetValue(action, out description)) description = action;
				lines.Add(hotkey.HotKey.PadRight(34) + description + status);
			}
			lines.Add("");
			lines.Add("Built into Windows");
			lines.Add("");
			lines.Add("Ctrl + Win + Left/Right".PadRight(34) + "Previous/next desktop");
			lines.Add("Ctrl + Win + D".PadRight(34) + "New desktop");
			lines.Add("Ctrl + Win + F4".PadRight(34) + "Close this desktop");
			lines.Add("Win + Tab".PadRight(34) + "Task View");
			lines.Add("");
			lines.Add("Mouse");
			lines.Add("");
			if(Settings.GetBool("feature.mouseWheelOnTrayIcons")) lines.Add("Wheel over the tray number".PadRight(34) + "Previous/next desktop");
			lines.Add("Right-click the tray number".PadRight(34) + "Desktops, windows and options");
			lines.Add("");
			lines.Add("Change the shortcuts in the config file (tray menu > Options > Open config folder).");
			var form = new Forms.LogForm();
			form.Text = "Keyboard Shortcuts";
			form.SetLogText(string.Join("\n", lines));
			form.Show();
		}

		public void CreateDesktopAndSwitch() {
			var ext = VDAPIExtended;
			if(ext == null) return;
			try {
				var index = ext.CreateDesktop();
				if(index >= 0) SwitchToDesktop(index);
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not create desktop: " + e.Message);
			}
		}

		public void RenameCurrentDesktop() {
			var ext = VDAPIExtended;
			if(ext == null) return;
			var index = (int)this.CurrentVDDisplayNumber;
			var current = this.CurrentVDDisplayName ?? "";
			if(current == $"Desktop {index + 1}") current = "";
			var name = Util.Prompt.Show("Rename desktop", $"Name for desktop {index + 1} (leave empty for the default name):", current);
			if(name == null) return; // cancelled
			try {
				ext.RenameDesktop(index, name.Trim());
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not rename desktop: " + e.Message);
			}
		}

		public void RemoveCurrentDesktop() {
			var ext = VDAPIExtended;
			if(ext == null) return;
			try {
				ext.RemoveDesktop((int)this.GetVDDisplayNumber(true));
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not remove desktop: " + e.Message);
			}
		}

		// A short message in the switch overlay style, e.g. after pinning a window
		public void ShowFeedback(string text) {
			SwitchNotificationForm.CloseAllNotifications(this.AppForm);
			_switchOverlays.Clear();
			var form = new SwitchNotificationForm();
			form.LabelText = text;
			form.DisplayTimeMS = 1200;
			form.Show();
		}

		// Builds the desktop section of the tray menu: all desktops (click to switch), desktop management,
		// actions for the last used window and quick options
		public List<ToolStripItem> BuildTrayMenuItems() {
			var items = new List<ToolStripItem>();
			var ext = VDAPIExtended;
			var current = (int)this.CurrentVDDisplayNumber;

			// Desktops
			List<string> names = null;
			try { if(ext != null) names = ext.GetDesktopNames(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: desktop names: " + e.Message); }
			if(names == null) {
				names = new List<string>();
				for(var i = 0; i < this.CurrentVDDisplayCount; i++) names.Add(i == current ? this.CurrentVDDisplayName : $"Desktop {i + 1}");
			}
			for(var i = 0; i < names.Count; i++) {
				var index = i;
				var jumpKey = GetHotKeyForAction("Desktop" + (i + 1));
				var item = new ToolStripMenuItem($"{i + 1}    {names[i]}") { Checked = i == current, ShortcutKeyDisplayString = jumpKey };
				if(i == current) item.Font = new Font(item.Font, FontStyle.Bold);
				item.Click += (s, e) => SwitchToDesktop(index);
				items.Add(item);
			}

			if(ext != null) {
				items.Add(new ToolStripSeparator());
				var newItem = new ToolStripMenuItem("New desktop") { ShortcutKeyDisplayString = "Ctrl + Win + D" };
				newItem.Click += (s, e) => CreateDesktopAndSwitch();
				items.Add(newItem);
				var renameItem = new ToolStripMenuItem("Rename this desktop...");
				renameItem.Click += (s, e) => RenameCurrentDesktop();
				items.Add(renameItem);
				var removeItem = new ToolStripMenuItem("Close this desktop") { Enabled = names.Count > 1, ShortcutKeyDisplayString = "Ctrl + Win + F4" };
				removeItem.Click += (s, e) => RemoveCurrentDesktop();
				items.Add(removeItem);

				// Window actions for the window the user was last working in
				var hwnd = GetActiveUserWindow(false);
				if(hwnd != IntPtr.Zero) {
					var title = Util.OS.GetHandleWndName(hwnd);
					if(title.Length > 40) title = title.Substring(0, 37) + "...";
					title = title.Replace("&", "&&");
					items.Add(new ToolStripSeparator());
					var windowDesktop = ext.GetWindowDesktop(hwnd);
					var moveItem = new ToolStripMenuItem($"Move \"{title}\" to");
					for(var i = 0; i < names.Count; i++) {
						var index = i;
						var sub = new ToolStripMenuItem($"{i + 1}    {names[i]}") { Enabled = i != windowDesktop };
						sub.Click += (s, e) => MoveWindowToDesktop(hwnd, index);
						moveItem.DropDownItems.Add(sub);
					}
					items.Add(moveItem);
					bool pinned = false;
					try { pinned = ext.IsWindowPinned(hwnd); } catch(Exception) { }
					var pinItem = new ToolStripMenuItem($"Show \"{title}\" on all desktops") { Checked = pinned, ShortcutKeyDisplayString = GetHotKeyForAction("TogglePinWindow") };
					pinItem.Click += (s, e) => TogglePinWindow(hwnd);
					items.Add(pinItem);
					var newDesktopItem = new ToolStripMenuItem($"Move \"{title}\" to a new desktop") { ShortcutKeyDisplayString = GetHotKeyForAction("MoveWindowToNewDesktop") };
					newDesktopItem.Click += (s, e) => MoveWindowToNewDesktop(hwnd);
					items.Add(newDesktopItem);

					// App level actions
					var appName = Util.OS.GetWindowAppName(hwnd).Replace("&", "&&");
					var processName = Util.OS.GetWindowProcessName(hwnd);
					if(appName != "") {
						bool appPinned = false;
						try { appPinned = ext.IsAppPinned(hwnd); } catch(Exception) { }
						var pinAppItem = new ToolStripMenuItem($"Show all {appName} windows on all desktops") { Checked = appPinned, ShortcutKeyDisplayString = GetHotKeyForAction("TogglePinApp") };
						pinAppItem.Click += (s, e) => TogglePinApp(hwnd);
						items.Add(pinAppItem);
						var autoPinItem = new ToolStripMenuItem($"Always show {appName} on all desktops") { Checked = IsAutoPinned(processName) };
						autoPinItem.Click += (s, e) => SetAutoPinned(hwnd, !IsAutoPinned(processName));
						items.Add(autoPinItem);
						var gatherItem = new ToolStripMenuItem($"Bring all {appName} windows here") { ShortcutKeyDisplayString = GetHotKeyForAction("GatherAppWindows") };
						gatherItem.Click += (s, e) => GatherAppWindows(hwnd);
						items.Add(gatherItem);
					}
				}

				// Checklist of all open windows, to pin several windows in one go
				items.Add(new ToolStripSeparator());
				items.Add(_buildPinChecklistMenu(names));
			}

			// Quick options
			items.Add(new ToolStripSeparator());
			var shortcutsItem = new ToolStripMenuItem("Keyboard shortcuts...");
			shortcutsItem.Click += (s, e) => ShowKeyboardShortcuts();
			items.Add(shortcutsItem);
			var options = new ToolStripMenuItem("Options");
			options.DropDownItems.Add(_optionMenuItem("Wrap around at first/last desktop", "feature.wrapAround"));
			options.DropDownItems.Add(_optionMenuItem("Color the desktop number per desktop", "feature.colorIconsPerDesktop"));
			options.DropDownItems.Add(_optionMenuItem("Mouse wheel over the tray icons switches desktops", "feature.mouseWheelOnTrayIcons"));
			if(ext != null) options.DropDownItems.Add(_optionMenuItem("Switch along when moving a window", "feature.moveWindow.follow"));
			options.DropDownItems.Add(new ToolStripSeparator());
			var openConfig = new ToolStripMenuItem("Open config folder");
			openConfig.Click += (s, e) => OpenURL(Settings.GetConfigDirectory());
			options.DropDownItems.Add(openConfig);
			var openLog = new ToolStripMenuItem("Open log file");
			openLog.Click += (s, e) => { var log = System.IO.Path.Combine(Settings.GetConfigDirectory(), "WindowsVirtualDesktopHelper.log"); if(System.IO.File.Exists(log)) OpenURL(log); };
			options.DropDownItems.Add(openLog);
			items.Add(options);
			items.Add(new ToolStripSeparator());
			return items;
		}

		// Set while a click in the pin checklist is being handled, so the tray menu stays open (see AppForm)
		public bool KeepTrayMenuOpen = false;

		// "Pin windows to all desktops" submenu: every open window with a checkmark when pinned. Clicking toggles
		// the pin and keeps the menu open, so several windows can be pinned/unpinned in a row. The list is built
		// when the submenu opens (it needs one API call per window).
		private ToolStripMenuItem _buildPinChecklistMenu(List<string> desktopNames) {
			var menu = new ToolStripMenuItem("Pin windows to all desktops");
			menu.DropDownItems.Add(new ToolStripMenuItem("(loading...)") { Enabled = false }); // so the submenu arrow shows
			menu.DropDown.Closing += (s, e) => {
				if(e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && KeepTrayMenuOpen) e.Cancel = true;
			};
			menu.DropDownOpening += (s, e) => {
				menu.DropDownItems.Clear();
				var ext = VDAPIExtended;
				if(ext == null) return;
				var current = (int)this.CurrentVDDisplayNumber;
				var entries = new List<Tuple<IntPtr, string, int, bool>>();
				foreach(var hwnd in Util.OS.GetAppWindows()) {
					if(!_isUserWindow(hwnd)) continue;
					bool pinned;
					try { pinned = ext.IsWindowPinned(hwnd); } catch(Exception) { continue; } // not a normal app window
					var desktop = pinned ? -1 : ext.GetWindowDesktop(hwnd);
					entries.Add(Tuple.Create(hwnd, Util.OS.GetHandleWndName(hwnd), desktop, pinned));
					if(entries.Count >= 60) break;
				}
				// Pinned first, then the current desktop, then the other desktops in order
				var sorted = entries.OrderBy(t => t.Item4 ? -2 : (t.Item3 == current ? -1 : (t.Item3 < 0 ? int.MaxValue : t.Item3))).ToList();
				if(sorted.Count == 0) {
					menu.DropDownItems.Add(new ToolStripMenuItem("(no windows)") { Enabled = false });
					return;
				}
				foreach(var entry in sorted) {
					var hwnd = entry.Item1;
					var title = entry.Item2;
					if(title.Length > 50) title = title.Substring(0, 47) + "...";
					title = title.Replace("&", "&&");
					if(!entry.Item4 && entry.Item3 >= 0 && entry.Item3 != current) {
						var desktopName = entry.Item3 < desktopNames.Count ? desktopNames[entry.Item3] : $"Desktop {entry.Item3 + 1}";
						title += $"    ({desktopName})";
					}
					var item = new ToolStripMenuItem(title) { Checked = entry.Item4, CheckOnClick = false };
					item.Click += (s2, e2) => {
						KeepTrayMenuOpen = true;
						try {
							var pinned = !ext.IsWindowPinned(hwnd);
							ext.SetWindowPinned(hwnd, pinned);
							item.Checked = pinned;
							Util.Logging.WriteLine($"App: {(pinned ? "pinned" : "unpinned")} window \"{entry.Item2}\"");
						} catch(Exception ex) {
							Util.Logging.WriteLine("App: Error: could not pin/unpin window: " + ex.Message);
						}
						// Reset after the menu's close attempt for this click has been cancelled
						_postToUI(() => KeepTrayMenuOpen = false);
					};
					menu.DropDownItems.Add(item);
				}
			};
			return menu;
		}

		private ToolStripMenuItem _optionMenuItem(string text, string setting) {
			var item = new ToolStripMenuItem(text) { Checked = Settings.GetBool(setting) };
			item.Click += (s, e) => {
				Settings.SetBool(setting, !Settings.GetBool(setting));
				try { Settings.SaveConfig(); } catch(Exception ex) { Util.Logging.WriteLine("App: Error: saving config: " + ex.Message); }
				ApplySettings();
			};
			return item;
		}

		#endregion

		#region Config File Watching

		private System.IO.FileSystemWatcher _configWatcher = null;
		private System.Windows.Forms.Timer _configReloadTimer = null;

		// Applies config file edits immediately (debounced, and ignoring our own saves). Must run on the UI thread.
		public void StartConfigWatcher() {
			if(!Settings.GetBool("feature.autoReloadConfig")) return;
			try {
				_configReloadTimer = new System.Windows.Forms.Timer { Interval = 600 };
				_configReloadTimer.Tick += (s, e) => { _configReloadTimer.Stop(); _reloadConfig(); };
				_configWatcher = new System.IO.FileSystemWatcher(Settings.GetConfigDirectory(), "*.config");
				_configWatcher.NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.Size;
				_configWatcher.SynchronizingObject = this.AppForm; // raise the events on the UI thread
				System.IO.FileSystemEventHandler changed = (s, e) => { _configReloadTimer.Stop(); _configReloadTimer.Start(); };
				_configWatcher.Changed += changed;
				_configWatcher.Created += changed;
				_configWatcher.Deleted += changed;
				_configWatcher.Renamed += (s, e) => { _configReloadTimer.Stop(); _configReloadTimer.Start(); };
				_configWatcher.EnableRaisingEvents = true;
				Util.Logging.WriteLine("App: watching the config folder for changes");
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not watch the config folder: " + e.Message);
			}
		}

		private void _reloadConfig() {
			// Our own save also changes the file, that must not trigger a reload
			if((DateTime.UtcNow - Settings.LastSavedUtc).TotalSeconds < 3) return;
			try {
				Settings.ReloadConfig();
				Util.Logging.WriteLine("App: config file changed, reloaded");
				ApplySettings();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not reload the config: " + e.Message);
			}
		}

		// Applies the current settings to everything which is set up once (hotkeys, icons, overlays, ...)
		public void ApplySettings() {
			try { CheckThemeChanged(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: theme: " + e.Message); }
			try { SetupHotKeys(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: hotkeys: " + e.Message); }
			try { UIUpdate(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: icons: " + e.Message); }
			try { UpdateStatusOverlayWindows(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: status overlay: " + e.Message); }
			try { UpdateTrayMouseWheel(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: tray mouse wheel: " + e.Message); }
			try { ApplyAutoPinToAllWindows(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: auto pin: " + e.Message); }
			try { if(Settings.GetBool("feature.useShellNotifications") != _vdNotificationsActive) StartVDNotifications(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: notifications: " + e.Message); }
			try { if(this.SettingsForm != null && !this.SettingsForm.IsDisposed) this.SettingsForm.ReloadFromSettings(); } catch(Exception e) { Util.Logging.WriteLine("App: Error: settings window: " + e.Message); }
		}

		#endregion

		#region Actions API

		//TODO: expose this API to the command line in a --noGUI mode

		public string RunAction(string action) {
			// The following actions are supported:
			// - DesktopForward
			// - DesktopBackward
			// - PreviousDesktop
			// - Desktop1...Desktop99
			// - MoveWindowForward, MoveWindowBackward, MoveWindowToDesktop1...MoveWindowToDesktop99 (Windows 11 24H2+)
			// - TogglePinWindow, TogglePinApp (Windows 11 24H2+)
			// - MoveWindowToNewDesktop, GatherAppWindows (Windows 11 24H2+)
			// - NewDesktop (Windows 11 24H2+)
			try {

				action = action.Trim().ToLower();
				if(action == "desktopforward") {
					this.SwitchDesktopForward();
					return null;
				} else if(action == "desktopbackward") {
					this.SwitchDesktopBackward();
					return null;
				} else if(action == "previousdesktop") {
					this.SwitchToPreviousDesktop();
					return null;
				} else if(action == "movewindowforward") {
					this.MoveActiveWindowBy(+1);
					return null;
				} else if(action == "movewindowbackward") {
					this.MoveActiveWindowBy(-1);
					return null;
				} else if(action.StartsWith("movewindowtodesktop")) {
					int target;
					if(!int.TryParse(action.Replace("movewindowtodesktop", ""), out target)) throw new Exception("invalid desktop number");
					this.MoveWindowToDesktop(this.GetActiveUserWindow(true), target - 1);
					return null;
				} else if(action == "togglepinapp") {
					this.TogglePinApp(this.GetActiveUserWindow(true));
					return null;
				} else if(action == "movewindowtonewdesktop") {
					this.MoveWindowToNewDesktop(this.GetActiveUserWindow(true));
					return null;
				} else if(action == "gatherappwindows") {
					this.GatherAppWindows(this.GetActiveUserWindow(true));
					return null;
				} else if(action == "togglepinwindow") {
					this.TogglePinWindow(this.GetActiveUserWindow(true));
					return null;
				} else if(action == "newdesktop") {
					this.CreateDesktopAndSwitch();
					return null;
				} else if(action.StartsWith("desktop")) {
					var desktopNumber = 0;
					if(int.TryParse(action.Replace("desktop", ""), out desktopNumber)) {
						this.SwitchToDesktop(desktopNumber - 1);
						return null;
					} else {
						throw new Exception("invalid desktop number");
					}
				} else {
					throw new Exception("invalid action");
				}

			} catch(Exception e) {
				Util.Logging.WriteLine($"RunAction: error with action \"{action}\": {e.Message}");
				return $"error with action \"{action}\": {e.Message}";
			}
		}

		#endregion

		#region Hot Keys

		// Hotkeys which were not registered because the same key combination is used by another of our hotkeys
		private List<string> _hotKeyDuplicates = new List<string>();

		// Status of a hotkey (or, for the 1..9 hotkeys, of a modifier combination) for the settings window:
		// null = active, otherwise the reason why it isn't
		public string GetHotKeyProblem(string hotkeyOrModifiers) {
			if(string.IsNullOrWhiteSpace(hotkeyOrModifiers)) return null;
			var normalized = _normalizeHotKey(hotkeyOrModifiers);
			System.Func<string, bool> matches = entry => {
				var hotkey = _normalizeHotKey(entry.Split('=')[0]);
				return hotkey == normalized || hotkey.StartsWith(normalized + "+");
			};
			if(_hotKeyConflicts.Any(matches)) return "Used by another app";
			if(_hotKeyDuplicates.Any(matches)) return "Same keys as another shortcut";
			return null;
		}

		private static string _normalizeHotKey(string hotkey) {
			return string.Join("+", hotkey.Split('+').Select(k => k.Trim().ToLowerInvariant()).Where(k => k != ""));
		}

		// Temporarily releases all hotkeys, e.g. while the user records a new shortcut (otherwise pressing an
		// existing shortcut would run its action instead of being recorded)
		public void SuspendHotKeys() {
			if(this._keyboardHooks != null) {
				this._keyboardHooks.Dispose();
				this._keyboardHooks = null;
			}
		}

		public void ResumeHotKeys() {
			if(this._keyboardHooks == null) SetupHotKeys();
		}

		public void SetupHotKeys() {
			// Clear old hooks
			if(this._keyboardHooks != null) {
				this._keyboardHooks.Dispose();
				this._keyboardHooks = null;
			}

			// Compile a list of all hotkeys and their actions
			// A hotkey defintion and command looks like the following samples:
			// "Alt + I = Desktop1"
			// "Alt + O = Desktop2"
			// "Alt + P = Desktop3"
			// "Ctrl + Alt + Left = DesktopLeft"
			var hotkeys = new List<string>();

			// Compile from custom configs
			// hotkeys.myCustomKey1: "Alt + I = Desktop1"
			// hotkeys.myCustomKey2: "Alt + O = Desktop2"
			// hotkeys.myCustomKey3: "Alt + P = Desktop3"
			// hotkeys.myCustomKey4: "Ctrl + Alt + Left = DesktopLeft"
			foreach(var setting in Settings.GetKeys()) {
				if(setting.StartsWith("hotkeys.")) {
					hotkeys.Add(Settings.GetString(setting));
				}
			}

			// Compile from features
			if(Settings.GetBool("feature.useHotKeyToJumpToDesktopNumber")) {
				var hotKey = Settings.GetString("feature.useHotKeyToJumpToDesktopNumber.hotkey");
				if(hotKey != null && hotKey != "") {
					for(var i = 1; i <= 9; i++) {
						hotkeys.Add($"{hotKey} + D{i} = Desktop{i}");
						hotkeys.Add($"{hotKey} + NumPad{i} = Desktop{i}");
					}
				}
			}
			if(Settings.GetBool("feature.useHotKeyToJumpToPreviousDesktop")) {
				var hotKey = Settings.GetString("feature.useHotKeyToJumpToPreviousDesktop.hotkey");
				if(hotKey != null && hotKey != "") {
					hotkeys.Add($"{hotKey} = PreviousDesktop");
				}
			}
			if(Settings.GetBool("feature.useHotKeyToSwitchDesktopForward")) {
				var hotKey = Settings.GetString("feature.useHotKeyToSwitchDesktopForward.hotkey");
				if(hotKey != null && hotKey != "") {
					hotkeys.Add($"{hotKey} = DesktopForward");
				}
			}
			if(Settings.GetBool("feature.useHotKeyToSwitchDesktopBackward")) {
				var hotKey = Settings.GetString("feature.useHotKeyToSwitchDesktopBackward.hotkey");
				if(hotKey != null && hotKey != "") {
					hotkeys.Add($"{hotKey} = DesktopBackward");
				}
			}

			// Window/desktop management hotkeys, only when the Windows version supports it (otherwise
			// we would take the key combinations away from other apps for nothing)
			if(this.VDAPIExtended != null) {
				_addFeatureHotKey(hotkeys, "feature.useHotKeyToMoveWindowForward", "MoveWindowForward");
				_addFeatureHotKey(hotkeys, "feature.useHotKeyToMoveWindowBackward", "MoveWindowBackward");
				_addFeatureHotKey(hotkeys, "feature.useHotKeyToTogglePinWindow", "TogglePinWindow");
				_addFeatureHotKey(hotkeys, "feature.useHotKeyToTogglePinApp", "TogglePinApp");
				_addFeatureHotKey(hotkeys, "feature.useHotKeyToMoveWindowToNewDesktop", "MoveWindowToNewDesktop");
				_addFeatureHotKey(hotkeys, "feature.useHotKeyToGatherAppWindows", "GatherAppWindows");
				if(Settings.GetBool("feature.useHotKeyToMoveWindowToDesktopNumber")) {
					var hotKey = Settings.GetString("feature.useHotKeyToMoveWindowToDesktopNumber.hotkey");
					if(!string.IsNullOrWhiteSpace(hotKey)) {
						for(var i = 1; i <= 9; i++) {
							hotkeys.Add($"{hotKey} + D{i} = MoveWindowToDesktop{i}");
							hotkeys.Add($"{hotKey} + NumPad{i} = MoveWindowToDesktop{i}");
						}
					}
				}
			}

			// Parse all hotkeys to cached HotKeyAction structs
			_keyboardHooksHotKeysAndActions = new List<HotKeyAction>();
			var duplicates = new List<string>();
			foreach(var hotkeyAndAction in hotkeys) {
				 {
					// Init HotKeyAction struct
					var hotkeyAction = new HotKeyAction();
					hotkeyAction.HotKeyAndAction = hotkeyAndAction;
					hotkeyAction.HotKey = hotkeyAndAction.Split('=').First().Trim();
					hotkeyAction.Action = hotkeyAndAction.Split('=').Last().Trim();
					// Parse the keys to the Keys and KeyModifiers enums
					{
						var keys = hotkeyAction.HotKey.Split('+');
						//TODO: explode some special keys like Number to D0..D9, NumPad0..NumPad9
						Keys hookKeys = 0;
						ModifierKeys hookModifierKeys = 0;
						foreach(var key in keys) {
							var keyCleaned = key.Trim();
							// Normalize some key names
							if(keyCleaned.ToLower() == "ctrl") keyCleaned = "control";
							// Support Oem keys
							// OemSemicolon = 0xBA,
							// Oem1 = 0xBA,
							// Oemplus = 0xBB,
							// Oemcomma = 0xBC,
							// OemMinus = 0xBD,
							// OemPeriod = 0xBE,
							// OemQuestion = 0xBF,
							// Oem2 = 0xBF,
							// Oemtilde = 0xC0,
							// Oem3 = 0xC0,
							// OemOpenBrackets = 0xDB,
							// Oem4 = 0xDB,
							// OemPipe = 0xDC,
							// Oem5 = 0xDC,
							// OemCloseBrackets = 0xDD,
							// Oem6 = 0xDD,
							// OemQuotes = 0xDE,
							// Oem7 = 0xDE,
							// Oem8 = 0xDF,
							// OemBackslash = 0xE2,
							// Oem102 = 0xE2,
							if(keyCleaned.ToLower() == "semicolon") keyCleaned = "OemSemicolon";
							if(keyCleaned.ToLower() == "plus") keyCleaned = "Oemplus";
							if(keyCleaned.ToLower() == "comma") keyCleaned = "Oemcomma";
							if(keyCleaned.ToLower() == "minus") keyCleaned = "OemMinus";
							if(keyCleaned.ToLower() == "period") keyCleaned = "OemPeriod";
							if(keyCleaned.ToLower() == "question") keyCleaned = "OemQuestion";
							if(keyCleaned.ToLower() == "tilde") keyCleaned = "Oemtilde";
							if(keyCleaned.ToLower() == "openbrackets") keyCleaned = "OemOpenBrackets";
							if(keyCleaned.ToLower() == "pipe") keyCleaned = "OemPipe";
							if(keyCleaned.ToLower() == "closebrackets") keyCleaned = "OemCloseBrackets";
							if(keyCleaned.ToLower() == "quotes") keyCleaned = "OemQuotes";
							if(keyCleaned.ToLower() == "backslash") keyCleaned = "OemBackslash";
							if(keyCleaned.ToLower() == "clear") keyCleaned = "OemClear";
							ModifierKeys keyModifier;
							var keyModifierValid = Enum.TryParse<ModifierKeys>(keyCleaned, true, out keyModifier);
							if(keyModifierValid) {
								// Add to hookModifierKeys enum
								hookModifierKeys = hookModifierKeys | keyModifier; // can be multiple modifiers
							} else {
								Keys keyKey;
								var keyKeyValid = Enum.TryParse<Keys>(keyCleaned, true, out keyKey);
								if(keyKeyValid) {
									hookKeys = keyKey; // only a single key is supported
								}
							}
						}
						hotkeyAction.Keys = hookKeys;
						hotkeyAction.Modifiers = hookModifierKeys;
					}
					// Validate: must have at least one key and one modifier
					bool isValid = true;
					if(hotkeyAction.Modifiers == 0) {
						Util.Logging.WriteLine($"SetupHotKeys: Invalid hotkey {hotkeyAction.HotKeyAndAction}, a hotkey must have at least one modifier (Ctrl,Alt,Shift,Win)");
						isValid = false;
					}
					if(hotkeyAction.Keys == 0 || hotkeyAction.Keys == Keys.None) {
						Util.Logging.WriteLine($"SetupHotKeys: Invalid hotkey {hotkeyAction.HotKeyAndAction}, a hotkey must have at least one key");
						isValid = false;
					}
					// Register (a combination listed twice would otherwise run its action once per duplicate)
					if(isValid) {
						var duplicate = _keyboardHooksHotKeysAndActions.FirstOrDefault(h => h.Keys == hotkeyAction.Keys && h.Modifiers == hotkeyAction.Modifiers);
						if(duplicate != null) {
							Util.Logging.WriteLine($"SetupHotKeys: ignoring hotkey {hotkeyAction.HotKeyAndAction}, the same key combination is already used by {duplicate.HotKeyAndAction}");
							duplicates.Add(hotkeyAction.HotKeyAndAction);
						} else {
							_keyboardHooksHotKeysAndActions.Add(hotkeyAction);
						}
					}
				}
			}

			// Init the keyboard hooks
			this._keyboardHooks = new KeyboardHook();
			this._keyboardHooks.KeyPressed += new EventHandler<KeyPressedEventArgs>(_hotKeyPressed);

			// Register all the hotkeys in _keyboardHooksHotKeysAndActions
			// Note: registration can fail, for example when another application already owns the
			// hotkey or when a combination is listed twice - this must not crash the app, so we
			// log the failure and keep the remaining hotkeys working
			var conflicts = new List<string>();
			foreach(var hotkeyAction in _keyboardHooksHotKeysAndActions) {
				try {
					this._keyboardHooks.RegisterHotKey(hotkeyAction.Modifiers, hotkeyAction.Keys);
				} catch(Exception e) {
					Util.Logging.WriteLine($"SetupHotKeys: could not register hotkey {hotkeyAction.HotKeyAndAction}: {e.Message}");
					conflicts.Add(hotkeyAction.HotKeyAndAction);
				}
			}
			_hotKeyConflicts = conflicts;
			_hotKeyDuplicates = duplicates;
			// At startup the tray icons don't exist yet, then StartUp shows the notification
			if(this.AppForm != null && this.AppForm.IsHandleCreated) _postToUI(NotifyHotKeyConflicts);
		}

		private void _addFeatureHotKey(List<string> hotkeys, string feature, string action) {
			if(!Settings.GetBool(feature)) return;
			var hotKey = Settings.GetString(feature + ".hotkey");
			if(!string.IsNullOrWhiteSpace(hotKey)) hotkeys.Add($"{hotKey} = {action}");
		}

		private List<string> _hotKeyConflicts = new List<string>();
		private string _hotKeyConflictsNotified = "";

		// Tells the user which hotkeys could not be registered (usually because another app already uses them),
		// instead of the hotkeys silently not working. Each distinct set of conflicts is only shown once.
		public void NotifyHotKeyConflicts() {
			try {
				var key = string.Join("\n", _hotKeyConflicts);
				if(_hotKeyConflicts.Count == 0 || key == _hotKeyConflictsNotified) return;
				if(!Settings.GetBool("feature.notifyHotKeyConflicts")) return;
				_hotKeyConflictsNotified = key;
				var shown = _hotKeyConflicts.Take(6).ToList();
				var text = string.Join("\n", shown) + (_hotKeyConflicts.Count > shown.Count ? $"\n(+{_hotKeyConflicts.Count - shown.Count} more)" : "");
				text += "\nThey are probably used by another app. Change them in the config file.";
				ShowTrayNotification("Some hotkeys could not be registered", text, ToolTipIcon.Warning);
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: notifying hotkey conflicts: " + e.Message);
			}
		}

		public void ShowTrayNotification(string title, string text, ToolTipIcon icon) {
			var notifyIcon = new[] { this.AppForm.notifyIconNumber, this.AppForm.notifyIconName, this.AppForm.notifyIconNext, this.AppForm.notifyIconPrev }.FirstOrDefault(i => i.Visible);
			if(notifyIcon == null) return;
			if(text.Length > 255) text = text.Substring(0, 252) + "...";
			notifyIcon.ShowBalloonTip(10000, title, text, icon);
		}

		private void _hotKeyPressed(object sender, KeyPressedEventArgs e) {
			var keys = e.Key;
			var modifiers = e.Modifier;
			// Find matching HotKeyAction
			foreach(var hotkeyAction in _keyboardHooksHotKeysAndActions) {
				if(hotkeyAction.Keys == keys && hotkeyAction.Modifiers == modifiers) {
					RunAction(hotkeyAction.Action);
				}
			}
		}

		#endregion

		#region OS/System Theme

		public void MonitorSystemThemeSwitch() {
			var thread = new Thread(new ThreadStart(_MonitorSystemThemeSwitch));
			thread.IsBackground = true;
			thread.Start();
		}

		private void _MonitorSystemThemeSwitch() {
			while (true) {
				try {
					// Theme changes are normally picked up instantly via WM_SETTINGCHANGE (see AppForm.WndProc),
					// this poll is only a safety net
					CheckThemeChanged();
					System.Threading.Thread.Sleep(10000);
				} catch (Exception e) {
					Util.Logging.WriteLine("App: Error: " + e.Message);
					System.Threading.Thread.Sleep(5000);
				}
			}
		}

		public void CheckThemeChanged() {
			var newSystemThemeName = this.GetSystemThemeName();
			if (newSystemThemeName != this.CurrentSystemThemeName) {
				this.CurrentSystemThemeName = newSystemThemeName;
				ThemeSwitched();
			}
		}

		public void ThemeSwitched() {
			// May be called from the theme monitor thread
			_postToUI(() => {
				this.UIUpdateIcons();
				this.UpdateStatusOverlayWindows(); // re-created with the new theme colors
			});
		}

		public string GetSystemThemeName() {
			var themeSetting = Settings.GetString("general.theme");
			if(themeSetting == "auto") {
				return Util.OS.IsSystemLightThemeModeEnabled() == true ? "light" : "dark";
			} else if(themeSetting == "light") {
				return "light";
			} else if(themeSetting == "dark") {
				return "dark";
			} else {
				throw new Exception("invalid theme setting general.theme: " + themeSetting);
			}

		}

		#endregion

		#region Startup

		public void EnableStartupWithWindows() {
			// https://stackoverflow.com/questions/674628/how-do-i-set-a-program-to-launch-at-startup
			try {
				using(var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run")) {
					// Quoted, so that a path containing spaces can never be misparsed as a command line
					key.SetValue(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>().Title, "\"" + Application.ExecutablePath + "\"");
				}
			} catch (Exception e) {
				throw new Exception("EnableStartupWithWindows: could not set registry value: " + e.Message, e);
			}
		}

		public void DisableStartupWithWindows() {
			// https://stackoverflow.com/questions/674628/how-do-i-set-a-program-to-launch-at-startup
			try {
				using(var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)) {
					if(key != null) key.DeleteValue(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyTitleAttribute>().Title, false);
				}
			} catch (Exception e) {
				throw new Exception("DisableStartupWithWindows: could not delete registry value: " + e.Message, e);
			}
		}

		#endregion

		#region UI

		public void UIUpdate() {
			// Icons
			UIUpdateIcons();
		}

		public void UIUpdateIcons() {
			var theme = App.Instance.CurrentSystemThemeName;
			// Set current display icons
			UIUpdateIconForVDDisplayNumber(theme, App.Instance.CurrentVDDisplayNumber, App.Instance.CurrentVDDisplayName);
			UIUpdateIconForVDDisplayName(theme, App.Instance.CurrentVDDisplayName);
			UIUpdateNextPrevIconVisibility(theme);
			// Visibility by feature
			this.AppForm.notifyIconName.Visible = Settings.GetBool("feature.showDesktopNameInIconTray");
			this.AppForm.notifyIconNumber.Visible = Settings.GetBool("feature.showDesktopNumberInIconTray");
		}


		// The DPI the tray icons are rendered for: the taskbar's actual DPI (updates live when the scaling
		// changes), falling back to the DPI of our hidden host window
		public int TrayDpi {
			get {
				var dpi = Util.OS.GetTaskbarDpi();
				return dpi > 0 ? dpi : this.AppForm.DeviceDpi;
			}
		}

		public void UIUpdateIconForVDDisplayNumber(string theme, uint number, string name) {
			var color = _getDesktopColor(theme, (int)number);
			number++;
			this.AppForm.notifyIconNumber.Icon = Util.Icons.GenerateNotificationIcon(number.ToString(), theme, this.TrayDpi, false, 1.0, color);
			UIUpdateTooltips();
		}

		// "Work - desktop 2 of 4" (the name is omitted if it is just the default "Desktop n")
		public void UIUpdateTooltips() {
			var number = (int)this.CurrentVDDisplayNumber + 1;
			var name = this.CurrentVDDisplayName ?? "";
			var text = $"Desktop {number} of {this.CurrentVDDisplayCount}";
			if(name != "" && name != $"Desktop {number}") text = $"{name} - desktop {number} of {this.CurrentVDDisplayCount}";
			if(text.Length > 63) text = text.Substring(0, 60) + "..."; // NotifyIcon.Text is limited to 63 characters
			this.AppForm.notifyIconNumber.Text = text;
			this.AppForm.notifyIconName.Text = text;
		}

		// The per desktop icon color (feature.colorIconsPerDesktop), or null for the theme color
		private string _getDesktopColor(string theme, int index) {
			if(!Settings.GetBool("feature.colorIconsPerDesktop")) return null;
			var colors = (Settings.GetString("theme.icons.desktopColors." + theme) ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Where(c => c != "").ToList();
			if(colors.Count == 0 || index < 0) return null;
			return colors[index % colors.Count];
		}

		public void UIUpdateIconForVDDisplayName(string theme, string name) {
			var nameToShow = name;
			if(nameToShow == null) nameToShow = "";
			if(nameToShow.Length > 1) nameToShow = new StringInfo(nameToShow).SubstringByTextElements(0, 1);
			this.AppForm.notifyIconName.Icon = Util.Icons.GenerateNotificationIcon(nameToShow, theme, this.TrayDpi, false, 1.0, _getDesktopColor(theme, (int)this.CurrentVDDisplayNumber));
		}

		public void UIUpdateNextPrevIconVisibility(string theme) {
			if(Settings.GetBool("feature.showPrevNextIcons")) {
				int count = App.Instance.CurrentVDDisplayCount - 1;
				var prevChar = Settings.GetString("feature.showPrevNextIcons.prevChar");
				var nextChar = Settings.GetString("feature.showPrevNextIcons.nextChar");
				var hasNextDesktop = count != 0 && App.Instance.CurrentVDDisplayNumber != count;
				var hasPrevDesktop = App.Instance.CurrentVDDisplayNumber != 0;
				if(count != 0 && Settings.GetBool("feature.wrapAround")) {
					hasNextDesktop = true; // with wrap around there is always a next/previous desktop
					hasPrevDesktop = true;
				}
				// Update prev/next icons
				this.AppForm.notifyIconPrev.Icon = Util.Icons.GenerateNotificationIcon(prevChar, theme, this.TrayDpi, true, hasPrevDesktop ? 1.0f : Settings.GetDouble("theme.icons.disabledOpacity"));
				this.AppForm.notifyIconNext.Icon = Util.Icons.GenerateNotificationIcon(nextChar, theme, this.TrayDpi, true, hasNextDesktop ? 1.0f : Settings.GetDouble("theme.icons.disabledOpacity"));
				// Show or hide?
				if(Settings.GetBool("feature.showPrevNextIcons.automaticallyHidePrevNextOnBounds")) {
					this.AppForm.notifyIconNext.Visible = hasNextDesktop;
					this.AppForm.notifyIconPrev.Visible = hasPrevDesktop;
				} else {
					this.AppForm.notifyIconNext.Visible = true;
					this.AppForm.notifyIconPrev.Visible = true;
				}
			} else {
				this.AppForm.notifyIconNext.Visible = false;
				this.AppForm.notifyIconPrev.Visible = false;
			}
		}

		#endregion

		#region Forms and Windows

		public void ShowAbout() {
			this.AppForm.Invoke((Action)(() => {
				var form = new AboutForm();
				form.Show();
			}));
		}

		public void ShowSettings() {
			this.SettingsForm.Show();
		}

		public void ShowSplash() {
			if(Settings.GetBool("feature.showSplashScreen")) {
				if(Settings.GetBool("feature.showDesktopSwitchOverlay")) {
					this.AppForm.Invoke((Action)(() => {
						var form = new SwitchNotificationForm();
						form.DisplayTimeMS = Settings.GetInt("feature.showSplashScreen.duration");
						form.LabelText = Settings.GetString("feature.showSplashScreen.text");
						form.Show();
					}));
				}
			}
		}

		#endregion

		#region Misc

		public void OpenURL(string url) {
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}

		public void Exit() {
			// Settings are otherwise only saved when the settings window is closed, so persist
			// any pending changes before quitting
			try {
				if(Settings.HasUnsavedChanges) Settings.SaveConfig();
			} catch(Exception e) {
				Util.Logging.WriteLine("App: Error: could not save the config on exit: " + e.Message);
			}
			Application.Exit();
			System.Environment.Exit(0);
		}

		#endregion

	}
}
