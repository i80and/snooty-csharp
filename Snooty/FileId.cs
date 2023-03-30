using System.Text.RegularExpressions;

public partial class FileId : PosixPath
{
    [GeneratedRegex(@"\.((txt)|(rst)|(yaml))$", RegexOptions.Compiled, "en-US")]
    private static partial Regex PAT_FILE_EXTENSIONS();

    public FileId() : base() { }

    public FileId(string path) : base(path) { }

    public FileId(IEnumerable<string> parts) : base(parts) { }

    public FileId CollapseDots()
    {
        var result = new List<string>();
        foreach (var part in _parts)
        {
            if (part == "..")
            {
                result.RemoveAt(result.Count - 1);
            }
            else if (part == ".")
            {
                continue;
            }
            else
            {
                result.Add(part);
            }
        }
        return new FileId(result);
    }

    public string WithoutKnownSuffix()
    {
        var fileid = WithName(PAT_FILE_EXTENSIONS().Replace(GetName(), ""));
        return fileid.AsPosix();
    }

    public string AsDirHtml()
    {
        // The project root is special
        if (_parts == new string[] { "index.txt" })
        {
            return "";
        }

        return WithoutKnownSuffix() + "/";
    }

    public string GetSuffix()
    {
        return Path.GetExtension(GetName());
    }
}
