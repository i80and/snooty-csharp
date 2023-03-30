namespace tinydocutils;

using RoleFunctionType = Func<string, string, string, int, Inliner, (List<Node>, List<SystemMessage>)>;

public enum Thresholds
{
    info = 1,
    warning = 2,
    error = 3,
    severe = 4,
    none = 5
}

public class OptionParser
{
    public const bool character_level_inline_markup = false;

    public object? components { get; set; }
    public bool debug { get; set; } = false;

    public Dictionary<string, string?> settings { get; set; } = new Dictionary<string, string?>();

    private Dictionary<string, IDirective> _directives = new Dictionary<string, IDirective>();

    public (RoleFunctionType?, List<SystemMessage>) LookupRole(string role_name, int lineno, Reporter reporter)
    {
        throw new Exception();
    }

    public IDirective? LookupDirective(string name)
    {
        try
        {
            return _directives[name];
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public int halt_level { get; set; } = 5;
    public int report_level { get; set; } = 1;
    public bool trim_footnote_reference_space { get; set; } = false;
    public int tab_width { get; set; } = 8;
    public string id_prefix { get; set; } = "";
    public string auto_id_prefix { get; set; } = "id";
}
