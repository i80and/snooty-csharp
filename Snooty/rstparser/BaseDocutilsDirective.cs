namespace rstparser;

using System.Text.RegularExpressions;
using tinydocutils;

public abstract class BaseDocutilsDirective : IDirective
{
    public required DirectiveSpec Spec { get; init; }

    public int RequiredArguments { get; init; }
    public int OptionalArguments { get; init; }

    public bool FinalArgumentWhitespace { get; init; }

    public Dictionary<string, Func<string?, object>> OptionSpec { get; init; }

    public bool HasContent { get; init; }

    public List<Node> Run(
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

        var rstobject_spec = Spec.Rstobject;
        var constructor = (rstobject_spec is null) ? Directive.Make : TargetDirective.Make;
        var node = constructor((Spec.Domain is null) ? "" : Spec.Domain, name);
        node.Document = state.Document;
        node.Source = source;
        node.Line = line;
        node.Options = options;

        // Check for required options
        var option_names = new HashSet<string>(options.Keys);
        var missing_options = new HashSet<string>(Spec.RequiredOptions);
        missing_options.ExceptWith(option_names);
        if (missing_options.Count > 0)
        {
            var missing_option_names = string.Join(", ", missing_options);
            var pluralization = (missing_option_names.Length > 1) ? "s" : "";
            node.Add(
                state.Document!.Reporter.Error(
                    $"\"{name}\" requires the following option{pluralization}: {missing_option_names}",
                    line
                )
            );
        }

        // If directive is deprecated, warn
        if (Spec.Deprecated == true)
        {
            node.Add(
                state.Document!.Reporter.Warning(
                    $"Directive \"{name}\" has been deprecated", line
                )
            );
        }

        // If this is an rstobject, we need to generate a target property
        if (rstobject_spec is not null)
        {
            var prefix = (rstobject_spec.Prefix.Length > 0) ? rstobject_spec.Prefix + "." : "";
            List<(string, string)> targets = new();
            if (rstobject_spec.Type == RstTargetType.plain)
            {
                targets = new() {
                    (rstobject_spec.Prefix.Length > 0) ? (prefix + arguments[0], arguments[0]) : (arguments[0], arguments[0])
                };
            }
            else if (rstobject_spec.Type == RstTargetType.callable)
            {
                var stripped = StripParameters(arguments[0]);
                targets = new() { (prefix + stripped, stripped + "()") };
            }
            else if (rstobject_spec.Type == RstTargetType.cmdline_option)
            {
                foreach (var (arg_id, arg_ok) in ParseOptions(arguments[0]))
                {
                    if (!arg_ok)
                    {
                        node.Add(
                            state.Document!.Reporter.Error(arg_id.ToString(), line)
                        );
                        continue;
                    }
                    targets.Add((prefix + arg_id, arg_id));
                }
            }

            // title is the node that should be presented at this point in the doctree
            Node title_node = new Text(arguments[0]);
            if (rstobject_spec.Format is not null)
            {
                title_node = FormatNode(title_node, rstobject_spec.Format);
            }
            var argumentNode = new DirectiveArgument(arguments[0], "")
            {
                Children = new() { title_node }
            };
            node.Add(argumentNode);

            foreach (var (target_id, target_title) in targets)
            {
                var identifier_node = new TargetIdentifier
                {
                    Ids = new() { target_id }
                };
                identifier_node.Add(new Text(target_title));
                node.Add(identifier_node);
            }

            // Append list of supported fields
            node.Options["fields"] = rstobject_spec.Fields;
        }
        else if (name == "pubdate" || name == "updated-date")
        {
            var date = ValidateDate(arguments[0]);
            if (date is null)
            {
                // Throw error and set date field to null
                var err = "Expected ISO 8061 date format (YYYY-MM-DD)";
                node.Add(
                    state.Document!.Reporter.Error($"{err}: {arguments[0]}", line)
                );
            }
            else
            {
                node.Options["date"] = date;
            }
        }
        else
        {
            ParseArgument(arguments, options, lineno, contentOffset, blockText, state, node, source!, line);
        }

        // Parse the content
        state.NestedParse(
            content, contentOffset, node, match_titles: true
        );

        return new() { node };
    }

