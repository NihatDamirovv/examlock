using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ExamLock;

// İmtahan proqramının öz faylını kompüterdən yığışdırır.
//
// Windows işləyən proqramın öz exe faylını SİLMƏYƏ icazə vermir (fayl kilidlidir),
// amma onu EYNİ QOVLUQDA başqa ada DƏYİŞMƏYƏ icazə verir. Ona görə:
//   1) «Sil» basılanda exe öz adını gizli «.~exlkdel_*.tmp» adına dəyişir —
//      tələbənin gördüyü «Imtahan-*.exe» dərhal Yükləmələrdən yox olur;
//   2) gizli qalıq fayl növbəti dəfə istənilən imtahan exe-si açılanda silinir
//      (CleanupLeftovers), çünki o an fayl işləmir və silinə bilir.
//
// Qeyd: işləyən faylı dərhal fiziki silmək (cmd ilə, və ya «yenidən başlayanda sil»
// ilə) bu kompüterlərdə ya Windows Defender tərəfindən bloklanır, ya da administrator
// icazəsi tələb edir — ona görə o yollardan istifadə olunmur. Təkrar imtahanın
// qarşısı serverdə alınır (hər kompüterdən bir dəfə), buna görə qalıq fayl təhlükəsizdir.
internal static class SelfDelete
{
	private const string LeftoverPrefix = ".~exlkdel_";

	private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool MoveFileExW(string existing, string? newName, uint flags);

	// «Sil» düyməsi çağırır. İşləyən exe-ni gözdən itirir; true = ad dəyişdi.
	public static bool RemoveSelf()
	{
		try
		{
			string? self = Environment.ProcessPath;
			if (self == null || !File.Exists(self))
			{
				return true;
			}
			string dir = Path.GetDirectoryName(self) ?? Path.GetTempPath();
			string hidden = Path.Combine(dir, LeftoverPrefix + Guid.NewGuid().ToString("N") + ".tmp");
			File.Move(self, hidden);
			try
			{
				File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);
			}
			catch
			{
			}
			// Administrator olanda qalıq fayl yenidən başlayanda tam silinir (admin yoxdursa təsirsizdir).
			try
			{
				MoveFileExW(hidden, null, MOVEFILE_DELAY_UNTIL_REBOOT);
			}
			catch
			{
			}
			return true;
		}
		catch
		{
			// Ad dəyişmək alınmasa (nadir hal) — qalıq faylı sonra təmizlənsin deyə işarələyirik
			return false;
		}
	}

	// Program başlayanda çağırılır: əvvəlki imtahanların gizli qalıq fayllarını silir.
	public static void CleanupLeftovers()
	{
		foreach (string dir in CommonFolders())
		{
			try
			{
				foreach (string f in Directory.EnumerateFiles(dir, LeftoverPrefix + "*.tmp"))
				{
					try
					{
						File.SetAttributes(f, FileAttributes.Normal);
						File.Delete(f);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
	}

	private static System.Collections.Generic.IEnumerable<string> CommonFolders()
	{
		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var list = new System.Collections.Generic.List<string>
		{
			Path.Combine(profile, "Downloads"),
			Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			Path.GetTempPath()
		};
		string? oneDrive = Environment.GetEnvironmentVariable("OneDrive");
		if (!string.IsNullOrEmpty(oneDrive))
		{
			list.Add(Path.Combine(oneDrive, "Desktop"));
			list.Add(Path.Combine(oneDrive, "Downloads"));
			list.Add(Path.Combine(oneDrive, "Documents"));
		}
		var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string d in list)
		{
			if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && seen.Add(d))
			{
				yield return d;
			}
		}
	}
}
