namespace tinydocutils;

using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public record StyleKind(string Underline, string? Overline)
{
    public int Count
    {
        get
        {
            return (Overline is null) ? 1 : 2;
        }
    }
}

public class DataError : Exception
{
    public DataError(string message) : base(message) { }
}

public interface IHaveBlankFinish
{
    public bool BlankFinish { get; set; }
}

public class ApplicationError : Exception
{
    public ApplicationError(string message) : base(message) { }
}

public class MarkupError : DataError
{
    public MarkupError(string message) : base(message) { }
}

public class ParserError : ApplicationError
{
    public ParserError(string message) : base(message) { }
}

public class MarkupMismatch : Exception { }

public class Inliner
{
    public record RegexDefinitionGroup(string Name, string Prefix, string Suffix, IList<string>? Parts, IList<RegexDefinitionGroup>? Children)
    {
        public RegexWrapper Build()
        {
            // Build, compile and return a regular expression based on `definition`.

            // :Parameter: `definition`: a 4-tuple (group name, prefix, suffix, parts),
            //     where "parts" is a list of regular expressions and/or regular
            //     expression definitions to be joined into an or-group.

            string inner(RegexDefinitionGroup definition)
            {
                List<string> part_strings = new List<string>();

                if (definition.Parts is not null)
                {
                    foreach (var part in definition.Parts)
                    {
                        part_strings.Add(part);
                    }
                }

                if (definition.Children is not null)
                {
                    foreach (var part in definition.Children)
                    {
                        part_strings.Add(inner(part));
                    }
                }
                var or_group = String.Join('|', part_strings);
                var regexp = $"{definition.Prefix}(?<{definition.Name}>{or_group}){definition.Suffix}";
                return regexp;
            }

            return new RegexWrapper(inner(this));
        }
    }


    public record PatternRegistry(
        RegexWrapper initial,
        RegexWrapper emphasis,
        RegexWrapper strong,
        RegexWrapper interpreted_or_phrase_ref,
        RegexWrapper embedded_link,
        RegexWrapper literal,
        RegexWrapper target,
        RegexWrapper substitution_ref,
        RegexWrapper email,
        RegexWrapper uri)
    {

        public const string non_whitespace_before = """(?<!\s)""";
        public const string non_whitespace_escape_before = """(?<![\s\x00])""";
        public const string non_unescaped_whitespace_escape_before = """(?<!(?<!\x00)[\s\x00])""";
        public const string non_whitespace_after = """(?!\s)""";
        // Alphanumerics with isolated internal [-._+:] chars (i.e. not 2 together):
        public const string simplename = """(?:(?!_)\w)+(?:[-._+:](?:(?!_)\w)+)*""";
        // Valid URI characters (see RFC 2396 & RFC 2732);
        // final \x00 allows backslash escapes in URIs:
        public const string uric = """[-_.!~*'()[\];/:@&=+$,%a-zA-Z0-9\x00]""";
        // Delimiter indicating the end of a URI (not part of the URI):
        public const string uri_end_delim = """[>]""";
        // Last URI character; same as uric but no punctuation:
        public const string urilast = """[_~*/=+a-zA-Z0-9]""";
        // End of a URI (either 'urilast' or 'uric followed by a
        // uri_end_delim'):
        public const string uri_end = $"""(?:{urilast}|{uric}(?={uri_end_delim}))""";
        public const string emailc = """[-_!~*'{|}/#?^`&=+$%a-zA-Z0-9\x00]""";
        public const string email_pattern = $"""
            {emailc}+(?:\.{emailc}+)*     (?# // name)
            (?<!\x00)@                    (?# // at)
            {emailc}+(?:\.{emailc}*)*     (?# // host)
            {uri_end}                     (?# // final URI char)
            """;

        public static PatternRegistry Generate()
        {
            // lookahead and look-behind expressions for inline markup rules
            string start_string_prefix;
            string end_string_suffix;
            if (OptionParser.character_level_inline_markup)
            {
                start_string_prefix = "(^|(?<!\x00))";
                end_string_suffix = "";
            }
            else
            {
                start_string_prefix = $"""(^|(?<=\s|[{PunctuationChars.openers}{PunctuationChars.delimiters}]))""";
                end_string_suffix = $"""($|(?=\s|[\x00{PunctuationChars.closing_delimiters}{PunctuationChars.delimiters}{PunctuationChars.closers}]))""";
            }

            var parts = new RegexDefinitionGroup(
                "initial_inline",
                start_string_prefix,
                "",
                null,
                new List<RegexDefinitionGroup> {
                    new RegexDefinitionGroup(
                        "start",
                        "",
                        non_whitespace_after,  // simple start-strings
                        new List<string> {
                            """\*\*""",  // strong
                            """\*(?!\*)""",  // emphasis but not strong
                            """``""",  // literal
                            """_`""",  // inline internal target
                            """\|(?!\|)"""
                        },  // substitution reference
                        null
                    ),
                    new RegexDefinitionGroup(
                        "whole",
                        "",
                        end_string_suffix,  // whole constructs
                        new List<string> { $"""(?<refname>{simplename})(?<refend>__?)""" },
                        new List<RegexDefinitionGroup> {  // reference name & end-string
                            new RegexDefinitionGroup(
                                "footnotelabel",
                                """\[""",
                                """(?<fnend>\]_)""",
                                new List<string> {
                                    """[0-9]+""",  // manually numbered
                                    $"""\#({simplename})?""", // auto-numbered (w/ label?)
                                    """\*""",  // auto-symbol
                                    $"""(?<citationlabel>{simplename})"""
                                },  // citation reference
                                null
                            )
                        }
                    ),
                    new RegexDefinitionGroup(
                        "backquote",  // interpreted text or phrase reference
                        $"(?<role>(:{simplename}:)?)",  // optional role
                        non_whitespace_after,
                        new List<string> {"`(?!`)"},  // but not literal
                        null
                    )
                }
            );

            var uriPattern = $@"""
                {start_string_prefix}
                (?<whole>
                (?<absolute>               (?# absolute URI)
                    (?<scheme>             (?# scheme: http, ftp, mailto)
                    [a-zA-Z][a-zA-Z0-9.+-]*
                    )
                    :
                    (
                    (                       (?# either:)
                        (//?)?                  (?# hierarchical URI)
                        {uric}*                 (?# URI characters)
                        {uri_end}               (?#final URI char)
                    )
                    (                       (?# optional query)
                        \?{uric}*
                        {uri_end}
                    )?
                    (                       (?# optional fragment)
                        \#{uric}*
                        %(uri_end)s
                    )?
                    )
                )
                |                       (?# *OR*)
                (?<email>              (?# email address)
                    """
                    + email_pattern
                    + $@"""
                )
                )
                {end_string_suffix}
                """;

            return new PatternRegistry(
                initial: parts.Build(),
                emphasis: new RegexWrapper(
                    non_whitespace_escape_before + """(\*)""" + end_string_suffix
                ),
                strong: new RegexWrapper(
                    non_whitespace_escape_before + """(\*\*)""" + end_string_suffix
                ),
                interpreted_or_phrase_ref: new RegexWrapper(
                    $@"""
                {non_unescaped_whitespace_escape_before}
                (
                    `
                    (?<suffix>
                    (?<role>:%(simplename)s:)?
                    (?<refend>__?)?
                    )
                )
                {end_string_suffix}
                """,
                    RegexOptions.IgnorePatternWhitespace
                ),
                embedded_link: new RegexWrapper(
                    $@"""
                (
                    (?:[ \n]+|^)            (?# spaces or beginning of line/string)
                    <                       (?# open bracket)
                    {non_whitespace_after}
                    (([^<>]|\x00[<>])+)     (?# anything but unescaped angle brackets)
                    {non_whitespace_escape_before}
                    >                       (?# close bracket)
                )
                $                           (?# end of string)
                """,
                    RegexOptions.IgnorePatternWhitespace
                ),
                literal: new RegexWrapper(
                    non_whitespace_before + "(``)" + end_string_suffix
                ),
                target: new RegexWrapper(
                    non_whitespace_escape_before + """(`)""" + end_string_suffix
                ),
                substitution_ref: new RegexWrapper(
                    non_whitespace_escape_before
                    + """(\|_{0,2})"""
                    + end_string_suffix
                ),
                email: new RegexWrapper(email_pattern + "$", RegexOptions.IgnorePatternWhitespace),
                uri: new RegexWrapper(uriPattern, RegexOptions.IgnorePatternWhitespace)
            );
        }
    }

    private Dictionary<string, Func<MatchWrapper, int, (string, List<Node>, string, List<SystemMessage>)>> _dispatch;

    private Element? _parent;
    private Reporter? _reporter;
    private Document? _document;

    private (RegexWrapper, Func<MatchWrapper, int, List<Node>>) _implicitDispatch;
    public static PatternRegistry Patterns { get; } = PatternRegistry.Generate();

    public Inliner()
    {
        _implicitDispatch = (Patterns.uri, StandaloneUri);

        _dispatch = new Dictionary<string, Func<MatchWrapper, int, (string, List<Node>, string, List<SystemMessage>)>> {
            { "*", (match, lineno) => HandleEmphasis(match, lineno) },
            { "**", (match, lineno) => HandleStrong(match, lineno) },
            { "`", (match, lineno) => HandleInterpretedOrPhraseRef(match, lineno) },
            { "``", (match, lineno) => HandleLiteral(match, lineno) },
            { "_`", (match, lineno) => HandleInlineInternalTarget(match, lineno) },
            { "]_", (match, lineno) => HandleFootnoteReference(match, lineno) },
            { "|", (match, lineno) => HandleSubstitutionReference(match, lineno) },
            { "_", (match, lineno) => HandleReference(match, lineno) },
            { "__", (match, lineno) => HandleAnonymousReference(match, lineno) },
        };
    }

    public (List<Node>, List<SystemMessage>) Parse(
        string text,
        int lineno,
        StateMachineMemo memo,
        Element parent
    )
    {
        // Needs to be refactored for nested inline markup.
        // Add nested_parse() method?

        // Return 2 lists: nodes (text and inline elements), and system_messages.

        // Using `self.patterns.initial`, a pattern which matches start-strings
        // (emphasis, strong, interpreted, phrase reference, literal,
        // substitution reference, and inline target) and complete constructs
        // (simple reference, footnote reference), search for a candidate.  When
        // one is found, check for validity (e.g., not a quoted '*' character).
        // If valid, search for the corresponding end string if applicable, and
        // check it for validity.  If not found or invalid, generate a warning
        // and ignore the start-string.  Implicit inline markup (e.g. standalone
        // URIs) is found last.

        _reporter = memo.Reporter;
        _document = memo.Document;
        _parent = parent;
        var pattern_search = (string x) => Patterns.initial.Search(x);
        var remaining = Util.Escape2Null(text);
        var processed = new List<Node>();
        var unprocessed = new List<string>();
        var messages = new List<SystemMessage>();
        while (!String.IsNullOrEmpty(remaining))
        {
            var match = pattern_search(remaining);
            if (match.Match.Success)
            {
                string methodName = new List<string> { "start", "backquote", "refend", "fnend" }.Select(x => match.Match.Groups[x].Value).FirstOrDefault("");
                var method = _dispatch[methodName];
                (var before, var inlines, remaining, var sysmessages) = method(match, lineno);
                unprocessed.Add(before);
                messages.AddRange(sysmessages);
                if (inlines.Count > 0)
                {
                    processed.AddRange(ImplicitInline(String.Concat(unprocessed), lineno));
                    processed.AddRange(inlines);
                    unprocessed = new List<string>();
                }
            }
            else
            {
                break;
            }
        }
        remaining = String.Concat(unprocessed) + remaining;
        if (remaining.Length > 0)
        {
            processed.AddRange(ImplicitInline(remaining, lineno));
        }
        return (processed, messages);
    }

    private List<Node> ImplicitInline(string text, int lineno)
    {
        // Check each of the patterns in `self.implicit_dispatch` for a match,
        // and dispatch to the stored method for the pattern.  Recursively check
        // the text before and after the match.  Return a list of `nodes.Text`
        // and inline element nodes.

        if (text.Length == 0)
        {
            return new List<Node>();
        }

        var (pattern, method) = _implicitDispatch;
        var match = pattern.Search(text);
        if (match.Match.Success)
        {
            try
            {
                // Must recurse on strings before *and* after the match;
                // there may be multiple patterns.
                var result = ImplicitInline(text[..match.Match.Index], lineno);
                result.AddRange(method(match, lineno));
                result.AddRange(ImplicitInline(text[(match.Match.Index + match.Match.Length)..], lineno));
                return result;
            }
            catch (MarkupMismatch) { }
        }
        return new List<Node> { new Text(text, Util.Unescape(text, true)) };
    }


