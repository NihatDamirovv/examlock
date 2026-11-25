using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace ExamLock;

internal sealed class ExamForm : Form
{
	private readonly Api api;

	private readonly LaunchConfig cfg;

	private readonly LaunchInfo info;

	private readonly WebView2 web = new WebView2();

	private readonly Label clock = new Label();

	private readonly Label status = new Label();

	private readonly System.Windows.Forms.Timer guardTimer = new System.Windows.Forms.Timer
	{
		Interval = 400
	};

	private readonly System.Windows.Forms.Timer clockTimer = new System.Windows.Forms.Timer
	{
		Interval = 1000
	};

	private readonly System.Windows.Forms.Timer heartbeatTimer = new System.Windows.Forms.Timer
	{
		Interval = 20000
	};

	private readonly List<CoverForm> covers = new List<CoverForm>();

	private readonly Dictionary<uint, bool> webViewPids = new Dictionary<uint, bool>();

	private readonly Dictionary<string, DateTime> lastBlocked = new Dictionary<string, DateTime>();

	private readonly HashSet<string> allowedHosts;

	private readonly string profileDir;

	private System.Threading.Timer? killTimer;

	private KeyboardBlocker? keyboard;

	private bool allowClose;

	private bool foreign;

	private bool cleaned;

	// Son uğurla açılan icazəli səhifə — qadağan keçiddən sonra ora qayıdırıq
	private string? lastGoodUrl;

	// Qadağan keçid bloklandıqdan sonra səhifənin sağlam qalıb-qalmadığını yoxlamaq lazımdır
	private bool recoverPending;

	private readonly List<DateTime> recoveries = new List<DateTime>();

	private readonly Button langButton = MakeButton("⌨  AZ", Color.FromArgb(55, 65, 81));

	public bool FinishedNormally { get; private set; }

