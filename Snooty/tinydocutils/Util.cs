namespace tinydocutils;

using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

public sealed partial class Util
{
    [GeneratedRegex("[\v\f]", RegexOptions.NonBacktracking)]
    private static partial Regex DEFAULT_WHITESPACE_PAT();
    private static readonly string[] SEP_SEQUENCES = new string[] { "\x00 ", "\x00\n", "\x00" };

    public static List<string> String2Lines(
        string astring,
        int tab_width = 8,
        bool convert_whitespace = false,
        Regex? whitespace = null
    )
    {
        if (convert_whitespace)
        {
            if (whitespace is null)
            {
                whitespace = DEFAULT_WHITESPACE_PAT();
            }
            astring = whitespace.Replace(astring, " ");
        }
        var spaceTab = new string(' ', tab_width);
        var lines = astring.Split('\n').Select(s => s.Replace("\t", spaceTab).TrimEnd()).ToList();
        return lines;
    }

    public static string Escape2Null(string text)
    {
        // Return a string with escape-backslashes converted to nulls.
        var parts = new List<string>();
        int start = 0;
        while (true)
        {
            var found = text.IndexOf("\\", start);
            if (found == -1)
            {
                parts.Add(text[start..]);
                return String.Concat(parts);
            }
            parts.Add(text[start..found]);
            parts.Add("\x00" + text[(found + 1)..(found + 2)]);
            start = found + 2;  // skip character after escape
        }
    }

    public static string Unescape(string text, bool restoreBackslashes = false)
    {
        // Return a string with nulls removed or restored to backslashes.
        // Backslash-escaped spaces are also removed.
        if (restoreBackslashes)
        {
            return text.Replace('\x00', '\\');
        }
        else
        {
            foreach (var sep in SEP_SEQUENCES)
            {
                text = String.Concat(text.Split(sep));
            }
            return text;
        }
    }

    const int _LOWER_ZERO = (int)'a' - 1;
    const int _UPPER_ZERO = (int)'A' - 1;

    public static int _loweralpha_to_int(string s)
    {
        if (s.Length != 1)
        {
            throw new ArgumentOutOfRangeException(s);
        }
        return (int)s[0] - _LOWER_ZERO;
    }


    public static int _upperalpha_to_int(string s)
    {
        if (s.Length != 1)
        {
            throw new ArgumentOutOfRangeException(s);
        }
        return (int)s[0] - _UPPER_ZERO;
    }


    public static int _lowerroman_to_int(string s)
    {
        return Roman.FromRoman(s.ToUpperInvariant());
    }

    public static string WhitespaceNormalizeName(string name)
    {
        // Return a whitespace-normalized name.
        return String.Join(' ', name.Split());
    }

    public static string FullyNormalizeName(string name)
    {
        return WhitespaceNormalizeName(name);
    }

    public static IEnumerable<string> SplitEscapedWhitespace(string text)
    {
        // Split `text` on escaped whitespace (null+space or null+newline).
        // Return a list of strings.

        var strings = text.Split("\x00 ");
        // flatten list of lists of strings to list of strings:
        foreach (var str in strings)
        {
            foreach (var part in str.Split("\x00\n"))
            {
                yield return part;
            }
        }
    }

    public static string MakeId(string str)
    {
        // Convert `string` into an identifier and return it.

        // Docutils identifiers will conform to the regular expression
        // ``[a-z](-?[a-z0-9]+)*``.  For CSS compatibility, identifiers (the "class"
        // and "id" attributes) should have no underscores, colons, or periods.
        // Hyphens may be used.

        // - The `HTML 4.01 spec`_ defines identifiers based on SGML tokens:

        //     ID and NAME tokens must begin with a letter ([A-Za-z]) and may be
        //     followed by any number of letters, digits ([0-9]), hyphens ("-"),
        //     underscores ("_"), colons (":"), and periods (".").

        // - However the `CSS1 spec`_ defines identifiers based on the "name" token,
        // a tighter interpretation ("flex" tokenizer notation; "latin1" and
        // "escape" 8-bit characters have been replaced with entities)::

        //     unicode     \\[0-9a-f]{1,4}
        //     latin1      [&iexcl;-&yuml;]
        //     escape      {unicode}|\\[ -~&iexcl;-&yuml;]
        //     nmchar      [-a-z0-9]|{latin1}|{escape}
        //     name        {nmchar}+

        // The CSS1 "nmchar" rule does not include underscores ("_"), colons (":"),
        // or periods ("."), therefore "class" and "id" attributes should not contain
        // these characters. They should be replaced with hyphens ("-"). Combined
        // with HTML's requirements (the first character must be a letter; no
        // "unicode", "latin1", or "escape" characters), this results in the
        // ``[a-z](-?[a-z0-9]+)*`` pattern.

        // .. _HTML 4.01 spec: http://www.w3.org/TR/html401
        // .. _CSS1 spec: http://www.w3.org/TR/REC-CSS1

        var id = str.ToLowerInvariant();
        // get rid of non-ascii characters.
        id = id.Normalize(System.Text.NormalizationForm.FormKD);
        // shrink runs of whitespace and replace by hyphen
        id = _non_id_chars().Replace(String.Join(' ', id.Split()), "-");
        id = _non_id_at_ends().Replace(id, "");
        return id;
    }