    /// Parse the directive's argument.
    ///
    /// An argument spans from the 0th line to the first non-option line; this
    /// is a heuristic that is not part of docutils, since docutils requires
    /// each directive to define its syntax.
    public void ParseArgument(
        List<string> arguments,
        Dictionary<string, object> options,
        int lineno,
        int contentOffset,
        string blockText,
        RSTState state,
        Directive node,
        string source,
        int line)
    {
        if (arguments.Count == 0 || arguments[0].StartsWith(':'))
        {
            return;
        }

        var arg_lines = arguments[0].Split('\n');
        if (
            arg_lines.Length > 1
            && options.Count == 0
            && PAT_BLOCK_HAS_ARGUMENT.IsMatch(blockText)
        )
        {
            var content_lines = PrepareViewlist(arguments[0]);
            state.NestedParse(
                new StringList(
                    content_lines, source: arguments[0]
                ),
                contentOffset,
                node,
                match_titles: true
            );
        }
        else
        {
            var argument_text = arg_lines[0];
            var (textnodes, messages) = state.InlineText(argument_text, lineno);
            var argument = new DirectiveArgument(argument_text, "")
            {
                Children = textnodes,
                Document = state.Document,
                Source = source,
                Line = line
            };
            node.Add(argument);
        }
    }

    private static List<string> PrepareViewlist(string text, int ignore = 1)
    {
        var lines = Util.String2Lines(
            text, tab_width: 4, convert_whitespace: true
        );

        // Remove any leading blank lines.
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        // make sure there is an empty line at the end
        if (lines.Count > 0 && lines[^0].Length > 0)
        {
            lines.Add("");
        }

        return lines;
    }

    private static IEnumerable<(string, bool)> ParseOptions(string option)
    {
        var all_parts = option.Split(", ").Select(part => part.Trim());
        foreach (var part in all_parts)
        {
            var match = PAT_OPTION.Match(part);
            if (!match.Success)
            {
                yield return (part, false);
                continue;
            }

            yield return (match.Groups[1].Value, true);
        }
    }

    private static string? ValidateDate(string date)
    {
        if (PAT_ISO_8601.IsMatch(date))
        {
            return date;
        }

        return null;
    }

    /// <summary>
    /// Remove trailing ALGOL-style parameters from a target name;
    /// e.g. foo(bar, baz) -> foo.
    /// </summary>
    private static string StripParameters(string target)
    {
        var match = PAT_PARAMETERS.Match(target);
        if (!match.Success)
        {
            return target;
        }

        var starting_index = match.Index;
        if (starting_index == -1)
        {
            return target;
        }

        return target[0..starting_index];
    }

    /// <summary>
    /// Format a docutils node with a set of inline formatting roles.
    /// </summary>
    private static Node FormatNode(
        Node node,
        IEnumerable<FormattingType> formatting
    )
    {
        foreach (var hint in formatting)
        {
            var newNode = FORMATTING_MAP[hint]("", "");
            if (node is Element el && el.Children.Count > 0)
            {
                newNode.Children = new() { node };
            }
            node = newNode;
        }

        return node;
    }

    private readonly static Dictionary<FormattingType, Func<string, string, Element>> FORMATTING_MAP = new() {
        { FormattingType.strong, Strong.Make },
        { FormattingType.emphasis, Emphasis.Make },
        { FormattingType.monospace, Literal.Make },
    };

    private static Regex PAT_BLOCK_HAS_ARGUMENT = new Regex(@"^\x20*\.\.\x20[^\s]+::\s*\S+");
    private static Regex PAT_OPTION = new Regex(@"((?:/|--|-|\+)?[^\s=]+)(=?\s*.*)");
    private static Regex PAT_ISO_8601 = new Regex(@"^([0-9]{4})-(1[0-2]|0[1-9])-(3[01]|0[1-9]|[12][0-9])$");
    private static Regex PAT_PARAMETERS = new Regex(@"\s*\(.*?\)\s*$");
}
