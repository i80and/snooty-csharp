using System.Globalization;
using System.Text.Json.Serialization;

public interface IMakeCorrectionMixin
{
    public IReadOnlyList<string> DidYouMean();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization, IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(UnexpectedIndentation), "UnexpectedIndentation")]
[JsonDerivedType(typeof(InvalidURL), "InvalidURL")]
[JsonDerivedType(typeof(ExpectedPathArg), "ExpectedPathArg")]
[JsonDerivedType(typeof(UnnamedPage), "UnnamedPage")]
[JsonDerivedType(typeof(ExpectedImageArg), "ExpectedImageArg")]
[JsonDerivedType(typeof(ImageSuggested), "ImageSuggested")]
[JsonDerivedType(typeof(InvalidField), "InvalidField")]
[JsonDerivedType(typeof(GitMergeConflictArtifactFound), "GitMergeConflictArtifactFound")]
[JsonDerivedType(typeof(DocUtilsParseError), "DocUtilsParseError")]
[JsonDerivedType(typeof(ErrorParsingYAMLFile), "ErrorParsingYAMLFile")]
[JsonDerivedType(typeof(InvalidDirectiveStructure), "InvalidDirectiveStructure")]
[JsonDerivedType(typeof(InvalidInclude), "InvalidInclude")]
[JsonDerivedType(typeof(InvalidLiteralInclude), "InvalidLiteralInclude")]
[JsonDerivedType(typeof(SubstitutionRefError), "SubstitutionRefError")]
[JsonDerivedType(typeof(InvalidContextError), "InvalidContextError")]
[JsonDerivedType(typeof(ConstantNotDeclared), "ConstantNotDeclared")]
[JsonDerivedType(typeof(InvalidTableStructure), "InvalidTableStructure")]
[JsonDerivedType(typeof(MissingOption), "MissingOption")]
[JsonDerivedType(typeof(MissingRef), "MissingRef")]
[JsonDerivedType(typeof(MalformedGlossary), "MalformedGlossary")]
[JsonDerivedType(typeof(FailedToInheritRef), "FailedToInheritRef")]
[JsonDerivedType(typeof(RefAlreadyExists), "RefAlreadyExists")]
[JsonDerivedType(typeof(UnknownSubstitution), "UnknownSubstitution")]
[JsonDerivedType(typeof(TargetNotFound), "TargetNotFound")]
[JsonDerivedType(typeof(AmbiguousTarget), "AmbiguousTarget")]
[JsonDerivedType(typeof(ChildlessRef), "ChildlessRef")]
[JsonDerivedType(typeof(TodoInfo), "TodoInfo")]
[JsonDerivedType(typeof(UnmarshallingError), "UnmarshallingError")]
[JsonDerivedType(typeof(CannotOpenFile), "CannotOpenFile")]
[JsonDerivedType(typeof(CannotRenderOpenAPI), "CannotRenderOpenAPI")]
[JsonDerivedType(typeof(MissingTocTreeEntry), "MissingTocTreeEntry")]
[JsonDerivedType(typeof(InvalidTocTree), "InvalidTocTree")]
[JsonDerivedType(typeof(InvalidIAEntry), "InvalidIAEntry")]
[JsonDerivedType(typeof(UnknownTabset), "UnknownTabset")]
[JsonDerivedType(typeof(UnknownTabID), "UnknownTabID")]
[JsonDerivedType(typeof(TabMustBeDirective), "TabMustBeDirective")]
[JsonDerivedType(typeof(IncorrectMonospaceSyntax), "IncorrectMonospaceSyntax")]
[JsonDerivedType(typeof(IncorrectLinkSyntax), "IncorrectLinkSyntax")]
[JsonDerivedType(typeof(MalformedRelativePath), "MalformedRelativePath")]
[JsonDerivedType(typeof(MissingTab), "MissingTab")]
[JsonDerivedType(typeof(ExpectedTabs), "ExpectedTabs")]
[JsonDerivedType(typeof(DuplicateDirective), "DuplicateDirective")]
[JsonDerivedType(typeof(RemovedLiteralBlockSyntax), "RemovedLiteralBlockSyntax")]
[JsonDerivedType(typeof(UnsupportedFormat), "UnsupportedFormat")]
[JsonDerivedType(typeof(FetchError), "FetchError")]
[JsonDerivedType(typeof(InvalidChild), "InvalidChild")]
[JsonDerivedType(typeof(ConfigurationProblem), "ConfigurationProblem")]
[JsonDerivedType(typeof(ChapterAlreadyExists), "ChapterAlreadyExists")]
[JsonDerivedType(typeof(InvalidChapter), "InvalidChapter")]
[JsonDerivedType(typeof(MissingChild), "MissingChild")]
[JsonDerivedType(typeof(GuideAlreadyHasChapter), "GuideAlreadyHasChapter")]
[JsonDerivedType(typeof(IconMustBeDefined), "IconMustBeDefined")]
abstract public class Diagnostic
{
    public enum Level : int
    {
        info = 1,
        warning = 2,
        error = 3
    }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("line")]
    public int Line { get; set; }

    public Diagnostic(string message, int startLine)
    {
        Message = message;
        Line = startLine;
    }

    public static Diagnostic.Level Severity;

    public string SeverityString()
    {
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Severity.ToString());
    }

    public object Serialize()
    {
        var diag = new Dictionary<string, object> { };
        diag["severity"] = SeverityString().ToUpperInvariant();
        diag["start"] = Line;
        diag["message"] = Message;
        return diag;
    }

}
class UnexpectedIndentation : Diagnostic, IMakeCorrectionMixin
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public UnexpectedIndentation(int startLine) : base("Unexpected indentation", startLine)
    {
    }
    public IReadOnlyList<string> DidYouMean()
    {
        return new List<string> { ".. blockquote::" };
    }

}
class InvalidURL : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidURL(int startLine) : base("Invalid URL", startLine) { }

}
class ExpectedPathArg : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string Name { get; init; }
    public ExpectedPathArg(string name, int startLine) : base($"\"{name}\" expected a path argument", startLine)
    {
        Name = name;
    }

}
class UnnamedPage : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string Filename { get; init; }
    public UnnamedPage(string filename, int startLine) : base($"Page title not found: {filename}", startLine)
    {
        Filename = filename;
    }

}
class ExpectedImageArg : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ExpectedImageArg(string message, int startLine) : base(message, startLine) { }

}
class ImageSuggested : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.info;
    public ImageSuggested(string name, int startLine) : base($"\"{name}\" expected an image argument", startLine) { }
}
class InvalidField : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidField(string message, int startLine) : base(message, startLine) { }

}
class GitMergeConflictArtifactFound : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public GitMergeConflictArtifactFound(string? path, int startLine) : base($"Git merge conflict artifact found{((path is null) ? " in " + path : "")} on line {startLine.ToString()}", startLine) { }

}
class DocUtilsParseError : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.warning;
    public DocUtilsParseError(string message, int startLine) : base(message, startLine) { }

}
class ErrorParsingYAMLFile : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ErrorParsingYAMLFile(string? path, string reason, int startLine) : base(((path is null) ? $"Error parsing YAML file {path}: {reason}" : $"Error parsing YAML: {reason}"), startLine) { }

}
class InvalidDirectiveStructure : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidDirectiveStructure(string msg, int startLine) : base($"Directive \"io-code-block\" {msg}", startLine) { }
}
class InvalidInclude : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public InvalidInclude(string msg, int startLine) : base(msg, startLine) { }

}
class InvalidLiteralInclude : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidLiteralInclude(string message, int startLine) : base(message, startLine) { }

}
class SubstitutionRefError : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public SubstitutionRefError(string msg, int startLine) : base(msg, startLine) { }

}
class InvalidContextError : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidContextError(string name, int startLine) : base($"Cannot substitute block elements into an inline context: |{name}|", startLine) { }

}
class ConstantNotDeclared : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ConstantNotDeclared(string name, int startLine) : base($"{name} not defined as a source constant", startLine) { }
}
class InvalidTableStructure : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidTableStructure(string message, int startLine) : base(message, startLine) { }

}
class MissingOption : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public MissingOption(int startLine) : base("'.. option::' must follow '.. program::'", startLine) { }
}
class MissingRef : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public MissingRef(string name, int startLine) : base($"Missing ref; all {name} must define a ref", startLine) { }
}
class MalformedGlossary : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public MalformedGlossary(int startLine) : base("Malformed glossary: glossary must contain only a definition list", startLine) { }
}
class FailedToInheritRef : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public FailedToInheritRef(string message, int startLine) : base(message, startLine) { }

}
class RefAlreadyExists : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public RefAlreadyExists(string message, int startLine) : base(message, startLine) { }

}
class UnknownSubstitution : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.warning;
    public UnknownSubstitution(string message, int startLine) : base(message, startLine) { }

}
class TargetNotFound : Diagnostic, IMakeCorrectionMixin
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string Name { get; init; }
    public string Target { get; init; }
    private List<string> _candidates;
    public TargetNotFound(string name, string target, IEnumerable<string> candidates, int startLine) : base($"Target not found: \"{name}:{target}\"", startLine)
    {
        Name = name;
        Target = target;
        _candidates = candidates.ToList();
    }
    public IReadOnlyList<string> DidYouMean()
    {
        return _candidates;
    }

}
class AmbiguousTarget : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public AmbiguousTarget(string name, string target, List<string> candidates, int startLine) : base($"Ambiguous target: \"{name}:{target}\". Locations: {string.Join(", ", candidates)}", startLine) { }

}

