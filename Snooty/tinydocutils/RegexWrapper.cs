namespace tinydocutils;

using System.Text.RegularExpressions;

public record MatchWrapper(string Text, Match Match)
{
    public int End()
    {
        return Match.Index + Match.Length;
    }
}

// Python has a neat ability where a compiled regex pattern has both search() and match() methods
// so at usage you can choose whether to anchor at the start or not. Replicate that, but dumbly.
public sealed class RegexWrapper
{
    public string PatternText { get; init; }
    public RegexOptions Options { get; init; }

    private Regex? _regexAnchoredAtStart;
    private Regex? _regex;

    public Regex RegexAnchoredAtStart
    {
        get
        {
            if (_regexAnchoredAtStart is not null)
            {
                return _regexAnchoredAtStart;
            }

            if (PatternText.StartsWith('^'))
            {
                return Regex;
            }

            _regexAnchoredAtStart = new Regex("^" + PatternText, Options | RegexOptions.Compiled);
            return _regexAnchoredAtStart;
        }
    }

    public Regex Regex
    {
        get
        {
            if (_regex is not null)
            {
                return _regex;
            }

            _regex = new Regex(PatternText, Options | RegexOptions.Compiled);
            return _regex;
        }
    }

    public MatchWrapper MatchAnchored(string needle)
    {
        return new MatchWrapper(needle, RegexAnchoredAtStart.Match(needle));
    }

    public MatchWrapper Search(string needle)
    {
        return new MatchWrapper(needle, Regex.Match(needle));
    }

    public RegexWrapper(string pattern, RegexOptions options = RegexOptions.None)
    {
        PatternText = pattern;
        Options = options;
    }
}
