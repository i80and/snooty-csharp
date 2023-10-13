using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

public static class EnumExtension
{
    public static IEnumerable<(int index, T item)> WithIndex<T>(this IEnumerable<T> self)
    => self.Select((item, index) => (index, item));
}

class PostprocessorUtils
{
    public static C? GetTitleInjectionCandidate<C>(N.Node node) where C : N.IParent<N.InlineNode>
    {
        while (true)
        {
            if (node is N.IAgnosticParent parentNode)
            {
                var children = parentNode.GetChildrenAgnostically();
                if (children.Count > 1)
                {
                    return default(C);
                }
                else if (children.Count == 1)
                {
                    node = children[0];
                }
                else
                {
                    return (C)parentNode;
                }
            }
            else
            {
                return default(C);
            }
        }
    }

    public static N.Node? GetDeepest(N.Node node)
    {
        while (true)
        {
            if (node is N.BlockParent<N.Node> parentNode)
            {
                if (parentNode.Children.Count > 1)
                {
                    return null;
                }
                else if (parentNode.Children.Count == 1)
                {
                    node = parentNode.Children[0];
                }
                else
                {
                    return node;
                }
            }
            else
            {
                return node;
            }
        }
    }


    public static void DeepCopyPosition(N.Node source, N.Node dest)
    {
        var source_position = source.Span;
        dest.Span = source_position;
        if (dest is N.BlockParent<N.Node> parentNode)
        {
            foreach (var child in parentNode.Children)
            {
                DeepCopyPosition(source, child);
            }
        }
    }


    public static List<N.InlineNode>? ExtractInline(
        List<N.Node> nodes
    )
    {
        if (nodes.All(node => node is N.InlineNode))
        {
            return nodes.Cast<N.InlineNode>().ToList();
        }

        var node = nodes[0];
        if (
            nodes.Count == 1
            && node is N.Paragraph paragraphNode
            && paragraphNode.Children.Count == 1)
        {
            if (paragraphNode.Children[0] is N.InlineNode inlineChild)
            {
                return new List<N.InlineNode> { inlineChild };
            }
        }

        return null;
    }
}

public class ProgramOptionHandler : Handler
{
    public N.Target? PendingProgram;

    public ProgramOptionHandler(Context context) : base(context) { }

    public override void EnterPage(FileIdStack fileIdStack, Page page)
    {
        PendingProgram = null;
    }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        var target = node as N.Target;
        if (target is null)
        {
            return;
        }

        var identifier = $"{target.Domain}:{target.Name}";
        if (identifier == "std:program")
        {
            PendingProgram = target;
        }
        else if (identifier == "std:option")
        {
            if (PendingProgram == null)
            {
                var line = node.Span.Start;
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(
                    new MissingOption(line)
                );
                return;
            }
            var program_target = ((N.IAgnosticParent)PendingProgram).GetChildOfType<N.TargetIdentifier>().First();
            var program_name_node = (N.Text)(program_target.Children[0]);
            var program_name = program_name_node.Value;
            var new_identifiers = new List<N.Node>();
            foreach (var child in ((N.IAgnosticParent)target).GetChildOfType<N.TargetIdentifier>())
            {
                var child_ids = child.Ids;
                child_ids.AddRange(child_ids.Select(child_id => $"{program_name}.{child_id}"));

                var text_node = (N.Text)(target.Children[0]);
                var value = text_node.Value;
                text_node.Value = $"{program_name} {value}";
            }

            target.Children.AddRange(new_identifiers);
        }
    }
}

class IncludeHandler : Handler
{
    public Dictionary<string, FileId> SlugFileIdMapping;
    public IncludeHandler(Context context) : base(context)
    {
        SlugFileIdMapping = new Dictionary<string, FileId>();
        foreach (var key in context.Pages.Keys)
        {
            SlugFileIdMapping[key.WithoutKnownSuffix()] = key;
        }

    }
    bool is_bound(N.Node node, string? search_text)
    {
        if (node is N.Comment commentNode)
        {
            if (commentNode.Children.Count > 0 && commentNode.Children[0] is N.Text textNode)
            {
                var comment_text = commentNode.GetText();
                return search_text == comment_text;
            }
        }
        else
        {
            if (node is N.Target targetNode)
            {
                if (targetNode.Domain == "std" && targetNode.Name == "label")
                {
                    if (targetNode.Children.Count > 0 && targetNode.Children[0] is N.TargetIdentifier targetIdentifierNode)
                    {
                        if (targetIdentifierNode.Ids.Count > 0)
                        {
                            return targetIdentifierNode.Ids.Contains(search_text);
                        }
                    }

                }
            }
        }

        return false;

    }
    (List<N.Node>, bool, bool) bound_included_AST(List<N.Node> nodes, string? start_after_text, string? end_before_text)
    {
        int start_index = 0;
        int end_index = nodes.Count;

        bool any_start = false;
        bool any_end = false;
        foreach (var (i, node) in nodes.WithIndex())
        {
            bool has_start = false;
            bool has_end = false;
            bool is_start = is_bound(node, start_after_text);
            bool is_end = is_bound(node, end_before_text);
            if (node is N.BlockParent<N.Node> parentNode)
            {
                List<N.Node> children;
                (children, has_start, has_end) = bound_included_AST(parentNode.Children, start_after_text, end_before_text);
                parentNode.Children = children;

            }
            if (is_start || has_start)
            {
                any_start = true;
                start_index = i;

            }
            if (is_end || has_end)
            {
                any_end = true;
                end_index = i;

            }
        }
        if (start_index > end_index)
        {
            throw new Exception("start-after text should precede end-before text");

        }
        return (nodes.GetRange(start_index, (start_index - (end_index + 1))), any_start, any_end);

    }
    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Directive directiveNode)
        {
            enterDirective(fileIdStack, directiveNode);
        }
    }

    void enterDirective(FileIdStack fileIdStack, N.Directive node)
    {
        if (node.Name != "include" && node.Name != "sharedinclude")
        {
            return;
        }

        var argument = String.Concat(node.Argument.Select(arg => arg.GetText()));
        if (argument.Length == 0)
        {
            return;

        }
        var include_slug = Util.CleanSlug(argument);
        var include_fileid = SlugFileIdMapping.GetValueOrDefault(include_slug);
        if (include_fileid is null)
        {
            include_slug = argument.Trim('/');
            include_fileid = SlugFileIdMapping.GetValueOrDefault(include_slug);
            if (include_fileid is null)
            {
                if (node.Name != "sharedinclude")
                {
                    Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new CannotOpenFile(include_slug, "File not found", node.Span.Start));
                }
                return;
            }

        }
        var include_page = Context.Pages.GetValueOrDefault(include_fileid);
        Debug.Assert(include_page != null);
        var deep_copy_children = new List<N.Node> { include_page.Ast.DeepClone() };
        var start_after_text = node.Options.GetAsStringOrNull("start-after")!;
        var end_before_text = node.Options.GetAsStringOrNull("end-before")!;
        if (!string.IsNullOrEmpty(start_after_text) || !string.IsNullOrEmpty(end_before_text))
        {
            var line = node.Span.Start;
            bool any_start = false;
            bool any_end = false;
            try
            {
                (deep_copy_children, any_start, any_end) = bound_included_AST(deep_copy_children, start_after_text, end_before_text);
            }
            catch (Exception e)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidInclude(e.ToString(), line));
            }

            var msg = "Please be sure your text is a comment or label. Search is case-sensitive.";
            if (!string.IsNullOrEmpty(start_after_text) && !any_start)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidInclude($"Could not find specified start-after text: '{start_after_text}'. {msg}", line));

            }
            if (!string.IsNullOrEmpty(end_before_text) && !any_end)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidInclude($"Could not find specified end-before text: '{end_before_text}'. {msg}", line));
            }

        }
        node.Children = (from child in node.Children where child is N.Directive directiveChild && directiveChild.Name == "replacement" select child).ToList();
        node.Children.AddRange(deep_copy_children);
    }

}
class NamedReferenceHandlerPass1 : Handler
{
    public Dictionary<FileId, Dictionary<string, string>> named_references = new Dictionary<FileId, Dictionary<string, string>>();
    public NamedReferenceHandlerPass1(Context context) : base(context) { }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.NamedReference namedReferenceNode)
        {
            named_references.GetOrAdd(fileIdStack.Root)[namedReferenceNode.RefName] = namedReferenceNode.RefUri;

        }
    }

}
class NamedReferenceHandlerPass2 : Handler
{
    public NamedReferenceHandlerPass2(Context context) : base(context) { }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Reference referenceNode)
        {
            enterReference(fileIdStack, referenceNode);
        }
    }

    void enterReference(FileIdStack fileIdStack, N.Reference node)
    {
        if (!string.IsNullOrEmpty(node.RefUri))
        {
            return;
        }
        var refuri = Context.Get<NamedReferenceHandlerPass1>().named_references.GetOrAdd(fileIdStack.Root).GetValueOrDefault(node.RefName);
        if (refuri is null)
        {
            var line = node.Span.Start;
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new TargetNotFound("extlink", node.RefName, new List<string> { }, line));
            return;

        }
        node.RefUri = refuri;
    }

}
class ContentsHandler : Handler
{
    record HeadingData(int Depth, string? Id, IReadOnlyList<N.InlineNode> Title);

