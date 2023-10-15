using System.ComponentModel.DataAnnotations;
using tinydocutils;


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

public enum PrimitiveType
{
    Integer,
    Nonnegative_integer,
    Path,
    Uri,
    String,
    Length,
    Boolean,
    Flag,
    Linenos,
}

public enum PrimitiveRoleType
{
    text,
    explicit_title
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
    public required object Type { get; set; }
    public bool Required { get; set; }
}

public class TabDefinition
{
    public required string Id { get; set; }
    public required string Title { get; set; }
}


/// Configuration for a role which links to a specific URL template.
public class LinkRoleType
{
    public required string Link { get; set; }
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
    public required string Name { get; set; }
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
    public required string Name { get; set; }
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

    public DirectiveSpec CreateDirective()
    {
        return new DirectiveSpec
        {
            Inherit = null,
            Help = Help,
            Example = null,
            ContentType = "block",
            ArgumentType = new DirectiveOption { Type = "string", Required = true },
            Required_context = null,
            Domain = Domain,
            Deprecated = Deprecated,
            Options = Options,
            Fields = new(),
            Name = Name,
            Rstobject = this,
        };
    }
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
        var rstobject = Rstobject!.GetValueOrDefault(rstobjectId, null);
        if (rstobject is null)
        {
            return title;
        }

        var candidate = $"{rstobject.Prefix}.";
        if (title.StartsWith(candidate))
        {
            return title[candidate.Length..];
        }

        return title;

    }

    /// Return a validation function for a given argument type. This function will take in a
    /// string, and either throw an exception or return an output value.
    public Func<string, object> GetValidator(object optionSpec)
    {
        if (optionSpec is DirectiveOption directiveOption)
        {
            optionSpec = directiveOption.Type;
        }

        if (optionSpec is List<object> optionSpecList)
        {
            var child_validators = optionSpecList.Select(GetValidator).ToArray();

            object validator(string argument)
            {
                foreach (var child_validator in child_validators)
                {
                    object result;
                    try
                    {
                        result = child_validator(argument);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    return result;
                }

                // Assertion to quiet mypy's failing type flow analysis
                // assert isinstance(option_spec, list)
                var options = string.Join(", ", optionSpecList.Select(spec => spec.ToString()));
                throw new ArgumentException($"Expected one of {options}; got {argument}");
            }

            return validator;
        }
        else if (optionSpec is PrimitiveType optionSpecPrimitiveType)
        {
            return VALIDATORS[optionSpecPrimitiveType];
        }
        else if (optionSpec is string optionSpecString && Enum.ContainsKey(optionSpecString))
        {
            string validator(string argument)
            {
                return Validators.Choice(
                    argument, Enum[optionSpecString]
                );
            }
            return validator;
        }

        throw new ArgumentException($"Unknown directive argument type \"{optionSpec}\"");
    }

    // docutils option validation function for each of the above primitive types
    private static readonly Dictionary<PrimitiveType, Func<string, object>> VALIDATORS = new() {
        { PrimitiveType.Integer,  (val) => int.Parse(val) },
        { PrimitiveType.Nonnegative_integer, Validators.NonnegativeInt },
        { PrimitiveType.Path, Validators.String },
        { PrimitiveType.Uri, Validators.Uri },
        { PrimitiveType.String, Validators.String },
        { PrimitiveType.Length, Validators.LengthOrPercentageOrUnitless },
        { PrimitiveType.Boolean, Validators.Bool },
        { PrimitiveType.Flag, Validators.Flag },
        { PrimitiveType.Linenos, Validators.String },
    };
}
