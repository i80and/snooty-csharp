// using System.Text.Json;
// using System.Text.Json.Serialization;
using System.Diagnostics;

// public class StringPairConverter : JsonConverter<(string, string)>
// {
//     public override (string, string) Read(
//         ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//     {
//         if (reader.TokenType != JsonTokenType.StartArray)
//         {
//             throw new JsonException();
//         }

//         reader.Read();
//         if (reader.TokenType != JsonTokenType.String)
//         {
//             throw new JsonException();
//         }
//         var string1 = reader.GetString()!;

//         reader.Read();
//         if (reader.TokenType != JsonTokenType.String)
//         {
//             throw new JsonException();
//         }
//         var string2 = reader.GetString()!;

//         if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
//         {
//             throw new JsonException();
//         }

//         return (string1, string2);
//     }

//     public override void Write(
//         Utf8JsonWriter writer, (string, string) pair, JsonSerializerOptions options)
//     {
//         writer.WriteStartArray();
//         writer.WriteStringValue(pair.Item1);
//         writer.WriteStringValue(pair.Item2);
//         writer.WriteEndArray();
//     }
// }

// public class IntPairConverter : JsonConverter<(int, int)>
// {
//     public override (int, int) Read(
//         ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//     {
//         if (reader.TokenType != JsonTokenType.StartArray)
//         {
//             throw new JsonException();
//         }

//         reader.Read();
//         if (reader.TokenType != JsonTokenType.Number)
//         {
//             throw new JsonException();
//         }
//         var string1 = reader.GetInt32()!;

//         reader.Read();
//         if (reader.TokenType != JsonTokenType.Number)
//         {
//             throw new JsonException();
//         }
//         var string2 = reader.GetInt32()!;

//         if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
//         {
//             throw new JsonException();
//         }

//         return (string1, string2);
//     }

//     public override void Write(
//         Utf8JsonWriter writer, (int, int) pair, JsonSerializerOptions options)
//     {
//         writer.WriteStartArray();
//         writer.WriteNumberValue(pair.Item1);
//         writer.WriteNumberValue(pair.Item2);
//         writer.WriteEndArray();
//     }
// }

// public class FileIdConverter : JsonConverter<FileId>
// {
//     public override FileId Read(
//         ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//     {
//         return new FileId(reader.GetString()!);
//     }

//     public override void Write(
//         Utf8JsonWriter writer, FileId fileid, JsonSerializerOptions options)
//     {
//         writer.WriteStringValue(fileid.AsPosix());
//     }
// }

// public class SpanConverter : JsonConverter<N.Span>
// {
//     public override N.Span Read(
//         ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
//     {
//         return new N.Span(reader.GetInt32()!);
//     }

//     public override void Write(
//         Utf8JsonWriter writer, N.Span span, JsonSerializerOptions options)
//     {
//         writer.WriteNumberValue(span.Start);
//     }
// }

// [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
// [JsonSerializable(typeof(PostprocessorResult))]
// [JsonSerializable(typeof(Dictionary<string, Page>))]
// internal partial class SourceGenerationContext : JsonSerializerContext
// {
// }

class MainClass
{
    static public void Main(string[] args)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        for (int i = 0; i < 10000; i += 1) {
            var document = tinydocutils.Document.New("foo.rst", new tinydocutils.OptionParser());
            var parser = new tinydocutils.Parser();
            parser.Parse("\n" + """
    :template: product-landing
    :hidefeedback: header
    :noprevnext:

    ================
    What is MongoDB?
    ================

    This is a test.
    """, document);
        }

        stopwatch.Stop();
        Console.WriteLine((double)(stopwatch.ElapsedMilliseconds) / 1000.0);
        //     var projectConfig = new ProjectConfig("/home/heli/work/docs-mongodb-internal/");
        //     var targetDatabase = new TargetDatabase();
        //     var cancellationToken = new CancellationToken();
        //     var postprocessor = new Postprocessor(projectConfig, targetDatabase, cancellationToken);

        //     var deserializeOptions = new JsonSerializerOptions
        //     {
        //         Converters = {
        //             new FileIdConverter(),
        //             new SpanConverter(),
        //             new StringPairConverter(),
        //             new IntPairConverter()
        //         },
        //         // TypeInfoResolver = SourceGenerationContext.Default
        //     };

        //     var stdin = Console.OpenStandardInput();
        //     Dictionary<string, Page> pages = new Dictionary<string, Page>();

        //     string text;
        //     using (var sr = new StreamReader(stdin, Console.InputEncoding))
        //     {
        //         text = sr.ReadToEnd();
        //     }

        //     Stopwatch stopWatch = new Stopwatch();
        //     stopWatch.Start();

        //     pages = JsonSerializer.Deserialize<Dictionary<string, Page>>(text, deserializeOptions)!;

        //     stopWatch.Stop();
        //     Console.Error.WriteLine($"Deserialization: {stopWatch.Elapsed.TotalSeconds}");

        //     var bahHumbig = new Dictionary<FileId, Page>();
        //     foreach (var (key, page) in pages)
        //     {
        //         bahHumbig.Add(new FileId(key), page);
        //     }


        //     stopWatch.Restart();
        //     var result = postprocessor.Run(bahHumbig);
        //     stopWatch.Stop();
        //     Console.Error.WriteLine($"Postprocessor: {stopWatch.Elapsed.TotalSeconds}");

        //     stopWatch.Restart();
        //     var serialized = JsonSerializer.Serialize(result, deserializeOptions);
        //     stopWatch.Stop();
        //     Console.Error.WriteLine($"Serialization: {stopWatch.Elapsed.TotalSeconds}");
        //     Console.Write(serialized);
    }
}
