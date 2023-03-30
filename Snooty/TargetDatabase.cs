using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

/// A database of targets known to this project.
public partial class TargetDatabase
{
    [GeneratedRegex("[_-]+", RegexOptions.Compiled, "en-US")]
    private static partial Regex PAT_TARGET_PART_SEPARATOR();

    public interface IResult
    {
        string CanonicalTargetName { get; }
        IReadOnlyList<N.InlineNode> Title { get; }
    }

    public abstract record Result
    {
        public record ExternalResult(string Url, string CanonicalTargetName, IReadOnlyList<N.InlineNode> Title) : Result, IResult;
        public record InternalResult((string, string) Result, string CanonicalTargetName, IReadOnlyList<N.InlineNode> Title) : Result, IResult;
    }

    public record LocalDefinition(string CanonicalName, FileId FileId, List<N.InlineNode> Title, string Html5Id);

    [JsonInclude]
    public Dictionary<string, IntersphinxInventory> _intersphinxInventories { get; set; } = new Dictionary<string, IntersphinxInventory>();

    [JsonInclude]
    public Dictionary<string, List<LocalDefinition>> _localDefinitions { get; set; } = new Dictionary<string, List<LocalDefinition>>();

    public TargetDatabase() { }

    public TargetDatabase(Dictionary<string, IntersphinxInventory> intersphinxInventories)
    {
        _intersphinxInventories = intersphinxInventories;
    }

    public TargetDatabase(Dictionary<string, IntersphinxInventory> intersphinxInventories, Dictionary<string, List<LocalDefinition>> localDefinitions)
    {
        _intersphinxInventories = intersphinxInventories;
        _localDefinitions = localDefinitions;
    }

    public IList<TargetDatabase.Result> Get(string key)
    {
        key = Util.NormalizeTarget(key);
        var results = new List<TargetDatabase.Result>();

        var spec = Spec.Get();

        // Check to see if the target is defined locally
        try
        {
            List<LocalDefinition> definitions;
            try
            {
                definitions = _localDefinitions[key];
            }
            catch (KeyNotFoundException)
            {
                definitions = new List<LocalDefinition>();
            }
            foreach (var definition in definitions)
            {
                results.Add(new TargetDatabase.Result.InternalResult(
                    (definition.FileId.WithoutKnownSuffix(), definition.Html5Id),
                    definition.CanonicalName,
                    definition.Title
                ));
            }
        }
        catch (KeyNotFoundException) { }

        // Get URL from intersphinx inventories
        foreach (var inventory in _intersphinxInventories.Values)
        {
            var entry = inventory.Get(key);

            // Sphinx, at least older versions, have a habit of lower-casing its intersphinx
            // inventory sections. Try that.
            if (entry is null)
            {
                entry = inventory.Get(key.ToLowerInvariant());
            }

            // FIXME: temporary until DOP-2345 is complete
            if (entry is null && key.StartsWith("mongodb:php"))
            {
                entry = inventory.Get(key.Replace("\\\\", "\\"));
            }

            if (entry is not null)
            {
                var url = Util.UrlCombine(inventory.BaseUrl, entry.Uri);

                var display_name = entry.DisplayName;
                if (display_name is null)
                {
                    display_name = entry.Name;

                    display_name = spec.StripPrefixFromName(
                        entry.DomainAndRole(), display_name
                    );
                }

                var title = new List<N.InlineNode> { new N.Text(new N.Span(-1), display_name) };
                results.Add(
                    new Result.ExternalResult(url, entry.Name, title)
                );
            }
        }

        return results;
    }

