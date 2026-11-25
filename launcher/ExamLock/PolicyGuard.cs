using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ExamLock;

internal static class PolicyGuard
{
	private sealed class Saved
	{
		public string Key { get; set; } = "";

		public string Name { get; set; } = "";

		public int? Value { get; set; }
	}

	private const string SystemKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";

	private const string ExplorerKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";

	private static readonly (string Key, string Name)[] Items = new (string, string)[4]
	{
		("Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", "DisableTaskMgr"),
		("Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", "DisableLockWorkstation"),
		("Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", "DisableChangePassword"),
		("Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer", "NoLogoff")
	};

	private static readonly object gate = new object();

	private static string BackupFile => Path.Combine(AppDirs.Root, "restore.json");

	public static void Apply()
	{
		lock (gate)
		{
			if (File.Exists(BackupFile))
			{
				return;
			}
			List<Saved> list = new List<Saved>();
			(string, string)[] items = Items;
			for (int i = 0; i < items.Length; i++)
			{
				var (text, name) = items[i];
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(text);
				list.Add(new Saved
				{
					Key = text,
					Name = name,
					Value = ((registryKey?.GetValue(name) is int value) ? new int?(value) : ((int?)null))
				});
			}
			File.WriteAllText(BackupFile, JsonSerializer.Serialize(list));
			items = Items;
			for (int i = 0; i < items.Length; i++)
			{
				var (subkey, name2) = items[i];
				try
				{
					using RegistryKey registryKey2 = Registry.CurrentUser.CreateSubKey(subkey);
					registryKey2.SetValue(name2, 1, RegistryValueKind.DWord);
				}
				catch
				{
				}
			}
		}
	}

	public static void RestoreIfNeeded()
	{
		lock (gate)
		{
			try
			{
				if (!File.Exists(BackupFile))
				{
					return;
				}
				foreach (Saved item in JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(BackupFile)) ?? new List<Saved>())
				{
					try
					{
						using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(item.Key);
						int? value = item.Value;
						if (value.HasValue)
						{
							int valueOrDefault = value.GetValueOrDefault();
							registryKey.SetValue(item.Name, valueOrDefault, RegistryValueKind.DWord);
						}
						else
						{
							registryKey.DeleteValue(item.Name, throwOnMissingValue: false);
						}
					}
					catch
					{
					}
				}
				File.Delete(BackupFile);
			}
			catch
			{
			}
		}
	}
}