class ChildlessRef : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ChildlessRef(string target, int startLine) : base($"Reference found without label: \"{target}\". Be sure to add label text to the :ref: itself OR place the target reference directly before a heading", startLine) { }
}

class TodoInfo : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.info;
    public TodoInfo(string message, int startLine) : base(message, startLine) { }

}
class UnmarshallingError : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string Reason { get; init; }
    public UnmarshallingError(string reason, int startLine) : base($"Unmarshalling Error: {reason}", startLine)
    {
        Reason = reason;
    }

}
class CannotOpenFile : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string? Path { get; init; }
    public string Reason { get; init; }

    public CannotOpenFile(string? path, string reason, int startLine) : base($"Error opening{((string.IsNullOrEmpty(path)) ? "" : ' ' + path)}: {reason}", startLine)
    {
        Path = path;
        Reason = reason;
    }

}
class CannotRenderOpenAPI : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string Path { get; init; }
    public string Reason { get; init; }

    public CannotRenderOpenAPI(string path, string reason, int startLine) : base($"Failed to render OpenAPI template for {path}: {reason}", startLine)
    {
        Path = path;
        Reason = reason;
    }

}
class MissingTocTreeEntry : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public MissingTocTreeEntry(string entry, int startLine) : base($"Could not locate toctree entry {entry}", startLine) { }
}
class InvalidTocTree : Diagnostic, IMakeCorrectionMixin
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidTocTree(int startLine) : base("Projects with both \"toctree\" and \"ia\" directives are not supported", startLine) { }
    public IReadOnlyList<string> DidYouMean()
    {
        return new List<string> { ".. ia::" };
    }

}
class InvalidIAEntry : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidIAEntry(string msg, int startLine) : base($"Invalid IA entry: {msg}", startLine) { }

}
class UnknownTabset : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public UnknownTabset(string tabset, int startLine) : base($"Tabset \"{tabset}\" is not defined in rstspec.toml", startLine) { }

}
class UnknownTabID : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public UnknownTabID(string tabid, string tabset, string reason, int startLine) : base($"tab id \"{tabid}\" given in \"{tabset}\" tabset is unrecognized: {reason}", startLine) { }

}
class TabMustBeDirective : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public TabMustBeDirective(string tab_type, int startLine) : base($"Tabs or Tab sets may only contain tab directives, but found {tab_type}", startLine) { }
}
class IncorrectMonospaceSyntax : Diagnostic, IMakeCorrectionMixin
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.warning;
    string _text;
    public IncorrectMonospaceSyntax(string text, int startLine) : base("Monospace text uses two backticks (``)", startLine)
    {
        _text = text;
    }
    public IReadOnlyList<string> DidYouMean()
    {
        return new List<string> { $"``{_text}``" };
    }

}
class IncorrectLinkSyntax : Diagnostic, IMakeCorrectionMixin
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    (string, string) _parts;
    public IncorrectLinkSyntax((string, string) parts, int startLine) : base("Malformed external link", startLine)
    {
        _parts = parts;
    }
    public IReadOnlyList<string> DidYouMean()
    {
        return new List<string> { $"`{_parts.Item1} <{_parts.Item2}>`__" };
    }

}
class MalformedRelativePath : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public string RelativePath { get; init; }
    public MalformedRelativePath(string relativePath, int startLine) : base($"Malformed relative path {relativePath}", startLine)
    {
        RelativePath = relativePath;
    }

}
class MissingTab : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public IReadOnlyCollection<string> Tabs { get; init; }
    public MissingTab(IReadOnlyCollection<string> tabs, int startLine) : base($"One or more set of tabs on this page was missing the following tab(s): {string.Join(", ", tabs)}", startLine)
    {
        Tabs = tabs;
    }

}
class ExpectedTabs : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ExpectedTabs(int startLine) : base("Expected tabs directive when tabs-selector directive in use", startLine) { }
}
class DuplicateDirective : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public string Name { get; init; }
    public DuplicateDirective(string name, int startLine) : base($"Directive \"{name}\" should only appear once per page", startLine)
    {
        Name = name;
    }

}
class RemovedLiteralBlockSyntax : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public RemovedLiteralBlockSyntax(int startLine) : base("Literal block syntax is unsupported; use a code-block directive instead", startLine) { }
}
class UnsupportedFormat : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public UnsupportedFormat(string actual, IEnumerable<string> expected, int startLine) : base($"Unsupported file format: {actual}. Must be one of {string.Join(", ", expected)}", startLine) { }

}
class FetchError : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public FetchError(string message, int startLine) : base($"Failed to download file: {message}", startLine) { }
}
class InvalidChild : Diagnostic, IMakeCorrectionMixin
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;

    public string Parent { get; init; }
    public string Suggestion { get; init; }

    public InvalidChild(string child, string parent, string suggestion, int startLine) : base($"{child} is not a valid child of {parent}", startLine)
    {
        Parent = parent;
        Suggestion = suggestion; ;
    }

    public IReadOnlyList<string> DidYouMean()
    {
        return new List<string> { $".. {Suggestion}::" };
    }

}
class ConfigurationProblem : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ConfigurationProblem(string message, int startLine) : base(message, startLine) { }

}
class ChapterAlreadyExists : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public ChapterAlreadyExists(string chapter_name, int startLine) : base($"Chapter \"{chapter_name}\" already exists", startLine) { }
}
class InvalidChapter : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public InvalidChapter(string message, int startLine) : base($"Invalid chapter: {message}", startLine) { }

}
class MissingChild : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public MissingChild(string directive, string expected_child, int startLine) : base($"Directive \"{directive}\" expects at least one child of type \"{expected_child}\"; found 0", startLine) { }

}
class GuideAlreadyHasChapter : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public GuideAlreadyHasChapter(string guide_slug, string assigned_chapter, string target_chapter, int startLine) : base($"Cannot add guide \"{guide_slug}\" to chapter \"{target_chapter}\" because the guide is already assigned to chapter \"{assigned_chapter}\"", startLine) { }

}
class IconMustBeDefined : Diagnostic
{
    public static new Diagnostic.Level Severity = Diagnostic.Level.error;
    public IconMustBeDefined(string icon_role, int startLine) : base($"The Icon {icon_role} does not exist", startLine) { }
}
