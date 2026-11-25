using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExamLock;

internal static class ConfigReader
{
	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EXLKCFG1");

	private static byte[] Key => SHA256.HashData(Encoding.UTF8.GetBytes("ExamLock/overlay/v1/7f3c9a2e5b814d6a"));

	public static LaunchConfig? Read()
	{
		try
		{
			string processPath = Environment.ProcessPath;
			if (processPath == null)
			{
				return null;
			}
			using FileStream fileStream = new FileStream(processPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (fileStream.Length < 64)
			{
				return null;
			}
			byte[] array = new byte[12];
			fileStream.Seek(-12L, SeekOrigin.End);
			fileStream.ReadExactly(array);
			if (!((ReadOnlySpan<byte>)array.AsSpan(4)).SequenceEqual((ReadOnlySpan<byte>)Magic))
			{
				return null;
			}
			int num = BitConverter.ToInt32(array, 0);
			if (num < 32 || num > 65536 || num > fileStream.Length - 12)
			{
				return null;
			}
			byte[] array2 = new byte[num];
			fileStream.Seek(-12 - num, SeekOrigin.End);
			fileStream.ReadExactly(array2);
			using Aes aes = Aes.Create();
			aes.Key = Key;
			LaunchConfig launchConfig = JsonSerializer.Deserialize<LaunchConfig>(aes.DecryptCbc(array2.AsSpan(16), array2.AsSpan(0, 16)));
			if (launchConfig == null || launchConfig.Server.Length == 0 || launchConfig.Code.Length == 0)
			{
				return null;
			}
			if (!Uri.TryCreate(launchConfig.Server, UriKind.Absolute, out Uri _))
			{
				return null;
			}
			return launchConfig;
		}
		catch
		{
			return null;
		}
	}
}