    int contents_depth = int.MaxValue;
    int current_depth = 0;
    bool has_contents_directive = false;
    List<HeadingData> headings = new List<HeadingData> { };

    public ContentsHandler(Context context) : base(context) { }

    public override void EnterPage(FileIdStack fileIdStack, Page page)
    {
        contents_depth = int.MaxValue;
        current_depth = 0;
        has_contents_directive = false;
        headings = new List<HeadingData> { };

    }
    public override void ExitPage(FileIdStack fileIdStack, Page page)
    {
        if (!has_contents_directive)
        {
            return;
        }

        var heading_list = (from h in headings where h.Depth - 1 <= contents_depth select JsonValue.Create(h)).ToArray();
        if (heading_list.Length > 0)
        {
            page.Ast.Options["headings"] = new JsonArray(heading_list);
        }
    }
    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Section)
        {
            current_depth += 1;
            return;
        }
        if (node is N.Directive directiveNode && directiveNode.Name == "contents")
        {
            if (has_contents_directive)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new DuplicateDirective(directiveNode.Name, directiveNode.Span.Start));
                return;

            }
            has_contents_directive = true;
            try
            {
                contents_depth = int.Parse(directiveNode.Options.GetAsStringOrDefault("depth", ""));
            }
            catch (KeyNotFoundException)
            {
                contents_depth = int.MaxValue;
            }
            return;

        }
        if (current_depth - 1 > contents_depth)
        {
            return;

        }
        if (node is N.Heading headingNode && current_depth > 1)
        {
            headings.Add(new ContentsHandler.HeadingData(current_depth, headingNode.Id, headingNode.Children));
        }

    }
    public override void ExitNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Section)
        {
            current_depth -= 1;

        }

    }

}
class TabsSelectorHandler : Handler
{
    Dictionary<string, List<Dictionary<string, List<N.InlineNode>>>> selectors = new Dictionary<string, List<Dictionary<string, List<N.InlineNode>>>>();
    public TabsSelectorHandler(Context context) : base(context) { }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Directive directiveNode)
        {
            enterDirective(fileIdStack, directiveNode);
        }

    }

    void enterDirective(FileIdStack fileIdStack, N.Directive node)
    {
        string tabset_name;
        if (node.Name == "tabs-pillstrip" || node.Name == "tabs-selector")
        {
            if (node.Argument.Count == 0)
            {
                return;

            }
            tabset_name = node.Argument[0].GetText();
            if (tabset_name == "languages")
            {
                tabset_name = "drivers";
            }
            if (selectors.ContainsKey(tabset_name))
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new DuplicateDirective(node.Name, node.Span.Start));
                return;

            }
            selectors[tabset_name] = new List<Dictionary<string, List<N.InlineNode>>>();
            return;

        }
        if (selectors.Count == 0 || node.Name != "tabs")
        {
            return;

        }

        tabset_name = node.Options.GetAsStringOrDefault("tabset", "");
        if (selectors.ContainsKey(tabset_name))
        {
            var tabs = new Dictionary<string, List<N.InlineNode>>();
            foreach (var tab in ((N.IAgnosticParent)node).GetChildOfType<N.Directive>())
            {
                if (tab.Name == "tab" && tab.Options.ContainsKey("tabid"))
                {
                    var tabId = tab.Options.GetAsStringOrDefault("tabid", "");
                    tabs[tabId] = tab.Argument;
                }
            }
            selectors[tabset_name].Add(tabs);

        }
    }

    public override void EnterPage(FileIdStack fileIdStack, Page page)
    {
        selectors = new Dictionary<string, List<Dictionary<string, List<N.InlineNode>>>>();
    }
    public override void ExitPage(FileIdStack fileIdStack, Page page)
    {
        if (selectors.Count == 0)
        {
            return;
        }
        foreach (var (tabset_name, tabsets) in selectors)
        {
            if (tabsets.Count == 0)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new ExpectedTabs(0));
                return;
            }
            if (!(tabsets.All(t => t.Count == tabsets[0].Count)))
            {
                // If all tabsets are not the same length, identify tabs that do not appear in every tabset
                var tabset_sets = tabsets.Select(t => new HashSet<string>(t.Keys));
                var union = new HashSet<string>();
                var intersection = new HashSet<string>(tabset_sets.First());
                foreach (var tabset in tabset_sets)
                {
                    union.UnionWith(tabset);
                    intersection.IntersectWith(tabset);
                }

                var error_tabs = union.Except(intersection).ToList();
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(
                    new MissingTab(error_tabs, 0)
                );
            }
            if (page.Ast is N.Root)
            {
                if (!page.Ast.Options.ContainsKey("selectors"))
                {
                    page.Ast.Options["selectors"] = new JsonObject();

                }

                var options = new Dictionary<string, dynamic>();
                foreach (var (tabid, title) in tabsets[0])
                {
                    options[tabid] = title.Select(node => node.ToJsonDictionary());
                }
                ((dynamic)page.Ast.Options)["selectors"][tabset_name] = options;
            }
        }
    }
}
class TargetHandler : Handler
{
    public Util.Counter<string> target_counter = new Util.Counter<string>();
    public TargetHandler(Context context) : base(context) { }
    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Target targetNode)
        {
            enterTarget(fileIdStack, targetNode);
        }

    }

    void enterTarget(FileIdStack fileIdStack, N.Target node)
    {
        // Frankly, this is silly. We just pick the longest identifier. This is arbitrary,
        // and we can consider this behavior implementation-defined to be changed later if needed.
        // It just needs to be something consistent.
        var identifiers = ((N.IAgnosticParent)node).GetChildOfType<N.TargetIdentifier>().ToList();
        var candidates = identifiers.Where(identifier => identifier.Ids.Count > 0).Select(identifier => identifier.Ids.MaxBy(id => id.Length)).ToList();
        if (candidates.Count == 0)
        {
            return;
        }
        var chosen_id = candidates.MaxBy(candidate => candidate.Length);
        var chosen_html_id = $"{node.Domain}-{node.Name}-{Util.MakeHtml5Id(chosen_id)}";
        var counter = target_counter.Get(chosen_html_id);
        if (counter > 0)
        {
            chosen_html_id += $"-{counter}";

        }
        target_counter.Add(chosen_html_id);
        node.HtmlId = chosen_html_id;
        foreach (var target_node in identifiers)
        {
            List<N.InlineNode> title;
            if (target_node.Children.Count == 0)
            {
                title = new List<N.InlineNode>();
            }
            else
            {
                title = target_node.Children.ToList();
            }

            var target_ids = target_node.Ids;
            Context.Get<TargetDatabase>().DefineLocalTarget(node.Domain, node.Name, target_ids, fileIdStack.Root, title, chosen_html_id);
        }
    }
    public override void EnterPage(FileIdStack fileIdStack, Page page)
    {
        target_counter.Clear();
    }

}
class HeadingHandler : Handler
{
    Util.Counter<string> heading_counter = new Util.Counter<string>();
    public Dictionary<string, dynamic> SlugTitleMapping = new Dictionary<string, dynamic>();
    public HeadingHandler(Context context) : base(context) { }
    public override void ExitPage(FileIdStack fileIdStack, Page page)
    {
        heading_counter.Clear();

    }
    public IReadOnlyList<N.InlineNode>? GetTitle(string slug)
    {
        try
        {
            return SlugTitleMapping[slug];
        }
        catch (KeyNotFoundException)
        {
            return null;
        }

    }
    bool Contains(string slug)
    {
        return SlugTitleMapping.ContainsKey(slug);

    }
    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Heading headingNode)
        {
            enterHeading(fileIdStack, headingNode);
        }

    }

    void enterHeading(FileIdStack fileIdStack, N.Heading node)
    {
        var counter = heading_counter.Get(node.Id);
        heading_counter.Add(node.Id);
        if (counter > 0)
        {
            node.Id += $"-{counter}";

        }
        var slug = fileIdStack.Root.WithoutKnownSuffix();
        if (!SlugTitleMapping.ContainsKey(slug))
        {
            Context.Get<TargetDatabase>().DefineLocalTarget("std", "doc", new string[] { slug }, fileIdStack.Root, node.Children, Util.MakeHtml5Id(node.Id));
            SlugTitleMapping[slug] = node.Children;
            Context.Get<TargetDatabase>().DefineLocalTarget("std", "doc", new string[] { fileIdStack.Root.WithoutKnownSuffix() }, fileIdStack.Root, node.Children, Util.MakeHtml5Id(node.Id));

        }
    }

}
class BannerHandler : Handler
{
    string _root;
    public BannerHandler(Context context) : base(context)
    {
        _root = Context.Get<ProjectConfig>().root;
    }