    private (string, List<Node>, string, List<SystemMessage>) HandleEmphasis(MatchWrapper match, int lineno)
    {
        var (before, inlines, remaining, sysmessages, endstring) = HandleInlineObj(
            match, lineno, Patterns.emphasis, Emphasis.Make
        );
        return (before, inlines, remaining, sysmessages);
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleStrong(MatchWrapper match, int lineno)
    {
        var (before, inlines, remaining, sysmessages, endstring) = HandleInlineObj(
            match, lineno, Patterns.strong, Strong.Make
        );
        return (before, inlines, remaining, sysmessages);
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleInterpretedOrPhraseRef(MatchWrapper match, int lineno)
    {
        var end_pattern = Patterns.interpreted_or_phrase_ref;
        var str = match.Text;
        var backquoteGroup = match.Match.Groups["backquote"];
        var matchstart = backquoteGroup.Index;
        var matchend = backquoteGroup.Index + backquoteGroup.Length;
        var rolestart = match.Match.Groups["role"].Index;
        var role = match.Match.Groups["role"].Value;
        var position = "";
        SystemMessage msg;
        if (role.Length > 0)
        {
            role = role[1..^1];
            position = "prefix";
        }
        else if (QuotedStart(match))
        {
            return (str[..matchend], new List<Node>(), str[matchend..], new List<SystemMessage>());
        }

        var endmatch = end_pattern.Search(str[matchend..]);
        if (endmatch.Match.Success && endmatch.Match.Groups[1].Index > 0)
        {  // 1 or more chars
            var textend = matchend + endmatch.End();
            if (endmatch.Match.Groups["role"].Value.Length > 0)
            {
                if (role.Length > 0)
                {
                    msg = _reporter!.Warning(
                        "Multiple roles in interpreted text (both " +
                        "prefix and suffix present; only one allowed).",
                        line: lineno
                    );
                    return (str[..rolestart], new List<Node>(), str[textend..], new List<SystemMessage> { msg });
                }
                role = endmatch.Match.Groups["suffix"].Value[1..^1];
                position = "suffix";
            }
            var escaped = endmatch.Text[..endmatch.Match.Groups[1].Index];
            var rawsource = Util.Unescape(str[matchstart..textend], true);
            if (rawsource[^1] == '_')
            {
                if (role.Length > 0)
                {
                    msg = _reporter!.Warning(
                        $"Mismatch: both interpreted text role {position} and " +
                        "reference suffix.",
                        line: lineno
                    );
                    return (str[..rolestart], new List<Node>(), str[textend..], new List<SystemMessage> { msg });
                }
                return HandlePhraseRef(
                    str[..matchstart], str[textend..], rawsource, escaped
                );
            }
            else
            {
                rawsource = Util.Unescape(str[rolestart..textend], true);
                var (nodelist, messages) = HandleInterpreted(rawsource, escaped, role, lineno);
                return (str[..rolestart], nodelist, str[textend..], messages);
            }
        }
        msg = _reporter!.Warning(
            "Inline interpreted text or phrase reference start-string " +
            "without end-string.",
            line: lineno
        );
        return (str[..matchstart], new List<Node>(), str[matchend..], new List<SystemMessage> { msg });
    }

    private (string, List<Node>, string, List<SystemMessage>) HandlePhraseRef(
        string before, string after, string rawsource, string escaped
    )
    {
        var match = Patterns.embedded_link.Search(escaped);

        string text;
        string unescaped;
        string rawtext;
        string alias = "";
        Target? target;
        string aliastype = "";

        if (match.Match.Success)
        {  // embedded <URI> or <alias_>
            text = escaped[..match.Match.Index];
            unescaped = Util.Unescape(text);
            rawtext = Util.Unescape(text, true);
            var aliastext = match.Match.Groups[2].Value;
            var rawaliastext = Util.Unescape(aliastext, true);
            var underscore_escaped = rawaliastext.EndsWith("""\_""");
            if (aliastext.EndsWith("_") && !(
                underscore_escaped || Patterns.uri.RegexAnchoredAtStart.IsMatch(aliastext)
            ))
            {
                aliastype = "name";
                alias = Util.FullyNormalizeName(Util.Unescape(aliastext[..^1]));
                target = new Target(match.Match.Groups[1].Value, "");
                target.Attributes["refname"] = alias;
            }
            else
            {
                aliastype = "uri";
                // remove unescaped whitespace
                var alias_parts = Util.SplitEscapedWhitespace(match.Match.Groups[2].Value);
                alias = String.Join(' ', alias_parts.Select(part => String.Concat(part.Split())));
                alias = AdjustUri(Util.Unescape(alias));
                if (alias.EndsWith("""\_"""))
                {
                    alias = alias[..^2] + "_";
                }
                target = new Target(match.Match.Groups[1].Value, "");
                target.Attributes["refuri"] = alias;
            }
            if (aliastext.Length == 0)
            {
                throw new ApplicationError($"problem with embedded link: {aliastext}");
            }
            if (text.Length == 0)
            {
                text = alias;
                unescaped = Util.Unescape(text);
                rawtext = rawaliastext;
            }
        }
        else
        {
            text = escaped;
            unescaped = Util.Unescape(text);
            target = null;
            rawtext = Util.Unescape(escaped, true);
        }

        var refname = Util.FullyNormalizeName(unescaped);
        var reference = new Reference(rawsource, text);
        reference.Attributes["name"] = Util.WhitespaceNormalizeName(unescaped);
        reference.Children[0].RawSource = rawtext;

        var node_list = new List<Node> { reference };

        if (rawsource[^2..] == "__")
        {
            if (target is not null && (aliastype == "name"))
            {
                reference.Attributes["refname"] = alias;
                _document!.NoteRefname(reference);
                // self.document.note_indirect_target(target) # required?
            }
            else if (target is not null && (aliastype == "uri"))
            {
                reference.Attributes["refuri"] = alias;
            }
            else
            {
                reference.Attributes["anonymous"] = true;
            }
        }
        else
        {
            if (target is not null)
            {
                target.Names.Add(refname);
                if (aliastype == "name")
                {
                    reference.Attributes["refname"] = alias;
                    _document!.NoteIndirectTarget(target);
                    _document!.NoteRefname(reference);
                }
                else
                {
                    reference.Attributes["refuri"] = alias;
                    Debug.Assert(_parent is not null);
                    _document!.NoteExplicitTarget(target, _parent);
                }

                Debug.Assert(target is not null);
                node_list.Add(target);
            }
            else
            {
                reference.Attributes["refname"] = refname;
                _document!.NoteRefname(reference);
            }
        }
        return (before, node_list, after, new List<SystemMessage>());
    }

    private (List<Node>, List<SystemMessage>) HandleInterpreted(
        string rawsource, string text, string role, int lineno
    )
    {
        Debug.Assert(_reporter is not null);
        var (role_fn, messages) = _document!.Settings.LookupRole(role, lineno, _reporter);
        if (role_fn is not null)
        {
            var (nodes, messages2) = role_fn(role, rawsource, text, lineno, this);
            messages.AddRange(messages2);
            return (nodes, messages);
        }
        else
        {
            var msg = _reporter!.Error(
                $"Unknown interpreted text role '{role}'.", line: lineno
            );
            messages.Add(msg);
            return (new List<Node>(), messages);
        }
    }

    public string AdjustUri(string uri)
    {
        if (Patterns.email.RegexAnchoredAtStart.IsMatch(uri))
        {
            return "mailto:" + uri;
        }

        return uri;
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleLiteral(MatchWrapper match, int lineno)
    {
        var (before, inlines, remaining, sysmessages, endstring) = HandleInlineObj(
            match,
            lineno,
            Patterns.literal,
            Literal.Make,
            restore_backslashes: true
        );
        return (before, inlines, remaining, sysmessages);
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleInlineInternalTarget(MatchWrapper match, int lineno)
    {
        var (before, inlines, remaining, sysmessages, endstring) = HandleInlineObj(
            match, lineno, Patterns.target, Target.Make
        );

        if (inlines.Count > 0)
        {
            if (inlines[0] is Target target)
            {
                Debug.Assert(inlines.Count == 1);
                var name = Util.FullyNormalizeName(target.AsText());
                target.Names.Add(name);
                _document!.NoteExplicitTarget(target, _parent!);
            }
        }

        return (before, inlines, remaining, sysmessages);
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleFootnoteReference(MatchWrapper match, int lineno)
    {
        // Handles `nodes.footnote_reference` and `nodes.citation_reference`
        // elements.

        var label = match.Match.Groups["footnotelabel"].Value;
        var refname = Util.FullyNormalizeName(label);
        var str = match.Text;
        var wholeGroup = match.Match.Groups["whole"];
        var before = str[..wholeGroup.Index];
        var remaining = str[(wholeGroup.Index + wholeGroup.Length)..];
        Element returnNode;
        if (match.Match.Groups["citationlabel"].Success)
        {
            var refnode = new CitationReference($"[{label}]_");
            returnNode = refnode;
            refnode.Attributes["refname"] = refname;
            refnode.Add(new Text(label));
            _document!.NoteCitationRef(refnode);
        }
        else
        {
            FootnoteReference refnode = new FootnoteReference($"[{label}]_");
            returnNode = refnode;
            if (refname[0] == '#')
            {
                refname = refname[1..];
                refnode.Attributes["auto"] = true;
                _document!.NoteAutofootnoteRef(refnode);
            }
            else if (refname == "*")
            {
                refname = "";
                refnode.Attributes["auto"] = "*";
                _document!.NoteSymbolFootnoteRef(refnode);
            }
            else
            {
                refnode.Add(new Text(label));
            }

            if (refname.Length > 0)
            {
                refnode.Attributes["refname"] = refname;
                _document!.NoteFootnoteRef(refnode);
            }
            if (_document!.Settings.trim_footnote_reference_space)
            {
                before = before.TrimEnd();
            }
        }
        return (before, new List<Node> { returnNode }, remaining, new List<SystemMessage>());

    }

    private (string, List<Node>, string, List<SystemMessage>) HandleSubstitutionReference(MatchWrapper match, int lineno)
    {
        var (before, inlines, remaining, sysmessages, endstring) = HandleInlineObj(
            match, lineno, Patterns.substitution_ref, SubstitutionReference.Make
        );
        if (inlines.Count == 1)
        {
            if (inlines[0] is SubstitutionReference subref_node)
            {
                var subref_text = subref_node.AsText();
                _document!.NoteSubstitutionRef(subref_node, subref_text);
                if (endstring[^1] == '_')
                {
                    var reference_node = new Reference(
                        $"|{subref_text}{endstring}", ""
                    );
                    if (endstring[^2..] == "__")
                    {
                        reference_node.Attributes["anonymous"] = true;
                    }
                    else
                    {
                        reference_node.Attributes["refname"] = Util.FullyNormalizeName(subref_text);
                        _document!.NoteRefname(reference_node);
                    }
                    reference_node.Add(subref_node);
                    inlines = new List<Node> { reference_node };
                }
            }
        }
        return (before, inlines, remaining, sysmessages);
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleReference(MatchWrapper match, int lineno, bool anonymous = false)
    {
        var referencename = match.Match.Groups["refname"].Value;
        var refname = Util.FullyNormalizeName(referencename);
        var referencenode = new Reference(
            referencename + match.Match.Groups["refend"].Value,
            referencename
        );
        referencenode.Attributes["name"] = Util.WhitespaceNormalizeName(referencename);
        referencenode.Children[0].RawSource = referencename;
        if (anonymous)
        {
            referencenode.Attributes["anonymous"] = true;
        }
        else
        {
            referencenode.Attributes["refname"] = refname;
            _document!.NoteRefname(referencenode);
        }
        var str = match.Text;

        var wholeGroup = match.Match.Groups["whole"];
        var matchstart = wholeGroup.Index;
        var matchend = wholeGroup.Index + wholeGroup.Length;
        return (str[..matchstart], new List<Node> { referencenode }, str[matchend..], new List<SystemMessage>());
    }

    private (string, List<Node>, string, List<SystemMessage>) HandleAnonymousReference(MatchWrapper match, int lineno)
    {
        return HandleReference(match, lineno, anonymous: true);
    }

    private bool QuotedStart(MatchWrapper match)
    {
        // Test if inline markup start-string is 'quoted'

        // 'Quoted' in this context means the start-string is enclosed in a pair
        // of matching opening/closing delimiters (not necessarily quotes)
        // or at the end of the match.

        var str = match.Text;
        var start = match.Match.Index;
        if (start == 0)
        {  // start-string at beginning of text
            return false;
        }
        var prestart = str[start - 1];
        try
        {
            var poststart = str[match.Match.Index + match.Match.Length];
            return PunctuationChars.match_chars(prestart, poststart);
        }
        catch (IndexOutOfRangeException)
        {  // start-string at end of text
            return true;  // not "quoted" but no markup start-string either
        }
    }


    private (string, List<Node>, string, List<SystemMessage>, string) HandleInlineObj(
        MatchWrapper match,
        int lineno,
        RegexWrapper end_pattern,
        Func<string, string, Node> nodeclass,
        bool restore_backslashes = false
    )
    {
        var str = match.Text;
        var startGroup = match.Match.Groups["start"];
        var matchstart = startGroup.Index;
        var matchend = startGroup.Index + startGroup.Length;

        string text;

        if (QuotedStart(match))
        {
            return (str[..matchend], new List<Node>(), str[matchend..], new List<SystemMessage>(), "");
        }
        var endmatch = end_pattern.Search(str[matchend..]);
        if (endmatch.Match.Success && endmatch.Match.Groups[1].Success)
        { // 1 or more chars
            text = endmatch.Text[..endmatch.Match.Groups[1].Index];
            if (restore_backslashes)
            {
                text = Util.Unescape(text, true);
            }

            var firstGroup = endmatch.Match.Groups[1];
            var textend = matchend + firstGroup.Index + firstGroup.Length;
            var rawsource = Util.Unescape(str[matchstart..textend], true);
            var node = nodeclass(rawsource, text);
            return (
                str[..matchstart],
                new List<Node> { node },
                str[textend..],
                new List<SystemMessage>(),
                endmatch.Match.Groups[1].Value
            );
        }
        var msg = _reporter!.Warning(
            $"Inline {nodeclass} start-string without end-string.",
            line: lineno
        );
        return (str[..matchstart], new List<Node>(), str[matchend..], new List<SystemMessage> { msg }, "");
    }

    private List<Node> StandaloneUri(MatchWrapper match, int lineno)
    {
        if (
            !match.Match.Groups.ContainsKey("scheme")
            || UriSchemes.SCHEMES.ContainsKey(match.Match.Groups["scheme"].Value.ToLowerInvariant())
        )
        {
            string addscheme = "";
            if (match.Match.Groups.ContainsKey("email"))
            {
                addscheme = "mailto:";
            }
            var text = match.Match.Groups["whole"].Value;
            var refuri = addscheme + Util.Unescape(text);
            var reference = new Reference(Util.Unescape(text, true), text);
            reference.Attributes["refuri"] = refuri;
            return new List<Node> { reference };
        }
        else
        {  // not a valid scheme
            throw new MarkupMismatch();
        }
    }
}

public class StateMachineMemo
{
    public Reporter Reporter { get; set; }
    public Document Document { get; set; }
    public List<StyleKind> TitleStyles { get; set; } = new List<StyleKind>();
    public int SectionLevel { get; set; }
    public bool SectionBubbleUpKludge { get; set; } = false;
    public Inliner Inliner { get; set; }

    public StateMachineMemo(Document document, Inliner inliner)
    {
        Document = document;
        Reporter = document.Reporter;
        Inliner = inliner;
    }
}

public class RSTStateMachine : tinydocutils.StateMachine
{
    public bool MatchTitles { get; set; }
    public StateMachineMemo? Memo { get; set; }
    public Document? Document { get; set; }
    public Element? Node { get; set; }
    public Reporter? Reporter { get; set; }

    public RSTStateMachine(tinydocutils.StateConfiguration stateConfig) : base(stateConfig) { }

    public void RunRST(
        StringList input_lines,
        Document document,
        int input_offset = 0,
        bool matchTitles = true,
        Inliner? inliner = null
    )
    {
        MatchTitles = matchTitles;
        if (inliner is null)
        {
            inliner = new Inliner();
        }
        Memo = new StateMachineMemo(document, inliner);
        Document = document;
        AttachObserver(document.NoteSource);
        Reporter = Memo.Reporter;
        Node = document;
        RunSM(
            input_lines, input_offset, null, Document.Source
        );
    }

    public void RunNestedSM(
        StringList input_lines,
        int input_offset,
        StateMachineMemo memo,
        Element node,
        bool match_titles = true
    )
    {
        MatchTitles = match_titles;
        Memo = memo;
        Document = memo.Document;
        AttachObserver(Document.NoteSource);
        Reporter = memo.Reporter;
        Node = node;
        RunSM(input_lines, input_offset);
    }

    public IHaveBlankFinish GetBlankFinishState(IStateBuilder stateClass)
    {
        var state = States[stateClass];
        return (IHaveBlankFinish)state;
    }
}

public abstract class RSTState : tinydocutils.State
{
    public readonly static IStateBuilder[] STATE_CLASSES = new IStateBuilder[] {
        BodyState.Builder.Instance,
        BulletListState.Builder.Instance,
        DefinitionListState.Builder.Instance,
        EnumeratedListState.Builder.Instance,
        FieldListState.Builder.Instance,
        OptionListState.Builder.Instance,
        LineBlockState.Builder.Instance,
        ExtensionOptionsState.Builder.Instance,
        ExplicitState.Builder.Instance,
        TextState.Builder.Instance,
        DefinitionState.Builder.Instance,
        LineState.Builder.Instance,
        SubstitutionDefState.Builder.Instance
    };

    protected readonly static RegexWrapper BLANK_PAT = new RegexWrapper(" *$");
    protected readonly static RegexWrapper INDENT_PAT = new RegexWrapper(" +");

    protected Element? _parent;
    protected Document? _document;
    protected Inliner? _inliner;
    protected Reporter? _reporter;
    protected StateMachineMemo? _memo;

    private readonly Stack<RSTStateMachine> _nestedSMCache = new Stack<RSTStateMachine>();


    public RSTState(StateMachine sm) : base(sm)
    {
        _stateConfig = new StateConfiguration(
            STATE_CLASSES,
            BodyState.Builder.Instance
        );

        Transitions = new List<TransitionTuple> {
            new TransitionTuple("blank", BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder())
        };
    }

    public override void RuntimeInit()
    {
        var sm = ((RSTStateMachine)_stateMachine);
        var memo = sm.Memo!;
        _memo = memo;
        _reporter = memo.Reporter;
        _inliner = memo.Inliner;
        _document = memo.Document;
        _parent = sm.Node;
        // enable the reporter to determine source and source-line
        if (_reporter.GetSourceAndLine is null)
        {
            _reporter.GetSourceAndLine = (lineno) => _stateMachine.GetSourceAndLine(lineno);
        }
    }


    public void GotoLine(int absLineOffset)
    {
        // Jump to input line `abs_line_offset`, ignoring jumps past the end.

        try
        {
            _stateMachine.GotoLine(absLineOffset);
        }
        catch (EOFError) { }
    }

    public int NestedParse(
        StringList block,
        int input_offset,
        Element node,
        bool match_titles = false,
        StateConfiguration? state_config = null
    )
    {
        // Create a new StateMachine rooted at `node` and run it over the input
        // `block`.
        int use_default = 0;
        if (state_config is null)
        {
            state_config = _stateConfig;
            use_default += 1;
        }
        var block_length = block.Count;

        RSTStateMachine? state_machine = null;
        if (use_default == 1 && _nestedSMCache.Count > 0)
        {
            state_machine = _nestedSMCache.Pop();
        }
        if (state_machine is null)
        {
            Debug.Assert(state_config is not null);
            state_machine = new RSTStateMachine(state_config);
        }
        state_machine.RunNestedSM(
            block, input_offset, memo: _memo!, node: node, match_titles: match_titles
        );
        if (use_default == 1)
        {
            _nestedSMCache.Push(state_machine);
        }
        else
        {
            state_machine.Unlink();
        }
        var new_offset = state_machine.AbsLineOffset();
        // No `block.parent` implies disconnected -- lines aren't in sync:
        if (block.Parent is not null && (block.Count - block_length) != 0)
        {
            // Adjustment for block if modified in nested parse:
            _stateMachine.NextLine(block.Count - block_length);
        }
        return new_offset;
    }

    protected (int, bool) NestedListParse(
        StringList block,
        int input_offset,
        Element node,
        IStateBuilder initial_state,
        bool blank_finish,
        IStateBuilder? blank_finish_state = null,
        EnumeratedListState.EnumeratedListSettings? extra_settings = null,
        bool match_titles = false,
        StateConfiguration? state_config = null
    )
    {
        // Create a new StateMachine rooted at `node` and run it over the input
        // `block`. Also keep track of optional intermediate blank lines and the
        // required final one.

        var state_classes = (state_config is not null) ? state_config.StateClasses : null;
        if (state_classes is null)
        {
            state_classes = _stateConfig!.StateClasses;
        }

        var new_state_config = new StateConfiguration(state_classes, initial_state);

        var state_machine = new RSTStateMachine(new_state_config);
        if (blank_finish_state is null)
        {
            blank_finish_state = initial_state;
        }

        state_machine.GetBlankFinishState(
            blank_finish_state
        ).BlankFinish = blank_finish;

        if (extra_settings is not null)
        {
            var enumeratorState = (EnumeratedListState)state_machine.States[initial_state];
            enumeratorState.Settings = extra_settings;
        }

        state_machine.RunNestedSM(
            block, input_offset, memo: _memo!, node: node, match_titles: match_titles
        );
        blank_finish = state_machine.GetBlankFinishState(
            blank_finish_state
        ).BlankFinish;
        state_machine.Unlink();
        return (state_machine.AbsLineOffset(), blank_finish);
    }

    protected void HandleSection(
        string title,
        string source,
        StyleKind style,
        int lineno,
        List<SystemMessage> messages
    )
    {
        // Check for a valid subsection and create one if it checks out.
        if (CheckSubsection(source, style, lineno))
        {
            NewSubsection(title, lineno, messages);
        }
    }

    protected bool CheckSubsection(string source, StyleKind style, int lineno)
    {
        // Check for a valid subsection header.  Return 1 (true) or None (false).

        // When a new section is reached that isn't a subsection of the current
        // section, back up the line count (use ``previous_line(-x)``), then
        // ``raise EOFError``.  The current StateMachine will finish, then the
        // calling StateMachine can re-examine the title.  This will work its way
        // back up the calling chain until the correct section level isreached.

        // @@@ Alternative: Evaluate the title, store the title info & level, and
        // back up the chain until that level is reached.  Store in memo? Or
        // return in results?

        // :Exception: `EOFError` when a sibling or supersection encountered.

        Debug.Assert(_memo is not null);
        var title_styles = _memo.TitleStyles;
        var mylevel = _memo.SectionLevel;

        // check for existing title style
        int level = title_styles.IndexOf(style) + 1;
        if (level == 0)
        {  // new title style
            if (title_styles.Count == _memo.SectionLevel)
            {   // new subsection
                title_styles.Add(style);
                return true;
            }
            else
            {  // not at lowest level
                _parent!.Add(TitleInconsistent(source, lineno));
                return false;
            }
        }

        if (level <= mylevel)
        {  // sibling or supersection
            _memo!.SectionLevel = level;  // bubble up to parent section
            if (style.Overline is not null)
            {
                _memo.SectionBubbleUpKludge = true;
            }

            // back up 2 lines for underline title, 3 for overline title
            _stateMachine.PreviousLine(style.Count + 1);
            throw new EOFError();  // let parent section re-evaluate
        }
        if (level == mylevel + 1)
        {  // immediate subsection
            return true;
        }
        else
        {  // invalid subsection
            _parent!.Add(TitleInconsistent(source, lineno));
            return false;
        }
    }

    protected SystemMessage TitleInconsistent(string sourcetext, int lineno)
    {
        return _reporter!.Severe(
            "Title level inconsistent:",
            line: lineno
        );
    }

    protected void NewSubsection(
        string title, int lineno, List<SystemMessage> messages
    )
    {
        // Append new subsection to document tree. On return, check level.
        Debug.Assert(_memo is not null);

        var mylevel = _memo.SectionLevel;
        _memo.SectionLevel += 1;
        var section_node = new Section();
        _parent!.Add(section_node);
        var (textnodes, title_messages) = InlineText(title, lineno);
        var titlenode = new Title(title, "");
        titlenode.AddRange(textnodes);
        var name = Util.FullyNormalizeName(titlenode.AsText());
        section_node.Names.Add(name);
        section_node.Add(titlenode);
        section_node.AddRange(messages);
        section_node.AddRange(title_messages);
        _document!.NoteImplicitTarget(section_node, section_node);

        var offset = _stateMachine.LineOffset + 1;
        var absoffset = _stateMachine.AbsLineOffset() + 1;
        var newabsoffset = NestedParse(
            _stateMachine.InputLines![offset..],
            input_offset: absoffset,
            node: section_node,
            match_titles: true
        );
        GotoLine(newabsoffset);
        if (_memo.SectionLevel <= mylevel)
        {  // can't handle next section?
            throw new EOFError();  // bubble up to supersection
        }
        // reset section_level; next pass will detect it properly
        _memo.SectionLevel = mylevel;
    }

    protected (List<Element>, bool) HandleParagraph(
        List<string> lines, int lineno
    )
    {

        // Return a list (paragraph & messages) & a boolean: literal_block next?

        var data = String.Join('\n', lines).TrimEnd();
        string text;
        bool literalnext;
        if (Regex.IsMatch(data, """(?<!\\)(\\\\)*::$"""))
        {
            if (data.Length == 2)
            {
                return (new List<Element>(), true);
            }
            else if (" \n".Contains(data[^3]))
            {
                text = data[..^3].TrimEnd();
            }
            else
            {
                text = data[..^1];
            }
            literalnext = true;
        }
        else
        {
            text = data;
            literalnext = false;
        }

        var (textnodes, messages) = InlineText(text, lineno);
        var p = new Paragraph(data, "");
        p.AddRange(textnodes);

        var sourceAndLine = _stateMachine.GetSourceAndLine(lineno);
        p.Source = sourceAndLine.Item1;
        p.Line = sourceAndLine.Item2;
        var result = new List<Element> { p };
        result.AddRange(messages);
        return (result, literalnext);
    }

    protected (List<Node>, List<SystemMessage>) InlineText(
        string text, int lineno
    )
    {
        // Return 2 lists: nodes (text and inline elements), and system_messages.
        Debug.Assert(_memo is not null);
        Debug.Assert(_parent is not null);
        return _inliner!.Parse(text, lineno, _memo, _parent);
    }

    public SystemMessage UnindentWarning(string node_name)
    {
        // the actual problem is one line below the current line

        int lineno = _stateMachine.AbsLineNumber() + 1;
        return _reporter!.Warning(
            $"{node_name} ends without a blank line; unexpected unindent.",
            line: lineno
        );
    }

    protected virtual TransitionResult BlankTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        // Handle blank lines. Does nothing. Override in subclasses.
        return NopTransition(match, context, next_state);
    }

    protected virtual TransitionResult IndentTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        throw new NotImplementedException();
    }

    protected abstract IStateBuilder GetStateBuilder();
}

public class BodyState : RSTState
{
    public sealed class EnumInfo
    {
        // Enumerated list parsing information.

        public record FormatInfo(string Prefix, string Suffix, int Start, int End);

        public static readonly Dictionary<string, FormatInfo> FORMAT_INFO = new Dictionary<string, FormatInfo> {
            {"parens", new FormatInfo("(", ")", 1, -1)},
            {"rparen", new FormatInfo("", ")", 0, -1)},
            {"period", new FormatInfo("", ".", 0, -1)}
        };

        public static readonly string[] FORMATS = FORMAT_INFO.Keys.ToArray();

        public static readonly string[] SEQUENCES = new string[] {
            "arabic",
            "loweralpha",
            "upperalpha",
            "lowerroman",
            "upperroman",
        };  // ORDERED!
        public static readonly Dictionary<string, string> SEQUENCE_PATS = new Dictionary<string, string> {
            { "arabic", "[0-9]+"},
            { "loweralpha", "[a-z]"},
            { "upperalpha", "[A-Z]"},
            { "lowerroman", "[ivxlcdm]+"},
            { "upperroman", "[IVXLCDM]+"}
        };

        public static readonly Dictionary<string, Func<string, int>> CONVERTERS = new Dictionary<string, Func<string, int>> {
            {"arabic", Int32.Parse},
            {"loweralpha", Util._loweralpha_to_int},
            {"upperalpha", Util._upperalpha_to_int},
            {"lowerroman", Util._lowerroman_to_int},
            {"upperroman", Roman.FromRoman},
        };

        public static readonly Dictionary<string, RegexWrapper> SEQUENCE_REGEXPS = SEQUENCES.Select(
            sequence => new KeyValuePair<string, RegexWrapper>(sequence, new RegexWrapper(SEQUENCE_PATS[sequence] + "$")))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }


    public class PatternRegistry
    {
        public RegexWrapper Bullet { get; init; }
        public RegexWrapper Enumerator { get; init; }
        public RegexWrapper FieldMarker { get; init; }
        public RegexWrapper OptionMarker { get; init; }
        public RegexWrapper Doctest { get; init; }
        public RegexWrapper LineBlock { get; init; }
        public RegexWrapper ExplicitMarkup { get; init; }
        public RegexWrapper Anonymous { get; init; }
        public RegexWrapper Line { get; init; }
        public RegexWrapper Text { get; init; }

        public PatternRegistry()
        {
            // Fragments of patterns used by transitions.
            var nonalphanum7bit = "[!-/:-@[-`{-~]";
            var alpha = "[a-zA-Z]";
            var alphanum = "[a-zA-Z0-9]";
            var alphanumplus = "[a-zA-Z0-9_-]";
            var optname = $"{alphanum}{alphanumplus}*";
            var optarg = $"({alpha}{alphanumplus}*|<[^<>]+>)";
            var shortopt = $"""(-|\+){alphanum}( ?{optarg})?""";
            var longopt = $"""(--|/){optname}([ =]{optarg})?""";
            var option = $"""({shortopt}|{longopt})""";

            var enumFragment = (
                $"({EnumInfo.SEQUENCE_PATS["arabic"]}|{EnumInfo.SEQUENCE_PATS["loweralpha"]}|{EnumInfo.SEQUENCE_PATS["upperalpha"]}|{EnumInfo.SEQUENCE_PATS["lowerroman"]}" +
                $"|{EnumInfo.SEQUENCE_PATS["upperroman"]}|#)"
            );

            var enumPats = new Dictionary<string, string>(EnumInfo.FORMATS.Length);
            foreach (var format in EnumInfo.FORMATS)
            {
                enumPats[format] = "(?<" + format + ">" +
                    Regex.Escape(EnumInfo.FORMAT_INFO[format].Prefix) +
                    enumFragment +
                    Regex.Escape(EnumInfo.FORMAT_INFO[format].Suffix) +
                    ")";
            }

            Bullet = new RegexWrapper("[-+*\u2022\u2023\u2043]( +|$)");
            Enumerator = new RegexWrapper($"""({enumPats["parens"]}|{enumPats["rparen"]}|{enumPats["period"]})( +|$)""");
            FieldMarker = new RegexWrapper(""":(?![: ])([^:\\]|\\.|:(?!([ `]|$)))*(?<! ):( +|$)""");
            OptionMarker = new RegexWrapper($"""{option}(, {option})*(  +| ?$)""");
            Doctest = new RegexWrapper(""">>>( +|$)""");
            LineBlock = new RegexWrapper("""\|( +|$)""");
            ExplicitMarkup = new RegexWrapper("""\.\.( +|$)""");
            Anonymous = new RegexWrapper("""__( +|$)""");
            Line = new RegexWrapper($"""({nonalphanum7bit})\1* *$""");
            Text = new RegexWrapper("");
        }
    }

    public static readonly PatternRegistry Patterns = new PatternRegistry();

    class ExplicitInfo
    {
        // Patterns and constants used for explicit markup recognition.

        public static readonly RegexWrapper PAT_TARGET = new RegexWrapper(
            $"""
                            ^(
                            _               (?# anonymous target)
                            |               (?# *OR*)
                            (?!_)           (?# no underscore at the beginning)
                            (?<quote>`?)   (?# optional open quote)
                            (?![ `])        (?# first char. not space or)
                                            (?# backquote)
                            (?<name>       (?# reference name)
                                .+?
                            )
                            {Inliner.PatternRegistry.non_whitespace_escape_before}
                            (?P=quote)      (?# close quote if open quote used)
                            )
                            (?<!(?<!\x00):) (?# no unescaped colon at end)
                            {Inliner.PatternRegistry.non_whitespace_escape_before}
                            [ ]?            (?# optional space)
                            :               (?# end of reference name)
                            ([ ]+|$)        (?# followed by whitespace)
                            """,
            RegexOptions.IgnorePatternWhitespace
        );
        public static readonly RegexWrapper PAT_REFERENCE = new RegexWrapper(
            $"""
                            ^(
                                (?<simple>{Inliner.PatternRegistry.simplename})_
                            |                  (?# *OR*)
                                `                  (?# open backquote)
                                (?![ ])            (?# not space)
                                (?<phrase>.+?)    (?# hyperlink phrase)
                                {Inliner.PatternRegistry.non_whitespace_escape_before}
                                `_                 (?# close backquote,)
                                                    (?# reference mark)
                            )
                            $                  (?# end of string)
                            """,
            RegexOptions.IgnorePatternWhitespace
        );
        public static readonly RegexWrapper PAT_SUBSTITUTION = new RegexWrapper(
            $"""
                                ^(
                                    (?![ ])          (?# first char. not space)
                                    (?<name>.+?)    (?# substitution text)
                                    {Inliner.PatternRegistry.non_whitespace_escape_before}
                                    \|               (?# close delimiter)
                                )
                                ([ ]+|$)             (?# followed by whitespace)
                                """,
            RegexOptions.IgnorePatternWhitespace
        );
    }

    public List<(Func<MatchWrapper, (List<Node>, bool)>, RegexWrapper)> Constructs { get; init; }

    private static readonly RegexWrapper PAT_FOOTNOTE = new RegexWrapper(
        $"""
            ^\.\.[ ]+                                      (?# explicit markup start)
            \[
            (                                             (?# footnote label:)
                [0-9]+                                      (?# manually numbered footnote)
            |                                           (?# *OR*)
                \#                                          (?# anonymous auto-numbered footnote)
            |                                           (?# *OR*)
                \#{Inliner.PatternRegistry.simplename}      (?# auto-number ed? footnote label)
            |                                           (?# *OR*)
                \*                                        (?# auto-symbol footnote)
            )
            \]
            ([ ]+|$)                                      (?# whitespace or end of line)
            """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled
    );

    private static readonly RegexWrapper PAT_CITATION = new RegexWrapper(
        $"""
            ^\.\.[ ]+          (?# explicit markup start)
            \[({Inliner.PatternRegistry.simplename})\]          (?# citation label)
            ([ ]+|$)          (?# whitespace or end of line)
            """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled
    );

    private static readonly RegexWrapper PAT_HYPERLINK_TARGET = new RegexWrapper(
        """
            ^\.\.[ ]+          (?# explicit markup start)
            _                 (?# target indicator)
            (?![ ]|$)         (?# first char. not space or EOL)
            """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled
    );

    private static readonly RegexWrapper PAT_SUBSTITUTION_DEF = new RegexWrapper(
        """
            ^\.\.[ ]+          (?# explicit markup start)
            \|                (?# substitution indicator)
            (?![ ]|$)         (?# first char. not space or EOL)
            """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled
    );

    private static readonly RegexWrapper PAT_DIRECTIVE = new RegexWrapper(
        $"""
            ^\.\.[ ]+                                  (?# explicit markup start)
            ({Inliner.PatternRegistry.simplename})    (?# directive name)
            [ ]?                                      (?# optional space)
            ::                                        (?# directive delimiter)
            ([ ]+|$)                                  (?# whitespace or end of line)
            """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled
    );

    public BodyState(StateMachine sm) : base(sm)
    {
        _stateConfig = new StateConfiguration(
            STATE_CLASSES,
            BodyState.Builder.Instance
        );

        Transitions = new List<TransitionTuple> {
            new TransitionTuple("blank", RSTState.BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", RSTState.INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("bullet", Patterns.Bullet, (match, context, nextState) => BulletTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("enumerator", Patterns.Enumerator, (match, context, nextState) => EnumeratorTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("field_marker", Patterns.FieldMarker, (match, context, nextState) => FieldMarkerTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("option_marker", Patterns.OptionMarker, (match, context, nextState) => OptionMarkerTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("doctest", Patterns.Doctest, (match, context, nextState) => DoctestTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("line_block", Patterns.LineBlock, (match, context, nextState) => LineBlockTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("explicit_markup", Patterns.ExplicitMarkup, (match, context, nextState) => ExplicitMarkupTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("anonymous", Patterns.Anonymous, (match, context, nextState) => AnonymousTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("line", Patterns.Line, (match, context, nextState) => LineTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("text", Patterns.Text, (match, context, nextState) => TextTransition(match, context, nextState), GetStateBuilder()),
        };

        Constructs = new List<(Func<MatchWrapper, (List<Node>, bool)>, RegexWrapper)> {
            ((match) => HandleFootnote(match), PAT_FOOTNOTE),
            ((match) => HandleCitation(match), PAT_CITATION),
            ((match) => HandleHyperlinkTarget(match), PAT_HYPERLINK_TARGET),
            ((match) => HandleSubstitutionDef(match), PAT_SUBSTITUTION_DEF),
            ((match) => HandleDirective(match), PAT_DIRECTIVE)
        };
    }

    protected virtual TransitionResult BulletTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        var bulletlist = new BulletList();
        Debug.Assert(_stateMachine.InputLines is not null);
        var sourceAndLine = _stateMachine.GetSourceAndLine();
        bulletlist.Source = sourceAndLine.Item1;
        bulletlist.Line = sourceAndLine.Item2;

        _parent!.Add(bulletlist);
        bulletlist.Attributes["bullet"] = match.Match.Groups[0].Value;
        var (i, blank_finish) = HandleListItem(match.Match.Index + match.Match.Length);
        bulletlist.Add(i);
        var offset = _stateMachine.LineOffset + 1;  // next line
        (var new_line_offset, blank_finish) = NestedListParse(
            _stateMachine.InputLines[offset..],
            input_offset: _stateMachine.AbsLineOffset() + 1,
            node: bulletlist,
            initial_state: BulletListState.Builder.Instance,
            blank_finish: blank_finish
        );
        GotoLine(new_line_offset);
        if (!blank_finish)
        {
            _parent!.Add(UnindentWarning("Bullet list"));
        }
        return new TransitionResult(new List<string>(), nextState);
    }

    protected virtual TransitionResult EnumeratorTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // Enumerated List Item
        var (format, sequence, text, ordinal) = ParseEnumerator(match);
        if (!IsEnumeratedListItem(ordinal, sequence, format))
        {
            throw new TransitionCorrection("text");
        }
        var enumlist = new EnumeratedList();
        _parent!.Add(enumlist);
        if (sequence == "#")
        {
            enumlist.Attributes["enumtype"] = "arabic";
        }
        else
        {
            enumlist.Attributes["enumtype"] = sequence;
        }
        enumlist.Attributes["prefix"] = EnumInfo.FORMAT_INFO[format].Prefix;
        enumlist.Attributes["suffix"] = EnumInfo.FORMAT_INFO[format].Suffix;
        if (ordinal != 1)
        {
            enumlist.Attributes["start"] = ordinal;
            var msg = _reporter!.Info(
                $"Enumerated list start value not ordinal-1: '{text}' (ordinal {ordinal})"
            );
            _parent.Add(msg);
        }
        var (listitem, blank_finish) = HandleListItem(match.Match.Index + match.Match.Length);
        enumlist.Add(listitem);

        var offset = _stateMachine.LineOffset + 1;  // next line
        (var newline_offset, blank_finish) = NestedListParse(
            _stateMachine.InputLines![offset..],
            input_offset: _stateMachine.AbsLineOffset() + 1,
            node: enumlist,
            initial_state: EnumeratedListState.Builder.Instance,
            blank_finish: blank_finish,
            extra_settings: new EnumeratedListState.EnumeratedListSettings(ordinal, format, sequence == "#")
        );
        GotoLine(newline_offset);
        if (!blank_finish)
        {
            _parent.Add(UnindentWarning("Enumerated list"));
        }
        return new TransitionResult(new List<string>(), nextState);
    }

    protected (string, string, string, int) ParseEnumerator(
        MatchWrapper match, string? expected_sequence = null
    )
    {
        // Analyze an enumerator and return the results.

        // :Return:
        //     - the enumerator format ('period', 'parens', or 'rparen'),
        //     - the sequence used ('arabic', 'loweralpha', 'upperroman', etc.),
        //     - the text of the enumerator, stripped of formatting, and
        //     - the ordinal value of the enumerator ('a' -> 1, 'ii' -> 2, etc.;
        //       ``None`` is returned for invalid enumerator text).

        // The enumerator format has already been determined by the regular
        // expression match. If `expected_sequence` is given, that sequence is
        // tried first. If not, we check for Roman numeral 1. This way,
        // single-character Roman numerals (which are also alphabetical) can be
        // matched. If no sequence has been matched, all sequences are checked in
        // order.

        var sequence = "";
        int ordinal;
        string? selectedFormat = null;
        foreach (var format in EnumInfo.FORMATS)
        {
            if (match.Match.Groups[format].Length > 0)
            {  // was this the format matched?
                selectedFormat = format;
                break;  // yes; keep `format`
            }
        }

        if (selectedFormat is null)
        {  // shouldn't happen
            throw new ParserError("enumerator format not matched");
        }
        var text = match.Match.Groups[selectedFormat].Value[
            EnumInfo.FORMAT_INFO[selectedFormat].Start..EnumInfo.FORMAT_INFO[selectedFormat].End
        ];
        if (text == "#")
        {
            sequence = "#";
        }
        else if (!String.IsNullOrEmpty(expected_sequence))
        {
            try
            {
                if (EnumInfo.SEQUENCE_REGEXPS[expected_sequence].RegexAnchoredAtStart.IsMatch(text))
                {
                    sequence = expected_sequence;
                }
            }
            catch (KeyNotFoundException)
            {  // shouldn't happen
                throw new ParserError($"unknown enumerator sequence: {sequence}");
            }
        }
        else if (text == "i")
        {
            sequence = "lowerroman";
        }
        else if (text == "I")
        {
            sequence = "upperroman";
        }
        if (String.IsNullOrEmpty(sequence))
        {
            foreach (var candidateSequence in EnumInfo.SEQUENCES)
            {
                if (EnumInfo.SEQUENCE_REGEXPS[sequence].RegexAnchoredAtStart.IsMatch(text))
                {
                    sequence = candidateSequence;
                    break;
                }
            }

            if (String.IsNullOrEmpty(sequence))
            {  // shouldn't happen
                throw new ParserError("enumerator sequence not matched");
            }
        }
        if (sequence == "#")
        {
            ordinal = 1;
        }
        else
        {
            try
            {
                ordinal = EnumInfo.CONVERTERS[sequence](text);
            }
            catch (ArgumentOutOfRangeException err)
            {
                throw new ParserError("Roman numeral error: " + err.Message);
            }
        }
        return (selectedFormat, sequence, text, ordinal);
    }

    protected bool IsEnumeratedListItem(
        int ordinal, string sequence, string format
    )
    {
        // Check validity based on the ordinal value and the second line.

        // Return true if the ordinal is valid and the second line is blank,
        // indented, or starts with the next enumerator or an auto-enumerator.

        string next_line;
        try
        {
            next_line = _stateMachine.NextLine();
        }
        catch (EOFError)
        {  // end of input lines
            _stateMachine.PreviousLine();
            return true;
        }

        _stateMachine.PreviousLine();

        if (next_line[..1].Trim().Length == 0)
        {  // blank or indented
            return true;
        }
        var result = MakeEnumerator(ordinal + 1, sequence, format);
        if (result is not null)
        {
            var (next_enumerator, auto_enumerator) = ((string, string))result;
            if (next_line.StartsWith(next_enumerator) || next_line.StartsWith(
                auto_enumerator
            ))
            {
                return true;
            }
        }
        return false;
    }

    protected (string, string)? MakeEnumerator(
        int ordinal, string sequence, string format
    )
    {
        // Construct and return the next enumerated list item marker, and an
        // auto-enumerator ("#" instead of the regular enumerator).

        // Return ``None`` for invalid (out of range) ordinals.
        string enumerator;

        if (sequence == "#")
        {
            enumerator = "#";
        }
        else if (sequence == "arabic")
        {
            enumerator = ordinal.ToString();
        }
        else
        {
            if (sequence.EndsWith("alpha"))
            {
                if (ordinal > 26)
                {
                    return null;
                }
                enumerator = ((char)(ordinal + (int)'a' - 1)).ToString();
            }
            else if (sequence.EndsWith("roman"))
            {
                try
                {
                    enumerator = Roman.ToRoman(ordinal);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
            else
            {  // shouldn't happen
                throw new ParserError($"unknown enumerator sequence: '{sequence}'");
            }
            if (sequence.StartsWith("lower"))
            {
                enumerator = enumerator.ToLowerInvariant();
            }
            else if (sequence.StartsWith("upper"))
            {
                enumerator = enumerator.ToUpperInvariant();
            }
            else
            {  // shouldn't happen
                throw new ParserError($"unknown enumerator sequence: '{sequence}'");
            }
        }
        var formatinfo = EnumInfo.FORMAT_INFO[format];
        var next_enumerator = formatinfo.Prefix + enumerator + formatinfo.Suffix + " ";
        var auto_enumerator = formatinfo.Prefix + "#" + formatinfo.Suffix + " ";
        return (next_enumerator, auto_enumerator);
    }

    protected virtual TransitionResult FieldMarkerTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // Field list item.
        var field_list = new FieldList();
        _parent!.Add(field_list);
        var (field, blank_finish) = FieldHandler(match);
        field_list.Add(field);

        var offset = _stateMachine.LineOffset + 1;  // next line
        (var newline_offset, blank_finish) = NestedListParse(
            _stateMachine.InputLines![offset..],
            input_offset: _stateMachine.AbsLineOffset() + 1,
            node: field_list,
            initial_state: FieldListState.Builder.Instance,
            blank_finish: blank_finish
        );
        GotoLine(newline_offset);
        if (!blank_finish)
        {
            _parent.Add(UnindentWarning("Field list"));
        }
        return new TransitionResult(new List<string>(), nextState);
    }

    protected (Field, bool) FieldHandler(MatchWrapper match)
    {
        var name = ParseFieldMarker(match);

        var (src, srcline) = _stateMachine.GetSourceAndLine();
        var lineno = _stateMachine.AbsLineNumber();
        var (
            indented,
            indent,
            line_offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length);
        var field_node = new Field();
        field_node.Source = src;
        field_node.Line = srcline;
        var (name_nodes, name_messages) = InlineText(name, lineno);
        var field_name = new FieldName(name, "");
        field_name.AddRange(name_nodes);
        field_node.Add(field_name);
        var field_body = new FieldBody(String.Join('\n', indented));
        field_body.AddRange(name_messages);
        field_node.Add(field_body);
        if (indented.Count > 0)
        {
            ParseFieldBody(indented, line_offset, field_body);
        }
        return (field_node, blank_finish);
    }

    protected string ParseFieldMarker(MatchWrapper match)
    {
        // Extract & return field name from a field marker match.
        var field = match.Text[1..];               // strip off leading ':'
        field = field[..field.LastIndexOf(':')];    // strip off trailing ':' etc.
        return field;
    }


    protected virtual TransitionResult OptionMarkerTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // Option list item.
        var optionlist = new OptionList();
        var sourceAndLine = _stateMachine.GetSourceAndLine();
        optionlist.Source = sourceAndLine.Item1;
        optionlist.Line = sourceAndLine.Item2;

        OptionListItem listitem;
        bool blank_finish;

        try
        {
            (listitem, blank_finish) = HandleOptionListItem(match);
        }
        catch (MarkupError error)
        {
            // This shouldn't happen; pattern won't match.
            var msg = _reporter!.Error($"Invalid option list marker: {error}");
            _parent!.Add(msg);
            (
                var indented,
                var indent,
                var line_offset,
                blank_finish
            ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length);
            var elements = HandleBlockQuote(indented, line_offset);
            _parent.AddRange(elements);
            if (!blank_finish)
            {
                _parent.Add(UnindentWarning("Option list"));
            }
            return new TransitionResult(new List<string>(), nextState);
        }
        _parent!.Add(optionlist);
        optionlist.Add(listitem);
        var offset = _stateMachine.LineOffset + 1;  // next line
        (var newline_offset, blank_finish) = NestedListParse(
            _stateMachine.InputLines![offset..],
            input_offset: _stateMachine.AbsLineOffset() + 1,
            node: optionlist,
            initial_state: OptionListState.Builder.Instance,
            blank_finish: blank_finish
        );
        GotoLine(newline_offset);
        if (blank_finish)
        {
            _parent.Add(UnindentWarning("Option list"));
        }
        return new TransitionResult(new List<string>(), nextState);
    }

    protected virtual TransitionResult DoctestTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        var data = String.Join('\n', _stateMachine.GetTextBlock());
        // TODO: prepend class value ['pycon'] (Python Console)
        // parse with `directives.body.CodeBlock` (returns literal-block
        // with class "code" and syntax highlight markup).
        _parent!.Add(new DoctestBlock(data, data));
        return new TransitionResult(new List<string>(), nextState);
    }

    protected virtual TransitionResult LineBlockTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // First line of a line block.
        var block = new LineBlock();
        _parent!.Add(block);

        var lineno = _stateMachine.AbsLineNumber();
        var (line, messages, blank_finish) = LineBlockLine(match, lineno);
        block.Add(line);
        _parent!.AddRange(messages);
        if (!blank_finish)
        {
            var offset = _stateMachine.LineOffset + 1;  // next line
            (var new_line_offset, blank_finish) = NestedListParse(
                _stateMachine.InputLines![offset..],
                input_offset: _stateMachine.AbsLineOffset() + 1,
                node: block,
                initial_state: LineBlockState.Builder.Instance,
                blank_finish: false
            );
            GotoLine(new_line_offset);
        }
        if (!blank_finish)
        {
            _parent.Add(
                _reporter!.Warning(
                    "Line block ends without a blank line.", line: lineno + 1
                )
            );
        }
        if (block.Count() > 0)
        {
            NestLineBlockLines(block);
        }
        return new TransitionResult(new List<string>(), nextState);
    }

    protected virtual TransitionResult ExplicitMarkupTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // Footnotes, hyperlink targets, directives, comments.
        var (nodelist, blank_finish) = ExplicitConstruct(match);
        _parent!.AddRange(nodelist);
        HandleExplicitList(blank_finish);
        return new TransitionResult(new List<string>(), nextState);

    }

