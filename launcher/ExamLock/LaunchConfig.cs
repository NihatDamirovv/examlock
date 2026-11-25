using System.Text.Json.Serialization;

namespace ExamLock;

internal sealed class LaunchConfig
{
	[JsonPropertyName("s")]
	public string Server { get; init; } = "";

	[JsonPropertyName("c")]
	public string Code { get; init; } = "";

	[JsonPropertyName("k")]
	public string Key { get; init; } = "";

	public string ServerBase => Server.TrimEnd('/');
}
