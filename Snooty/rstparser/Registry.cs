namespace rstparser;
using tinydocutils;

using RoleFunctionType = Func<string, string, string, int, tinydocutils.Inliner, (List<tinydocutils.Node>, List<tinydocutils.SystemMessage>)>;

public record class Domain
{
    public Dictionary<string, DirectiveDefinition> Directives = new();
    public Dictionary<string, RoleFunctionType> Roles = new();
}


public class Registry
{
    public class Builder
    {
        private readonly Dictionary<string, Domain> _domains = new();

        public void AddDirective(string fullName, DirectiveDefinition directive)
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

    // Hard-coded sequence of domains in which to search for a directives
    // and roles if no domain is explicitly provided.. Eventually this should
    // not be hard-coded.
    private static readonly string[] DOMAIN_RESOLUTION_SEQUENCE = new string[] { "mongodb", "std", "" };

    private readonly Dictionary<string, Domain> _domains;
    private readonly Domain[] _domainSequence;

    public Registry(string? defaultDomain, Dictionary<string, Domain> domains)
    {
        _domains = domains;

        IEnumerable<string> nameSequence = DOMAIN_RESOLUTION_SEQUENCE;
        if (defaultDomain is not null)
        {
            nameSequence = global::Util.SingleEnumerable(defaultDomain).Concat(nameSequence);
        }
        _domainSequence = nameSequence.Where(_domains.ContainsKey).Select((domainName) => _domains[domainName]).ToArray();
    }

    public DirectiveDefinition? LookupDirective(
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
        settings.LookupDirective = LookupDirective;
        settings.LookupRole = LookupRole;
    }

    /// Register all of the definitions in the spec with docutils.
    public Registry CreateRegistry(
        Spec spec,
        string? defaultDomain
    )
    {

        var builder = new Builder();
        var directives = spec.Directive.Select(pair => (pair.Key, pair.Value)).ToList();
        var roles = spec.Role.ToList();

        // Define rstobjects
        foreach (var (name, rst_object) in spec.Rstobject)
        {
            var directive = rst_object.CreateDirective();
            directives.Add((name, directive));
            // role = rst_object.create_role()
            // roles.Add((name, role));
        }

        foreach (var (name, directive) in directives)
        {
            // Skip abstract base directives
            if (name.StartsWith("_"))
            {
                continue;
            }

            Dictionary<string, Func<string, object>> options = new();
            foreach (var (optionName, option) in directive.Options)
            {
                options[optionName] = spec.GetValidator(option);
            }

            var directiveHandler = MakeDocutilsDirectiveHandler(
                directive, name, options
            );

            //     // Tabs have special handling because of the need to support legacy syntax
            //     if name == "tabs":
            //         base_class = BaseTabsDirective
            if (SPECIAL_DIRECTIVE_HANDLERS.ContainsKey(name))
            {
                directiveHandler.Run = SPECIAL_DIRECTIVE_HANDLERS[name].Run;
            }

            builder.AddDirective(name, directiveHandler);
        }

        // # reference tabs directive declaration as first step in registering tabs-* with docutils
        // tabs_directive = spec.directive["tabs"]

        // # Define tabsets
        // for name in spec.tabs:
        //     tabs_base_class: Any = BaseTabsDirective
        //     tabs_name = "tabs-" + name

        //     # copy and modify the tabs directive to update its name to match the deprecated tabs-* naming convention
        //     modified_tabs_directive = dataclasses.replace(tabs_directive, name=tabs_name)

        //     tabs_options: Dict[str, object] = {
        //         option_name: spec.get_validator(option)
        //         for option_name, option in tabs_directive.options.items()
        //     }

        //     DocutilsDirective = make_docutils_directive_handler(
        //         modified_tabs_directive, tabs_base_class, "tabs", tabs_options
        //     )

        //     builder.AddDirective(tabs_name, DocutilsDirective)

        // Docutils builtins
        builder.AddDirective("unicode", UnicodeDirective.Make());
        builder.AddDirective("replace", ReplaceDirective.Make());

        // # Define roles
        // builder.add_role("", handle_role_null)
        // for name, role_spec in roles:
        //     handler: Optional[RoleHandlerType] = None
        //     domain = role_spec.domain or ""
        //     if not role_spec.type or role_spec.type == specparser.PrimitiveRoleType.text:
        //         handler = TextRoleHandler(domain)
        //     elif isinstance(role_spec.type, specparser.LinkRoleType):
        //         handler = LinkRoleHandler(
        //             role_spec.type.link,
        //             role_spec.type.ensure_trailing_slash == True,
        //             role_spec.type.format,
        //         )
        //     elif isinstance(role_spec.type, specparser.RefRoleType):
        //         handler = RefRoleHandler(
        //             role_spec.type.domain or domain,
        //             role_spec.type.name,
        //             role_spec.type.tag,
        //             role_spec.rstobject.type
        //             if role_spec.rstobject
        //             else specparser.TargetType.plain,
        //             role_spec.type.format,
        //         )
        //     elif role_spec.type == specparser.PrimitiveRoleType.explicit_title:
        //         handler = ExplicitTitleRoleHandler(domain)

        //     if not handler:
        //         raise ValueError('Unknown role type "{}"'.format(role_spec.type))

        //     builder.add_role(name, handler)

        return builder.Build(defaultDomain);
    }

    public static DirectiveDefinition MakeDocutilsDirectiveHandler(
        DirectiveSpec spec,
        string name,
        Dictionary<string, Func<string, object>> options
    )
    {
        var optional_args = 0;
        var required_args = 0;

        var argument_type = spec.ArgumentType;
        if (argument_type is not null)
        {
            if (
                argument_type is DirectiveOption directiveOption
                && directiveOption.Required
            )
            {
                required_args = 1;
            }
            else
            {
                optional_args = 1;
            }
        }

        var directive = new BaseDocutilsDirective()
        {
            Spec = spec
        };
        var result = new DirectiveDefinition
        {
            HasContent = spec.ContentType is not null && (bool)spec.ContentType,
            OptionalArguments = optional_args,
            RequiredArguments = required_args,
            FinalArgumentWhitespace = true,
            OptionSpec = options,
            Run = directive.Run
        };

        return result;
    }

    static private readonly Dictionary<string, DirectiveDefinition> SPECIAL_DIRECTIVE_HANDLERS = new() {
        {"code-block", BaseCodeDirective.Make()},
        {"code", BaseCodeDirective.Make()},
        {"input", BaseCodeIODirective.Make()},
        {"output", BaseCodeIODirective.Make()},
        {"sourcecode", BaseCodeDirective.Make()},
        {"versionadded", BaseVersionDirective.Make()},
        {"versionchanged", BaseVersionDirective.Make()},
        {"deprecated", DeprecatedVersionDirective.Make()},
        // {"card-group", BaseCardGroupDirective},
        // {"toctree", BaseTocTreeDirective},
    };
}
