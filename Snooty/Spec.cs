/// Indicates the target protocol of a target: either a file local to the
/// current project, or a URL (from an intersphinx inventory).
public enum TargetType
{
    fileid,
    url
}

public enum FormattingType
{
    strong,
    monospace,
    emphasis
}

public enum RstTargetType
{
    plain,
    callable,
    cmdline_option
}

public class MissingList<T> : List<T> { }

public class RstObjectSpec
{
    public string? inherit;
    public string? help;
    public string? domain;
    public string prefix = "";
    public RstTargetType type = RstTargetType.plain;
    public bool deprecated = false;
    public string name = "";
    public IReadOnlyList<object> fields = new MissingList<object>();
    public IReadOnlySet<FormattingType> format = new HashSet<FormattingType> { FormattingType.monospace };
}

public class Spec
{
    public Dictionary<string, RstObjectSpec> rstobject = new Dictionary<string, RstObjectSpec>();

    public static Spec Get()
    {
        return new Spec();
    }

    public string StripPrefixFromName(string rstobjectId, string title)
    {
        return "";
    }
}
