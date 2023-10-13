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

public enum DateFormattingType
{
    iso_8601
}


public class MissingList<T> : List<T> { }
public class MissingDict<T> : Dictionary<string, T> { }

/// Configuration for a directive that specifies a date
public class DateType
{

    public DateFormattingType date { get; set; } // default=DateFormattingType.iso_8601);
}

/// Meta information about the file as a whole.
public record class Meta
{
    public int Version { get; set; }
}

public class DirectiveOption
{
    public object Type { get; set; }
    public bool Required { get; set; }
}

public class TabDefinition
{
    public string Id { get; set; }
    public string Title { get; set; }
}


/// Configuration for a role which links to a specific URL template.
public class LinkRoleType
{
    public string Link { get; set; }
    public bool? EnsureTrailingSlash { get; set; }
    public HashSet<FormattingType> Format { get; set; } = new();

    //     def __post_init__(self) -> None:
    //         if self.link.count("%s") != 1:
    //             raise ValueError(
    //                 f"Link definitions in rstspec.toml need to contain a single '%s' placeholder: {self.link}"
    //             )
}

/// Configuration for a role which links to an optionally namespaced target.
public class RefRoleType
{
    public string? Domain { get; set; }
    public string Name { get; set; }
    public string? Tag { get; set; }
    public HashSet<FormattingType> Format { get; set; } = new() { FormattingType.monospace };
}

/// <summary>
///  Declaration of a reStructuredText directive (block content).
/// </summary>
public class DirectiveSpec
{
    private HashSet<string>? _requiredOptions;

    public string? Inherit { get; set; }
    public string? Help { get; set; }
    public string? Example { get; set; }
    public object ContentType { get; set; }
    public object ArgumentType { get; set; }
    public string? Required_context { get; set; }
    public string? Domain { get; set; }
    public bool Deprecated { get; set; } = false;
    public Dictionary<string, object> Options { get; set; } = new MissingDict<object>();
    public List<object> Fields { get; set; } = new MissingList<object>();
    public string Name { get; set; } = "";
    public RstObjectSpec? Rstobject = null;

    public IEnumerable<string> RequiredOptions
    {
        get
        {
            if (_requiredOptions is null)
            {
                _requiredOptions = new(Options.Keys.Where(key =>
                {
                    var value = Options[key];
                    if (value is DirectiveOption directiveOption)
                    {
                        return directiveOption.Required;
                    }
                    return false;
                }));
            }

            return _requiredOptions;
        }
    }
}

/// Declaration of a reStructuredText role (inline content).
public class RoleSpec
{
    public string? Inherit { get; set; }
    public string? Help { get; set; }
    public string? Example { get; set; }
    public object? Type { get; set; }
    public string? Domain { get; set; }
    public bool Deprecated { get; set; }
    public string Name { get; set; }
    public RstObjectSpec? Rstobject { get; set; }
}

public class RstObjectSpec
{
    public string? Inherit { get; set; }
    public string? Help { get; set; }
    public string? Domain { get; set; }
    public string Prefix { get; set; } = "";
    public RstTargetType Type { get; set; } = RstTargetType.plain;
    public bool Deprecated { get; set; } = false;
    public string Name { get; set; } = "";
    public List<object> Fields { get; set; } = new MissingList<object>();
    public Dictionary<string, object> Options { get; set; } = new MissingDict<object>();
    public HashSet<FormattingType> Format { get; set; } = new HashSet<FormattingType> { FormattingType.monospace };
}

public class BuildSettingsSpec
{
    public string? cache_url_prefix { get; set; }
}


public class Spec
{
    public Meta Meta { get; set; } = new Meta { Version = 0 };

    public Dictionary<string, List<string>> Enum { get; set; } = new();
    public BuildSettingsSpec Build { get; set; } = new();
    public Dictionary<string, DirectiveSpec> Directive { get; set; } = new();
    public Dictionary<string, RoleSpec> Role { get; set; } = new();
    public Dictionary<string, RstObjectSpec> Rstobject { get; set; } = new();
    public Dictionary<string, List<TabDefinition>> Tabs { get; set; } = new();
    public List<string> DataFields { get; set; } = new();

    public static Spec Get()
    {
        return new Spec();
    }

    public string StripPrefixFromName(string rstobjectId, string title)
    {
        return "";
    }
}
