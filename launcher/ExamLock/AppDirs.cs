using System;
using System.IO;

namespace ExamLock;

internal static class AppDirs
{
	public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExamLock");

	public static string Profiles => Path.Combine(Root, "profiles");

	public static void Ensure()
	{
		Directory.CreateDirectory(Root);
		Directory.CreateDirectory(Profiles);
	}

	public static void CleanOldProfiles()
	{
		try
		{
			string[] directories = Directory.GetDirectories(Profiles);
			foreach (string path in directories)
			{
				try
				{
					Directory.Delete(path, recursive: true);
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
