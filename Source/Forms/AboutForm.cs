using System;
using System.Reflection;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper {
	public partial class AboutForm : Form {
		public AboutForm() {
			InitializeComponent();
		}

		private void AboutForm_Load(object sender, EventArgs e) {
			labelVersion.Text = "version "
				+ Assembly.GetExecutingAssembly().GetName().Version.Major
				+ "."
				+ Assembly.GetExecutingAssembly().GetName().Version.Minor;
		}

		private void linkLabel4_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
			var logForm = new Forms.LogForm();
			logForm.Text = "Log";
			logForm.SetLogText(string.Join("\n", Util.Logging.GetLogHistory()));
			logForm.Show();
		}

		private void linkLabel5_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
			var logForm = new Forms.LogForm();
			logForm.Text = "Config";
			var txt = "";
			txt += "Using Config File(s): ";
			txt += "\n\n" + string.Join("\n", Settings.GetUsedConfigFiles());
			txt += "\n\n";
			txt += "Your Config: ";
			txt += "\n\n" + Settings.GetSettingsAsString();
			txt += "\n\n";
			txt += "Configuration Options: ";
			txt += "\n\n" + Settings.GetDocumentationAsString();
			txt += "\n\n";
			txt += "Configuration Table: ";
			txt += "\n\n" + Settings.GetDocumentationAsMarkdown();
			logForm.SetLogText(txt);
			logForm.Show();
		}
	}
}
