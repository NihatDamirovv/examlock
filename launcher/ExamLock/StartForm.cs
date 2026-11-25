using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExamLock;

internal sealed class StartForm : Form
{
	private static readonly Color Accent = Color.FromArgb(37, 99, 235);

	private readonly Api api;

	private readonly Label title = new Label();

	private readonly Label apps = new Label();

	private readonly Label error = new Label();

	private readonly TextBox name = new TextBox();

	private readonly Button start = new Button();

	private readonly Timer appsTimer = new Timer
	{
		Interval = 2000
	};

	public LaunchInfo? Launched { get; private set; }

	private readonly LaunchConfig cfg;

	public StartForm(Api api, LaunchConfig cfg)
	{
		this.api = api;
		this.cfg = cfg;
		Text = "İmtahan";
		Font = new Font("Segoe UI", 10f);
		StartPosition = FormStartPosition.CenterScreen;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		BackColor = Color.White;
		ClientSize = new Size(560, 430);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(32, 28, 32, 24),
			ColumnCount = 1,
			AutoSize = false
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		title.Text = "Yüklənir…";
		title.Font = new Font("Segoe UI", 17f, FontStyle.Bold);
		title.ForeColor = Color.FromArgb(17, 24, 39);
		title.AutoSize = true;
		title.MaximumSize = new Size(490, 0);
		title.Margin = new Padding(0, 0, 0, 8);
		Label label = new Label
		{
			Text = "Ad və soyadınızı yazıb «İmtahana başla» düyməsini basın. İmtahan bitənə qədər kompüterdə başqa proqram və ya sayt açmaq mümkün olmayacaq.",
			ForeColor = Color.FromArgb(75, 85, 99),
			AutoSize = true,
			MaximumSize = new Size(490, 0),
			Margin = new Padding(0, 0, 0, 22)
		};
		Label label2 = new Label
		{
			Text = "Ad, soyad",
			AutoSize = true,
			Font = new Font("Segoe UI", 10f, FontStyle.Bold),
			Margin = new Padding(0, 0, 0, 4)
		};
		name.Font = new Font("Segoe UI", 13f);
		name.Width = 490;
		name.MaxLength = 100;
		name.Margin = new Padding(0, 0, 0, 14);
		apps.ForeColor = Color.FromArgb(146, 64, 14);
		apps.AutoSize = true;
		apps.MaximumSize = new Size(490, 0);
		apps.Margin = new Padding(0, 0, 0, 8);
		error.ForeColor = Color.FromArgb(185, 28, 28);
		error.AutoSize = true;
		error.MaximumSize = new Size(490, 0);
		error.Margin = new Padding(0, 0, 0, 10);
		start.Text = "İmtahana başla";
		start.Size = new Size(490, 46);
		start.FlatStyle = FlatStyle.Flat;
		start.FlatAppearance.BorderSize = 0;
		start.BackColor = Accent;
		start.ForeColor = Color.White;
		start.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
		start.Cursor = Cursors.Hand;
		start.Click += async (object? _, EventArgs _) =>
		{
			await StartAsync();
		};
		Control[] array = new Control[7] { title, label, label2, name, apps, error, start };
		foreach (Control value in array)
		{
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutPanel.Controls.Add(value);
		}
		Controls.Add(tableLayoutPanel);
		AcceptButton = start;
		appsTimer.Tick += (object? _, EventArgs _) =>
		{
			RefreshApps();
		};
	}

	protected override async void OnShown(EventArgs e)
	{
		base.OnShown(e);
		name.Focus();
		RefreshApps();
		appsTimer.Start();
		try
		{
			var (text, flag, text2) = await api.Info();
			title.Text = text;
			Text = "İmtahan — " + text;
			if (text2 != null)
			{
				error.Text = text2;
				start.Enabled = false;
				name.Enabled = false;
				start.BackColor = Color.FromArgb(156, 163, 175);
				// Bu kompüterdə artıq bitirilib — exe-ni yığışdırırıq
				FormClosed += (object? _, FormClosedEventArgs _) =>
				{
					SelfDelete.RemoveSelf();
				};
			}
			else if (!flag)
			{
				error.Text = "Bu imtahan hazırda aktiv deyil. Müəllim imtahanı açanda yenidən cəhd edin.";
			}
		}
		catch (ApiException ex)
		{
			title.Text = "Serverə qoşulmaq olmadı";
			error.Text = ex.Message;
		}
	}

	private void RefreshApps()
	{
		List<string> list = ProcessGuard.FindRunning();
		apps.Text = ((list.Count == 0) ? "" : ("Başlayanda bu proqramlar avtomatik bağlanacaq (yadda saxlanmamış işlər itə bilər): " + string.Join(", ", list)));
	}

	private async Task StartAsync()
	{
		string text = string.Join(' ', name.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
		if (text.Length < 3)
		{
			error.Text = "Ad və soyadınızı tam yazın.";
			name.Focus();
			return;
		}
		error.Text = "";
		start.Enabled = false;
		name.Enabled = false;
		start.Text = "Başlayır…";
		try
		{
			LaunchInfo launched = await api.Launch(text);
			appsTimer.Stop();
			ProcessGuard.KillAll();
			Launched = launched;
			Close();
		}
		catch (ApiException ex)
		{
			error.Text = ex.Message;
			start.Enabled = true;
			name.Enabled = true;
			start.Text = "İmtahana başla";
		}
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		appsTimer.Dispose();
		base.OnFormClosed(e);
	}
}
