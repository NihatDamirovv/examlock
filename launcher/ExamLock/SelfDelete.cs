using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace ExamLock;

// İmtahan bitəndən sonra proqram bu kompüterdən silinir:
//   1) işləyən exe-nin özü (proqram bağlanandan sonra cmd ilə),
//   2) eyni imtahanın başqa nüsxələri (Yükləmələr, Masaüstü, Sənədlər, Temp — məs. «Imtahan-X (1).exe»),
//   3) Zibil qutusundakı (Recycle Bin) nüsxələr,
//   4) «Son fayllar» (Recent / Quick access) qısayolları.
// Yalnız İÇİNDƏ EYNİ İMTAHAN KODU olan exe-lər silinir — başqa fayllara toxunulmur.
internal static class SelfDelete
{
	private static bool scheduled;

	private const long MaxExeSize = 200L * 1024 * 1024;

	public static void Schedule(LaunchConfig cfg)
	{
		if (scheduled)
		{
			return;
		}
		string? processPath = Environment.ProcessPath;
		if (processPath == null)
		{
			return;
		}
		scheduled = true;

		List<string> names = new List<string> { Path.GetFileName(processPath) };
		try
		{
			DeleteOtherCopies(cfg.Code, processPath, names);
		}
		catch
		{
		}
		try
		{
			DeleteFromRecycleBin(cfg.Code);
		}
		catch
		{
		}
		try
		{
			DeleteRecentShortcuts(names);
		}
		catch
		{
		}

		// İşləyən exe özünü silə bilməz — proqram bağlanandan sonra cmd silir (bir neçə cəhd)
		string root = AppDirs.Root;
		string extractDir = Path.Combine(Path.GetTempPath(), ".net", Path.GetFileNameWithoutExtension(processPath));
		bool removeRoot = !File.Exists(Path.Combine(root, "restore.json"));
		string text = $"del /f /q \"{processPath}\" 2>nul & rmdir /s /q \"{extractDir}\" 2>nul";
		if (removeRoot)
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

	private static bool IsSameExam(string path, string code)
	{
		try
		{
			FileInfo fi = new FileInfo(path);
			if (!fi.Exists || fi.Length < 1024 * 1024 || fi.Length > MaxExeSize)
			{
				return false;
			}
			LaunchConfig? c = ConfigReader.Read(path);
			return c != null && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.SetAttributes(path, FileAttributes.Normal);
			File.Delete(path);
		}
		catch
		{
		}
	}

	private static IEnumerable<string> SearchFolders()
	{
		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		List<string> list = new List<string>
		{
			Path.Combine(profile, "Downloads"),
			Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			Path.GetTempPath()
		};
		// OneDrive ilə sinxronlaşan Masaüstü / Sənədlər
		string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
		if (!string.IsNullOrEmpty(oneDrive))
		{
			list.Add(Path.Combine(oneDrive, "Desktop"));
			list.Add(Path.Combine(oneDrive, "Documents"));
			list.Add(Path.Combine(oneDrive, "Downloads"));
		}
		return list.Where((string d) => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase);
	}

	internal static void DeleteOtherCopies(string code, string self, List<string> names)
	{
		EnumerationOptions opts = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			MaxRecursionDepth = 2,
			IgnoreInaccessible = true,
			AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
		};
		foreach (string dir in SearchFolders())
		{
			IEnumerable<string> files;
			try
			{
				files = Directory.EnumerateFiles(dir, "*.exe", opts).ToList();
			}
			catch
			{
				continue;
			}
			foreach (string f in files)
			{
				if (string.Equals(Path.GetFullPath(f), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (IsSameExam(f, code))
				{
					names.Add(Path.GetFileName(f));
					TryDelete(f);
				}
			}
		}
	}

	// Zibil qutusu: X:\$Recycle.Bin\<istifadəçi SID>\$Rxxxx.exe (fayl) + $Ixxxx.exe (məlumat)
	internal static void DeleteFromRecycleBin(string code)
	{
		string? sid = WindowsIdentity.GetCurrent().User?.Value;
		if (sid == null)
		{
			return;
		}
		foreach (DriveInfo drive in DriveInfo.GetDrives())
		{
			if (!drive.IsReady || drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
			{
				continue;
			}
			string bin = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
			if (!Directory.Exists(bin))
			{
				continue;
			}
			string[] items;
			try
			{
				items = Directory.GetFiles(bin, "$R*.exe");
			}
			catch
			{
				continue;
			}
			foreach (string r in items)
			{
				if (!IsSameExam(r, code))
				{
					continue;
				}
				TryDelete(r);
				TryDelete(Path.Combine(bin, "$I" + Path.GetFileName(r).Substring(2)));
			}
		}
	}

	// «Son fayllar»: %APPDATA%\Microsoft\Windows\Recent\<fayl adı>.lnk
	internal static void DeleteRecentShortcuts(List<string> names)
	{
		string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
		if (string.IsNullOrEmpty(recent) || !Directory.Exists(recent))
		{
			return;
		}
		HashSet<string> wanted = new HashSet<string>(names.Select((string n) => n + ".lnk"), StringComparer.OrdinalIgnoreCase);
		foreach (string lnk in Directory.EnumerateFiles(recent, "*.lnk"))
		{
			string name = Path.GetFileName(lnk);
			// Eyni imtahanın faylı və ya ümumiyyətlə «Imtahan-*.exe» qısayolu
			if (wanted.Contains(name) || name.StartsWith("Imtahan-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe.lnk", StringComparison.OrdinalIgnoreCase))
			{
				TryDelete(lnk);
			}
		}
	}
}
