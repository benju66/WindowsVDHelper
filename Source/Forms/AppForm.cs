using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using WindowsVirtualDesktopHelper.WindowsHotKeyAPI;

namespace WindowsVirtualDesktopHelper {
	public partial class AppForm : Form {

		private bool _startupDone = false;

		public AppForm() {
			// Init UI
			InitializeComponent();

			// The tray menu gets the desktop list and actions added each time it opens, and is
			// also available on the desktop name icon
			this.notifyIconName.ContextMenuStrip = this.contextMenuStrip1;
			this.contextMenuStrip1.Opening += contextMenuStrip1_Opening;
		}

		private readonly System.Collections.Generic.List<ToolStripItem> _dynamicMenuItems = new System.Collections.Generic.List<ToolStripItem>();

		private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e) {
			try {
				foreach (var item in _dynamicMenuItems) {
					this.contextMenuStrip1.Items.Remove(item);
					item.Dispose();
				}
				_dynamicMenuItems.Clear();
				var items = App.Instance.BuildTrayMenuItems();
				for (var i = 0; i < items.Count; i++) this.contextMenuStrip1.Items.Insert(i, items[i]);
				_dynamicMenuItems.AddRange(items);
			} catch (Exception ex) {
				Util.Logging.WriteLine("AppForm: Error building the tray menu: " + ex.Message);
			}
		}



		#region Form Events

		protected override CreateParams CreateParams {
			get {
				CreateParams createParams = base.CreateParams;

				int WS_EX_NOACTIVATE = 0x08000000;
				int WS_EX_LAYERED = 0x80000;
				int WS_EX_TRANSPARENT = 0x20;
				createParams.ExStyle |= WS_EX_NOACTIVATE;
				createParams.ExStyle |= WS_EX_LAYERED;
				createParams.ExStyle |= WS_EX_TRANSPARENT;

				return createParams;
			}
		}

		// This form is only a host for the tray icons and the UI message pump; it must
		// NEVER be visible. The previous approach (WindowState=Minimized + layered styles)
		// did not actually prevent display: Windows could restore and repaint it on events
		// like session unlock, DPI/display changes, or an Explorer/taskbar restart, leaving a
		// stray "Windows Virtual Desktop Manager" dialog with no close button (ControlBox=false).
		// Overriding SetVisibleCore guarantees the window can never be shown, while still
		// creating its handle so Invoke(), the message pump, and the NotifyIcons keep working.
		protected override void SetVisibleCore(bool value) {
			if (!this.IsHandleCreated) this.CreateHandle(); // ensure handle for Invoke/pump/tray
			base.SetVisibleCore(false);                     // but never actually show it
		}

		// Because the form is never shown, the Load event no longer fires, so the startup
		// wiring that used to live in AppForm_Load now runs here, once the handle exists.
		protected override void OnHandleCreated(EventArgs e) {
			base.OnHandleCreated(e);
			if (_startupDone) return;
			_startupDone = true;
			StartUp();
		}

		private void StartUp() {
			App.Instance.ShowSplash();
			App.Instance.StartVDNotifications();
			App.Instance.MonitorVDSwitch();
			App.Instance.MonitorSystemThemeSwitch();
			App.Instance.MonitorVDisplayCount();
			App.Instance.MonitorFGWindowName();
			App.Instance.MonitorFocusedWindow();

			// Update permanent overlay
			App.Instance.UpdateStatusOverlayWindows();

			App.Instance.UIUpdate();

			App.Instance.StartConfigWatcher();
			App.Instance.NotifyHotKeyConflicts();
		}

		// System events we react to instead of polling
		private static readonly int WM_TASKBARCREATED = (int)Util.OS.RegisterWindowMessage("TaskbarCreated");
		private const int WM_SETTINGCHANGE = 0x001A;
		private const int WM_DISPLAYCHANGE = 0x007E;
		private const int WM_DPICHANGED = 0x02E0;