    protected virtual TransitionResult AnonymousTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // Anonymous hyperlink targets.
        var (nodelist, blank_finish) = HandleAnonymousTarget(match);
        _parent!.AddRange(nodelist);
        HandleExplicitList(blank_finish);
        return new TransitionResult(new List<string>(), nextState);
    }

    protected virtual TransitionResult LineTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        // Section title overline or transition marker.

        if (((RSTStateMachine)_stateMachine).MatchTitles)
        {
            return new TransitionResult(new List<string> { match.Text }, LineState.Builder.Instance);
        }
        else if (match.Text.Trim() == "::")
        {
            throw new TransitionCorrection("text");
        }
        else if (match.Text.Trim().Length < 4)
        {
            var msg = _reporter!.Info(
                "Unexpected possible title overline or transition.\n" +
                "Treating it as ordinary text because it's so short.",
                line: _stateMachine.AbsLineNumber()
            );
            _parent!.Add(msg);
            throw new TransitionCorrection("text");
        }
        else
        {
            var blocktext = _stateMachine.Line;
            Debug.Assert(blocktext is not null);
            var msg = _reporter!.Severe(
                "Unexpected section title or transition.",
                line: _stateMachine.AbsLineNumber()
            );
            _parent!.Add(msg);
            return new TransitionResult(new List<string>(), nextState);
        }
    }

    protected virtual TransitionResult TextTransition(MatchWrapper match, List<string> context, IStateBuilder nextState)
    {
        return new TransitionResult(new List<string> { match.Text }, TextState.Builder.Instance);
    }

    private (List<Target>, bool) HandleAnonymousTarget(MatchWrapper match)
    {
        var lineno = _stateMachine.AbsLineNumber();
        var (
            block,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length, untilBlank: true);
        var blocktext = match.Text[..(match.Match.Index + match.Match.Length)] + String.Join('\n', block);
        var escaped_block = block.Select(line => Util.Escape2Null(line)).ToList();
        var target = MakeTarget(escaped_block, blocktext, lineno, "");
        return (new List<Target> { target }, blank_finish);
    }

    private Target MakeTarget(
        List<string> block, string block_text, int lineno, string target_name
    )
    {
        var (target_type, data) = ParseTarget(block);
        Debug.Assert(data is not null);
        if (target_type == "refname")
        {
            var target = new Target(block_text, "");
            target.Attributes["refname"] = Util.FullyNormalizeName(data);
            AddTarget(target_name, "", target, lineno);
            _document!.NoteIndirectTarget(target);
            return target;
        }
        else if (target_type == "refuri")
        {
            var target = new Target(block_text, "");
            AddTarget(target_name, data, target, lineno);
            return target;
        }
        else
        {
            throw new UnreachableException();
        }
    }

    private (string, string?) ParseTarget(List<string> block)
    {
        // Determine the type of reference of a target.

        // :Return: A 2-tuple, one of:

        //     - 'refname' and the indirect reference name
        //     - 'refuri' and the URI
        //     - 'malformed' and a system_message node

        string reference;
        if (block.Count > 0 && block[^1].Trim().Last() == '_')
        {  // possible indirect target
            reference = String.Join(' ', block.Select(line => line.Trim()));
            var refname = IsReference(reference);
            if (!String.IsNullOrEmpty(refname))
            {
                return ("refname", refname);
            }
        }
        var ref_parts = Util.SplitEscapedWhitespace(String.Join(' ', block));

        reference = String.Join(' ', ref_parts.Select(part => String.Concat(Util.Unescape(part).Split())));
        return ("refuri", reference);
    }

    private string? IsReference(string reference)
    {
        var match = ExplicitInfo.PAT_REFERENCE.MatchAnchored(Util.WhitespaceNormalizeName(reference));
        if (!match.Match.Success)
        {
            return null;
        }

        var result = match.Match.Groups["simple"].Value;
        if (result.Length == 0)
        {
            result = match.Match.Groups["phrase"].Value;
        }

        return Util.Unescape(result);
    }

    private void AddTarget(
        string targetname, string refuri, Target target, int lineno
    )
    {
        target.Line = lineno;
        if (targetname.Length > 0)
        {
            var name = Util.FullyNormalizeName(Util.Unescape(targetname));
            target.Names.Add(name);
            if (refuri.Length > 0)
            {
                var uri = _inliner!.AdjustUri(refuri);
                if (!String.IsNullOrEmpty(uri))
                {
                    target.Attributes["refuri"] = uri;
                }
                else
                {
                    throw new ApplicationError($"problem with URI: {refuri}");
                }
            }
            Debug.Assert(_parent is not null);
            _document!.NoteExplicitTarget(target, _parent);
        }
        else
        {  // anonymous target
            if (refuri.Length > 0)
            {
                target.Attributes["refuri"] = refuri;
            }
            target.Attributes["anonymous"] = true;
            _document!.NoteAnonymousTarget(target);
        }
    }


    protected (ListItem, bool) HandleListItem(int indent)
    {
        StringList indented;
        int line_offset;
        bool blank_finish;
        if (_stateMachine.Line![indent..].Length > 0)
        {
            (indented, line_offset, blank_finish) = _stateMachine.GetKnownIndented(
                indent
            );
        }
        else
        {
            (
                indented,
                indent,
                line_offset,
                blank_finish
            ) = _stateMachine.GetFirstKnownIndented(indent);
        }
        var listitem = new ListItem(String.Join('\n', indented));
        if (indented.Count > 0)
        {
            NestedParse(indented, input_offset: line_offset, node: listitem);
        }
        return (listitem, blank_finish);

    }

    protected (OptionListItem, bool) HandleOptionListItem(MatchWrapper match)
    {
        var offset = _stateMachine.AbsLineOffset();
        var options = ParseOptionMarker(match);
        var (
            indented,
            indent,
            line_offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length);
        if (indented.Count == 0)
        {  // not an option list item
            GotoLine(offset);
            throw new TransitionCorrection("text");
        }
        var option_group = new OptionGroup("");
        option_group.Children.AddRange(options);
        var description = new Description(String.Join('\n', indented));
        var option_list_item = new OptionListItem("", option_group, description);
        if (indented.Count == 0)
        {
            NestedParse(indented, input_offset: line_offset, node: description);
        }
        return (option_list_item, blank_finish);
    }

    private void HandleExplicitList(bool blank_finish)
    {
        // Create a nested state machine for a series of explicit markup
        // constructs (including anonymous hyperlink targets).

        var offset = _stateMachine.LineOffset + 1; //  next line
        Debug.Assert(_parent is not null);
        (var newline_offset, blank_finish) = NestedListParse(
            _stateMachine.InputLines![offset..],
            input_offset: _stateMachine.AbsLineOffset() + 1,
            node: _parent,
            initial_state: ExplicitState.Builder.Instance,
            blank_finish: blank_finish,
            match_titles: ((RSTStateMachine)_stateMachine).MatchTitles
        );
        GotoLine(newline_offset);
        if (!blank_finish)
        {
            _parent.Add(UnindentWarning("Explicit markup"));
        }
    }

    private (List<Node>, bool) ExplicitConstruct(MatchWrapper match)
    {
        // Determine which explicit construct this is, parse & return it.
        var errors = new List<SystemMessage>();
        foreach (var (method, pattern) in Constructs)
        {
            var expmatch = pattern.Search(match.Text);
            if (expmatch.Match.Success)
            {
                try
                {
                    return method(expmatch);
                }
                catch (MarkupError error)
                {
                    var lineno = _stateMachine.AbsLineNumber();
                    errors.Add(_reporter!.Warning(error.Message, line: lineno));
                    break;
                }
            }
        }
        var (nodelist, blank_finish) = HandleComment(match);
        nodelist.AddRange(errors);
        return (nodelist, blank_finish);
    }


    private (List<Node>, bool) HandleComment(MatchWrapper match)
    {
        if (
            match.Text[(match.Match.Index + match.Match.Length)..].Trim().Length == 0
            && _stateMachine!.IsNextLineBlank()
        )
        {  // an empty comment?
            return (new List<Node> { new Comment() }, true);  // "A tiny but practical wart."
        }
        var (
            indented,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length);
        while (indented.Count > 0 && indented[^1].Trim().Length == 0)
        {
            indented.TrimEnd();
        }
        var text = String.Join('\n', indented);
        return (new List<Node> { new Comment(text, text) }, blank_finish);
    }

    private List<Option> ParseOptionMarker(MatchWrapper match)
    {
        // Return a list of `node.option` and `node.option_argument` objects,
        // parsed from an option marker match.

        // :Exception: `MarkupError` for invalid option markers.
        var optlist = new List<Option>();
        var optionstrings = match.Text.TrimEnd().Split(", ");
        foreach (var optionstring in optionstrings)
        {
            var tokens = optionstring.Split().ToList();
            var delimiter = " ";
            var firstopt = tokens[0].Split("=", 1);
            if (firstopt.Length > 1)
            {
                // "--opt=value" form
                var finalToken = tokens.Last();
                tokens = firstopt.ToList();
                tokens.Add(finalToken);
                delimiter = "=";
            }
            else if (tokens[0].Length > 2 && (
                (tokens[0].StartsWith("-") && !tokens[0].StartsWith("--"))
                || tokens[0].StartsWith("+")))
            {
                // "-ovalue" form
                tokens = new List<string> { tokens[0][..2], tokens[0][2..], tokens.Last() };
                delimiter = "";
            }
            if (tokens.Count > 1 && (
                tokens[1].StartsWith("<") && tokens[^1].EndsWith(">")
            ))
            {
                // "-o <value1 value2>" form; join all values into one token
                tokens = new List<string> { tokens[0], String.Join(' ', tokens.Skip(1)) };
            }

            if (tokens.Count > 0 && tokens.Count <= 2)
            {
                var option = new Option(optionstring);
                option.Add(new OptionString(tokens[0], tokens[0]));
                if (tokens.Count > 1)
                {
                    option.Add(
                        new OptionArgument(tokens[1], tokens[1], delimiter: delimiter)
                    );
                }
                optlist.Add(option);
            }
            else
            {
                throw new MarkupError($"wrong number of option tokens (={tokens.Count}), should be 1 or 2: '{optionstring}'");
            }
        }
        return optlist;
    }

    public virtual void ParseFieldBody(
        StringList indented, int offset, Element node
    )
    {
        NestedParse(indented, input_offset: offset, node: node);
    }

    protected (Line, List<SystemMessage>, bool) LineBlockLine(MatchWrapper match, int lineno)
    {
        // Return one line element of a line_block.

        var (
            indented,
            indent,
            line_offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length, untilBlank: true);
        var text = String.Join('\n', indented);
        var (text_nodes, messages) = InlineText(text, lineno);
        var line = new Line(text, "");
        foreach (var node in text_nodes)
        {
            line.Add(node);
        }
        if (match.Text.TrimEnd() != "|")
        {  // not empty
            line.Indent = match.Match.Groups[1].Length - 1;
        }
        return (line, messages, blank_finish);
    }


    private void NestLineBlockLines(LineBlock block)
    {
        var lines = block.Lines().ToList();
        foreach (var index in Enumerable.Range(1, lines.Count))
        {
            if (lines[index] is null)
            {
                lines[index].Indent = lines[index - 1].Indent;
            }
        }
        NestLineBlockSegment(block);
    }

    private void NestLineBlockSegment(LineBlock block)
    {
        var lines = block.Lines().ToList();
        var indents = lines.Select(line => line.Indent).ToList();
        var least = Enumerable.Min(indents);
        var new_items = new List<Element>();
        var new_block = new LineBlock();
        foreach (var item in lines)
        {
            if (item.Indent > least)
            {
                new_block.Add(item);
            }
            else
            {
                if (new_block.Count > 0)
                {
                    NestLineBlockSegment(new_block);
                    new_items.Add(new_block);
                    new_block = new LineBlock();
                }
                new_items.Add(item);
            }
        }
        if (new_block.Count > 0)
        {
            NestLineBlockSegment(new_block);
            new_items.Add(new_block);
        }

        block.Children.Clear();
        block.Children.AddRange(new_items);
    }

    List<Element> HandleBlockQuote(
        StringList indented, int lineOffset
    )
    {
        var elements = new List<Element>();
        var blockquote = new BlockQuote();
        NestedParse(indented, lineOffset, blockquote);
        elements.Add(blockquote);

        return elements;
    }

    public class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new BodyState(sm);
        }
    }

    protected (List<Node>, bool) HandleFootnote(MatchWrapper match)
    {
        var (src, srcline) = _stateMachine.GetSourceAndLine();
        var (
            indented,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length);
        var label = match.Match.Groups[1].Value;
        var name = Util.FullyNormalizeName(label);
        var footnote = new Footnote(String.Join('\n', indented));
        footnote.Source = src;
        footnote.Line = srcline;
        if (name[0] == '#')
        {  // auto-numbered
            name = name[1..];  // autonumber label
            footnote.Attributes["auto"] = true;
            if (name.Length > 0)
            {
                footnote.Names.Add(name);
            }
            _document!.NoteAutofootnote(footnote);
        }
        else if (name == "*")
        {  // auto-symbol
            name = "";
            footnote.Attributes["auto"] = "*";
            _document!.NoteSymbolFootnote(footnote);
        }
        else
        {  // manually numbered
            footnote.Add(new Label("", label));
            footnote.Names.Add(name);
            _document!.NoteFootnote(footnote);
        }
        if (name.Length > 0)
        {
            _document!.NoteExplicitTarget(footnote, footnote);
        }
        else
        {
            _document!.SetId(footnote, footnote);
        }
        if (indented.Count > 0)
        {
            NestedParse(indented, input_offset: offset, node: footnote);
        }
        return (new List<Node> { footnote }, blank_finish);
    }

    protected (List<Node>, bool) HandleCitation(MatchWrapper match)
    {
        var (src, srcline) = _stateMachine.GetSourceAndLine();
        var (
            indented,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length);
        var label = match.Match.Groups[1].Value;
        var name = Util.FullyNormalizeName(label);
        var citation = new Citation(String.Join('\n', indented));
        citation.Source = src;
        citation.Line = srcline;
        citation.Add(new Label("", label));
        citation.Names.Add(name);
        _document!.NoteCitation(citation);
        _document!.NoteExplicitTarget(citation, citation);
        if (indented.Count > 0)
        {
            NestedParse(indented, input_offset: offset, node: citation);
        }
        return (new List<Node> { citation }, blank_finish);
    }

    protected (List<Node>, bool) HandleHyperlinkTarget(MatchWrapper match)
    {
        var pattern = ExplicitInfo.PAT_TARGET;

        var lineno = _stateMachine.AbsLineNumber();
        var (
            block,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(
            match.Match.Index + match.Match.Length, untilBlank: true, stripIndent: false
        );
        var blocktext = match.Text[..(match.Match.Index + match.Match.Length)] + String.Join('\n', block);
        var escaped_block = block.Select(lineno => Util.Escape2Null(lineno)).ToList();
        var escaped = escaped_block[0];
        var blockindex = 0;
        MatchWrapper targetmatch;
        while (true)
        {
            targetmatch = pattern.MatchAnchored(escaped);
            if (targetmatch.Match.Success)
            {
                break;
            }
            blockindex += 1;
            try
            {
                escaped += escaped_block[blockindex];
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new MarkupError("malformed hyperlink target.");
            }
        }
        escaped_block.RemoveRange(0, blockindex);
        escaped_block[0] = (escaped_block[0] + " ")[
            ((targetmatch.Match.Index + targetmatch.Match.Length) - escaped.Length - 1)..
        ].Trim();
        var target = MakeTarget(
            escaped_block, blocktext, lineno, targetmatch.Match.Groups["name"].Value
        );
        return (new List<Node> { target }, blank_finish);
    }

    protected (List<Node>, bool) HandleSubstitutionDef(MatchWrapper match)
    {
        var pattern = ExplicitInfo.PAT_SUBSTITUTION;

        var (src, srcline) = _stateMachine.GetSourceAndLine();
        var (
            block,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length, stripIndent: false);
        var blocktext = match.Text[..(match.Match.Index + match.Match.Length)] + String.Join('\n', block);
        block.Disconnect();
        var escaped = Util.Escape2Null(block[0].TrimEnd());
        var blockindex = 0;
        MatchWrapper subdefmatch;
        while (true)
        {
            subdefmatch = pattern.MatchAnchored(escaped);
            if (subdefmatch.Match.Success)
            {
                break;
            }
            blockindex += 1;
            try
            {
                escaped = escaped + " " + Util.Escape2Null(block[blockindex].Trim());
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new MarkupError("malformed substitution definition.");
            }
        }
        block.RemoveRange(0, blockindex); // strip out the substitution marker
        var startSliceIndex = Math.Max(0, ((subdefmatch.Match.Index + subdefmatch.Match.Length) - escaped.Length - 1));
        block[0] = (block[0].Trim() + " ")[startSliceIndex..^1];
        if (block[0].Length == 0)
        {
            block.RemoveAt(0);
            offset += 1;
        }
        while (block.Count > 0 && block.Last().Trim().Length == 0)
        {
            block.Pop();
        }
        var subname = subdefmatch.Match.Groups["name"].Value;
        var substitution_node = new SubstitutionDefinition(blocktext);
        substitution_node.Source = src;
        substitution_node.Line = srcline;
        if (block.Count == 0)
        {
            var msg = _reporter!.Warning(
                $"Substitution definition '{subname}' missing contents.",
                line: srcline
            );
            return (new List<Node> { msg }, blank_finish);
        }
        block[0] = block[0].Trim();
        substitution_node.Names.Add(Util.WhitespaceNormalizeName(subname));
        (var new_abs_offset, blank_finish) = NestedListParse(
            block,
            input_offset: offset,
            node: substitution_node,
            initial_state: SubstitutionDefState.Builder.Instance,
            blank_finish: blank_finish
        );
        int i = 0;
        foreach (var node in substitution_node.ToList())
        {
            if (!(node is IInline || node is Text))
            {
                _parent!.Add(substitution_node.Children[i]);
                substitution_node.RemoveAt(i);
            }
            else
            {
                i += 1;
            }
        }
        foreach (var node in substitution_node.Traverse<Element>())
        {
            if (DisallowedInsideSubstitutionDefinitions(node))
            {
                var msg = _reporter!.Error(
                    $"Substitution definition contains illegal element {node}",
                    line: srcline
                );
                return (new List<Node> { msg }, blank_finish);
            }
        }
        if (substitution_node.Count == 0)
        {
            var msg = _reporter!.Warning(
                "Substitution definition '{subname}' empty or invalid.",
                line: srcline
            );
            return (new List<Node> { msg }, blank_finish);
        }

        return (new List<Node> { substitution_node }, blank_finish);
    }

    protected bool DisallowedInsideSubstitutionDefinitions(Element node)
    {
        if (
            node.Ids.Count > 0
            || node is Reference
            && node.Attributes.ContainsKey("anonymous")
            || node is FootnoteReference
            && node.Attributes.ContainsKey("auto")
        )
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    protected (List<Node>, bool) HandleDirective(MatchWrapper match)
    {
        // Returns a 2-tuple: list of nodes, and a "blank finish" boolean.
        var type_name = match.Match.Groups[1].Value;
        Debug.Assert(_document is not null);
        var directive_class = _document.Settings.LookupDirective(type_name);
        if (directive_class is not null)
        {
            return RunDirective(directive_class, match, type_name, new Dictionary<string, object>());
        }
        else
        {
            return UnknownDirective(type_name);
        }
    }

    protected (List<Node>, bool) RunDirective(
        IDirective directive,
        MatchWrapper match,
        string type_name,
        Dictionary<string, object> option_presets
    )
    {
        // Parse a directive then run its directive function.

        // Parameters:

        // - `directive`: The class implementing the directive.  Must be
        //   a subclass of `rst.Directive`.

        // - `match`: A regular expression match object which matched the first
        //   line of the directive.

        // - `type_name`: The directive name, as used in the source text.

        // - `option_presets`: A dictionary of preset options, defaults for the
        //   directive options.  Currently, only an "alt" option is passed by
        //   substitution definitions (value: the substitution name), which may
        //   be used by an embedded image directive.

        // Returns a 2-tuple: list of nodes, and a "blank finish" boolean.

        var lineno = _stateMachine.AbsLineNumber();
        var initial_line_offset = _stateMachine.LineOffset;
        var (
            indented,
            indent,
            line_offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(match.Match.Index + match.Match.Length, stripTop: false);
        var block_text = String.Join('\n',
            _stateMachine.InputLines![
                initial_line_offset..(_stateMachine.LineOffset + 1)
            ]
        );

        List<string> arguments;
        Dictionary<string, object> options;
        StringList content;
        int content_offset;

        try
        {
            (arguments, options, content, content_offset) = ParseDirectiveBlock(
                indented, line_offset, directive, option_presets
            );
        }
        catch (MarkupError detail)
        {
            var error = _reporter!.Error(
                $"Error in '{type_name}' directive:\n{detail.Message}.",
                line: lineno
            );
            return (new List<Node> { error }, blank_finish);

        }

        var result = new List<Node>();

        try
        {
            result = directive.Run(
                type_name,
                arguments,
                options,
                content,
                lineno,
                content_offset,
                block_text,
                this,
                (RSTStateMachine)_stateMachine);
        }
        catch (DirectiveError error)
        {
            var msg_node = _reporter!.MakeSystemMesage(
                error.Level, error.Message, line: lineno
            );
            msg_node.Add(new LiteralBlock(block_text, block_text));
            result = new List<Node> { msg_node };
        }

        return (result, blank_finish || _stateMachine.IsNextLineBlank());
    }

    protected (List<string>, Dictionary<string, object>, StringList, int) ParseDirectiveBlock(
        StringList indented,
        int line_offset,
        IDirective directive,
        Dictionary<string, object> option_presets
    )
    {
        if (indented.Count > 0 && indented[0].Trim().Length == 0)
        {
            indented.TrimStart();
            line_offset += 1;
        }
        while (indented.Count > 0 && indented[^1].Trim().Length == 0)
        {
            indented.TrimEnd();
        }

        StringList content;
        StringList arg_block;
        int content_offset;
        Dictionary<string, object> options;
        int i = 0;

        if (indented.Count > 0 && (
            directive.RequiredArguments > 0 || directive.OptionalArguments > 0 || (directive.OptionSpec.Count > 0)
        ))
        {
            bool didBreak = false;
            for (; i < indented.Count; i += 1)
            {
                var line = indented[i];
                if (line.Trim().Length == 0)
                {
                    didBreak = true;
                    break;
                }
            }
            if (!didBreak)
            {
                i += 1;
            }
            arg_block = indented[..i];
            content = indented[(i + 1)..];
            content_offset = line_offset + i + 1;
        }
        else
        {
            content = indented;
            content_offset = line_offset;
            arg_block = new StringList(Enumerable.Empty<string>(), null);
        }
        if (directive.OptionSpec.Count > 0)
        {
            (options, arg_block) = ParseDirectiveOptions(
                option_presets, directive.OptionSpec, arg_block
            );
        }
        else
        {
            options = new Dictionary<string, object>();
        }
        if (arg_block.Count > 0 && !(
            directive.RequiredArguments > 0 || directive.OptionalArguments > 0
        ))
        {
            content = arg_block.Concat(indented[i..]);
            content_offset = line_offset;
            arg_block = new StringList(Enumerable.Empty<string>(), null);
        }
        while (content.Count > 0 && content[0].Trim().Length == 0)
        {
            content.TrimStart();
            content_offset += 1;
        }

        string[] arguments;
        if (directive.RequiredArguments > 0 || directive.OptionalArguments > 0)
        {
            arguments = ParseDirectiveArguments(directive, arg_block);
        }
        else
        {
            arguments = new string[0];
        }
        if (content.Count > 0 && !directive.HasContent)
        {
            throw new MarkupError("no content permitted");
        }
        return (arguments.ToList(), options, content, content_offset);
    }

    protected (Dictionary<string, object>, StringList) ParseDirectiveOptions(
        Dictionary<string, object> option_presets,
        Dictionary<string, Func<string?, object>> option_spec,
        StringList arg_block
    )
    {
        var options = new Dictionary<string, object>(option_presets);
        StringList? opt_block = null;
        for (int i = 0; i < arg_block.Count; i += 1)
        {
            var line = arg_block[i];

            if (BodyState.Patterns.FieldMarker.RegexAnchoredAtStart.IsMatch(line))
            {
                opt_block = arg_block[i..];
                arg_block = arg_block[..i];
                break;
            }
        }

        if (opt_block is null)
        {
            opt_block = new StringList(Enumerable.Empty<string>(), null);
        }

        if (opt_block.Count > 0)
        {
            var data = ParseExtensionOptions(option_spec, opt_block);
            foreach (var (key, value) in data)
            {
                options[key] = value;
            }
        }
        return (options, arg_block);
    }

    protected string[] ParseDirectiveArguments(
        IDirective directive, IEnumerable<string> arg_block
    )
    {
        var arg_text = String.Join('\n', arg_block);
        var arguments = arg_text.Split();
        if (arguments.Length < directive.RequiredArguments)
        {
            throw new MarkupError(
                $"{directive.RequiredArguments} argument(s) required, {arguments.Length} supplied"
            );
        }
        else if (arguments.Length > (directive.RequiredArguments + directive.OptionalArguments))
        {
            if (directive.FinalArgumentWhitespace)
            {
                arguments = arg_text.Split(null, directive.RequiredArguments + directive.OptionalArguments - 1);
            }
            else
            {
                throw new MarkupError(
                    $"maximum {directive.RequiredArguments + directive.OptionalArguments} argument(s) allowed, {arguments.Length} supplied"
                );
            }
        }
        return arguments;
    }

    protected Dictionary<string, object> ParseExtensionOptions(
        Dictionary<string, Func<string?, object>> option_spec,
        StringList datalines
    )
    {
        // Parse `datalines` for a field list containing extension options
        // matching `option_spec`.

        // :Parameters:
        //     - `option_spec`: a mapping of option name to conversion
        //       function, which should raise an exception on bad input.
        //     - `datalines`: a list of input strings.

        // :Return:
        //     - Success value, 1 or 0.
        //     - An option dictionary on success, an error string on failure.

        var node = new FieldList();
        var (newline_offset, blank_finish) = NestedListParse(
            datalines, 0, node, initial_state: ExtensionOptionsState.Builder.Instance, blank_finish: true
        );
        if (newline_offset != datalines.Count)
        {  // incomplete parse of block
            throw new MarkupError("invalid option block");
        }

        Dictionary<string, object> options;

        try
        {
            options = Util.ExtractExtensionOptions(node, option_spec);
        }
        catch (KeyNotFoundException detail)
        {
            throw new MarkupError($"unknown option: '{detail.Message}'");
        }
        catch (ArgumentException detail)
        {
            throw new MarkupError($"invalid option value: {detail.Message}");
        }

        if (blank_finish)
        {
            return options;
        }
        else
        {
            throw new MarkupError("option data incompletely parsed");
        }
    }

    protected (List<Node>, bool) UnknownDirective(string typeName)
    {

        var lineno = _stateMachine.AbsLineNumber();
        var (
            indented,
            indent,
            offset,
            blank_finish
        ) = _stateMachine.GetFirstKnownIndented(0, stripIndent: false);
        var text = String.Join('\n', indented);
        var error = _reporter!.Error(
            $"Unknown directive type '{typeName}'.",
            line: lineno
        );
        return (new List<Node> { error }, blank_finish);
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class SpecializedBodyState : BodyState
{
    public SpecializedBodyState(StateMachine sm) : base(sm) { }

    protected TransitionResult InvalidInput(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        // Not a compound element member. Abort this state machine.

        _stateMachine.PreviousLine();  // back up so parent SM can reassess
        throw new EOFError();
    }

    protected override TransitionResult IndentTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult BulletTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult EnumeratorTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected virtual TransitionResult Field_markerTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected virtual TransitionResult Option_markerTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult DoctestTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult LineBlockTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult ExplicitMarkupTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult AnonymousTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult LineTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult TextTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult BlankTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }


    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new SpecializedBodyState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class BulletListState : SpecializedBodyState, IHaveBlankFinish
{
    // Second and subsequent bullet_list list_items.

    public bool BlankFinish { get; set; } = false;

    public BulletListState(StateMachine sm) : base(sm) { }

    protected override TransitionResult BulletTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Bullet list item.
        if (match.Text[0] != ((string)_parent!.Attributes["bullet"])[0])
        {
            // different bullet: new list
            InvalidInput(match, context, nextState);
        }
        var (listitem, blank_finish) = HandleListItem(match.Match.Index + match.Match.Length);
        _parent!.Add(listitem);
        BlankFinish = blank_finish;
        return new TransitionResult(new List<string>(), nextState);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new BulletListState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class DefinitionListState : SpecializedBodyState
{
    // Second and subsequent definition_list_items.

    public DefinitionListState(StateMachine sm) : base(sm) { }

    protected override TransitionResult TextTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Definition lists.
        return new TransitionResult(new List<string> { match.Text }, DefinitionState.Builder.Instance);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new DefinitionListState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class EnumeratedListState : SpecializedBodyState, IHaveBlankFinish
{
    // Second and subsequent enumerated_list list_items.

    public class EnumeratedListSettings
    {
        public int LastOrdinal { get; set; }
        public string Format { get; set; }
        public bool Auto { get; set; }

        public EnumeratedListSettings(int lastOrdinal, string format, bool auto)
        {
            LastOrdinal = lastOrdinal;
            Format = format;
            Auto = auto;
        }
    }

    public bool BlankFinish { get; set; } = false;
    public EnumeratedListSettings? Settings { get; set; }

    public EnumeratedListState(StateMachine sm) : base(sm) { }

    protected override TransitionResult EnumeratorTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Enumerated list item.
        Debug.Assert(Settings is not null);

        var parentEnumType = (string)(_parent!.Attributes["enumtype"]);
        var (format, sequence, text, ordinal) = ParseEnumerator(
            match, parentEnumType
        );
        if (
            format != Settings.Format
            || (
                sequence != "#"
                && (
                    sequence != parentEnumType
                    || Settings.Auto
                    || ordinal != (Settings.LastOrdinal + 1)
                )
            )
            || !IsEnumeratedListItem(ordinal, sequence, format)
        )
        {
            // different enumeration: new list
            InvalidInput(match, context, nextState);
        }
        if (sequence == "#")
        {
            Settings.Auto = true;
        }
        var (listitem, blank_finish) = HandleListItem(match.Match.Index + match.Match.Length);
        _parent.Add(listitem);
        BlankFinish = blank_finish;
        Settings.LastOrdinal = ordinal;
        return new TransitionResult(new List<string>(), nextState);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new EnumeratedListState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class FieldListState : SpecializedBodyState, IHaveBlankFinish
{
    public bool BlankFinish { get; set; } = false;

    public FieldListState(StateMachine sm) : base(sm) { }


    protected override TransitionResult FieldMarkerTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Field list field.
        var (field, blank_finish) = FieldHandler(match);
        _parent!.Add(field);
        BlankFinish = blank_finish;
        return new TransitionResult(new List<string>(), nextState);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new FieldListState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class OptionListState : SpecializedBodyState, IHaveBlankFinish
{
    // Second and subsequent option_list option_list_items.

    public bool BlankFinish { get; set; } = false;
    public OptionListState(StateMachine sm) : base(sm) { }

    protected override TransitionResult OptionMarkerTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Option list item.
        OptionListItem option_list_item;
        bool blank_finish;
        try
        {
            (option_list_item, blank_finish) = HandleOptionListItem(match);
        }
        catch (MarkupError)
        {
            InvalidInput(match, context, nextState);
            throw new UnreachableException();
        }
        _parent!.Add(option_list_item);
        BlankFinish = blank_finish;
        return new TransitionResult(new List<string>(), nextState);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new OptionListState(sm);
        }
    }
}

public class LineBlockState : SpecializedBodyState, IHaveBlankFinish
{
    public bool BlankFinish { get; set; } = false;

    public LineBlockState(StateMachine sm) : base(sm) { }

    protected override TransitionResult LineBlockTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // New line of line block.

        var lineno = _stateMachine.AbsLineNumber();
        var (line, messages, blank_finish) = LineBlockLine(match, lineno);
        _parent!.Add(line);
        var grandparent = _parent!.Parent!;
        grandparent.AddRange(messages);
        BlankFinish = blank_finish;
        return new TransitionResult(new List<string>(), nextState);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new LineBlockState(sm);
        }
    }
}

public class ExtensionOptionsState : FieldListState
{
    // Parse field_list fields for extension options.
    // No nested parsing is done (including inline markup parsing).
    public ExtensionOptionsState(StateMachine sm) : base(sm) { }

    public override void ParseFieldBody(
        StringList indented, int offset, Element node
    )
    {
        // Override `Body.parse_field_body` for simpler parsing.
        var lines = new List<string>();
        foreach (var line in indented.Concat(new List<string> { "" }))
        {
            if (line.Trim().Length > 0)
            {
                lines.Add(line);
            }
            else if (lines.Count > 0)
            {
                var text = String.Join('\n', lines);
                node.Add(new Paragraph(text, text));
                lines = new List<string>();
            }
        }
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new ExtensionOptionsState(sm);
        }
    }
}

public class ExplicitState : SpecializedBodyState, IHaveBlankFinish
{
    public bool BlankFinish { get; set; } = false;
    public ExplicitState(StateMachine sm) : base(sm) { }
    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new ExplicitState(sm);
        }
    }
}

public class SubstitutionDefState : BodyState, IHaveBlankFinish
{
    public static readonly RegexWrapper EMBEDDED_DIRECTIVE_PAT = new RegexWrapper($"""({Inliner.PatternRegistry.simplename})::( +|$)""");
    public static readonly RegexWrapper TEXT_PAT = new RegexWrapper("");

    public bool BlankFinish { get; set; } = false;

    public SubstitutionDefState(StateMachine sm) : base(sm)
    {
        Transitions = new List<TransitionTuple> {
            new TransitionTuple("blank", SubstitutionDefState.BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", SubstitutionDefState.INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("embedded_directive", SubstitutionDefState.EMBEDDED_DIRECTIVE_PAT, (match, context, nextState) => EmbeddedDirectiveTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("text", SubstitutionDefState.TEXT_PAT, (match, context, nextState) => TextTransition(match, context, nextState), GetStateBuilder())
        };
    }


    public TransitionResult EmbeddedDirectiveTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // XXX: docutils provided an "alt" option here
        var (nodelist, blank_finish) = HandleDirective(match);
        _parent!.AddRange(nodelist);

        if (!_stateMachine.AtEof())
        {
            BlankFinish = blank_finish;
        }
        throw new EOFError();
    }

    protected override TransitionResult TextTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        if (!_stateMachine.AtEof())
        {
            BlankFinish = _stateMachine.IsNextLineBlank();
        }
        throw new EOFError();
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new SubstitutionDefState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class TextState : RSTState
{
    public static readonly RegexWrapper UNDERLINE_PAT = BodyState.Patterns.Line;
    public static readonly RegexWrapper TEXT_PAT = new RegexWrapper("");

    public TextState(StateMachine sm) : base(sm)
    {
        Transitions = new List<TransitionTuple>{
            new TransitionTuple("blank", TextState.BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", TextState.INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("underline", TextState.UNDERLINE_PAT, (match, context, nextState) => UnderlineTransition(match, context, nextState), BodyState.Builder.Instance),
            new TransitionTuple("text", TextState.TEXT_PAT, (match, context, nextState) => TextTransition(match, context, nextState), BodyState.Builder.Instance),
        };
    }

    protected override TransitionResult BlankTransition(
        MatchWrapper? match,
        List<string> context,
        IStateBuilder? nextState
    )
    {
        // End of paragraph.
        // NOTE: self.paragraph returns [ node, system_message(s) ], literalnext

        var (paragraph, literalnext) = HandleParagraph(
            context, _stateMachine.AbsLineNumber() - 1
        );
        _parent!.AddRange(paragraph);
        if (literalnext)
        {
            _parent.AddRange(HandleLiteralBlock());
        }
        return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
    }

    public override List<string> Eof(List<string> context)
    {
        if (context.Count > 0)
        {
            BlankTransition(null, context, null);
        }
        return new List<string>();
    }

    protected override TransitionResult IndentTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Definition list item.
        var definitionlist = new DefinitionList();
        var (definitionlistitem, blank_finish) = HandleDefinitionListItem(context);
        definitionlist.Add(definitionlistitem);
        _parent!.Add(definitionlist);
        var offset = _stateMachine.LineOffset + 1;  // next line
        (var newline_offset, blank_finish) = NestedListParse(
            _stateMachine.InputLines![offset..],
            input_offset: _stateMachine.AbsLineOffset() + 1,
            node: definitionlist,
            initial_state: DefinitionListState.Builder.Instance,
            blank_finish: blank_finish,
            blank_finish_state: DefinitionState.Builder.Instance
        );
        GotoLine(newline_offset);
        if (!blank_finish)
        {
            _parent!.Add(UnindentWarning("Definition list"));
        }
        return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
    }


    protected virtual TransitionResult UnderlineTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Section title.

        var lineno = _stateMachine.AbsLineNumber();
        var title = context[0].TrimEnd();
        var underline = match.Text.TrimEnd();
        var source = title + "\n" + underline;
        var messages = new List<SystemMessage>();
        string blocktext;
        if (Util.ColumnWidth(title) > underline.Length)
        {
            if (underline.Length < 4)
            {
                if (((RSTStateMachine)_stateMachine).MatchTitles)
                {
                    var msg = _reporter!.Info(
                        "Possible title underline, too short for the title.\n" +
                        "Treating it as ordinary text because it's so short.",
                        line: lineno
                    );
                    _parent!.Add(msg);
                }
                throw new TransitionCorrection("text");
            }
            else
            {
                blocktext = context[0] + "\n" + _stateMachine.Line;
                var msg = _reporter!.Warning(
                    "Title underline too short.",
                    line: lineno
                );
                messages.Add(msg);
            }
        }
        if (!((RSTStateMachine)_stateMachine).MatchTitles)
        {
            blocktext = context[0] + "\n" + _stateMachine.Line;
            // We need get_source_and_line() here to report correctly
            var (src, srcline) = _stateMachine.GetSourceAndLine();
            // TODO: why is AbsLineNumber() == srcline+1
            // if the error is in a table (try with test_tables.py)?
            // print("get_source_and_line", srcline)
            // print("abs_line_number", _stateMachine.AbsLineNumber())
            var msg = _reporter!.Severe(
                "Unexpected section title.",
                line: srcline
            );
            _parent!.AddRange(messages);
            _parent!.Add(msg);
            return new TransitionResult(new List<string>(), nextState);
        }
        var style = new StyleKind(underline, null);
        context.Clear();
        HandleSection(title, source, style, lineno - 1, messages);
        return new TransitionResult(new List<string>(), nextState);
    }

    protected virtual TransitionResult TextTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Paragraph.
        var startline = _stateMachine.AbsLineNumber() - 1;
        SystemMessage? msg = null;
        IEnumerable<string> block = Enumerable.Empty<string>();
        try
        {
            block = _stateMachine.GetTextBlock(flush_left: true);
        }
        catch (UnexpectedIndentationError err)
        {
            // XXX this looks suspiciously broken in the original source: block is list()'d, but in this branch, never bound.
            msg = _reporter!.Error(
                "Unexpected indentation.", line: err.SourceLine
            );
        }
        var lines = context.Concat(block).ToList();
        var (paragraph, literalnext) = HandleParagraph(lines, startline);
        _parent!.AddRange(paragraph);
        if (msg is not null)
        {
            _parent.Add(msg);
        }
        if (literalnext)
        {
            try
            {
                _stateMachine.NextLine();
            }
            catch (EOFError) { }
            _parent.AddRange(HandleLiteralBlock());
        }
        return new TransitionResult(new List<string>(), nextState);
    }

    protected List<Node> HandleLiteralBlock()
    {
        // Return a list of nodes.

        var (indented, indent, offset, blank_finish) = _stateMachine.GetIndented();
        while (indented.Count > 0 && indented.Last().Trim().Length == 0)
        {
            indented.TrimEnd();
        }
        var data = String.Join('\n', indented);
        var literal_block = new LiteralBlock(data, data);
        var sourceAndLine = _stateMachine.GetSourceAndLine(offset + 1);
        literal_block.Source = sourceAndLine.Item1;
        literal_block.Line = sourceAndLine.Item2;
        var nodelist = new List<Node> { literal_block };
        if (!blank_finish)
        {
            nodelist.Add(UnindentWarning("Literal block"));
        }
        return nodelist;
    }

    protected (DefinitionListItem, bool) HandleDefinitionListItem(List<string> termline)
    {
        var (indented, indent, line_offset, blank_finish) = _stateMachine.GetIndented();
        var itemnode = new DefinitionListItem(String.Join('\n', termline.Concat(indented)));
        var lineno = _stateMachine.AbsLineNumber() - 1;
        var sourceAndLine = _stateMachine.GetSourceAndLine(lineno);
        itemnode.Source = sourceAndLine.Item1;
        itemnode.Line = sourceAndLine.Item2;
        var (termlist, messages) = HandleTerm(termline, lineno);
        itemnode.AddRange(termlist);
        var definition = new Definition("");
        definition.AddRange(messages);
        itemnode.Add(definition);
        if (termline[0].TakeLast(2).ToString() == "::")
        {
            definition.Add(
                _reporter!.Info(
                    "Blank line missing before literal block (after the '::')? " +
                    "Interpreted as a definition list item.",
                    line: lineno + 1
                )
            );
        }
        NestedParse(indented, input_offset: line_offset, node: definition);
        return (itemnode, blank_finish);
    }

    protected (List<Element>, List<SystemMessage>) HandleTerm(List<string> lines, int lineno)
    {
        Debug.Assert(lines.Count == 1);

        var (text_nodes, messages) = InlineText(lines[0], lineno);
        var term_node = new Term(lines[0]);
        var sourceAndLine = _stateMachine.GetSourceAndLine(lineno);
        term_node.Source = sourceAndLine.Item1;
        term_node.Line = sourceAndLine.Item2;
        var node_list = new List<Element> { term_node };
        for (int i = 0; i < text_nodes.Count; i += 1)
        {
            if (text_nodes[i] is Text node)
            {
                node_list[^1].Add(node);
            }
            else
            {
                node_list[^1].Add(text_nodes[i]);
            }
        }
        return (node_list, messages);
    }


    public class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new TextState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class SpecializedTextState : TextState
{
    public SpecializedTextState(StateMachine sm) : base(sm)
    {
        Transitions = new List<TransitionTuple>{
            new TransitionTuple("blank", BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("underline", INDENT_PAT, (match, context, nextState) => UnderlineTransition(match, context, nextState), BodyState.Builder.Instance),
            new TransitionTuple("text", INDENT_PAT, (match, context, nextState) => TextTransition(match, context, nextState), BodyState.Builder.Instance),
        };
    }

    public override List<string> Eof(List<string> context)
    {
        // Incomplete construct.
        return new List<string>();
    }

    protected TransitionResult InvalidInput(
        MatchWrapper? match,
        List<string> context,
        IStateBuilder? next_state
    )
    {
        // Not a compound element member. Abort this state machine.
        throw new EOFError();
    }

    protected override TransitionResult BlankTransition(
        MatchWrapper? match,
        List<string> context,
        IStateBuilder? next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult IndentTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult UnderlineTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    protected override TransitionResult TextTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder next_state
    )
    {
        return InvalidInput(match, context, next_state);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new SpecializedTextState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class DefinitionState : SpecializedTextState, IHaveBlankFinish
{
    public bool BlankFinish { get; set; } = false;

    public DefinitionState(StateMachine sm) : base(sm)
    {
        Transitions = new List<TransitionTuple>{
            new TransitionTuple("blank", BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("underline", INDENT_PAT, (match, context, nextState) => UnderlineTransition(match, context, nextState), BodyState.Builder.Instance),
            new TransitionTuple("text", INDENT_PAT, (match, context, nextState) => TextTransition(match, context, nextState), BodyState.Builder.Instance),
        };
    }

    public override List<string> Eof(List<string> context)
    {
        // Not a definition.
        _stateMachine.PreviousLine(2);  // so parent SM can reassess
        return new List<string>();
    }

    protected override TransitionResult IndentTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Definition list item.
        var (itemnode, blank_finish) = HandleDefinitionListItem(context);
        _parent!.Add(itemnode);
        BlankFinish = blank_finish;
        return new TransitionResult(new List<string>(), DefinitionListState.Builder.Instance);
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new DefinitionState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}

public class LineState : SpecializedTextState
{
    // Second line of over- & underlined section title or transition marker.
    private bool _eofcheck = true;  // @@@ ???

    public LineState(StateMachine sm) : base(sm)
    {
        Transitions = new List<TransitionTuple>{
            new TransitionTuple("blank", BLANK_PAT, (match, context, nextState) => BlankTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("indent", INDENT_PAT, (match, context, nextState) => IndentTransition(match, context, nextState), GetStateBuilder()),
            new TransitionTuple("underline", UNDERLINE_PAT, (match, context, nextState) => UnderlineTransition(match, context, nextState), BodyState.Builder.Instance),
            new TransitionTuple("text", TEXT_PAT, (match, context, nextState) => TextTransition(match, context, nextState), BodyState.Builder.Instance),
        };
    }

    public override List<string> Eof(List<string> context)
    {
        // Transition marker at end of section or document.
        var marker = context[0].Trim();
        if (_memo!.SectionBubbleUpKludge)
        {
            _memo.SectionBubbleUpKludge = false;
        }
        else if (marker.Length < 4)
        {
            StateCorrection(context);
        }

        if (_eofcheck)
        {  // ignore EOFError with sections
            var (src, srcline) = _stateMachine.GetSourceAndLine();
            // lineno = _stateMachine.AbsLineNumber() - 1
            var transition = new Transition(context[0]);
            transition.Source = src;
            transition.Line = srcline - 1;
            // transition.line = lineno
            _parent!.Add(transition);
        }
        _eofcheck = true;
        return new List<string>();
    }

    protected override TransitionResult BlankTransition(
        MatchWrapper? match,
        List<string> context,
        IStateBuilder? nextState
    )
    {
        // Transition marker.

        var (src, srcline) = _stateMachine.GetSourceAndLine();
        var marker = context[0].Trim();
        if (marker.Length < 4)
        {
            StateCorrection(context);
        }
        var transition = new Transition(marker);
        transition.Source = src;
        transition.Line = srcline - 1;
        _parent!.Add(transition);
        return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
    }

    protected override TransitionResult TextTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        // Potential over- & underlined title.

        var lineno = _stateMachine.AbsLineNumber() - 1;
        var overline = context[0];
        var title = match.Text;
        var underline = "";
        try
        {
            underline = _stateMachine.NextLine();
        }
        catch (EOFError)
        {
            var blocktext = overline + "\n" + title;
            if (overline.TrimEnd().Length < 4)
            {
                HandleShortOverline(context, blocktext, lineno, 2);
            }
            else
            {
                var msg = _reporter!.Severe(
                    "Incomplete section title.",
                    line: lineno
                );
                _parent!.Add(msg);
                return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
            }
        }
        var source = $"{overline}\n{title}\n{underline}";
        overline = overline.TrimEnd();
        underline = underline.TrimEnd();
        var underlineMatch = Transitions.Where(x => x.Name == "underline").First().Pattern.MatchAnchored(underline);
        // if not self.transitions["underline"][0].match(underline):
        if (!underlineMatch.Match.Success || underlineMatch.Match.Index > 0)
        {
            var blocktext = overline + "\n" + title + "\n" + underline;
            if (overline.TrimEnd().Length < 4)
            {
                HandleShortOverline(context, blocktext, lineno, 2);
            }
            else
            {
                var msg = _reporter!.Severe(
                    "Missing matching underline for section title overline.",
                    line: lineno
                );
                _parent!.Add(msg);
                return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
            }
        }
        else if (overline != underline)
        {
            var blocktext = overline + "\n" + title + "\n" + underline;
            if (overline.TrimEnd().Length < 4)
            {
                HandleShortOverline(context, blocktext, lineno, 2);
            }
            else
            {
                var msg = _reporter!.Severe(
                    "Title overline & underline mismatch.",
                    line: lineno
                );
                _parent!.Add(msg);
                return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
            }
        }
        title = title.TrimEnd();
        var messages = new List<SystemMessage>();
        if (Util.ColumnWidth(title) > overline.Length)
        {
            var blocktext = overline + "\n" + title + "\n" + underline;
            if (overline.TrimEnd().Length < 4)
            {
                HandleShortOverline(context, blocktext, lineno, 2);
            }
            else
            {
                var msg = _reporter!.Warning(
                    "Title overline too short.",
                    line: lineno
                );
                messages.Add(msg);
            }
        }
        var style = new StyleKind(underline[0].ToString(), overline[0].ToString());
        _eofcheck = false;  // @@@ not sure this is correct
        HandleSection(title.TrimStart(), source, style, lineno + 1, messages);
        _eofcheck = true;
        return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
    }

    // indented title
    protected override TransitionResult IndentTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        return TextTransition(match, context, nextState);
    }

    protected override TransitionResult UnderlineTransition(
        MatchWrapper match,
        List<string> context,
        IStateBuilder nextState
    )
    {
        var overline = context[0];
        var blocktext = overline + "\n" + _stateMachine.Line;
        var lineno = _stateMachine.AbsLineNumber() - 1;
        if (overline.TrimEnd().Length < 4)
        {
            HandleShortOverline(context, blocktext, lineno, 1);
        }
        var msg = _reporter!.Error(
            "Invalid section title or transition marker.",
            line: lineno
        );
        _parent!.Add(msg);
        return new TransitionResult(new List<string>(), BodyState.Builder.Instance);
    }

    private void HandleShortOverline(
        List<string> context, string blocktext, int lineno, int lines = 1
    )
    {
        var msg = _reporter!.Info(
            "Possible incomplete section title.\nTreating the overline as " +
            "ordinary text because it's so short.",
            line: lineno
        );
        _parent!.Add(msg);
        StateCorrection(context, lines);
    }

    private void StateCorrection(List<string> context, int lines = 1)
    {
        _stateMachine.PreviousLine(lines);
        context.Clear();
        throw new StateCorrection(BodyState.Builder.Instance, "text");
    }

    public new class Builder : IStateBuilder
    {
        public static readonly Builder Instance = new Builder();

        public virtual State Build(StateMachine sm)
        {
            return new LineState(sm);
        }
    }

    protected override IStateBuilder GetStateBuilder()
    {
        return Builder.Instance;
    }
}