    N.Node? __find_target_insertion_node(N.BlockParent<N.Node> node)
    {
        var queue = new Queue<N.Node>(node.Children);
        int curr_iteration = 0;
        int max_iteration = 50;
        N.Node? insertion_node = null;
        while (queue.Count > 0 && (curr_iteration < max_iteration))
        {
            var candidate = queue.Dequeue();
            if (candidate is N.Section)
            {
                insertion_node = candidate;
                break;

            }
            if (candidate is N.BlockParent<N.Node> parentCandidate)
            {
                foreach (var child in parentCandidate.Children)
                {
                    queue.Enqueue(child);
                }
            }
            curr_iteration += 1;

        }
        return insertion_node;
    }

    int __determine_banner_index(N.BlockParent<N.Node> node)
    {
        return node.Children.WithIndex().Where(pair => pair.Item2 is N.Heading).Select(pair => pair.Item1).FirstOrDefault(0) + 1;
    }

    bool __page_target_match(List<string> targets, Page page, FileId fileid)
    {
        Debug.Assert(fileid.GetSuffix() == ".txt");
        var page_path_relative_to_source = Path.GetRelativePath(page.SourcePath, Path.Join(_root, "source"));
        foreach (var target in targets)
        {
            if (Util.GlobMatches(page_path_relative_to_source, target))
            {
                return true;
            }
        }
        return false;
    }

    public override void EnterPage(FileIdStack fileIdStack, Page page)
    {
        var banners = Context.Get<ProjectConfig>().banner_nodes;
        foreach (var banner in banners)
        {
            if (!__page_target_match(banner.targets, page, fileIdStack.Current))
            {
                continue;
            }
            var banner_parent = __find_target_insertion_node(page.Ast);
            if (banner_parent is N.BlockParent<N.Node> parentNode)
            {
                var target_insertion = __determine_banner_index(parentNode);
                parentNode.Children.Insert(target_insertion, banner.node.DeepClone());

            }
        }
    }

}

class GuidesHandler : Handler
{
    record ChapterData(string Id, int ChapterNumber, string? Description, List<string> Guides, string? Icon)
    {
        public Dictionary<string, object?> Serialize()
        {
            return new Dictionary<string, object?> {
                {"id", Id},
                {"chapter_number", ChapterNumber},
                {"description", Description},
                {"guides", Guides},
                {"icon", Icon}
            };
        }
    }

    class GuideData
    {
        public string chapter_name = "";
        public int completion_time = 0;
        public List<N.Node> description = new List<N.Node>();
        public List<N.InlineNode> title = new List<N.InlineNode>();
        public Dictionary<string, object> Serialize()
        {
            return new Dictionary<string, dynamic> {
                {"chapter_name", chapter_name},
                {"completion_time", completion_time},
                {"description", (from node in description select node).ToList()},
                {"title", (from node in title select node).ToList()}};
        }

    }

    Dictionary<string, ChapterData> _chapters = new Dictionary<string, ChapterData>();
    Dictionary<string, GuideData> _guides = new Dictionary<string, GuideData>();

    public GuidesHandler(Context context) : base(context) { }

    void add_guides_metadata(Dictionary<string, object> document)
    {
        if (_chapters.Count > 0)
        {
            var chaptersDict = new Dictionary<string, Dictionary<string, object?>>();
            foreach (var (k, v) in _chapters)
            {
                chaptersDict[k] = v.Serialize();
            }
            document["chapters"] = (object)chaptersDict;
        }
        if (_guides.Count > 0)
        {
            var slug_title_mapping = Context.Get<HeadingHandler>().SlugTitleMapping;
            foreach (var (slug, title) in slug_title_mapping)
            {
                if (_guides.ContainsKey(slug))
                {
                    _guides[slug].title = title;
                }
            }

            var guidesDictionary = new Dictionary<string, object>();
            foreach (var (k, v) in _guides)
            {
                guidesDictionary.Add(k, v.Serialize());
            }
            document["guides"] = guidesDictionary;
        }
    }

