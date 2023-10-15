namespace rstparser;
using tinydocutils;

/// Special handling for version change directives.
///
/// These directives include one required argument and an optional argument on the next line.
/// We need to ensure that these are both included in the `argument` field of the AST, and that
/// subsequent indented directives are included as children of the node.

class BaseVersionDirective
{
    public static DirectiveDefinition Make()
    {
        return new DirectiveDefinition
        {
            RequiredArguments = 1,
            OptionalArguments = 1,
            FinalArgumentWhitespace = false,
            HasContent = true,
            OptionSpec = new(),
            Run = Run
        };
    }

    public static List<Node> Run(
        DirectiveDefinition directiveDefinition,
        string name,
        List<string> arguments,
        Dictionary<string, object> options,
        StringList content,
        int lineno,
        int contentOffset,
        string blockText,
        RSTState state,
        RSTStateMachine stateMachine)
    {
        var (source, line) = stateMachine.GetSourceAndLine(lineno);
        var node = new Directive("", name);
        node.Document = state.Document;
        node.Source = source;
        node.Line = line;
        node.Options = options;

        if (arguments.Count > 0)
        {
            var splitArguments = string.Join(' ', arguments).Split(null, 1);
            var textnodes = new List<tinydocutils.Node>();
            foreach (var argumentText in splitArguments)
            {
                var (text, messages) = state.InlineText(argumentText, lineno);
                textnodes.AddRange(text);
            }
            var argument = new DirectiveArgument("", "");
            argument.Children = textnodes;
            argument.Document = state.Document;
            argument.Source = source;
            argument.Line = line;
            node.Add(argument);
        }

        if (content.Count > 0)
        {
            state.NestedParse(
                content, contentOffset, node, match_titles: true
            );
        }

        return new() { node };
    }
}


/// Variant of BaseVersionDirective for the deprecated directive, which does not
/// require an argument.
class DeprecatedVersionDirective : BaseVersionDirective
{

    public new static DirectiveDefinition Make()
    {
        return new DirectiveDefinition
        {
            RequiredArguments = 0,
            OptionalArguments = 1,
            FinalArgumentWhitespace = false,
            HasContent = true,
            OptionSpec = new(),
            Run = Run
        };
    }
}
