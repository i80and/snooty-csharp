namespace tinydocutils;

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections;

public class EOFError : Exception { }
public class StateMachineError : Exception { }
public class DuplicateStateError : StateMachineError { }
public class UnknownTransitionError : StateMachineError { }
public class DuplicateTransitionError : StateMachineError { }

public class UnexpectedIndentationError : StateMachineError
{
    public StringList Block { get; init; }
    public string? SourceText { get; init; }
    public int? SourceLine { get; init; }

    public UnexpectedIndentationError(StringList block, string? src, int? srcline)
    {
        Block = block;
        SourceText = src;
        SourceLine = srcline;
    }
}

public class TransitionCorrection : Exception
{
    public string Transition { get; init; }
    public TransitionCorrection(string transition)
    {
        Transition = transition;
    }
}

public class StateCorrection : Exception
{
    public IStateBuilder NewState { get; init; }
    public string? Transition { get; init; }

    public StateCorrection(IStateBuilder newState, string? transition)
    {
        NewState = newState;
        Transition = transition;
    }
}

public record StateConfiguration(IStateBuilder[] StateClasses, IStateBuilder InitialState);

public record TransitionResult(List<string> Context, IStateBuilder NextState);

public sealed class StringList : IEnumerable<string>
{
    public StringList? Parent { get; set; }
    public List<(string?, int)> Items { get; set; }
    public int? ParentOffset;
    public List<string> Data { get; set; }

    public StringList(IEnumerable<string> initList, string? source, List<(string?, int)>? items = null, StringList? parent = null, int? parentOffset = null)
    {
        Parent = parent;
        ParentOffset = parentOffset;
        Data = initList.ToList();
        if (items is not null && items.Count > 0)
        {
            Items = items;
        }
        else
        {
            Items = Enumerable.Range(0, initList.Count()).Select(i => (source, i)).ToList();
        }
    }

