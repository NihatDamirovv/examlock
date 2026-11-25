using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ExamLock;

internal sealed class Api
{
	private readonly HttpClient http;

	private readonly LaunchConfig cfg;

	public static readonly string MachineId = ComputeMachineId();

	public string? SessionToken { get; private set; }

	public Api(LaunchConfig cfg)
	{
		this.cfg = cfg;
		http = new HttpClient
		{
			BaseAddress = new Uri(cfg.ServerBase + "/"),
			Timeout = TimeSpan.FromSeconds(15L)
		};
		http.DefaultRequestHeaders.UserAgent.ParseAdd("ExamLock/1.0");
	}

	private async Task<JsonElement> Post(string path, object body, CancellationToken ct = default(CancellationToken))
	{
		HttpResponseMessage resp;
		try
		{
			resp = await http.PostAsJsonAsync(path, body, ct);
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) ? true : false)
		{
			throw new ApiException("Serverə qoşulmaq mümkün olmadı. İnternet bağlantısını yoxlayın.");
		}
		string json = await resp.Content.ReadAsStringAsync(ct);
		JsonElement result;
		try
		{
			result = JsonDocument.Parse(json).RootElement.Clone();
		}
		catch
		{
			throw new ApiException($"Serverdən gözlənilməz cavab gəldi ({(int)resp.StatusCode}).");
		}
		JsonElement value;
		if (!resp.IsSuccessStatusCode)
		{
			throw new ApiException(result.TryGetProperty("error", out value) ? (value.GetString() ?? "Server xətası") : "Server xətası");
		}
		return result;
	}

	private static string ComputeMachineId()
	{
		string text = "";
		try
		{
			using RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
			text = (registryKey?.GetValue("MachineGuid") as string) ?? "";
		}
		catch
		{
		}
		if (text.Length == 0)
		{
			text = Environment.MachineName;
		}
		return Hash.Sha256Hex("examlock-machine:" + text.ToLowerInvariant());
	}

	public async Task<(string Title, bool Active, string? FinishedMessage)> Info()
	{
		JsonElement jsonElement = await Post("api/client/info", new
		{
			c = cfg.Code,
			k = cfg.Key,
			m = MachineId
		});
		string item = ((jsonElement.TryGetProperty("finished", out var value) && value.ValueKind == JsonValueKind.True) ? jsonElement.GetProperty("finishedMessage").GetString() : null);
		return (Title: jsonElement.GetProperty("title").GetString() ?? "", Active: jsonElement.GetProperty("active").GetBoolean(), FinishedMessage: item);
	}

	public async Task<LaunchInfo> Launch(string student)
	{
		JsonElement jsonElement = await Post("api/client/launch", new
		{
			c = cfg.Code,
			k = cfg.Key,
			m = MachineId,
			student = student,
			machine = Environment.MachineName,
			user = Environment.UserName,
			os = Environment.OSVersion.VersionString
		});
		SessionToken = jsonElement.GetProperty("session").GetString();
		string[] allowed = (from x in jsonElement.GetProperty("allowed").EnumerateArray()
			select x.GetString() ?? "" into x
			where x.Length > 0
			select x).ToArray();
		string quitHash = ((jsonElement.TryGetProperty("quitHash", out var value) && value.ValueKind == JsonValueKind.String) ? value.GetString() : null);
		return new LaunchInfo(jsonElement.GetProperty("title").GetString() ?? "", student, cfg.ServerBase + jsonElement.GetProperty("go").GetString(), allowed, quitHash);
	}

	public async Task<string> Renew()
	{
		JsonElement jsonElement = await Post("api/client/renew", new
		{
			token = SessionToken
		});
		return cfg.ServerBase + jsonElement.GetProperty("go").GetString();
	}

	public void Event(string type, string detail = "")
	{
		if (SessionToken == null)
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				await Post("api/client/event", new
				{
					token = SessionToken,
					type = type,
					detail = detail
				});
			}
			catch
			{
			}
		});
	}

	public async Task EventAsync(string type, string detail, TimeSpan timeout)
	{
		if (SessionToken == null)
		{
			return;
		}
		using CancellationTokenSource cts = new CancellationTokenSource(timeout);
		try
		{
			await Post("api/client/event", new
			{
				token = SessionToken,
				type = type,
				detail = detail
			}, cts.Token);
		}
		catch
		{
		}
	}
}
