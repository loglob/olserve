using System.Reflection.Metadata.Ecma335;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Olserve;

public record Config( Dictionary<string, BuildConfig> Routes, ushort Port = 80 );

public record BuildConfig( string Link, string? MainFile = null, HooksConfig? Hooks = null );

public record HooksConfig(
	HookConfig? OnCompileFailed = null
);

[JsonConverter(typeof(HookConverter))]
public record HookConfig(
	string Program,
	string[] Args
);

public class HookConverter : JsonConverter<HookConfig>
{
	// accepts 
	//		"foo" -> HookConfig(Program: "foo")
	// 		["foo"] -> HookConfig(Program: "foo") 
	//		["foo", "bar"] -> HookConfig(Program: "foo", Args: ["bar"])
	//		[] is a format error
	
	public override HookConfig? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if(reader.TokenType == JsonTokenType.String)
			return new(reader.GetString()!, []);

		if(reader.TokenType != JsonTokenType.StartArray)
			throw new JsonException("Expected hook to be an array of strings");

		List<string> parts = [];

		while(reader.Read())
		{
			if(reader.TokenType == JsonTokenType.EndArray)
			{
				if(parts.Count == 0)
					throw new JsonException("Hook must specify at least a program");

				return new(parts[0], [..parts.Skip(1)]);
			}

			if(reader.TokenType != JsonTokenType.String)
				throw new JsonException($"Expected hook to be an array of strings, found {reader.TokenType}");

			parts.Add(reader.GetString()!);
		}

		throw new JsonException("Unterminated hook array");
	}

	public override void Write(Utf8JsonWriter writer, HookConfig value, JsonSerializerOptions options)
	{
		writer.WriteStartArray();
		writer.WriteStringValue(value.Program);

		foreach(var arg in value.Args)
			writer.WriteStringValue(arg);

		writer.WriteEndArray();
	}
}