    public override string ToString() {
        return $"StringList([{String.Join(", ", Data.Select(part => $"\"{part}\""))}])";
    }

    public string this[int index]
    {
        get
        {
            return Data[index];
        }

        set
        {
            Data[index] = value;
        }
    }

    public StringList Slice(int index, int count) => GetRange(index, count);

    public StringList GetRange(int index, int count)
    {
        return new StringList(
            Data.GetRange(index, count),
            source: null,
            items: Items.GetRange(index, count),
            parent: this,
            parentOffset: index
        );
    }

    public void RemoveRange(int index, int count)
    {
        Data.RemoveRange(index, count);
        Items.RemoveRange(index, count);
        if (Parent is not null)
        {
            Parent.RemoveRange((index + (int)ParentOffset!), (count + (int)ParentOffset));
        }
    }

    public void RemoveAt(int index)
    {
        Data.RemoveAt(index);
        Items.RemoveAt(index);
        if (Parent is not null)
        {
            Parent.RemoveAt(index + (int)ParentOffset!);
        }
    }

    public StringList Concat(StringList other)
    {
        return new StringList(Data.Concat(other.Data), null, Items.Concat(other.Items).ToList());
    }

    public (string?, int?) Info(int i)
    {
        if (i < 0 || i >= Items.Count) {
            if (i == Data.Count)
            {
                return (Items[i - 1].Item1, null);
            }
            else
            {
                throw new ArgumentOutOfRangeException($"{i} is out of range: {Items.Count}");
            }
        }

        return Items[i];
    }

    public void Disconnect()
    {
        Parent = null;
    }

    public IEnumerator<string> GetEnumerator()
    {
        return (IEnumerator<string>)Data.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count
    {
        get
        {
            return Data.Count;
        }
    }

    public string Pop(int i = -1)
    {
        int index;
        if (Parent is not null)
        {
            index = (Data.Count + i) % Data.Count;
            Parent.Pop(index + (int)ParentOffset!);
        }
        Items.RemoveAt(Items.Count - 1);

        index = Data.Count - 1;
        string val = Data[index];
        Data.RemoveAt(index);
        return val;
    }

    public void TrimStart(int n = 1)
    {
        // Remove items from the start of the list, without touching the parent.
        if (n > Data.Count)
        {
            throw new ArgumentOutOfRangeException(
                "Size of trim too large; can't trim %s items " +
                $"from a list of size {Data.Count}."
            );
        }
        else if (n < 0)
        {
            throw new ArgumentOutOfRangeException("Trim size must be >= 0.");
        }

        Data.RemoveRange(0, n);
        Items.RemoveRange(0, n);
        if (Parent is not null)
        {
            Debug.Assert(ParentOffset is not null);
            ParentOffset += n;
        }
    }

    public void TrimEnd(int n = 1)
    {
        // Remove items from the end of the list, without touching the parent.

        if (n > Data.Count)
        {
            throw new ArgumentOutOfRangeException(
                $"Size of trim too large; can't trim {n} items " +
                $"from a list of size {Data.Count}."
            );
        }
        else if (n < 0)
        {
            throw new ArgumentOutOfRangeException("Trim size must be >= 0.");
        }

        int startAt = Data.Count - n;
        Data.RemoveRange(startAt, n);
        Items.RemoveRange(startAt, n);
    }

    public void TrimLeft(int length, int start = 0, int end = Int32.MaxValue)
    {
        // Trim `length` characters off the beginning of each item, in-place,
        // from index `start` to `end`.  No whitespace-checking is done on the
        // trimmed text.  Does not affect slice parent.
        for (int i = start; i < end; i += 1)
        {
            Data[i] = Data[i][length..];
        }
    }

    public StringList GetTextBlock(int start, bool flushLeft = false)
    {
        // Return a contiguous block of text.

        // If `flush_left` is true, raise `UnexpectedIndentationError` if an
        // indented line is encountered before the text block ends (with a blank
        // line).
        var end = start;
        var last = Data.Count;
        while (end < last)
        {
            var line = Data[end];
            if (line.Trim().Length == 0)
            {
                break;
            }
            if (flushLeft && (line[0] == ' '))
            {
                var (source, offset) = Info(end);
                Debug.Assert(offset is not null);
                throw new UnexpectedIndentationError(this[start..end], source, offset + 1);
            }
            end += 1;
        }
        return this[start..end];
    }

    public (StringList, int, bool) GetIndented(
        int start = 0,
        bool untilBlank = false,
        bool stripIndent = true,
        int? blockIndent = null,
        int? firstIndent = null
    )
    {
        // Extract and return a StringList of indented lines of text.

        // Collect all lines with indentation, determine the minimum indentation,
        // remove the minimum indentation from all indented lines (unless
        // `strip_indent` is false), and return them. All lines up to but not
        // including the first unindented line will be returned.

        // :Parameters:
        //   - `start`: The index of the first line to examine.
        //   - `until_blank`: Stop collecting at the first blank line if true.
        //   - `strip_indent`: Strip common leading indent if true (default).
        //   - `blockIndent`: The indent of the entire block, if known.
        //   - `firstIndent`: The indent of the first line, if known.

        // :Return:
        //   - a StringList of indented lines with mininum indent removed;
        //   - the amount of the indent;
        //   - a boolean: did the indented block finish with a blank line or EOF?
        var indent = blockIndent;  // start with null if unknown
        var end = start;
        if (blockIndent is not null && firstIndent is null)
        {
            firstIndent = blockIndent;
        }
        if (firstIndent is not null)
        {
            end += 1;
        }
        var last = Data.Count;

        bool didBreak = false;
        bool blankFinish = false;
        while (end < last)
        {
            var line = Data[end];
            if (line.Length > 0 && (
                line[0] != ' '
                || (blockIndent is not null && line.Substring(0, (int)blockIndent).Trim().Length > 0))
            )
            {
                // Line not indented or insufficiently indented.
                // Block finished properly iff the last indented line blank:
                blankFinish = (end > start) && Data[end - 1].Trim().Length == 0;
                didBreak = true;
                break;
            }
            var stripped = line.TrimStart();
            if (stripped.Length == 0)
            {  // blank line
                if (untilBlank)
                {
                    blankFinish = true;
                    didBreak = true;
                    break;
                }
            }
            else if (blockIndent is null)
            {
                var lineIndent = line.Length - stripped.Length;
                if (indent is null)
                {
                    indent = lineIndent;
                }
                else
                {
                    indent = Math.Min((int)indent, lineIndent);
                }
            }
            end += 1;
        }

        if (!didBreak)
        {
            blankFinish = true;  // block ends at end of lines
        }
        var block = this[start..end];
        if (firstIndent is not null && block.Count > 0)
        {
            block.Data[0] = block.Data[0].Substring((int)firstIndent);
        }
        if (indent is not null && indent != 0 && stripIndent)
        {
            block.TrimLeft((int)indent!, start: (firstIndent is null) ? 0 : 1);
        }
        return (block, (indent is not null) ? (int)indent : 0, blankFinish);
    }
}