    List<string> __get_guides(N.Directive chapter, string chapter_title, FileId current_file)
    {
        var guides = new List<string>();
        foreach (var child in ((N.IAgnosticParent)chapter).GetChildOfType<N.Directive>())
        {
            var line = child.Span.Start;
            if (child.Name != "guide")
            {
                Context.Diagnostics[current_file].Add(new InvalidChild(child.Name, "chapter", "guide", line));
                continue;

            }
            var guide_argument = child.Argument;
            if (guide_argument.Count == 0)
            {
                Context.Diagnostics[current_file].Add(new ExpectedPathArg(child.Name, line));
                continue;
            }
            var guide_slug = Util.CleanSlug(guide_argument[0].GetText());
            var current_guide_data = _guides[guide_slug];
            if (current_guide_data.chapter_name.Length > 0)
            {
                Context.Diagnostics[current_file].Add(new GuideAlreadyHasChapter(guide_slug, current_guide_data.chapter_name, chapter_title, line));
                continue;
            }
            else
            {
                current_guide_data.chapter_name = chapter_title;
            }

            guides.Add(guide_slug);

        }
        return guides;

    }
    void __handle_chapter(N.Directive chapter, FileId current_file)
    {
        var line = chapter.Span.Start;
        var title_argument = chapter.Argument;
        if (title_argument.Count != 1)
        {
            Context.Diagnostics[current_file].Add(new InvalidChapter("Invalid title argument. The title should be plain text.", line));
            return;

        }
        var title = title_argument[0].GetText();
        if (string.IsNullOrEmpty(title))
        {
            Context.Diagnostics[current_file].Add(new InvalidChapter("Invalid title argument. The title should be plain text.", line));
            return;

        }
        var description = chapter.Options.GetAsStringOrNull("description");
        if (String.IsNullOrEmpty(description))
        {
            return;
        }
        var guides = __get_guides(chapter, title, current_file);
        if (guides.Count == 0)
        {
            Context.Diagnostics[current_file].Add(new MissingChild("chapter", "guide", line));
            return;

        }
        if (!_chapters.ContainsKey(title))
        {
            var icon = chapter.Options.GetAsStringOrNull("icon");
            _chapters[title] = new ChapterData(Util.MakeHtml5Id(title).ToLowerInvariant(), _chapters.Count + 1, description, guides, icon);

        }
        else
        {
            Context.Diagnostics[current_file].Add(new ChapterAlreadyExists(title, line));
        }
    }
    void __handle_include(N.Directive node, FileId current_file)
    {
        if (node.Children.Count == 1)
        {
            var root = node.Children[0];
            if (root is N.Root rootNode)
            {
                __handle_chapters(rootNode, current_file);
            }
        }
    }
    void __handle_chapters(N.BlockParent<N.Node> chapters, FileId current_file)
    {
        var line = chapters.Span.Start;
        foreach (var child in ((N.IAgnosticParent)chapters).GetChildOfType<N.Directive>())
        {
            if (child.Name == "chapter")
            {
                __handle_chapter(child, current_file);
            }
            else
            {
                if (child.Name == "include")
                {
                    __handle_include(child, current_file);
                }
                else
                {
                    Context.Diagnostics[current_file].Add(new InvalidChild(child.Name, "chapters", "chapter", line));
                    continue;
                }
            }
        }
        if (_chapters.Count == 0)
        {
            Context.Diagnostics[current_file].Add(new MissingChild("chapters", "chapter", line));
        }

    }
    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Directive directiveNode)
        {
            enterDirective(fileIdStack, directiveNode);
        }
    }

    void enterDirective(FileIdStack fileIdStack, N.Directive node)
    {
        var current_file = fileIdStack.Current;
        var current_slug = Util.CleanSlug(current_file.WithoutKnownSuffix());
        if (node.Name == "chapters" && current_file == new FileId("index.txt"))
        {
            if (_chapters.Count > 0)
            {
                return;
            }
            __handle_chapters(node, current_file);
        }
        else
        {
            if (node.Name == "time")
            {
                if (node.Argument.Count == 0)
                {
                    return;
                }
                try
                {
                    int completion_time = int.Parse(node.Argument[0].GetText());
                    _guides[current_slug].completion_time = completion_time;
                }
                catch (SystemException) { }
            }
            else
            {
                if (node.Name == "short-description")
                {
                    _guides[current_slug].description = node.Children;
                }
            }

        }
    }
}

class OpenAPIHandler : Handler
{
    record SourceData(string SourceType, string Source);

    Dictionary<string, SourceData> openapi_pages = new Dictionary<string, SourceData>();

    public OpenAPIHandler(Context context) : base(context) { }

    Dictionary<string, Dictionary<string, string>> get_metadata()
    {
        Dictionary<string, Dictionary<string, string>> result = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (k, v) in openapi_pages)
        {
            Dictionary<string, string> entry = new Dictionary<string, string> {
                {"source_type", v.SourceType},
                {"source", v.Source},
            };
            result[k] = entry;
        }
        return result;

    }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Directive directiveNode)
        {
            enterDirective(fileIdStack, directiveNode);
        }
    }

    void enterDirective(FileIdStack fileIdStack, N.Directive node)
    {
        if (node.Name != "openapi" || node.Options.ContainsKey("preview"))
        {
            return;
        }

        var current_file = fileIdStack.Current;
        var current_slug = Util.CleanSlug(current_file.WithoutKnownSuffix());
        if (openapi_pages.ContainsKey(current_slug))
        {
            Context.Diagnostics[current_file].Add(new DuplicateDirective(node.Name, node.Span.Start));
            return;

        }
        var source_type = node.Options.GetAsStringOrNull("source_type");
        if (source_type is null)
        {
            return;
        }
        string source;
        var argument = node.Argument[0];

        if (source_type == "local" || source_type == "atlas")
        {
            source = (argument as N.Text)!.GetText();
        }
        else
        {
            source = (argument as N.Reference)!.RefUri;
        }

        openapi_pages[current_slug] = new SourceData(source_type, source);
    }
}

class IAHandler : Handler
{
    class IAData
    {
        IReadOnlyList<N.InlineNode> Title;
        string? Url;
        string? Slug;
        string? ProjectName;
        bool? Primary;

        public IAData(IReadOnlyList<N.InlineNode> title, string? url, string? slug, string? projectName, bool? primary)
        {
            Title = title;
            Url = url;
            Slug = slug;
            ProjectName = projectName;
            Primary = primary;
        }

        Dictionary<string, object> serialize()
        {
            var result = new Dictionary<string, object> { { "title", (from node in Title select node).ToList() } };
            if (!string.IsNullOrEmpty(ProjectName))
            {
                result["project_name"] = ProjectName;
            }
            if (!string.IsNullOrEmpty(Slug))
            {
                result["slug"] = Slug;
            }
            if (!string.IsNullOrEmpty(Url))
            {
                result["url"] = Url;
            }
            if (Primary != null)
            {
                result["primary"] = Primary;
            }
            return result;

        }

    }

    List<IAData> _ia = new List<IAData>();

