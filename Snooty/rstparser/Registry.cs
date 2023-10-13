namespace rstparser;
using tinydocutils;

using RoleFunctionType = Func<string, string, string, int, tinydocutils.Inliner, (List<tinydocutils.Node>, List<tinydocutils.SystemMessage>)>;

public record class Domain
{
    public Dictionary<string, IDirective> Directives = new Dictionary<string, IDirective>();
    public Dictionary<string, RoleFunctionType> Roles = new Dictionary<string, RoleFunctionType>();
}


public class Registry
{
    public class Builder
    {
        Dictionary<string, Domain> _domains = new();

        public void AddDirective(string fullName, IDirective directive)
        {
            var (domain, name) = global::Util.SplitDomain(fullName);
            _domains[domain].Directives[name] = directive;
        }

        public void AddRole(string fullName, RoleFunctionType role)
        {
            var (domain, name) = global::Util.SplitDomain(fullName);
            _domains[domain].Roles[name] = role;
        }

        public Registry Build(string? defaultDomain = null)
        {
            return new Registry(defaultDomain, _domains);
        }
    }

    // This is effectively an LRU cache of size 1
    public static Registry? REGISTRY_SINGLETON = null;

    // Hard-coded sequence of domains in which to search for a directives
    // and roles if no domain is explicitly provided.. Eventually this should
    // not be hard-coded.
    private static readonly List<string> DOMAIN_RESOLUTION_SEQUENCE = new() { "mongodb", "std", "" };

    private string? _defaultDomain;
    private Dictionary<string, Domain> _domains;
    private List<Domain> _domainSequence;

    public Registry(string? defaultDomain, Dictionary<string, Domain> domains)
    {
        _defaultDomain = defaultDomain;
        _domains = domains;

        var nameSequence = DOMAIN_RESOLUTION_SEQUENCE;
        if (defaultDomain is not null)
        {
            nameSequence = new List<string> { defaultDomain };
            nameSequence.AddRange(nameSequence);
        }
        _domainSequence = nameSequence.Where((domainName) => _domains.ContainsKey(domainName)).Select((domainName) => _domains[domainName]).ToList();
    }

    public IDirective? LookupDirective(
        string directiveName
    )
    {
        string domainName;

        // Remove the built-in directives we don't want
        (domainName, directiveName) = global::Util.SplitDomain(directiveName);
        if (domainName.Length > 0)
        {
            return _domains[domainName].Directives!.GetValueOrDefault(directiveName, null);
        }

        foreach (var domain in _domainSequence)
        {
            if (domain.Directives.ContainsKey(directiveName))
            {
                return domain.Directives[directiveName];
            }
        }

        return null;
    }

    public RoleFunctionType? LookupRole(string roleName)
    {
        string domainName;
        (domainName, roleName) = global::Util.SplitDomain(roleName);
        if (domainName.Length > 0)
        {
            return _domains[domainName].Roles!.GetValueOrDefault(roleName, null);
        }

        foreach (var domain in _domainSequence)
        {
            if (domain.Roles.ContainsKey(roleName))
            {
                return domain.Roles[roleName];
            }
        }

        return null;
    }

    public void Activate(OptionParser settings)
    {
        settings.LookupDirective = (name) => LookupDirective(name);
        settings.LookupRole = (name) => LookupRole(name);
    }

    // @classmethod
    // def get(cls, default_domain: Optional[str]) -> "Registry":
    //     if (
    //         cls.CURRENT_REGISTRY is not None
    //         and cls.CURRENT_REGISTRY[0] == default_domain
    //     ):
    //         return cls.CURRENT_REGISTRY[1]

    //     registry = register_spec_with_docutils(specparser.Spec.get(), default_domain)
    //     cls.CURRENT_REGISTRY = (default_domain, registry)
    //     return registry

    static private readonly Dictionary<string, IDirective> SPECIAL_DIRECTIVE_HANDLERS = new() {
        {"code-block", new BaseCodeDirective()},
        {"code", new BaseCodeDirective()},
        {"input", new BaseCodeIODirective()},
        {"output", new BaseCodeIODirective()},
        {"sourcecode", new BaseCodeDirective()},
        {"versionadded", new BaseVersionDirective()},
        {"versionchanged", new BaseVersionDirective()},
        {"deprecated", new DeprecatedVersionDirective()},
        // {"card-group", BaseCardGroupDirective},
        // {"toctree", BaseTocTreeDirective},
    };
}
