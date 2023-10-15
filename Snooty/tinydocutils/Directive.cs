namespace tinydocutils;

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

public enum DiagnosticLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Severe = 4

}

public class DirectiveError : Exception
{
    public DiagnosticLevel Level { get; init; }
    public DirectiveError(DiagnosticLevel level, string message) : base(message)
    {
        Level = level;
    }

    public static void AssertHasContent(string name, StringList content)
    {
        if (content.Count == 0)
        {
            throw new DirectiveError(
                DiagnosticLevel.Error,
                $"Content block expected for the '{name}' directive; none found."
            );
        }
    }
}

public record class DirectiveDefinition
{
    // Number of required directive arguments.
    public int RequiredArguments { get; init; } = 0;

    // Number of optional arguments after the required arguments.
    public int OptionalArguments { get; init; } = 0;

    // May the final argument contain whitespace?
    public bool FinalArgumentWhitespace { get; init; } = false;

    // Mapping of option names to validator functions.
    public required Dictionary<string, Func<string, object>> OptionSpec { get; init; }

    // May the directive have content?
    public bool HasContent { get; init; } = false;

    public delegate List<Node> RunPrototype(
        DirectiveDefinition definition,
        string name,
        List<string> arguments,
        Dictionary<string, object> options,
        StringList content,
        int lineno,
        int content_offset,
        string block_text,
        RSTState state,
        RSTStateMachine state_machine
    );
    public required RunPrototype Run;
}


public class ReplaceDirective
{
    public static DirectiveDefinition Make()
    {
        return new DirectiveDefinition
        {
            RequiredArguments = 0,
            OptionalArguments = 0,
            FinalArgumentWhitespace = false,
            HasContent = true,
            OptionSpec = new(),
            Run = Run
        };
    }

    public static List<Node> Run(
            DirectiveDefinition definition,
            string name,
            List<string> arguments,
            Dictionary<string, object> options,
            StringList content,
            int lineno,
            int content_offset,
            string block_text,
            RSTState state,
            RSTStateMachine state_machine
        )
    {
        if (state is not SubstitutionDefState)
        {
            throw new DirectiveError(
                DiagnosticLevel.Error,
                $"Invalid context: the '{name}' directive can only be used within " +
                "a substitution definition."
            );
        }

        DirectiveError.AssertHasContent(name, content);

        var text = String.Join('\n', content);
        var element = new Element(text);
        state.NestedParse(content, content_offset, element);
        // element might contain [paragraph] + system_message(s)
        Element? node = null;
        var messages = new List<Node>();
        foreach (var elem in element)
        {
            if (node is null && elem is Paragraph parNode)
            {
                node = parNode;
            }
            else if (elem is SystemMessage sysMessage)
            {
                messages.Add(sysMessage);
            }
            else
            {
                throw new DirectiveError(
                    DiagnosticLevel.Error,
                    "Error in '{name}' directive: may contain a single paragraph " +
                    "only."
                );
            }
        }
        if (node is not null)
        {
            return messages.Concat(node.Children).ToList();
        }
        return messages;
    }
}

public partial class UnicodeDirective
{
    // Convert Unicode character codes (numbers) to characters.  Codes may be
    // decimal numbers, hexadecimal numbers (prefixed by ``0x``, ``x``, ``\x``,
    // ``U+``, ``u``, or ``\u``; e.g. ``U+262E``), or XML-style numeric character
    // entities (e.g. ``&#x262E;``).  Text following ".." is a comment and is
    // ignored.  Spaces are ignored, and any other text remains as-is.

    public static DirectiveDefinition Make()
    {
        return new DirectiveDefinition
        {
            RequiredArguments = 1,
            OptionalArguments = 0,
            FinalArgumentWhitespace = true,
            HasContent = false,
            OptionSpec = new() {
                { "trim", Validators.Flag },
                { "ltrim", Validators.Flag },
                { "rtrim", Validators.Flag },
            },
            Run = Run
        };
    }


    [GeneratedRegex("""( |\n|^)\.\. """)]
    private static partial Regex COMMENT_PATTERN();


    public static List<Node> Run(
            DirectiveDefinition definition,
            string name,
            List<string> arguments,
            Dictionary<string, object> options,
            StringList content,
            int lineno,
            int content_offset,
            string block_text,
            RSTState state,
            RSTStateMachine state_machine
        )
    {
        if (state is not SubstitutionDefState)
        {
            throw new DirectiveError(
                DiagnosticLevel.Error,
                $"Invalid context: the '{name}' directive can only be used within " +
                "a substitution definition."
            );
        }

        var substitution_definition = state_machine.Node!;
        Debug.Assert(substitution_definition is not null);
        if (options.ContainsKey("trim"))
        {
            substitution_definition.Attributes["ltrim"] = true;
            substitution_definition.Attributes["rtrim"] = true;
        }
        if (options.ContainsKey("ltrim"))
        {
            substitution_definition.Attributes["ltrim"] = true;
        }
        if (options.ContainsKey("rtrim"))
        {
            substitution_definition.Attributes["rtrim"] = true;
        }
        var codes = COMMENT_PATTERN().Split(arguments[0])[0].Split();
        var element = new Element();
        foreach (var code in codes)
        {
            try
            {
                var decoded = Util.UnicodeCode(code);
                element.Add(new Text(decoded));
            }
            catch (ArgumentException error)
            {
                throw new DirectiveError(DiagnosticLevel.Error, $"Invalid character code: {code}\n{error.Message}");
            }
        }
        return element.Children;
    }
}
