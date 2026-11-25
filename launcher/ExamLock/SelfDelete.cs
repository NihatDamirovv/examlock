using System;
using System.Diagnostics;
using System.IO;

namespace ExamLock;

internal static class SelfDelete
{
	private static bool scheduled;

	public static void Schedule()
	{
		if (scheduled)
		{
			return;
		}
		string processPath = Environment.ProcessPath;
		if (processPath == null)
		{
			return;
		}
		scheduled = true;
		string root = AppDirs.Root;
		string value = Path.Combine(Path.GetTempPath(), ".net", Path.GetFileNameWithoutExtension(processPath));
		bool flag = !File.Exists(Path.Combine(root, "restore.json"));
		string text = $"del /f /q \"{processPath}\" 2>nul & rmdir /s /q \"{value}\" 2>nul";
		if (flag)
		{
			text = text + " & rmdir /s /q \"" + root + "\" 2>nul";
		}
		string arguments = $"/d /c ping 127.0.0.1 -n 4 >nul & {text} & ping 127.0.0.1 -n 4 >nul & {text} & ping 127.0.0.1 -n 8 >nul & {text}";
		try
		{
			Process.Start(new ProcessStartInfo("cmd.exe", arguments)
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = Path.GetTempPath()
			});
		}
		catch
		{
		}
	}
}