    public List<string> GetSuggestions(string key)
    {
        key = Util.NormalizeTarget(key);
        key = key.Split(":", 2)[2];
        var candidates = new List<string>();

        var intersphinxKeys = _intersphinxInventories.Values.Select(inventory => inventory.TargetNames()).SelectMany(x => x);
        var all_keys = (new IEnumerable<string>[] { _localDefinitions.Keys, intersphinxKeys }).SelectMany(x => x);

        var key_parts = PAT_TARGET_PART_SEPARATOR().Split(key);

        foreach (var original_key_definition in all_keys)
        {
            var key_definition = original_key_definition.Split(":", 2)[2];
            if (Math.Abs(key.Length - key_definition.Length) > 2)
            {
                continue;
            }

            // Tokens tend to be separated by - and _: if there's a different number of
            // separators, don't attempt a typo correction
            var key_definition_parts = PAT_TARGET_PART_SEPARATOR().Split(key_definition);
            if (key_definition_parts.Length != key_parts.Length)
            {
                continue;
            }

            // Evaluate each part separately, since we can abort before evaluating the rest.
            // Small bonus: complexity is O(N*M)
            if (key_parts.Zip(key_definition_parts).Select(arg => Util.DamerauLevenshteinDistance(arg.Item1, arg.Item2)).All(dist => dist <= 2))
            {
                candidates.Add(original_key_definition);
            }
        }

        return candidates;
    }

    public void DefineLocalTarget(
        string domain,
        string name,
        IEnumerable<string> targets,
        FileId pageid,
        List<N.InlineNode> title,
        string html5Id
    )
    {
        // If multiple target names are given, prefer placing the one with the most periods
        // into referring RefRole nodes. This is an odd heuristic, but should work for now.
        // e.g. if a RefRole links to "-v", we want it to get normalized to "mongod.-v" if that's
        // what gets resolved.
        var canonical_target_name = targets.MaxBy(key => key.Count(ch => ch == '.'));
        if (canonical_target_name is null)
        {
            throw new ArgumentException("targets must not be empty");
        }

        foreach (var target in targets)
        {
            var normalizedTarget = Util.NormalizeTarget(target);
            var key = $"{domain}:{name}:{normalizedTarget}";
            _localDefinitions.GetOrAdd(key).Add(
                new TargetDatabase.LocalDefinition(
                    canonical_target_name, pageid, title, html5Id
                )
            );
        }
    }

    public List<(string, string)> Reset(ProjectConfig config)
    {
        var failed_requests = new List<(string, string)>();

        var fetched_inventories = new Dictionary<string, IntersphinxInventory>();

        foreach (var url in config.intersphinx)
        {
            try
            {
                fetched_inventories[url] = IntersphinxInventory.FetchInventory(url);
            }
            catch (HttpRequestException err)
            {
                failed_requests.Add((url, err.ToString()));
            }
        }

        _intersphinxInventories = fetched_inventories;
        _localDefinitions.Clear();

        return failed_requests;
    }


    public IntersphinxInventory generate_inventory(string baseUrl)
    {
        var targets = new Dictionary<string, TargetDefinition>();
        foreach (var (key, definitions) in _localDefinitions)
        {
            if (definitions.Count == 0)
            {
                continue;
            }

            var definition = definitions[0];
            var uri = definition.FileId.AsDirHtml();
            var dispname = String.Concat(definition.Title.Select(node => node.GetText()));
            var split_key = key.Split(":", 2);
            var domain = split_key[0];
            var role_name = split_key[1];
            var name = split_key[2];

            if (dispname == String.Empty)
            {
                dispname = null;
            }

            var baseUri = uri;
            if ((domain, role_name) != ("std", "doc"))
            {
                baseUri += "#" + definition.Html5Id;
                uri += "#" + definition.Html5Id;
            }

            targets[key] = new TargetDefinition(
                definition.CanonicalName,
                (domain, role_name),
                -1,
                baseUri,
                uri,
                dispname
            );
        }
        return new IntersphinxInventory(baseUrl, targets);
    }

    TargetDatabase CopyCleanSlate()
    {
        return new TargetDatabase(_intersphinxInventories);
    }

    public static (TargetDatabase, List<(string, string)>) Load(
        ProjectConfig config
    )
    {
        var db = new TargetDatabase();
        var failed_urls = db.Reset(config);
        return (db, failed_urls);
    }
}
