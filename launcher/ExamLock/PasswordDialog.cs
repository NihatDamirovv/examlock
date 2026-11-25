using System.Drawing;
using System.Windows.Forms;

namespace ExamLock;

internal sealed class PasswordDialog : Form
{
	private readonly TextBox box = new TextBox
	{
		UseSystemPasswordChar = true,
		Width = 310,
		Font = new Font("Segoe UI", 12f)
	};

	public string Password => box.Text;

	public PasswordDialog()
	{
		Text = "Çıxış şifrəsi";
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = (MinimizeBox = false);
		ShowInTaskbar = false;
		TopMost = true;
		Font = new Font("Segoe UI", 10f);
		ClientSize = new Size(360, 160);
		Label label = new Label
		{
			Text = "Müəllimin verdiyi çıxış şifrəsini yazın:",
			AutoSize = true,
			Location = new Point(24, 20)
		};
		box.Location = new Point(24, 50);
		Button button = new Button
		{
			Text = "Çıxış",
			DialogResult = DialogResult.OK,
			Location = new Point(150, 106),
			Size = new Size(90, 34)
		};
		Button button2 = new Button
		{
			Text = "Ləğv et",
			DialogResult = DialogResult.Cancel,
			Location = new Point(248, 106),
			Size = new Size(90, 34)
		};
		Controls.AddRange(label, box, button, button2);
		AcceptButton = button;
		CancelButton = button2;
	}
}
