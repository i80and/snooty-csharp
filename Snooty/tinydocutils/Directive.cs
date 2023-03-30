namespace tinydocutils;

public class DirectiveError : Exception
{
    public int Level { get; init; }
    public DirectiveError(string message, int level) : base(message)
    {
        Level = level;
    }
}

public interface IDirective
{
    // Number of required directive arguments.
    public int RequiredArguments { get; }

    // Number of optional arguments after the required arguments.
    public int OptionalArguments { get; }

    // May the final argument contain whitespace?
    public bool FinalArgumentWhitespace { get; }

    // Mapping of option names to validator functions.
    public Dictionary<string, Func<string?, object>> OptionSpec { get; }

    // May the directive have content?
    public bool HasContent { get; }

    public List<Element> Run(
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
}