		protected override void WndProc(ref Message m) {
			base.WndProc(ref m);
			if (App.Instance == null || !_startupDone) return;
			try {
				if (m.Msg == WM_TASKBARCREATED && WM_TASKBARCREATED != 0) {
					// explorer.exe (re)started: its virtual desktop API objects and our notification
					// registration are gone. Deferred a bit, so the shell is ready when we reconnect
					var timer = new Timer { Interval = 2000 };
					timer.Tick += (s, e) => { timer.Stop(); timer.Dispose(); App.Instance.OnExplorerRestarted(); };
					timer.Start();
				} else if (m.Msg == WM_SETTINGCHANGE) {
					var area = m.LParam != IntPtr.Zero ? System.Runtime.InteropServices.Marshal.PtrToStringUni(m.LParam) : null;
					if (area == "ImmersiveColorSet") App.Instance.CheckThemeChanged(); // dark/light mode switched
				} else if (m.Msg == WM_DISPLAYCHANGE || m.Msg == WM_DPICHANGED) {
					// Scaling or monitors changed: re-render the tray icons for the new size
					App.Instance.UIUpdateIcons();
				}
			} catch (Exception ex) {
				Util.Logging.WriteLine("AppForm: Error handling window message " + m.Msg + ": " + ex.Message);
			}
		}

		private void AppForm_Load(object sender, EventArgs e) {
			// Intentionally empty: startup now runs from OnHandleCreated (see SetVisibleCore).
		}

		private void AppForm_Shown(object sender, EventArgs e) {
			
		}

		private void AppForm_FormClosed(object sender, FormClosedEventArgs e) {

		}

		private void AppForm_FormClosing(object sender, FormClosingEventArgs e) {
			if(e.CloseReason == CloseReason.UserClosing) {
				e.Cancel = true;
				Hide();
			} else if(e.CloseReason == CloseReason.ApplicationExitCall || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) {
				// Remove all notif icons
				notifyIconName.Visible = false;
				notifyIconNumber.Visible = false;
				notifyIconPrev.Visible = false;
				notifyIconNext.Visible = false;
			}
		}

		#endregion

		#region Menu and Icon Tray Events

		private void contextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e) {
			var tag = e.ClickedItem.Tag as string; // the desktop items have no tag, they handle their own clicks
			if(tag == "exit") App.Instance.Exit();
			else if(tag == "settings") App.Instance.ShowSettings();
			else if(tag == "about") App.Instance.ShowAbout();
			else if(tag == "donate") App.Instance.OpenDonatePage();
		}

		// Note: NotifyIcon.Click is raised for right-clicks too, so we use MouseClick and check the button.
		// A quick second click is delivered as a double-click (and no second MouseClick), so the same
		// handler serves MouseDoubleClick, otherwise every second click of a rapid series would be lost
		private void notifyIconPrev_MouseClick(object sender, MouseEventArgs e) {
			if(e.Button == MouseButtons.Left) App.Instance.SwitchDesktopBackward();
		}

		private void notifyIconNext_MouseClick(object sender, MouseEventArgs e) {
			if(e.Button == MouseButtons.Left) App.Instance.SwitchDesktopForward();
		}

		private void notifyIconName_MouseClick(object sender, MouseEventArgs e) {
			if(Settings.GetBool("feature.showDesktopNumberInIconTray.clickToOpenTaskView")) {
				if(e.Button == MouseButtons.Left) {
					// Already open?
					if(App.Instance.IsTaskViewOpen()) {
						// Do nothing
					} else {
						Util.OS.OpenTaskView();
					}
				}
			}
		}

		private void notifyIconNumber_MouseClick(object sender, MouseEventArgs e) {
			if (Settings.GetBool("feature.showDesktopNumberInIconTray.clickToOpenTaskView")) {
				if(e.Button == MouseButtons.Left) {
					// Already open?
					if(App.Instance.IsTaskViewOpen()) {
						// Do nothing
					} else {
						Util.OS.OpenTaskView();
					}
				}
			}
		}

		#endregion




	}
}
