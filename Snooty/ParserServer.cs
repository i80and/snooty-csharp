using System.Text.Json;
using System.Text.Json.Serialization;

class ParserServer
{
    private rstparser.Registry? _registry;
    private readonly tinydocutils.OptionParser _settings = new();

    public void SetupRegistry()
    {
        _registry = new rstparser.Registry(null, new());
        _registry.Activate(_settings);
    }

    public void Parse(string path, string text)
    {
        var document = tinydocutils.Document.New(path, _settings);
        var parser = new tinydocutils.Parser();
        parser.Parse(text, document);
    }
}
