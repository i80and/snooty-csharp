using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

public class PosixPath : IEquatable<PosixPath>
{
    protected string[] _parts;

    public PosixPath()
    {
        _parts = new string[] { };
    }

    public PosixPath(string path)
    {
        _parts = path.Split("/");
    }

    public PosixPath(IEnumerable<string> parts)
    {
        _parts = parts.ToArray();
    }

    public String GetName()
    {
        return _parts[^1];
    }

    public FileId WithName(string name)
    {
        if (name.Contains('/'))
        {
            throw new ArgumentException(name);
        }
        var newParts = _parts.ToArray();
        newParts[^1] = name;
        return new FileId(newParts);
    }

    public string AsPosix()
    {
        return String.Join('/', _parts);
    }

    public bool Equals(PosixPath? other)
    {
        return other != null && _parts.SequenceEqual(other._parts);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (var part in _parts)
        {
            hash.Add(part);
        }
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return AsPosix();
    }
}

class Util
{
    static readonly IReadOnlySet<string> SOURCE_FILE_EXTENSIONS = new HashSet<string> { ".txt", ".rst", ".yaml" };
    /// Normalize targets to allow easy matching against the target
    /// database: normalize whitespace.
    static readonly Regex PAT_WHITESPACE = new Regex(@"\s+");
    static readonly Regex PAT_INVALID_ID_CHARACTERS = new Regex(@"[^\w_\.\-]");
    public static string NormalizeTarget(string target)
    {
        return PAT_WHITESPACE.Replace(target, " ");
    }

    public static int DamerauLevenshteinDistance(string a, string b)
    {
        // Strings are 1-indexed, and d is -1-indexed.

        // var da = {ch: 0 for ch in set(a).union(b)};
        var da = new Dictionary<char, int>();
        foreach (var ch in (new HashSet<char>(a)).Union(new HashSet<char>(b)))
        {
            da.Add(ch, 0);
        }

        var width = a.Length + 2;
        var height = b.Length + 2;
        var d = new int[width * height];

        void matrix_set(int x, int y, int value)
        {
            d[(width * (y + 1)) + (x + 1)] = value;
        }

        int matrix_get(int x, int y)
        {
            return d[(width * (y + 1)) + (x + 1)];
        }

        var maxdist = a.Length + b.Length;
        matrix_set(-1, -1, maxdist);

        for (int i = 0; i < a.Length + 1; i += 1)
        {
            matrix_set(i, -1, maxdist);
            matrix_set(i, 0, i);
        }

        for (int j = 0; j < b.Length + 1; j += 1)
        {
            matrix_set(-1, j, maxdist);
            matrix_set(0, j, j);
        }

        for (int i = 1; i < a.Length + 1; i += 1)
        {
            int db = 0;
            for (int j = 1; j < b.Length + 1; j += 1)
            {
                int k = da[b[j - 1]];
                int l = db;
                int cost;
                if (a[i - 1] == b[j - 1])
                {
                    cost = 0;
                    db = j;
                }
                else
                {
                    cost = 1;
                }
                matrix_set(
                    i,
                    j,
                    (new int[] {
                        matrix_get(i - 1, j - 1) + cost,  // substitution
                        matrix_get(i, j - 1) + 1,  // insertion
                        matrix_get(i - 1, j) + 1,  // deletion
                        matrix_get(k - 1, l - 1)
                        + (i - k - 1)
                        + 1
                        + (j - l - 1)  // transposition
                    }).Min()
                );
            }
            da[a[i - 1]] = i;
        }

        return matrix_get(a.Length, b.Length);
    }

    public static string UrlCombine(string baseUrl, string relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return relativeUrl;
        }

        if (string.IsNullOrWhiteSpace(relativeUrl))
        {
            return baseUrl;
        }

        baseUrl = baseUrl.TrimEnd('/');
        relativeUrl = relativeUrl.TrimStart('/');

