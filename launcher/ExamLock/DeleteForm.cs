using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExamLock;

// İmtahan bitəndən sonra çıxan məcburi ekran: tələbə «Sil» basmadan bağlaya bilmir.
// «Sil» imtahan proqramını kompüterdən yığışdırır (bax: SelfDelete.RemoveSelf).
internal sealed class DeleteForm : Form
{
	private static readonly Color Accent = Color.FromArgb(185, 28, 28);

	private readonly Button delButton = new Button();

	private bool done;

	public DeleteForm()
	{
		Text = "İmtahan bitdi";
		Font = new Font("Segoe UI", 10f);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterScreen;
		MaximizeBox = false;
		MinimizeBox = false;
		ControlBox = false;
		ShowInTaskbar = true;
		TopMost = true;
		BackColor = Color.White;
		ClientSize = new Size(520, 300);

		TableLayoutPanel root = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(34, 30, 34, 26),
			ColumnCount = 1
		};
		root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

		Label title = new Label
		{
			Text = "İmtahanınız müəllimə göndərildi",
			Font = new Font("Segoe UI", 16f, FontStyle.Bold),
			ForeColor = Color.FromArgb(17, 24, 39),
			AutoSize = true,
			MaximumSize = new Size(452, 0),
			Margin = new Padding(0, 0, 0, 12)
		};
		Label text = new Label
		{
			Text = "İmtahan bitdi. İndi imtahan proqramını bu kompüterdən silmək lazımdır.\n\nDavam etmək üçün «Sil» düyməsini basın. Düyməni basmadan pəncərəni bağlamaq olmaz.",
			ForeColor = Color.FromArgb(75, 85, 99),
			AutoSize = true,
			MaximumSize = new Size(452, 0),
			Margin = new Padding(0, 0, 0, 24)
		};

		delButton.Text = "Sil";
		delButton.Size = new Size(452, 50);
		delButton.FlatStyle = FlatStyle.Flat;
		delButton.FlatAppearance.BorderSize = 0;
		delButton.BackColor = Accent;
		delButton.ForeColor = Color.White;
		delButton.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
		delButton.Cursor = Cursors.Hand;
		delButton.Click += OnDelete;

		root.Controls.Add(title);
		root.Controls.Add(text);
		root.Controls.Add(delButton);
		Controls.Add(root);
		AcceptButton = delButton;
	}

	private void OnDelete(object? sender, EventArgs e)
	{
		delButton.Enabled = false;
		delButton.Text = "Silinir…";
		SelfDelete.RemoveSelf();
		done = true;
		Close();
	}

	// «Sil» basılana qədər bağlanmağa icazə vermir (X, Alt+F4, system menu)
	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		if (!done && e.CloseReason != CloseReason.ApplicationExitCall && e.CloseReason != CloseReason.WindowsShutDown)
		{
			e.Cancel = true;
			return;
		}
		base.OnFormClosing(e);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		// Alt+F4 və Escape bağlamasın
		if (keyData == (Keys.Alt | Keys.F4) || keyData == Keys.Escape)
		{
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}
}