    public IAHandler(Context context) : base(context) { }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Directive directiveNode)
        {
            enterDirective(fileIdStack, directiveNode);
        }

    }

    void enterDirective(FileIdStack fileIdStack, N.Directive node)
    {
        if (node.Name != "ia" || node.Domain != "")
        {
            return;
        }

        if (_ia.Count > 0)
        {
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new DuplicateDirective(node.Name, node.Span.Start));
            return;
        }
        foreach (var entry in ((N.IAgnosticParent)node).GetChildOfType<N.Directive>())
        {
            if (entry.Name != "entry")
            {
                var line = node.Span.Start;
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidChild(entry.Name, "ia", "entry", line));
                continue;

            }

            if (!entry.Options.ContainsKey("url"))
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidIAEntry("IA entry directives must include the :url: option", node.Span.Start));
                continue;

            }

            string? url;
            string? slug;

            var parsed = new Uri(entry.Options.GetAsStringOrDefault("url", ""));
            if (parsed.Scheme.Length > 0)
            {
                url = entry.Options.GetAsStringOrNull("url");
                slug = null;
            }
            else
            {
                url = null;
                slug = entry.Options.GetAsStringOrNull("url");
            }

            if (!string.IsNullOrEmpty(slug) && Context.Get<HeadingHandler>().GetTitle(Util.CleanSlug(slug)).Count == 0)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new MissingTocTreeEntry(slug, node.Span.Start));
                continue;

            }
            var title = new List<N.InlineNode> { };
            if (entry.Argument.Count > 0)
            {
                title = entry.Argument;
            }
            else
            {
                if (!string.IsNullOrEmpty(slug))
                {
                    var maybeTitle = Context.Get<HeadingHandler>().GetTitle(Util.CleanSlug(slug));
                    title = maybeTitle switch
                    {
                        null => new List<N.InlineNode>(),
                        _ => maybeTitle.ToList()
                    };
                }
            }

            var project_name = entry.Options.GetAsStringOrNull("project-name");
            if (!string.IsNullOrEmpty(project_name) && string.IsNullOrEmpty(url))
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidIAEntry("IA entry directives with :project-name: option must include :url: option", node.Span.Start));
                continue;

            }
            if (!string.IsNullOrEmpty(url) && title.Count == 0)
            {
                Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidIAEntry("IA entries to external URLs must include titles", node.Span.Start));
                continue;

            }
            _ia.Add(new IAHandler.IAData(title, url, slug, project_name,
                string.IsNullOrEmpty(project_name) ? Util.ParseBool(entry.Options.GetAsStringOrDefault("primary", "false")) : null));
        }
    }

    public override void EnterPage(FileIdStack fileIdStack, Page page)
    {
        _ia = new List<IAData>();

    }
    public override void ExitPage(FileIdStack fileIdStack, Page page)
    {
        if (_ia.Count == 0)
        {
            return;
        }

        if (page.Ast is N.Root rootNode)
        {
            rootNode.Options["ia"] = new JsonArray((from entry in _ia select JsonValue.Create(entry)).ToArray());
        }
    }
}

class SubstitutionHandler : Handler
{
    Dictionary<string, List<N.InlineNode>> substitution_definitions = new Dictionary<string, List<N.InlineNode>>();
    Stack<Dictionary<string, List<N.Node>>> include_replacement_definitions = new Stack<Dictionary<string, List<N.Node>>>();
    List<(N.ISubstitutionReference, FileId, int)> unreplaced_nodes = new List<(N.ISubstitutionReference, FileId, int)>();
    HashSet<string>? seen_definitions;

    ProjectConfig _projectConfig;