        return $"{baseUrl}/{relativeUrl}";
    }

    public class Counter<T> where T : notnull
    {
        private Dictionary<T, int> _counters = new Dictionary<T, int>();

        public void Clear()
        {
            _counters.Clear();
        }

        public int Get(T key)
        {
            try
            {
                return _counters[key];
            }
            catch (KeyNotFoundException)
            {
                return 0;
            }
        }

        public void Add(T key)
        {
            int val = Get(key);
            _counters[key] = val + 1;
        }
    }

    public static string MakeHtml5Id(string orig)
    {
        var clean_id = PAT_INVALID_ID_CHARACTERS.Replace(orig, "-");
        if (clean_id.Length == 0)
        {
            clean_id = "unnamed";
        }
        return clean_id;
    }

    public static string CleanSlug(string slug)
    {
        slug = slug.Trim('/');
        var root = Path.GetFileNameWithoutExtension(slug);
        var ext = Path.GetExtension(slug);
        if (SOURCE_FILE_EXTENSIONS.Contains(ext))
        {
            return root;
        }

        return slug;
    }

    public static (FileId, string) RerootPath(string filename, string docpath, string project_root)
    {
        FileId rel_fn;

        if (Path.IsPathFullyQualified(filename))
        {
            rel_fn = new FileId(filename.Split(Path.DirectorySeparatorChar)[1..]);
        }
        else
        {
            rel_fn = new FileId(Path.Join(Path.GetDirectoryName(docpath), filename).Split(Path.DirectorySeparatorChar)).CollapseDots();
        }
        return (rel_fn, Path.GetFullPath(Path.Join(project_root, rel_fn.AsPosix())));
    }

    public static bool ParseBool(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.ToLowerInvariant() == "true")
        {
            return true;
        }

        if (lower.ToLowerInvariant() == "false")
        {
            return false;
        }

        throw new ArgumentException(text);
    }

    public class SimpleCache<T>
    {
        private ThreadLocal<Dictionary<string, T>> _cache = new ThreadLocal<Dictionary<string, T>>(() => new Dictionary<string, T>());
        public SimpleCache() { }

        public T Get(string arg, Func<T> f)
        {
            var cache = _cache.Value!;
            if (cache.Count > 128)
            {
                cache.Clear();
            }

            if (!cache.ContainsKey(arg))
            {
                cache[arg] = f();
            }

            return cache[arg];
        }
    }

    private static readonly SimpleCache<Regex> GLOB_CACHE = new SimpleCache<Regex>();

    public static bool GlobMatches(string s, string glob)
    {
        var globPattern = GLOB_CACHE.Get(glob, () => new Regex("^" + Regex.Escape(glob).Replace("\\?", ".").Replace("\\*", ".*") + "$"));
        return globPattern.IsMatch(s);
    }

    public static bool PathGlobMatches(string s, string glob)
    {
        glob = glob.Normalize();
        var pat_parts = glob.Split('/');
        var s_parts = s.Split('/');
        if (pat_parts.Length == 0)
        {
            throw new ArgumentException("empty pattern");
        }
        if (pat_parts.Length > s_parts.Length)
        {
            return false;
        }
        foreach (var (part, pat) in s_parts.Reverse().Zip(pat_parts.Reverse()))
        {
            if (!GlobMatches(part, pat))
            {
                return false;
            }
        }
        return true;
    }
}

public static class Extensions
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key) where TKey : notnull where TValue : new()
    {
        TValue val;
        try
        {
            val = dictionary[key];
        }
        catch (KeyNotFoundException)
        {
            val = new TValue();
            dictionary[key] = val;
        }

        return val;
    }

    public static string? GetAsStringOrNull(this Dictionary<string, JsonNode> dictionary, string key)
    {
        if (dictionary.ContainsKey(key))
        {
            return dictionary[key].GetValue<string?>();
        }

        return null;
    }

    public static string GetAsStringOrDefault(this Dictionary<string, JsonNode> dictionary, string key, string defaultValue)
    {
        if (dictionary.ContainsKey(key))
        {
            return dictionary[key].GetValue<string>();
        }

        return defaultValue;
    }
}
