using System.Text.Json;
using KeystoneDigital.RazerController.Actions;
using NUnit.Framework;

namespace KeystoneDigital.RazerController.Tests.Actions;

[TestFixture]
public sealed class ParameterReaderTests
{
	private static readonly IReadOnlyList<string> AB = ["a", "b"];

	private static Dictionary<string, object?> With(object? value) => new() { ["p"] = value };

	private static JsonElement Json(string json)
	{
		using var doc = JsonDocument.Parse(json);
		return doc.RootElement.Clone();
	}

	[Test]
	public void StringList_reads_a_json_array() => Assert.That(ParameterReader.StringList(With(Json("""["a","b"]""")), "p"), Is.EqualTo(AB));

	[Test]
	public void StringList_reads_a_string_array() => Assert.That(ParameterReader.StringList(With(AB.ToArray()), "p"), Is.EqualTo(AB));

	[Test]
	public void StringList_reads_an_object_list() => Assert.That(ParameterReader.StringList(With(new List<object> { "a", "b" }), "p"), Is.EqualTo(AB));

	[Test]
	public void StringList_reads_a_comma_string() => Assert.That(ParameterReader.StringList(With("a, b,,"), "p"), Is.EqualTo(AB));

	[Test]
	public void StringList_reads_a_json_string_holding_an_array() => Assert.That(ParameterReader.StringList(With(Json("\"[\\\"a\\\",\\\"b\\\"]\"")), "p"), Is.EqualTo(AB));

	[Test]
	public void StringList_is_empty_for_null_or_missing()
	{
		Assert.That(ParameterReader.StringList(With(null), "p"), Is.Empty);
		Assert.That(ParameterReader.StringList(new Dictionary<string, object?>(), "p"), Is.Empty);
	}

	[TestCase(800)]
	[TestCase(800L)]
	[TestCase(800.0)]
	[TestCase("800")]
	public void Int_reads_numbers_in_any_shape(object value) => Assert.That(ParameterReader.Int(With(value), "p"), Is.EqualTo(800));

	[Test]
	public void Int_reads_a_json_number() => Assert.That(ParameterReader.Int(With(Json("800")), "p"), Is.EqualTo(800));

	[TestCase(5_000_000_000L)]
	[TestCase(-5_000_000_000L)]
	[TestCase(1e12)]
	public void Int_is_null_when_out_of_range(object value) => Assert.That(ParameterReader.Int(With(value), "p"), Is.Null);

	[Test]
	public void Int_is_null_for_garbage() => Assert.That(ParameterReader.Int(With("lots"), "p"), Is.Null);

	[Test]
	public void String_reads_a_json_string() => Assert.That(ParameterReader.String(With(Json("\"x\"")), "p"), Is.EqualTo("x"));

	[Test]
	public void String_is_null_for_blank() => Assert.That(ParameterReader.String(With("  "), "p"), Is.Null);
}