    [GeneratedRegex("[^a-z0-9]+", RegexOptions.NonBacktracking)]
    private static partial Regex _non_id_chars();
    [GeneratedRegex("^[-0-9]+|-+$", RegexOptions.NonBacktracking)]
    private static partial Regex _non_id_at_ends();

    public static Dictionary<string, object> ExtractExtensionOptions(
        FieldList field_list,
        Dictionary<string, Func<string?, object>> options_spec
    )
    {
        // Return a dictionary mapping extension option names to converted values.

        // :Parameters:
        //     - `field_list`: A flat field list without field arguments, where each
        //     field body consists of a single paragraph only.
        //     - `options_spec`: Dictionary mapping known option names to a
        //     conversion function such as `int` or `float`.

        // :Exceptions:
        //     - `KeyError` for unknown option names.
        //     - `ValueError` for invalid option values (raised by the conversion
        //     function).
        //     - `TypeError` for invalid option value types (raised by conversion
        //     function).
        //     - `DuplicateOptionError` for duplicate options.
        //     - `BadOptionError` for invalid fields.
        //     - `BadOptionDataError` for invalid option data (missing name,
        //     missing data, bad quotes, etc.).

        var option_list = ExtractOptions(field_list);
        var option_dict = AssembleOptionDict(option_list, options_spec);
        return option_dict;
    }


    public static List<(string, string?)> ExtractOptions(
        FieldList field_list
    )
    {
        // Return a list of option (name, value) pairs from field names & bodies.

        // :Parameter:
        //     `field_list`: A flat field list, where each field name is a single
        //     word and each field body consists of a single paragraph only.

        // :Exceptions:
        //     - `ArgumentException` for invalid fields.
        //     - `ArgumentException` for invalid option data (missing name,
        //     missing data, bad quotes, etc.).

        var option_list = new List<(string, string?)>();
        string? data;
        foreach (var node in field_list)
        {
            var field = (Field)node;
            if (field[0].AsText().Split().Length != 1)
            {
                throw new ArgumentException(
                    "extension option field name may not contain multiple words"
                );
            }
            var name = field[0].AsText().ToLowerInvariant();
            var body = (Element)field[1];

            if (body.Count == 0)
            {
                data = null;
            }
            else if (
                body.Count > 1
                || body[0] is not Paragraph
                || ((Element)body[0]).Count != 1
                || ((Element)body[0])[0] is not Text)
            {
                throw new ArgumentException(
                    "extension option field body may contain\n" +
                    $"a single paragraph only (option '{name}')"
                );
            }
            else
            {
                data = ((Element)body[0])[0].AsText();
            }
            option_list.Add((name, data));
        }
        return option_list;
    }


    public static Dictionary<string, object> AssembleOptionDict(
        IEnumerable<(string, string?)> option_list,
        Dictionary<string, Func<string?, object>> options_spec
    )
    {
        // Return a mapping of option names to values.

        // :Parameters:
        //     - `option_list`: A list of (name, value) pairs (the output of
        //     `extract_options()`).
        //     - `options_spec`: Dictionary mapping known option names to a
        //     conversion function such as `int` or `float`.

        // :Exceptions:
        //     - `KeyError` for unknown option names.
        //     - `DuplicateOptionError` for duplicate options.
        //     - `ValueError` for invalid option values (raised by conversion
        //     function).
        //     - `TypeError` for invalid option value types (raised by conversion
        //     function).

        var options = new Dictionary<string, object>();
        foreach (var (name, value) in option_list)
        {
            var convertor = options_spec[name];  // raises KeyError if unknown
            if (convertor is null)
            {
                throw new KeyNotFoundException(name);  // or if explicitly disabled
            }
            if (options.ContainsKey(name))
            {
                throw new ArgumentException($"duplicate option '{name}'");
            }

            options[name] = convertor(value);
        }
        return options;
    }

    public static int ColumnWidth(string text) {
        return new StringInfo(text).LengthInTextElements;
    }

    [GeneratedRegex("""^(?:0x|x|\\x|U\+?|\\u)([0-9a-f]+)$|&#x([0-9a-f]+);$""", RegexOptions.IgnoreCase)]
    private static partial Regex UNICODE_PATTERN();

    [GeneratedRegex("""^[0-9]+$""", RegexOptions.IgnoreCase)]
    private static partial Regex DIGITS();

    public static string UnicodeCode(string code) {
        // Convert a Unicode character code to a Unicode character.
        // (Directive option conversion function.)

        // Codes may be decimal numbers, hexadecimal numbers (prefixed by ``0x``,
        // ``x``, ``\x``, ``U+``, ``u``, or ``\u``; e.g. ``U+262E``), or XML-style
        // numeric character entities (e.g. ``&#x262E;``).  Other text remains as-is.

        // Raise ArgumentException for illegal Unicode code values.

        try {
            if (DIGITS().IsMatch(code)) {  // decimal number
                return ((Rune)Int32.Parse(code)).ToString();
            } else {
                var match = UNICODE_PATTERN().Match(code);
                if (match.Success) {   // hex number
                    var value = match.Groups[1].Value;
                    if (value.Length == 0) {
                        value = match.Groups[2].Value;
                    }
                    return ((Rune)(Int32.Parse(value, System.Globalization.NumberStyles.HexNumber))).ToString();
                } else {  // other text
                    throw new ArgumentException($"Unknown Unicode character '{code}'");
                }
            }
        } catch (OverflowException detail) {
            throw new ArgumentException($"code too large ({detail})");
        }
    }
}