    public SubstitutionHandler(Context context) : base(context)
    {
        _projectConfig = Context.Get<ProjectConfig>();
    }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.Directive directiveNode)
        {
            if (directiveNode.Name != "include" && directiveNode.Name != "sharedinclude")
            {
                return;
            }
            var definitions = new Dictionary<string, List<N.Node>>();
            include_replacement_definitions.Push(definitions);
            foreach (var replacement_directive in ((N.IAgnosticParent)directiveNode).GetChildOfType<N.Directive>())
            {
                if (replacement_directive.Name != "replacement")
                {
                    continue;
                }
                var arg = string.Concat(replacement_directive.Argument.Select(x => x.GetText())).Trim();
                definitions[arg] = replacement_directive.Children;
            }
        }
        else
        {
            if (node is N.SubstitutionDefinition substitutionDefinitionNode)
            {
                substitution_definitions[substitutionDefinitionNode.Name] = substitutionDefinitionNode.Children;
                seen_definitions = new HashSet<string>();
            }
            else
            {
                if (node is N.SubstitutionReference substitutionReferenceNode)
                {
                    var inline_substitution = search_inline(substitutionReferenceNode, fileIdStack);
                    if (inline_substitution != null)
                    {
                        substitutionReferenceNode.Children = inline_substitution;
                    }
                    else
                    {
                        unreplaced_nodes.Add((substitutionReferenceNode, fileIdStack.Current, node.Span.Start));
                    }

                    if (seen_definitions is not null)
                    {
                        seen_definitions.Add(substitutionReferenceNode.Name);
                    }

                }
                else
                {
                    if (node is N.BlockSubstitutionReference blockSubstitutionReferenceNode)
                    {
                        var block_substitution = search_block(blockSubstitutionReferenceNode, fileIdStack);
                        if (block_substitution is not null)
                        {
                            blockSubstitutionReferenceNode.Children = block_substitution.ToList();
                        }
                        else
                        {
                            unreplaced_nodes.Add((blockSubstitutionReferenceNode, fileIdStack.Current, node.Span.Start));
                        }

                        if (seen_definitions is not null)
                        {
                            seen_definitions.Add(blockSubstitutionReferenceNode.Name);
                        }
                    }
                }
            }
        }
    }
    public override void ExitNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.SubstitutionDefinition)
        {
            seen_definitions = null;
        }
        else
        {
            if (node is N.Directive directiveNode)
            {
                if (directiveNode.Name != "include" || directiveNode.Name != "sharedinclude")
                {
                    return;
                }
                include_replacement_definitions.Pop();
            }
        }
    }
    public override void ExitPage(FileIdStack fileIdStack, Page page)
    {
        foreach (var (node, fileid, line) in unreplaced_nodes)
        {
            var substitution = substitution_definitions.GetValueOrDefault(node.Name);
            if (substitution != null)
            {
                ((dynamic)node).Children = substitution; // XXX Take a closer look at this
            }
            else
            {
                Context.Diagnostics[fileid].Add(new SubstitutionRefError($"Substitution reference could not be replaced: \"|{node.Name}|\"", line));
            }
        }

        substitution_definitions = new Dictionary<string, List<N.InlineNode>>();
        include_replacement_definitions = new Stack<Dictionary<string, List<N.Node>>>();
        unreplaced_nodes = new List<(N.ISubstitutionReference, FileId, int)>();
    }
    List<N.InlineNode>? search_inline(N.SubstitutionReference node, FileIdStack fileIdStack)
    {
        var result = _search(node, fileIdStack);
        if (result is null)
        {
            return null;
        }

        var substitution = PostprocessorUtils.ExtractInline(result);
        if (substitution is null || substitution.Count == 0 || substitution.Count != result.Count)
        {
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new InvalidContextError(node.Name, node.Span.Start));
            return null;
        }
        return substitution;

    }

    IReadOnlyList<N.Node>? search_block(N.BlockSubstitutionReference node, FileIdStack fileIdStack)
    {
        var result = _search(node, fileIdStack);
        if (result is null)
        {
            return null;
        }
        var output = new List<N.Node> { };
        var current_paragraph = new List<N.Node> { };
        foreach (var element in result)
        {
            if (element is N.InlineNode)
            {
                current_paragraph.Add(element);
            }
            else
            {
                if (current_paragraph.Count > 0)
                {
                    output.Add(new N.Paragraph(node.Span, current_paragraph));
                    current_paragraph = new List<N.Node> { };
                }
                output.Add(element);
            }


        }
        if (current_paragraph.Count > 0)
        {
            output.Add(new N.Paragraph(node.Span, current_paragraph));
        }
        return output;
    }

    List<N.Node>? _search<T>(T node, FileIdStack fileIdStack) where T : N.ISubstitutionReference, N.INode
    {
        var name = node.Name;
        if (seen_definitions != null && seen_definitions.Contains(name))
        {
            try
            {
                substitution_definitions.Remove(name);
            }
            catch (KeyNotFoundException) { }

            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new SubstitutionRefError($"Circular substitution definition referenced: \"{name}\"", node.Span.Start));
            return null;
        }
        try
        {
            return include_replacement_definitions.Peek()[name].Select(node => node.DeepClone()).ToList();
        }
        catch (KeyNotFoundException) { }

        var substitution = substitution_definitions.GetValueOrDefault(name);
        if (substitution is null)
        {
            substitution = _projectConfig.substitution_nodes[name];
        }
        if (substitution is null)
        {
            return null;
        }
        return substitution.Select(node => node.DeepClone()).ToList();
    }

}
class AddTitlesToLabelTargetsHandler : Handler
{
    List<N.Node> pending_targets = new List<N.Node>();
    public AddTitlesToLabelTargetsHandler(Context context) : base(context) { }

    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (!(node is N.Target || node is N.Section || node is N.TargetIdentifier))
        {
            pending_targets = new List<N.Node>();

        }
        if (node is N.Target targetNode && targetNode.Domain == "std" && targetNode.Name == "label")
        {
            pending_targets.AddRange(targetNode.Children);
        }
        else
        {
            if (node is N.Section sectionNode)
            {
                foreach (var target in pending_targets)
                {
                    var heading = ((N.IAgnosticParent)sectionNode).GetChildOfType<N.Heading>().FirstOrDefault();
                    if (heading != null)
                    {
                        ((N.IParent<N.InlineNode>)target).Children = heading.Children;
                    }
                }
                pending_targets = new List<N.Node> { };
            }
        }
    }
}
class RefsHandler : Handler
{
    Spec _spec;
    public RefsHandler(Context context) : base(context)
    {
        _spec = Spec.Get();

    }
    public override void EnterNode(FileIdStack fileIdStack, N.Node node)
    {
        if (node is N.RefRole refRoleNode)
        {
            enterRefRoleNode(fileIdStack, refRoleNode);
        }
    }
    void enterRefRoleNode(FileIdStack fileIdStack, N.RefRole node)
    {
        var key = $"{node.Domain}:{node.Name}";
        if (key == "std:doc")
        {
            if (node.Children.Count == 0)
            {
                _attach_doc_title(fileIdStack, node);
            }
            return;
        }
        key += $":{node.Target}";

        var targets = Context.Get<TargetDatabase>();

        var target_candidates = targets.Get(key);
        if (target_candidates.Count == 0)
        {
            var line = node.Span.Start;
            var target_dict = _spec.Rstobject;
            var target_key = $"{node.Domain}:{node.Name}";
            var title = node.Target;
            if (target_dict.ContainsKey(target_key) && !string.IsNullOrEmpty(target_dict[target_key].Prefix))
            {
                title = title?.Replace($"{target_dict[target_key].Prefix}.", "");
            }
            var text_node = new N.Text(node.Span, title);
            var injection_candidate = PostprocessorUtils.GetTitleInjectionCandidate<N.InlineParent>(node);
            if (injection_candidate != null)
            {
                injection_candidate.Children = new List<N.InlineNode> { text_node };
            }
            var suggestions = targets.GetSuggestions(key);
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new TargetNotFound(node.Name, node.Target, suggestions, line));
            return;

        }
        if (target_candidates.Count > 1)
        {
            target_candidates = attempt_disambugation(fileIdStack.Root, target_candidates);
        }
        if (target_candidates.Count > 1)
        {
            var line = node.Span.Start;
            var candidate_descriptions = new List<string> { };
            foreach (var candidate in target_candidates)
            {
                candidate_descriptions.Add(candidate switch
                {
                    TargetDatabase.Result.InternalResult internalResult => internalResult.Result.Item1,
                    TargetDatabase.Result.ExternalResult externalResult => externalResult.Url,
                });
            }
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new AmbiguousTarget(node.Name, node.Target, candidate_descriptions, line));

        }

        var result = (TargetDatabase.IResult)target_candidates[-1];
        node.Target = result.CanonicalTargetName;

        if (result is TargetDatabase.Result.InternalResult internalResultValue)
        {
            node.FileId = internalResultValue.Result;
        }
        else if (result is TargetDatabase.Result.ExternalResult externalResultValue)
        {
            node.Url = externalResultValue.Url;
        }
        {
            var injection_candidate = PostprocessorUtils.GetTitleInjectionCandidate<N.InlineParent>(node);
            if (injection_candidate != null)
            {
                var cloned_title_nodes = result.Title.Select(node => (N.InlineNode)node.DeepClone()).ToList();
                foreach (var title_node in cloned_title_nodes)
                {
                    node.DeepCopyPositionTo(title_node);
                }
                if (node.Flag is not null && node.Flag.Contains('~') && cloned_title_nodes.Count > 0)
                {
                    var node_to_abbreviate = cloned_title_nodes[0];
                    if (node_to_abbreviate is N.Text textNode)
                    {
                        var index = textNode.Value.LastIndexOf('.');
                        var new_value = textNode.Value[(index + 1)..].Trim();
                        if (new_value is not null)
                        {
                            textNode.Value = new_value;
                        }
                    }
                }
                injection_candidate.Children = cloned_title_nodes;

                if (node.Children.Count == 0)
                {
                    var line = node.Span.Start;
                    Context.Diagnostics[fileIdStack.Current].Append(
                        new ChildlessRef(node.Target, line)
                    );
                }
            }
        }
    }
    List<TargetDatabase.Result> attempt_disambugation(FileId fileid, IEnumerable<TargetDatabase.Result> candidates)
    {
        var local_candidates = new List<TargetDatabase.Result.InternalResult>();
        foreach (var candidate in candidates)
        {
            if (candidate is TargetDatabase.Result.InternalResult internalResult)
            {
                local_candidates.Add(internalResult);
            }
        }
        if (local_candidates.Count == 1)
        {
            return new List<TargetDatabase.Result> { local_candidates[0] };
        }
        var current_fileid_candidates = (from candidate in local_candidates where candidate.Result.Item1 == fileid.WithoutKnownSuffix() select candidate).ToList();
        if (current_fileid_candidates.Count == 1)
        {
            return new List<TargetDatabase.Result> { current_fileid_candidates[0] };
        }
        return candidates.ToList();

    }
    void _attach_doc_title(FileIdStack fileIdStack, N.RefRole node)
    {
        string? target_fileid = node.FileId?.Item1;
        if (target_fileid is null)
        {
            var line = node.Span.Start;
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new ExpectedPathArg(node.Name, line));
            return;
        }
        var (relative, _) = Util.RerootPath(target_fileid, fileIdStack.Root.AsPosix(), Context.Get<ProjectConfig>().SourcePath);
        var slug = Util.CleanSlug(relative.AsPosix());
        var title = Context.Get<HeadingHandler>().GetTitle(slug);
        if (title is null)
        {
            var line = node.Span.Start;
            Context.Diagnostics.GetOrAdd(fileIdStack.Current).Add(new UnnamedPage(target_fileid, line));
            return;

        }
        node.Children = title.ToList();

    }

}
record PostprocessorResult
{
    [JsonPropertyName("pages")]
    public List<Page> Pages { get; init; }

