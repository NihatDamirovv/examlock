using System;
using System.Drawing;
using System.Windows.Forms;

namespace ExamLock;

internal sealed class CoverForm : Form
{
	protected override bool ShowWithoutActivation => true;

	public CoverForm(Rectangle bounds)
	{
		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.Manual;
		Bounds = bounds;
		BackColor = Color.Black;
		TopMost = true;
		ShowInTaskbar = false;
		Controls.Add(new Label
		{
			Text = "İmtahan əsas ekranda davam edir",
			ForeColor = Color.FromArgb(120, 120, 120),
			Font = new Font("Segoe UI", 16f),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter
		});
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		Native.HideFromCapture(Handle);
	}
}
