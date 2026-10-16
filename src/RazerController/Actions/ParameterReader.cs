using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace KeystoneDigital.RazerController.Actions;

/// <summary>
/// Reads action parameters whatever shape the host delivers them in: a JsonElement, a CLR primitive, a
/// list, or a string holding JSON.
/// </summary>
internal static class ParameterReader
{
	public static string? String<T>(IReadOnlyDictionary<string, T> parameters, string name)
	{
		if (!parameters.TryGetValue(name, out var raw) || raw is not object value) return null;

		var text = value switch
		{
			JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
			JsonElement { ValueKind: JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False } json => json.GetRawText(),
			JsonElement => null,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString(),
		};

		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static int? Int<T>(IReadOnlyDictionary<string, T> parameters, string name)
	{
		if (!parameters.TryGetValue(name, out var raw) || raw is not object value) return null;

		return value switch
		{
			int number => number,
			long number => number is >= int.MinValue and <= int.MaxValue ? (int)number : null,
			double number => InRange(number),
			JsonElement { ValueKind: JsonValueKind.Number } json when json.TryGetDouble(out var number) => InRange(number),
			_ => int.TryParse(String(parameters, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
		};
	}

	public static IReadOnlyList<string> StringList<T>(IReadOnlyDictionary<string, T> parameters, string name)
	{
		if (!parameters.TryGetValue(name, out var raw) || raw is not object value) return [];

		return value switch
		{
			JsonElement { ValueKind: JsonValueKind.Array } json => Clean(json.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())),
			JsonElement { ValueKind: JsonValueKind.String } json => FromText(json.GetString()),
			string text => FromText(text),
			IEnumerable items => Clean(items.Cast<object?>().Select(item => item?.ToString())),
			_ => [],
		};
	}

	private static int? InRange(double number) =>
		double.IsFinite(number) && Math.Round(number) is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(number) : null;

	private static List<string> FromText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return [];

		var trimmed = text.Trim();
		if (trimmed.StartsWith('['))
		{
			try
			{
				using var document = JsonDocument.Parse(trimmed);
				return Clean(document.RootElement.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText()));
			}
			catch (JsonException)
			{
			}
		}

		return Clean(trimmed.Split(','));
	}

	private static List<string> Clean(IEnumerable<string?> items) =>
		[.. items.Select(item => item?.Trim()).Where(item => !string.IsNullOrEmpty(item)).Select(item => item!)];
}