    [JsonPropertyName("metadata")]
    public object Metadata { get; init; }

    [JsonPropertyName("diagnostics")]
    public Dictionary<string, List<Diagnostic>> Diagnostics { get; init; }

    [JsonPropertyName("targets")]
    public TargetDatabase Targets { get; init; }

    public PostprocessorResult(List<Page> pages, object metadata, Dictionary<string, List<Diagnostic>> diagnostics, TargetDatabase targets)
    {
        Pages = pages;
        Metadata = metadata;
        Diagnostics = diagnostics;
        Targets = targets;
    }
}

// Dictionary<string, Union<(string, bytes>)> build_manpages(Context context) {
//     config = context[ProjectConfig];
//     result = new Dictionary<string, dynamic> {};
//     manpages = new List<dynamic> {};
//     foreach (var (name, definition) in config.manpages.items()) {
//             fileid = FileId(definition.file);
//         manpage_page = context.pages.get(fileid);
//         if (!manpage_page) {
//                     context.diagnostics[FileId(config.config_path.relative_to(config.root))].append(CannotOpenFile(PurePath(fileid), "Page not found", 0));
//             continue;

//         }
//         foreach (var (filename, rendered) in man.render(manpage_page, name, definition.title, definition.section).items()) {
//                     manpages.append((filename.as_posix(), rendered));
//             result[filename.as_posix()] = rendered;

//         }

//     }
//     if (manpages && config.bundle.manpages) {
//             try {
//                     result[config.bundle.manpages] = bundle(PurePath(config.bundle.manpages), manpages);
//                     } catch(ValueError) {            context.diagnostics[FileId(config.config_path.relative_to(config.root))].append(UnsupportedFormat(config.bundle.manpages, (".tar", ".tar.gz"), 0));
//                     }


//     }
//     return result;

// }
class Postprocessor
{
    static readonly IReadOnlyList<IReadOnlyList<Type>> PASSES = new List<List<Type>> {
        new List<Type> {typeof(IncludeHandler)},
        new List<Type> {typeof(SubstitutionHandler)},
        new List<Type> {typeof(HeadingHandler), typeof(AddTitlesToLabelTargetsHandler), typeof(ProgramOptionHandler), typeof(TabsSelectorHandler), typeof(ContentsHandler), typeof(BannerHandler), typeof(GuidesHandler), typeof(OpenAPIHandler)},
        new List<Type> {typeof(TargetHandler), typeof(IAHandler), typeof(NamedReferenceHandlerPass1)},
        new List<Type> {typeof(RefsHandler), typeof(NamedReferenceHandlerPass2)}};

    ProjectConfig _project_config;
    Dictionary<string, dynamic> _toctree = new Dictionary<string, dynamic>();
    Dictionary<FileId, Page> _pages = new Dictionary<FileId, Page>();
    TargetDatabase _targets;
    CancellationToken _cancellationToken;

    public Postprocessor(ProjectConfig project_config, TargetDatabase targets, CancellationToken cancellationToken)
    {
        _project_config = project_config;
        _targets = targets;
        _cancellationToken = cancellationToken;
    }

    public PostprocessorResult Run(Dictionary<FileId, Page> pages)
    {
        if (pages.Count == 0)
        {
            return new PostprocessorResult(new List<Page> { }, new Dictionary<string, object> { }, new Dictionary<string, List<Diagnostic>>(), _targets);
        }
        _pages = pages;
        var context = new Context(pages);
        context.Add(_project_config);
        context.Add(_targets);
        foreach (var project_pass in PASSES)
        {
            var instances = (from ty in project_pass select (Handler)Activator.CreateInstance(ty, new object[] { context })!).ToList();
            foreach (var instance in instances)
            {
                context.Add(instance);
            }

            run_event_parser(instances);

        }
        var document = generate_metadata(context);
        finalize(context, document);

        Console.Error.WriteLine($"{context.Diagnostics.Count}");
        foreach (var diagnostic in context.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic);
        }

