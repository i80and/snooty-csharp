namespace rstparser;
using tinydocutils;

/// Special handling for code input/output directives.
///
/// These directives can either take in a filepath or raw code content. If a filepath
/// is present, this should be included in the `argument` field of the AST. If raw code
/// content is present, it should become the value of the child Code node.
class BaseCodeIODirective
{
    public static DirectiveDefinition Make()
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

    public static List<Node> Run(
        DirectiveDefinition definition,
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
        var copyable = !options.ContainsKey("copyable") || (bool)options["copyable"];
        var linenos = options.ContainsKey("linenos");

        var node = new Directive("", name);
        node.Document = state.Document;
        node.Source = source;
        node.Line = line;
        node.Options = options;

        if (arguments.Count > 0)
        {
            var title_node = new Text(arguments[0]);
            var argumentNode = new DirectiveArgument(arguments[0], "");
            argumentNode.Children = new() { title_node };
            node.Add(argumentNode);
        }
        else
        {
            List<(int, int)> emphasizeLines;
            try
            {
                var n_lines = content.Count;
                var emphasize_lines_options = (string)options.GetValueOrDefault("emphasize-lines", "");
                emphasizeLines = BaseCodeDirective.ParseLinenos(emphasize_lines_options, n_lines);
            }
            catch (ArgumentException err)
            {
                var error_node = state.Document!.Reporter.Error(
                    err.ToString(), line = lineno
                );
                return new() { error_node };
            }

            var value = string.Join('\n', content);
            var child_code = new Code(value);
            child_code.EmphasizeLines = emphasizeLines;
            child_code.Linenos = linenos;
            child_code.Copyable = copyable;

            child_code.Document = state.Document;
            child_code.Source = source;
            child_code.Line = line;
            node.Add(child_code);
        }

        return new() { node };
    }
}
