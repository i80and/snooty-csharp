using System.Text.Json.Serialization;
using System.Text.Json;

public class Page
{
    [JsonInclude]
    [JsonPropertyName("source_path")]
    public string SourcePath { get; init; }

    [JsonInclude]
    [JsonPropertyName("output_filename")]
    public string OutputFilename { get; init; }

    [JsonInclude]
    [JsonPropertyName("ast")]
    public N.Root Ast { get; init; }

    public Page(string sourcePath, string outputFilename, N.Root ast)
    {
        SourcePath = sourcePath;
        OutputFilename = outputFilename;
        Ast = ast;
    }

    // public static Page LoadFromJsonElement(JsonElement element) {
    //     var path = element.GetProperty("source_path").GetString();
    //     var filename = element.GetProperty("output_filename").GetString();
    //     var ast = (N.Root)N.Node.LoadFromJsonElement(element.GetProperty("ast"));
    //     return new Page(path, filename, ast);
    // }
}
