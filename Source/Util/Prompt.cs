using System.Drawing;
using System.Windows.Forms;

namespace WindowsVirtualDesktopHelper.Util {
	// A minimal text input dialog
	public static class Prompt {

		// Returns the entered text, or null if cancelled
		public static string Show(string title, string label, string defaultValue) {
			using (var form = new Form()) {
				form.Text = title;
				form.FormBorderStyle = FormBorderStyle.FixedDialog;
				form.StartPosition = FormStartPosition.CenterScreen;
				form.MinimizeBox = false;
				form.MaximizeBox = false;
				form.ShowInTaskbar = true;
				try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (System.Exception) { }
				form.TopMost = true;
				form.AutoScaleMode = AutoScaleMode.Dpi;
				form.AutoSize = true;
				form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
				form.Padding = new Padding(12);
				form.Font = SystemFonts.MessageBoxFont;

				var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
				var labelControl = new Label { Text = label, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 8) };
				var textBox = new TextBox { Text = defaultValue ?? "", Width = 360, Margin = new Padding(0, 0, 0, 12) };
				var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
				var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
				var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
				buttons.Controls.Add(cancel);
				buttons.Controls.Add(ok);
				layout.Controls.Add(labelControl);
				layout.Controls.Add(textBox);
				layout.Controls.Add(buttons);
				form.Controls.Add(layout);
				form.AcceptButton = ok;
				form.CancelButton = cancel;
				form.Shown += (s, e) => { form.Activate(); textBox.Focus(); textBox.SelectAll(); };

				return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
			}
		}
	}
}