	public ExamForm(Api api, LaunchConfig cfg, LaunchInfo info)
	{
		ExamForm examForm = this;
		this.api = api;
		this.cfg = cfg;
		this.info = info;
		Text = info.Title;
		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.Manual;
		ShowInTaskbar = false;
		TopMost = true;
		BackColor = Color.White;
		Bounds = Screen.PrimaryScreen.Bounds;
		profileDir = Path.Combine(AppDirs.Profiles, Guid.NewGuid().ToString("N"));
		allowedHosts = new HashSet<string>(info.Allowed.Select((string h) => h.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase) { new Uri(cfg.ServerBase).Host.ToLowerInvariant() };
		web.Dock = DockStyle.Fill;
		web.DefaultBackgroundColor = Color.White;
		Controls.Add(web);
		Controls.Add(BuildBar());
		guardTimer.Tick += (object? _, EventArgs _) =>
		{
			examForm.Guard();
		};
		clockTimer.Interval = 500;
		clockTimer.Tick += (object? _, EventArgs _) =>
		{
			examForm.clock.Text = DateTime.Now.ToString("HH:mm:ss");
			examForm.UpdateLanguageLabel();
		};
		heartbeatTimer.Tick += (object? _, EventArgs _) =>
		{
			api.Event("heartbeat");
		};
		SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
		SystemEvents.SessionSwitch += OnSessionSwitch;
	}

	private Panel BuildBar()
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 46,
			BackColor = Color.FromArgb(17, 24, 39),
			Padding = new Padding(14, 0, 8, 0)
		};
		Label value = new Label
		{
			Text = info.Title + "   ·   " + info.Student,
			ForeColor = Color.White,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Right,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = false,
			Padding = new Padding(0, 7, 0, 0)
		};
		status.ForeColor = Color.FromArgb(251, 191, 36);
		status.Font = new Font("Segoe UI", 9.5f);
		status.AutoSize = true;
		status.Margin = new Padding(0, 7, 14, 0);
		clock.ForeColor = Color.FromArgb(209, 213, 219);
		clock.Font = new Font("Consolas", 11f);
		clock.AutoSize = true;
		clock.Margin = new Padding(0, 6, 14, 0);
		clock.Text = DateTime.Now.ToString("HH:mm:ss");
		langButton.Click += (object? _, EventArgs _) =>
		{
			SwitchLanguage();
		};
		UpdateLanguageLabel();
		Button button = MakeButton("↻  Yenilə", Color.FromArgb(55, 65, 81));
		button.Click += async (object? _, EventArgs _) =>
		{
			await ReloadAsync();
		};
		Button button2 = MakeButton("Çıxış", Color.FromArgb(185, 28, 28));
		button2.Click += async (object? _, EventArgs _) =>
		{
			await QuitAsync();
		};
		flowLayoutPanel.Controls.AddRange(status, clock, langButton, button, button2);
		panel.Controls.Add(value);
		panel.Controls.Add(flowLayoutPanel);
		return panel;
	}

	private void SwitchLanguage()
	{
		if (!cleaned)
		{
			web.Focus();
			BeginInvoke(() =>
			{
				KeyboardLayouts.Next(Handle);
				BeginInvoke(UpdateLanguageLabel);
			});
		}
	}

	private void UpdateLanguageLabel()
	{
		if (!cleaned && (IsHandleCreated || Handle != IntPtr.Zero))
		{
			string text = "⌨  " + KeyboardLayouts.Label(KeyboardLayouts.Current(Handle));
			if (langButton.Text != text)
			{
				langButton.Text = text;
			}
		}
	}

	private static Button MakeButton(string text, Color color)
	{
		Button button = new Button();
		button.Text = text;
		button.FlatStyle = FlatStyle.Flat;
		button.FlatAppearance.BorderSize = 0;
		button.BackColor = color;
		button.ForeColor = Color.White;
		button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
		button.AutoSize = true;
		button.MinimumSize = new Size(84, 32);
		button.Margin = new Padding(4, 0, 4, 0);
		button.Cursor = Cursors.Hand;
		button.TabStop = false;
		return button;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		Native.HideFromCapture(Handle);
	}

	protected override async void OnShown(EventArgs e)
	{
		base.OnShown(e);
		PolicyGuard.Apply();
		keyboard = new KeyboardBlocker();
		keyboard.LanguageSwitch += () =>
		{
			if (!cleaned)
			{
				BeginInvoke(SwitchLanguage);
			}
		};
		killTimer = new System.Threading.Timer((object? _) =>
		{
			KillApps();
		}, null, 0, 2000);
		ClearClipboard();
		BuildCovers();
		guardTimer.Start();
		clockTimer.Start();
		heartbeatTimer.Start();
		try
		{
			await InitWebAsync();
			web.CoreWebView2.Navigate(info.GoUrl);
		}
		catch (Exception ex)
		{
			status.Text = "Brauzer açılmadı";
			MessageBox.Show(this, "İmtahan brauzeri başlaya bilmədi:\n" + ex.Message + "\n\nKompüterdə «Microsoft Edge WebView2 Runtime» olmalıdır.", "İmtahan", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private async Task InitWebAsync()
	{
		Directory.CreateDirectory(profileDir);
		CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profileDir, new CoreWebView2EnvironmentOptions());
		await web.EnsureCoreWebView2Async(environment);
		CoreWebView2 core = web.CoreWebView2;
		CoreWebView2Settings settings = core.Settings;
		settings.AreDevToolsEnabled = false;
		settings.AreDefaultContextMenusEnabled = false;
		settings.AreBrowserAcceleratorKeysEnabled = false;
		settings.IsStatusBarEnabled = false;
		settings.IsPasswordAutosaveEnabled = false;
		settings.IsGeneralAutofillEnabled = false;
		settings.IsSwipeNavigationEnabled = false;
		settings.AreHostObjectsAllowed = false;
		settings.IsWebMessageEnabled = false;
		settings.UserAgent += " ExamLock/1.0";
		await core.AddScriptToExecuteOnDocumentCreatedAsync("window.print = function () {};");
		core.NavigationStarting += (object? _, CoreWebView2NavigationStartingEventArgs e) =>
		{
			if (!IsAllowed(e.Uri))
			{
				e.Cancel = true;
				ReportBlocked(e.Uri);
				// Yönləndirmə (redirect) bloklananda köhnə səhifə artıq bağlanıb — ekran boş/qara qalır.
				// Adi link bloklananda isə səhifə yerində qalır. Hər iki halda bir az sonra yoxlayırıq.
				recoverPending = true;
				ScheduleRecoveryCheck(e.IsRedirected ? 150 : 700);
			}
		};
		core.NewWindowRequested += (object? _, CoreWebView2NewWindowRequestedEventArgs e) =>
		{
			e.Handled = true;
			if (IsAllowed(e.Uri))
			{
				core.Navigate(e.Uri);
			}
			else
			{
				ReportBlocked(e.Uri);
			}
		};
		core.DownloadStarting += (object? _, CoreWebView2DownloadStartingEventArgs e) =>
		{
			e.Cancel = true;
		};
		core.LaunchingExternalUriScheme += (object? _, CoreWebView2LaunchingExternalUriSchemeEventArgs e) =>
		{
			e.Cancel = true;
		};
		core.NavigationCompleted += (object? _, CoreWebView2NavigationCompletedEventArgs e) =>
		{
			if (e.IsSuccess)
			{
				string src = core.Source ?? "";
				if (IsAllowed(src) && !src.StartsWith(cfg.ServerBase, StringComparison.OrdinalIgnoreCase) && src != "about:blank")
				{
					lastGoodUrl = src;
				}
				if (status.Text.StartsWith("Səhifə") || status.Text.StartsWith("Qadağan sayt"))
				{
					status.Text = "";
				}
			}
			else if (recoverPending)
			{
				ScheduleRecoveryCheck(150);
			}
			else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
			{
				status.Text = "Səhifə açılmadı — «Yenilə» basın";
			}
		};
		core.ProcessFailed += (object? _, CoreWebView2ProcessFailedEventArgs _) =>
		{
			status.Text = "Səhifə xətası — «Yenilə» basın";
		};
	}

	private bool IsAllowed(string uri)
	{
		if (uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
		{
			return uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
		}
		if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri result))
		{
			return false;
		}
		if (result.Scheme != Uri.UriSchemeHttps && result.Scheme != Uri.UriSchemeHttp)
		{
			return false;
		}
		string text = result.IdnHost.ToLowerInvariant();
		foreach (string allowedHost in allowedHosts)
		{
			if (text == allowedHost || text.EndsWith("." + allowedHost, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private void ReportBlocked(string uri)
	{
		string text;
		if (Uri.TryCreate(uri, UriKind.Absolute, out Uri result))
		{
			text = ((result.Host.Length > 0) ? result.Host : result.Scheme);
		}
		else
		{
			text = uri;
		}
		if (text.Length > 120)
		{
			text = text.Substring(0, 120);
		}
		status.Text = "Bu sayta keçid qadağandır: " + text;
		if (!lastBlocked.TryGetValue(text, out var value) || !(DateTime.UtcNow - value < TimeSpan.FromSeconds(10L)))
		{
			lastBlocked[text] = DateTime.UtcNow;
			api.Event("blocked_url", text);
		}
	}

	private void ScheduleRecoveryCheck(int delayMs)
	{
		System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = delayMs };
		t.Tick += async (object? _, EventArgs _) =>
		{
			t.Stop();
			t.Dispose();
			await RecoverIfBrokenAsync();
		};
		t.Start();
	}

	// Qadağan keçiddən sonra: səhifə sağlamdırsa toxunmuruq (tələbənin cavabları itməsin),
	// boş / xəta səhifəsi / icazəsiz ünvandırsa — son icazəli səhifəyə qaytarırıq.
	private async Task RecoverIfBrokenAsync()
	{
		if (cleaned || !recoverPending)
		{
			return;
		}
		CoreWebView2? core = web.CoreWebView2;
		if (core == null)
		{
			return;
		}
		string src = core.Source ?? "";
		bool broken = src.Length == 0 || src == "about:blank" || !IsAllowed(src);
		if (!broken)
		{
			try
			{
				string r = await core.ExecuteScriptAsync(
					"(function(){try{var h=location.href;if(!/^https?:/i.test(h))return 'dead';" +
					"var b=document.body;if(!b)return document.readyState==='loading'?'loading':'dead';" +
					"return (b.childElementCount>0||(b.innerText||'').trim().length>0)?'ok':'dead';}catch(e){return 'dead';}})()");
				if (r.Contains("loading"))
				{
					ScheduleRecoveryCheck(500);
					return;
				}
				broken = !r.Contains("ok");
			}
			catch
			{
				broken = true;
			}
		}
		recoverPending = false;
		if (!broken)
		{
			return;
		}
		// Sonsuz dövrə qarşı: 30 saniyədə ən çox 3 bərpa
		DateTime now = DateTime.UtcNow;
		recoveries.RemoveAll((DateTime d) => now - d > TimeSpan.FromSeconds(30L));
		if (recoveries.Count >= 3)
		{
			status.Text = "İmtahan səhifəsi açılmır — «Yenilə» basın və ya müəllimə müraciət edin";
			return;
		}
		bool firstTry = recoveries.Count == 0;
		recoveries.Add(now);
		status.Text = "Qadağan sayt bağlandı — imtahan səhifəsinə qayıdılır…";
		if (lastGoodUrl != null)
		{
			if (firstTry && core.CanGoBack)
			{
				// «Geri» — brauzer səhifəni yaddaşdan qaytara bilər, yazılmış cavablar qalır
				core.GoBack();
				recoverPending = true;
				ScheduleRecoveryCheck(1500);
			}
			else
			{
				core.Navigate(lastGoodUrl);
			}
			return;
		}
		// İmtahan səhifəsi heç açılmayıbsa — serverdən yeni keçid linki alırıq
		try
		{
			core.Navigate(await api.Renew());
		}
		catch (ApiException ex)
		{
			status.Text = ex.Message;
		}
	}

	private void Guard()
	{
		if (cleaned)
		{
			return;
		}
		nint fg = Native.GetForegroundWindow();
		if (fg == IntPtr.Zero)
		{
			return;
		}
		Native.GetWindowThreadProcessId(fg, out var pid);
		if (pid == (uint)Environment.ProcessId)
		{
			foreign = false;
			if (fg != Handle && covers.Any((CoverForm c) => c.Handle == fg))
			{
				Activate();
			}
			return;
		}
		if (IsWebViewPid(pid))
		{
			foreign = false;
			return;
		}
		string text = Native.ClassName(fg);
		if (!foreign)
		{
			foreign = true;
			api.Event("focus_lost", Describe(pid, text));
		}
		bool flag;
		switch (text)
		{
		case "Shell_TrayWnd":
		case "Shell_SecondaryTrayWnd":
		case "Progman":
		case "WorkerW":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			Native.ShowWindow(fg, 6);
		}
		TopMost = true;
		Native.SwitchToThisWindow(Handle, altTab: true);
		Activate();
	}

	private bool IsWebViewPid(uint pid)
	{
		if (webViewPids.TryGetValue(pid, out var value))
		{
			return value;
		}
		try
		{
			using Process process = Process.GetProcessById((int)pid);
			value = process.ProcessName.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			value = false;
		}
		webViewPids[pid] = value;
		return value;
	}

	private static string Describe(uint pid, string cls)
	{
		try
		{
			using Process process = Process.GetProcessById((int)pid);
			return process.ProcessName;
		}
		catch
		{
			return cls;
		}
	}

	private void KillApps()
	{
		if (cleaned)
		{
			return;
		}
		foreach (string item in ProcessGuard.KillAll())
		{
			api.Event("app_closed", item);
		}
	}

	private void BuildCovers()
	{
		foreach (CoverForm cover in covers)
		{
			cover.Close();
		}
		covers.Clear();
		Screen[] allScreens = Screen.AllScreens;
		foreach (Screen screen in allScreens)
		{
			if (!screen.Primary)
			{
				CoverForm coverForm = new CoverForm(screen.Bounds);
				coverForm.Show();
				covers.Add(coverForm);
			}
		}
	}

	private void OnDisplayChanged(object? sender, EventArgs e)
	{
		if (!cleaned)
		{
			BeginInvoke(() =>
			{
				Bounds = Screen.PrimaryScreen.Bounds;
				BuildCovers();
				api.Event("display", $"{Screen.AllScreens.Length} ekran");
			});
		}
	}

	private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
	{
		if (!cleaned)
		{
			api.Event("session_switch", e.Reason.ToString());
		}
	}

	private static void ClearClipboard()
	{
		try
		{
			Clipboard.Clear();
		}
		catch
		{
		}
	}

	private async Task ReloadAsync()
	{
		CoreWebView2 coreWebView = web.CoreWebView2;
		if (coreWebView == null)
		{
			return;
		}
		string text = coreWebView.Source ?? "";
		if (text.Length == 0 || text == "about:blank" || text.StartsWith(cfg.ServerBase, StringComparison.OrdinalIgnoreCase))
		{
			try
			{
				CoreWebView2 coreWebView2 = coreWebView;
				coreWebView2.Navigate(await api.Renew());
				status.Text = "";
				return;
			}
			catch (ApiException ex)
			{
				status.Text = ex.Message;
				return;
			}
		}
		coreWebView.Reload();
	}

	private async Task QuitAsync()
	{
		if (info.QuitHash == null)
		{
			if (MessageBox.Show(this, "İmtahandan çıxmaq istəyirsiniz?\n\nCavablarınızı GÖNDƏRDİYİNİZƏ əmin olun.", "Çıxış", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
			{
				return;
			}
		}
		else
		{
			using PasswordDialog passwordDialog = new PasswordDialog();
			if (passwordDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			if (!Hash.CheckQuit(cfg.Code, passwordDialog.Password, info.QuitHash))
			{
				api.Event("quit_failed");
				MessageBox.Show(this, "Şifrə səhvdir.", "Çıxış", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
		}
		status.Text = "Çıxılır…";
		await api.EventAsync("quit", "", TimeSpan.FromSeconds(4L));
		FinishedNormally = true;
		allowClose = true;
		Close();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		bool flag = !allowClose;
		if (flag)
		{
			CloseReason closeReason = e.CloseReason;
			bool flag2 = ((closeReason == CloseReason.None || closeReason == CloseReason.UserClosing) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			e.Cancel = true;
			return;
		}
		Cleanup();
		base.OnFormClosing(e);
	}

	private void Cleanup()
	{
		if (cleaned)
		{
			return;
		}
		cleaned = true;
		guardTimer.Stop();
		clockTimer.Stop();
		heartbeatTimer.Stop();
		killTimer?.Dispose();
		keyboard?.Dispose();
		SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
		SystemEvents.SessionSwitch -= OnSessionSwitch;
		PolicyGuard.RestoreIfNeeded();
		ClearClipboard();
		foreach (CoverForm cover in covers)
		{
			cover.Close();
		}
		covers.Clear();
		try
		{
			web.Dispose();
		}
		catch
		{
		}
	}
}