public record TransitionTuple(RegexWrapper Pattern, Func<Match, List<string>, IStateBuilder, TransitionResult> Method, IStateBuilder NextState);

public interface IStateBuilder
{
    public State Build(StateMachine sm);
}

public class StateMachine
{
    protected StateConfiguration _stateConfig;
    protected bool matchTitles = false;
    public StringList? InputLines { get; set; }
    protected int _inputOffset;
    public string? Line { get; set; }
    public int LineOffset = -1;
    protected IStateBuilder _initialState;
    protected IStateBuilder _currentState;
    public Dictionary<IStateBuilder, State> States = new Dictionary<IStateBuilder, State>();
    protected IList<Action<string?, int?>> _observers = new List<Action<string?, int?>>();
    private List<Action<string?, int?>> _observer = new List<Action<string?, int?>>();

    public StateMachine(StateConfiguration stateConfig)
    {
        _initialState = stateConfig.InitialState;
        _currentState = stateConfig.InitialState;
        _stateConfig = stateConfig;

        foreach (var stateClass in stateConfig.StateClasses)
        {
            AddState(stateClass);
        }
    }

    protected virtual void RuntimeInit()
    {
        foreach (var state in States.Values)
        {
            state.RuntimeInit();
        }
    }

    public void Unlink()
    {
        States.Clear();
    }

    public void RunSM(
        StringList inputLines,
        int inputOffset = 0,
        List<string>? context = null,
        string? inputSource = null,
        IStateBuilder? initialState = null
    )
    {
        RuntimeInit();
        InputLines = inputLines;
        _inputOffset = inputOffset;
        LineOffset = -1;
        _currentState = (initialState is not null) ? initialState : _initialState;
        List<string>? transitions = null;
        IStateBuilder nextState;
        var state = GetState();

        if (context is null)
        {
            context = new List<string>();
        }
        context = state.Bof(context);

        while (true)
        {
            bool thrown = false;
            try
            {
                try
                {
                    NextLine();
                    (context, nextState) = CheckLine(context, state, transitions);
                }
                catch (EOFError)
                {
                    state.Eof(context);
                    break;
                }
            }
            catch (TransitionCorrection correction)
            {
                PreviousLine();
                transitions = new List<string> { correction.Transition };
                thrown = true;
                continue;
            }
            catch (StateCorrection correction)
            {
                PreviousLine();
                nextState = correction.NewState;
                transitions = (correction.Transition is null) ? null : new List<string> { correction.Transition };
                thrown = true;
            }

            if (!thrown)
            {
                transitions = null;
            }

            state = GetState(nextState);
        }

        _observers = new List<Action<string?, int?>>();
    }

    public State GetState(IStateBuilder? nextState = null)
    {
        if (nextState is not null)
        {
            _currentState = nextState;
        }

        return States[_currentState];
    }

    public string? PreviousLine(int n = 1)
    {
        LineOffset -= n;
        if (LineOffset < 0)
        {
            Line = null;
        }
        else
        {
            Line = InputLines![LineOffset];
        }
        NotifyObservers();
        return Line;
    }

