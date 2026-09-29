using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper {
	public partial class ErrorForm : Form {
		public ErrorForm() {
			InitializeComponent();
		}

		public void UpdateUIForError(Exception e) {
			this.labelError.Text = e.Message;
			this.textBoxDetails.Text = e.Message;
			if (e.StackTrace != null) this.textBoxDetails.Text += "\r\n" + e.StackTrace.ToString();
			if (e.InnerException != null) this.textBoxDetails.Text += "\r\n\r\n" + e.InnerException.Message;
			if (e.InnerException != null && e.InnerException.StackTrace != null) this.textBoxDetails.Text += "\r\n" + e.InnerException.StackTrace.ToString();
			this.textBoxDetails.Text += "\r\n";
			this.textBoxDetails.Text += "\r\n" + "Windows Build: " + GetWindowsBuildVersion() + "."+ GetWindowsBuildRevision();
			//this.textBoxDetails.Text += "\r\n" + "Windows Release: " + GetWindowsReleaseId();
			//this.textBoxDetails.Text += "\r\n" + "Windows Product: " + GetWindowsProductName();
			this.textBoxDetails.Text += "\r\n" + "Windows Version: " + GetWindowsDisplayVersion();
			this.textBoxDetails.Text += "\r\n" + "Windows Virtual Desktop Helper Version: " + GetAppBuildVersion();
			this.textBoxDetails.Text += "\r\n" + "Virtual Desktop Implementation: " + App.DetectedVDImplementation;
			this.textBoxDetails.Text += "\r\n";
			this.textBoxDetails.Text += "\r\n" + "Log:";
			this.textBoxDetails.Text += "\r\n" + string.Join("\r\n", Util.Logging.GetLogHistory());
		}

		private string GetAppBuildVersion() {
			try {
				return Assembly.GetExecutingAssembly().GetName().Version.ToString();
			} catch (Exception e) {
				return "Error getting app version: " + e.Message;
			}
		}

		private string GetWindowsBuildVersion() {
			try {
				return Util.OS.GetWindowsBuildVersion().ToString();
			} catch(Exception e) {
				return "Error getting build version: " + e.Message;
			}
		}

		private string GetWindowsBuildRevision() {
			try {
				return Util.OS.GetWindowsBuildRevision().ToString();
			} catch(Exception e) {
				return "Error getting build revision: " + e.Message;
			}
		}

		private string GetWindowsProductName() {
			try {
				return Util.OS.GetWindowsProductName();
			} catch (Exception e) {
				return "Error getting build version: " + e.Message;
			}
		}

		private string GetWindowsDisplayVersion() {
			try {
				return Util.OS.GetWindowsDisplayVersion();
			} catch (Exception e) {
				return "Error getting display version: " + e.Message;
			}
		}

		private string GetWindowsReleaseId() {
			try {
				return Util.OS.GetWindowsReleaseId().ToString();
			} catch (Exception e) {
				return "Error getting release id: " + e.Message;
			}
		}

		private void buttonExit_Click(object sender, EventArgs e) {
			Application.Exit();
			System.Environment.Exit(1);
		}

		private void buttonOpenIssue_Click(object sender, EventArgs e) {
			try {
				Clipboard.SetText(this.textBoxDetails.Text);
			} catch (Exception) {
				// the clipboard can be locked by another app
			}
		}

		private void button1_Click(object sender, EventArgs e) {
			var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsVirtualDesktopHelper");
			if (System.IO.Directory.Exists(dir)) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
		}
	}
}
