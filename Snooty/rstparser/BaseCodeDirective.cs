namespace rstparser;
using tinydocutils;

public class BaseCodeDirective
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

        List<(int, int)> emphasizeLines;

        try
        {
            var n_lines = content.Count;
            var emphasize_lines_options = (string)options.GetValueOrDefault("emphasize-lines", "");
            emphasizeLines = ParseLinenos(emphasize_lines_options, n_lines);
        }
        catch (ArgumentException err)
        {
            var error_node = state.Document!.Reporter.Error(err.ToString(), lineno);
            return new List<Node> { error_node };
        }

        var value = string.Join('\n', content);
        var node = new Code(value);
        if (arguments.Count > 0)
        {
            node.Lang = arguments[0];
        }
        if (options.ContainsKey("caption"))
        {
            node.Caption = (string)options["caption"];
        }
        node.Copyable = copyable;
        node.EmphasizeLines = emphasizeLines;
        node.Linenos = linenos;
        node.Document = state.Document;
        node.Source = source;
        node.Line = line;
        return new List<Node> { node };
    }

    /// Parse a comma-delimited list of line numbers and ranges.
    public static List<(int, int)> ParseLinenos(string term, int maxVal)
    {
        var results = new List<(int, int)>();
        if (term.Trim().Length == 0)
        {
            return new List<(int, int)>();
        }

        foreach (var termPart in term.Trim().Split(','))
        {
            var parts = termPart.Split("-", 1);
            var lower = int.Parse(parts[0]);
            var higher = (parts.Length == 2) ? int.Parse(parts[1]) : lower;
            if (lower < 0 || higher < 0)
            {
                throw new ArgumentException(
                    $"Invalid line number specification: {termPart}. Expects non-negative integers."
                );
            }
            else if (lower > maxVal || higher > maxVal)
            {
                throw new ArgumentException(
                    $"Invalid line number specification: {termPart}. Expects maximum value of {maxVal}."
                );
            }
            else if (lower > higher)
            {
                throw new ArgumentException(
                    $"Invalid line number specification: {termPart}. Expects {lower} < {higher}."
                );
            }

            results.Add((lower, higher));
        }

        return results;
    }
}