        var diagnostics = new Dictionary<string, List<Diagnostic>>();
        foreach (var pair in context.Diagnostics)
        {
            diagnostics[pair.Key.ToString()] = pair.Value;
        }
        return new PostprocessorResult(pages.Where(pair => pair.Key.GetSuffix() == ".txt").Select(pair => pair.Value).ToList(), document, diagnostics, _targets);
    }
    protected void finalize(Context context, object metadata) { }
    object generate_metadata(Context context)
    {
        var project_config = context.Get<ProjectConfig>();
        var document = new Dictionary<string, dynamic>();
        // document["title"] = project_config.Title;
        //         document["eol"] = ;
        //         if (project_config.deprecated_versions) {
        //                     document["deprecated_versions"] = project_config.deprecated_versions;

        //         }
        //         if (project_config.associated_products) {
        //                     document["associated_products"] = project_config.associated_products;

        //         }
        //         document["slugToTitle"] = ;
        //         iatree = cls.build_iatree(context);
        //         toctree = cls.build_toctree(context);
        //         if (iatree && toctree.get("children")) {
        //                     context.diagnostics[FileId("index.txt")].append(InvalidTocTree(0));

        //         }
        //         tree = iatree || toctree;
        //         document.update(new Dictionary<string, dynamic> {{"toctree", toctree}, {"toctreeOrder", cls.toctree_order(tree)}, {"parentPaths", cls.breadcrumbs(tree)}});
        //         if (iatree) {
        //                     document["iatree"] = iatree;

        //         }
        //         context[GuidesHandler].add_guides_metadata(document);
        //         openapi_pages_metadata = context[OpenAPIHandler].get_metadata();
        //         if (len(openapi_pages_metadata) > 0) {
        //                     document["openapi_pages"] = openapi_pages_metadata;

        //         }
        //         manpages = build_manpages(context);
        //         document["static_files"] = manpages;
        return document;
    }
    void run_event_parser(IEnumerable<Handler> handlers)
    {
        var event_parser = new EventParser(_cancellationToken);
        foreach (var handler in handlers)
        {
            event_parser.AddHandler(handler);
        }

        var foobar = _pages.Where(pair => pair.Key.GetSuffix() == ".txt").Select(pair => (pair.Key, pair.Value)).ToList();

        event_parser.Consume(foobar);
    }
    //     Dictionary<string, SerializableType> build_iatree(Context context) {
    //             Page? _get_page_from_slug(Page current_page, string slug) {
    //                     (relative, _) = util.reroot_path(FileId(slug), current_page.source_path, context[ProjectConfig].source_path);
    //             try {
    //                             fileid_with_ext = context[IncludeHandler].slug_fileid_mapping[relative.as_posix()];
    //                             } catch(KeyError) {                return null;
    //                             }

    //             return context.pages.get(fileid_with_ext);

    //         }
    //         void iterate_ia(Page page, Dictionary<string, SerializableType> result) {
    //                     if (!page.ast is N.Root) {
    //                             return;

    //             }
    //             ia = page.ast.options.get("ia");
    //             if (!ia is List) {
    //                             return;

    //             }
    //             foreach (var entry in ia) {
    //                             curr = new Dictionary<string, dynamic> {{null, entry}, {"children", new List<dynamic> {}}};
    //                 if (result["children"] is List) {
    //                                     result["children"].append(curr);

    //                 }
    //                 slug = curr.get("slug");
    //                 if (slug is String) {
    //                                     child = _get_page_from_slug(page, slug);
    //                     if (child) {
    //                                             iterate_ia(child, curr);

    //                     }

    //                 }

    //             }

    //         }
    //         starting_page = context.pages.get(FileId("index.txt"));
    //         if (!starting_page) {
    //                     return new Dictionary<string, dynamic> {};

    //         }
    //         if (!starting_page.ast is N.Root) {
    //                     return new Dictionary<string, dynamic> {};

    //         }
    //         if ("ia" not in starting_page.ast.options) {
    //                     return new Dictionary<string, dynamic> {};

    //         }
    //         title = context[HeadingHandler].get_title("index") || new List<dynamic> {N.Text(ValueTuple.Create(0), context[ProjectConfig].title)};
    //         root = new Dictionary<string, dynamic> {{"title", (from node in title select node).ToList()}, {"slug", "/"}, {"children", new List<dynamic> {}}};
    //         iterate_ia(starting_page, root);
    //         return root;

    //     }
    // Dictionary<string, object> build_toctree(Context context) {
    //     var candidates = (new FileId("contents.txt"), new FileId("index.txt"));
    //     starting_fileid = next(, null);
    //     if (starting_fileid is null) {
    //                 return new Dictionary<string, dynamic> {};

    //     }
    //     var root = new Dictionary<string, dynamic> {{"title", new List<dynamic> {new N.Text(ValueTuple.Create(0), context.Get<ProjectConfig>().title).serialize()}}, {"slug", "/"}, {"children", new List<dynamic> {}}};
    //     var ast = context.pages[starting_fileid].ast;
    //     toc_landing_pages = (from slug in context[ProjectConfig].toc_landing_pages select slug).ToList();
    //     find_toctree_nodes(context, starting_fileid, ast, root, toc_landing_pages, new Set<dynamic> {starting_fileid});
    //     return root;
    // }
    //     void find_toctree_nodes(dynamic cls, Context context, FileId fileid, N.Node ast, Dictionary<string, dynamic> node, List<string> toc_landing_pages, Set<FileId> visited_file_ids) {
    //             if (!ast is N.Parent) {
    //                     return;

    //         }
    //         if (ast is N.TocTreeDirective) {
    //                     foreach (var entry in ast.entries) {
    //                             toctree_node = new Dictionary<string, dynamic> {};
    //                 if (entry.url) {
    //                                     toctree_node = new Dictionary<string, dynamic> {{"title", }, {"url", entry.url}, {"children", new List<dynamic> {}}};

    //                 } else {
    //                                     if (entry.slug) {
    //                                             slug_cleaned = CleanSlug(entry.slug);
    //                         try {
    //                                                     slug_fileid = context[IncludeHandler].slug_fileid_mapping[slug_cleaned];
    //                                                     } catch(KeyError) {                            context.diagnostics[fileid].append(MissingTocTreeEntry(slug_cleaned, ast.span[0]));
    //                             continue;
    //                                                     }

    //                         slug = slug_fileid.WithoutKnownSuffix();
    //                         if (entry.title) {
    //                                                     title = new List<dynamic> {N.Text(ValueTuple.Create(0), entry.title).serialize()};

    //                         } else {
    //                                                     title_nodes = context[HeadingHandler].get_title(slug);
    //                             title = ;
    //                                                     }

    //                         toctree_node_options = new Dictionary<string, dynamic> {{"drawer", slug not in toc_landing_pages}};
    //                         if (context.pages[FileId(slug_fileid)].ast.options) {
    //                                                     if ("tocicon" in context.pages[FileId(slug_fileid)].ast.options) {
    //                                                             toctree_node_options["tocicon"] = context.pages[FileId(slug_fileid)].ast.options["tocicon"];

    //                             }

    //                         }
    //                         toctree_node = new Dictionary<string, dynamic> {{"title", title}, {"slug", }, {"children", new List<dynamic> {}}, {"options", toctree_node_options}};
    //                         if (slug_fileid not in visited_file_ids) {
    //                                                     new_ast = context.pages[slug_fileid].ast;
    //                             cls.find_toctree_nodes(context, slug_fileid, new_ast, toctree_node, toc_landing_pages, visited_file_ids.union(new Set<dynamic> {slug_fileid}));

    //                         }

    //                     }
    //                                     }

    //                 if (toctree_node) {
    //                                     node["children"].append(toctree_node);

    //                 }

    //             }

    //         }
    //         foreach (var child_ast in ast.children) {
    //                     cls.find_toctree_nodes(context, fileid, child_ast, node, toc_landing_pages, visited_file_ids);

    //         }

    //     }
    //     Dictionary<string, List<string>> breadcrumbs(Dictionary<string, SerializableType> tree) {
    //             page_dict = new Dictionary<string, dynamic> {};
    //         all_paths = new List<dynamic> {};
    //         if ("children" in tree) {
    //                     Debug.Assert(tree["children"] is List);
    //             foreach (var node in tree["children"]) {
    //                             paths = new List<dynamic> {};
    //                 get_paths(node, new List<dynamic> {}, paths);
    //                 all_paths.extend(paths);

    //             }

    //         }
    //         foreach (var path in all_paths) {
    //                     foreach (var i in range(len(path))) {
    //                             slug = path[i];
    //                 page_dict[slug] = path["null:i:"];

    //             }

    //         }
    //         return page_dict;

    //     }
    //     List<string> toctree_order(Dictionary<string, SerializableType> tree) {
    //             order = new List<dynamic> {};
    //         pre_order(tree, order);
    //         return order;

    //     }

    // }
    void pre_order(Dictionary<string, dynamic> node, List<string> order)
    {
        if (node.Count == 0)
        {
            return;
        }
        if (node.ContainsKey("slug"))
        {
            order.Add(node["slug"]);
        }
        if (node.ContainsKey("children"))
        {
            foreach (var child in node["children"])
            {
                pre_order(child, order);
            }
        }
    }
}
// void get_paths(Dictionary<string, dynamic> node, List<string> path, List<dynamic> all_paths) {
//     if (!node) {
//             return;

//     }
//     if (node.get("children") is null || len(node["children"]) == 0) {
//             if ("slug" in node) {
//                     path.append(CleanSlug(node["slug"]));
//             all_paths.append(path);

//         } else {
//                     if ("project_name" in node && node.get("primary")) {
//                             path.append(node["project_name"]);
//                 all_paths.append(path);

//             }
//                     }


//     } else {
//             foreach (var child in node["children"]) {
//                     subpath = path["null::"];
//             subpath.append(CleanSlug(node["slug"]));
//             get_paths(child, subpath, all_paths);

//         }
//             }


// }

// }