    public string NextLine(int n = 1)
    {
        try
        {
            LineOffset += n;
            if (LineOffset < 0 || LineOffset >= InputLines!.Count) {
                Line = null;
                throw new EOFError();
            } else {
                Line = InputLines![LineOffset];
                return Line;
            }
        }
        finally
        {
            NotifyObservers();
        }
    }


    public bool IsNextLineBlank()
    {
        // Return 1 if the next line is blank or non-existant.
        try
        {
            return InputLines![LineOffset + 1].Trim().Length == 0;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    public bool AtEof() {
        // Return 1 if the input is at or past end-of-file.
        return LineOffset >= (InputLines!.Count - 1);
    }

    public string GotoLine(int line_offset)
    {
        // Jump to absolute line offset `line_offset`, load and return it.
        try
        {
            try
            {
                LineOffset = line_offset - _inputOffset;
                Line = InputLines![LineOffset];
            }
            catch (ArgumentOutOfRangeException)
            {
                Line = null;
                throw new EOFError();
            }
            Debug.Assert(Line is not null);
            return Line;
        }
        finally
        {
            NotifyObservers();
        }
    }

    public int AbsLineOffset()
    {
        // Return line offset of current line, from beginning of file.
        return LineOffset + _inputOffset;
    }

    public int AbsLineNumber()
    {
        // Return line number of current line (counting from 1).
        return LineOffset + _inputOffset + 1;
    }


    public (string?, int) GetSourceAndLine(int? lineno = null)
    {
        int offset;
        if (lineno is null)
        {
            offset = LineOffset;
        }
        else
        {
            offset = (int)lineno - _inputOffset - 1;
        }

        var (src, srcoffset) = InputLines!.Info(offset);
        var srcline = srcoffset + 1;

        return (src, (int)srcline!);
    }

    public StringList GetTextBlock(bool flush_left = false)
    {
        // Return a contiguous block of text.

        // If `flush_left` is true, raise `UnexpectedIndentationError` if an
        // indented line is encountered before the text block ends (with a blank
        // line).

        try
        {
            var block = InputLines!.GetTextBlock(LineOffset, flush_left);
            NextLine(block.Count() - 1);
            return block;
        }
        catch (UnexpectedIndentationError err)
        {
            NextLine(err.Block.Count() - 1);  // advance to last line of block
            throw;
        }
    }

    private TransitionResult CheckLine(List<string> context, State state, IEnumerable<string>? transitions = null)
    {
        if (transitions is null)
        {
            transitions = state.Transitions.Keys;
        }

        foreach (var name in transitions)
        {
            var transition = state.Transitions[name];
            Debug.Assert(Line is not null);
            var match = transition.Pattern.RegexAnchoredAtStart.Match(Line);
            // Console.WriteLine("{0}, {1}, {2}", name, Line, match.Success);
            if (match.Success)
            {
                return transition.Method(match, context, transition.NextState);
            }
        }

        throw new Exception("no transition pattern match.");
    }

    public void AddState(IStateBuilder stateClass)
    {
        var instance = stateClass.Build(this);
        States[stateClass] = instance;
    }

    public void AttachObserver(
        Action<string?, int?> observer
    )
    {
        _observers.Add(observer);
    }

    public void NotifyObservers()
    {
        string? source;
        int? lineno;
        try {
            (source, lineno) = InputLines!.Info(LineOffset);
        } catch (ArgumentOutOfRangeException) {
            source = null;
            lineno = null;
        }
        foreach (var observer in _observers)
        {
            observer(source, lineno);
        }
    }


    public (StringList, int, int, bool) GetIndented(bool untilBlank = false, bool stripIndent = true)
    {
        // Return a block of indented lines of text, and info.

        // Extract an indented block where the indent is unknown for all lines.

        // :Parameters:
        //     - `untilBlank`: Stop collecting at the first blank line if true.
        //     - `stripIndent`: Strip common leading indent if true (default).

        // :Return:
        //     - the indented block (a list of lines of text),
        //     - its indent,
        //     - its first line offset from BOF, and
        //     - whether or not it finished with a blank line.
        var offset = AbsLineOffset();
        Debug.Assert(InputLines is not null);
        var (indented, indent, blank_finish) = InputLines.GetIndented(
            LineOffset, untilBlank, stripIndent
        );
        if (indented.Count > 0)
        {
            NextLine(indented.Count - 1);  // advance to last indented line
        }
        while (indented.Count > 0 && indented[0].Trim().Length == 0)
        {
            indented.TrimStart();
            offset += 1;
        }
        return (indented, indent, offset, blank_finish);
    }

    public (StringList, int, bool) GetKnownIndented(
        int indent, bool untilBlank = false, bool stripIndent = true
    )
    {
        // Return an indented block and info.

        // Extract an indented block where the indent is known for all lines.
        // Starting with the current line, extract the entire text block with at
        // least `indent` indentation (which must be whitespace, except for the
        // first line).

        // :Parameters:
        //     - `indent`: The number of indent columns/characters.
        //     - `untilBlank`: Stop collecting at the first blank line if true.
        //     - `stripIndent`: Strip `indent` characters of indentation if true
        //       (default).

        // :Return:
        //     - the indented block,
        //     - its first line offset from BOF, and
        //     - whether or not it finished with a blank line.
        var offset = AbsLineOffset();
        Debug.Assert(InputLines is not null);
        (var indented, indent, var blankFinish) = InputLines.GetIndented(
            LineOffset, untilBlank, stripIndent, blockIndent: indent
        );
        NextLine(indented.Count - 1);  // advance to last indented line
        while (indented.Count > 0 && indented[0].Trim().Length == 0)
        {
            indented.TrimStart();
            offset += 1;
        }
        return (indented, offset, blankFinish);
    }

    public (StringList, int, int, bool) GetFirstKnownIndented(
        int indent, bool untilBlank = false, bool stripIndent = true, bool stripTop = true)
    {
        // Return an indented block and info.

        // Extract an indented block where the indent is known for the first line
        // and unknown for all other lines.

        // :Parameters:
        //     - `indent`: The first line's indent (# of columns/characters).
        //     - `untilBlank`: Stop collecting at the first blank line if true
        //       (1).
        //     - `stripIndent`: Strip `indent` characters of indentation if true
        //       (1, default).
        //     - `stripTop`: Strip blank lines from the beginning of the block.

        // :Return:
        //     - the indented block,
        //     - its indent,
        //     - its first line offset from BOF, and
        //     - whether or not it finished with a blank line.
        var offset = AbsLineOffset();
        Debug.Assert(InputLines is not null);
        (var indented, indent, var blank_finish) = InputLines.GetIndented(
            LineOffset, untilBlank, stripIndent, firstIndent: indent
        );
        NextLine(indented.Count - 1);  // advance to last indented line
        if (stripTop)
        {
            while (indented.Count > 0 && indented[0].Trim().Length == 0)
            {
                indented.TrimStart();
                offset += 1;
            }
        }
        return (indented, indent, offset, blank_finish);
    }
}

public class State
{
    protected Type? _nestedSM;
    protected StateMachine _stateMachine;
    protected StateConfiguration? _stateConfig;

    public Dictionary<string, TransitionTuple> Transitions { get; init; } = new Dictionary<string, TransitionTuple>();

    public State(StateMachine sm)
    {
        _stateMachine = sm;
    }

    public virtual void RuntimeInit() { }

    public virtual List<string> Bof(List<string> context)
    {
        return context;
    }

    public virtual List<string> Eof(List<string> context)
    {
        return new List<string>();
    }

    public virtual TransitionResult NopTransition(
        Match match, List<string> context, IStateBuilder nextState)
    {
        // A "do nothing" transition method.

        // Return unchanged `context` & `next_state`, empty result. Useful for
        // simple state changes (actionless transitions).
        return new TransitionResult(context, nextState);
    }
}
