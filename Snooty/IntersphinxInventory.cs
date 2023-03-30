using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Text;

public class HTTPCache
{
    public const string DEFAULT_CACHE_DIR = "";

    string _cacheDir = DEFAULT_CACHE_DIR;

    public HTTPCache() { }

    public HTTPCache(string cacheDir)
    {
        _cacheDir = cacheDir;
    }

    public byte[] Get(string url, object? cacheInterval)
    {
        return new byte[0];
    }
}

public record TargetDefinition(string Name, (string, string) Role, int Priority, string UriBase, string Uri, string? DisplayName)
{
    public string DomainAndRole()
    {
        return $"{Role.Item1}:{Role.Item2}";
    }
}


public partial class IntersphinxInventory
{
    [GeneratedRegex(@"(?x)(.+?)\s+(\S*:\S*)\s+(-?\d+)\s(\S*)\s+(.*)", RegexOptions.Compiled, "en-US")]
    private static partial Regex INVENTORY_PATTERN();

    [GeneratedRegex(@"\s+$", RegexOptions.Compiled, "en-US")]
    private static partial Regex TRAILING_WHITESPACE();

    [GeneratedRegex(@"/[^/]+$", RegexOptions.Compiled, "en-US")]
    private static partial Regex FINAL_PATH_COMPONENT();

    public string BaseUrl;
    Dictionary<string, TargetDefinition> _targets = new Dictionary<string, TargetDefinition>();

    public IntersphinxInventory(string baseUrl)
    {
        BaseUrl = baseUrl;
    }

    public IntersphinxInventory(string baseUrl, Dictionary<string, TargetDefinition> targets)
    {
        BaseUrl = baseUrl;
        _targets = targets;
    }

    public int Count
    {
        get
        {
            return _targets.Count;
        }
    }

    public bool ContainsKey(string target)
    {
        return _targets.ContainsKey(target);
    }

    TargetDefinition? GetValueOrNull(string target)
    {
        try
        {
            return _targets[target];
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public TargetDefinition Get(string target)
    {
        return _targets[target];
    }

    public IEnumerable<string> TargetNames()
    {
        return _targets.Keys;
    }

    public byte[] Dumps(string name, string version)
    {
        // Newlines break the (fragile) format; let's just be safe and make sure we're not smuggling any in.
        if (name.Contains("\n"))
        {
            throw new ArgumentException(name);
        }
        if (version.Contains("\n"))
        {
            throw new ArgumentException(version);
        }

        var buffer = new MemoryStream();
        buffer.Write(Encoding.UTF8.GetBytes($"# Sphinx inventory version 2\n# Project: {name}\n# Version: {version}\n# The remainder of this file is compressed using zlib.\n"));

        var lines = new List<string>();
        foreach (var target in _targets.Values)
        {
            lines.Add(
                String.Join(" ", (
                        target.Name,
                        String.Join(":", target.Role),
                        target.Priority.ToString(),
                        target.UriBase,
                        (target.DisplayName is null) ? "-" : target.DisplayName
                    )
                )
            );
        }

        // giza/intermanual expects a terminating newline
        lines.Add("");

        var bytes = Encoding.UTF8.GetBytes(String.Join("\n", lines));
        using (var compressor = new ZLibStream(buffer, CompressionMode.Compress, true))
        {
            compressor.Write(bytes, 0, bytes.Length);
        }

        return buffer.ToArray();
    }

    public static IntersphinxInventory parse(string baseUrl, byte[] text)
    {
        // Intersphinx always has 4 lines of ASCII before the payload.
        int startIndex = 0;
        for (var i = 0; i < 4; i += 1)
        {
            startIndex = Array.IndexOf(text, (byte)'\n', startIndex) + 1;
        }

        var compressedStream = new MemoryStream(text[startIndex..]);
        var decompressedStream = new MemoryStream();
        using (var compressor = new ZLibStream(compressedStream, CompressionMode.Decompress, true))
        {
            compressor.CopyTo(decompressedStream);
        }
        var decompressedBuffer = decompressedStream.ToArray();

        var decompressed = Encoding.UTF8.GetString(decompressedBuffer.ToArray());
        var inventory = new IntersphinxInventory(baseUrl);
        foreach (var line in decompressed.Split("\n"))
        {
            if (String.IsNullOrEmpty(line.Trim()))
            {
                continue;
            }

            var match = INVENTORY_PATTERN().Match(TRAILING_WHITESPACE().Replace(line, ""));
            if (match is null)
            {
                continue;
            }

            var name = match.Groups[1].Value;
            var domain_and_role = match.Groups[2].Value;
            var raw_priority = match.Groups[3].Value;
            var uri = match.Groups[4].Value;
            var raw_dispname = match.Groups[5].Value;

            // These are hard-coded in Sphinx as well. Support these names for compatibility.
            if (domain_and_role == "std:cmdoption")
            {
                domain_and_role = "std:option";
            }
            else if (domain_and_role == "py:method")
            {
                domain_and_role = "py:meth";
            }

            var uri_base = uri;
            if (uri.EndsWith("$"))
            {
                uri = uri[..^1] + name;
            }

            // The spec says that only {dispname} can contain spaces. In practice, this is a lie.
            // Just silently skip invalid lines.
            int priority;
            try
            {
                priority = Int32.Parse(raw_priority);
            }
            catch (SystemException ex) when (ex is FormatException || ex is OverflowException)
            {
                continue;
            }

            var domainAndRoleSplit = domain_and_role.Split(":", 1);
            var domain = domainAndRoleSplit[0];
            var role = domainAndRoleSplit[1];

            // "If {dispname} is identical to {name}, it is stored as -"
            var dispname = (raw_dispname == "-") ? null : raw_dispname;

            var target_definition = new TargetDefinition(
                name, (domain, role), priority, uri_base, uri, dispname
            );
            inventory._targets[$"{domain_and_role}:{name}"] = target_definition;
        }
        return inventory;
    }


    static public IntersphinxInventory FetchInventory(
        string url,
        string cache_dir = HTTPCache.DEFAULT_CACHE_DIR,
        object? cache_interval = null
    )
    {
        var base_url = FINAL_PATH_COMPONENT().Replace(url, "");
        base_url += "/";

        var data = new HTTPCache(cache_dir).Get(url, cache_interval);
        return IntersphinxInventory.parse(base_url, data);
    }
}
