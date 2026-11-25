using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ExamLock;

internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		ApplicationConfiguration.Initialize();
		AppDirs.Ensure();
		PolicyGuard.RestoreIfNeeded();
		AppDirs.CleanOldProfiles();
		SelfDelete.CleanupLeftovers();
		if (args.Contains("--restore"))
		{
			return 0;
		}
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (object _, ThreadExceptionEventArgs e) =>
		{
			Crash(e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += (object _, UnhandledExceptionEventArgs e) =>
		{
			Crash(e.ExceptionObject as Exception);
		};
		AppDomain.CurrentDomain.ProcessExit += (object? _, EventArgs _) =>
		{
			PolicyGuard.RestoreIfNeeded();
		};
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "Local\\ExamLock.SingleInstance", out createdNew))
		{
			if (!createdNew)
			{
				Msg("İmtahan proqramı artıq açıqdır.");
				return 1;
			}
			LaunchConfig launchConfig = ConfigReader.Read();
			if (launchConfig == null)
			{
				Msg("Bu fayl zədələnib və ya müəllim panelindən yüklənməyib.\nFaylı müəllimin verdiyi linkdən yenidən yükləyin.");
				return 1;
			}
			if (SystemCheck.IsRemoteSession())
			{
				Msg("Uzaq masaüstü (Remote Desktop) ilə imtahan vermək olmaz.\nİmtahanı birbaşa öz kompüterinizdə açın.");
				return 1;
			}
			if (SystemCheck.IsVirtualMachine(out string what))
			{
				Msg("Virtual maşında imtahan vermək olmaz.\n(" + what + ")");
				return 1;
			}
			Api api = new Api(launchConfig);
			StartForm startForm = new StartForm(api, launchConfig);
			Application.Run(startForm);
			LaunchInfo launched = startForm.Launched;
			if ((object)launched == null)
			{
				return 0;
			}
			ExamForm examForm = new ExamForm(api, launchConfig, launched);
			Application.Run(examForm);
			PolicyGuard.RestoreIfNeeded();
			Thread.Sleep(800);
			AppDirs.CleanOldProfiles();
			if (examForm.FinishedNormally)
			{
				// Məcburi «Sil» ekranı — tələbə basmadan bağlaya bilmir
				using DeleteForm deleteForm = new DeleteForm();
				Application.Run(deleteForm);
			}
			return 0;
		}
	}

	private static void Crash(Exception? ex)
	{
		PolicyGuard.RestoreIfNeeded();
		try
		{
			File.AppendAllText(Path.Combine(AppDirs.Root, "error.log"), $"{DateTime.Now:u} {ex}\n\n");
		}
		catch
		{
		}
		Msg("Proqramda gözlənilməz xəta baş verdi və imtahan bağlandı.\nFaylı yenidən açıb davam edə bilərsiniz.");
		Environment.Exit(1);
	}

	public static void Msg(string text)
	{
		MessageBox.Show(text, "İmtahan", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
	}
}
