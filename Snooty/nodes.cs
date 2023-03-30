using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace N;

public interface IToJsonDictionary
{
    public Dictionary<string, object> ToJsonDictionary();
}

public enum ListEnumType
{
    Unordered,
    Arabic,
    LowerAlpha,
    UpperAlpha,
    LowerRoman,
    UpperRoman
}

public class ListEnumTypeConverter : JsonConverter<ListEnumType>
{
    public override ListEnumType Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        return reader.GetString() switch
        {
            "unordered" => ListEnumType.Unordered,
            "arabic" => ListEnumType.Arabic,
            "loweralpha" => ListEnumType.LowerAlpha,
            "upperalpha" => ListEnumType.UpperAlpha,
            "lowerroman" => ListEnumType.LowerRoman,
            "upperroman" => ListEnumType.UpperRoman,
            _ => throw new JsonException()
        };
    }

    public override void Write(
        Utf8JsonWriter writer, ListEnumType listType, JsonSerializerOptions options)
    {
        writer.WriteStringValue(listType.ToString().ToLower());
    }
}

public struct Span
{
    [JsonInclude]
    [JsonPropertyName("start")]
    public int Start;

    [JsonConstructorAttribute]
    public Span(int start)
    {
        Start = start;
    }
}

public interface INode
{
    Span Span { get; }

    string GetText();
    void Verify();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization, IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(Code), "code")]
[JsonDerivedType(typeof(Comment), "comment")]
[JsonDerivedType(typeof(Label), "label")]
[JsonDerivedType(typeof(Section), "section")]
[JsonDerivedType(typeof(Paragraph), "paragraph")]
[JsonDerivedType(typeof(Footnote), "footnote")]
[JsonDerivedType(typeof(FootnoteReference), "footnote_reference")]
[JsonDerivedType(typeof(SubstitutionDefinition), "substitution_definition")]
[JsonDerivedType(typeof(SubstitutionReference), "substitution_reference")]
[JsonDerivedType(typeof(BlockSubstitutionReference), "block_substitution_reference")]
[JsonDerivedType(typeof(Root), "root")]
[JsonDerivedType(typeof(Heading), "heading")]
[JsonDerivedType(typeof(DefinitionListItem), "definitionListItem")]
[JsonDerivedType(typeof(DefinitionList), "definitionList")]
[JsonDerivedType(typeof(ListNodeItem), "listItem")]
[JsonDerivedType(typeof(ListElement), "list")]
[JsonDerivedType(typeof(Line), "line")]
[JsonDerivedType(typeof(LineBlock), "line_block")]
[JsonDerivedType(typeof(Directive), "directive")]
[JsonDerivedType(typeof(TocTreeDirective), "toctree_directive")]
[JsonDerivedType(typeof(DirectiveArgument), "directive_argument")]
[JsonDerivedType(typeof(Target), "target")]
[JsonDerivedType(typeof(TargetIdentifier), "target_identifier")]
[JsonDerivedType(typeof(InlineTarget), "inline_target")]
[JsonDerivedType(typeof(Reference), "reference")]
[JsonDerivedType(typeof(NamedReference), "named_reference")]
[JsonDerivedType(typeof(Role), "role")]
[JsonDerivedType(typeof(RefRole), "ref_role")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(Literal), "literal")]
[JsonDerivedType(typeof(Emphasis), "emphasis")]
[JsonDerivedType(typeof(Field), "field")]
[JsonDerivedType(typeof(FieldList), "field_list")]
[JsonDerivedType(typeof(Strong), "strong")]
[JsonDerivedType(typeof(Transition), "transition")]
public abstract class Node : INode, IToJsonDictionary
{
    [JsonPropertyOrder(-2)]
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyOrder(-1)]
    [JsonPropertyName("span")]
    public Span Span { get; set; }

    public Node()
    {
        Span = new Span(0);
    }

    public Node(Span span)
    {
        Span = span;
    }

    /// Return pure textual content from a given AST node. Most nodes will return an empty string.
    public virtual string GetText()
    {
        return "";
    }

    /// Perform optional validations on this node.
    public virtual void Verify()
    {

    }

    public virtual Node DeepClone()
    {
        return (Node)MemberwiseClone();
    }

    public Dictionary<string, object> ToJsonDictionary()
    {
        var result = new Dictionary<string, dynamic> {
            {"type", Type},
            {"position", new Dictionary<string, object> {{"start", new Dictionary<string, object> {{"line", Span.Start}}}}}
        };

        var selfType = GetType();
        BindingFlags bindingAttr = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;

        foreach (var propInfo in selfType.GetProperties(bindingAttr))
        {
            var value = propInfo.GetValue(this);

            // Fields with None values are excluded
            if (value is null) { continue; }

            Type valueType = value.GetType();
            if (value is Node nodeValue)
            {
                // Serialize nodes
                result[propInfo.Name] = nodeValue.ToJsonDictionary();
            }
            else if (value is string || valueType.IsPrimitive)
            {
                // Primitive value: include verbatim
                result[propInfo.Name] = value;
            }
            else if (value is System.Collections.IDictionary d)
            {
                // We exclude empty dicts, since they're mainly used for directive options and other such things.
                if (d.Count > 0)
                {
                    result[propInfo.Name] = value;
                }
            }
            else if (value is Enum e)
            {
                result[propInfo.Name] = e.ToString();
            }
            else if (value is System.Collections.IList l)
            {
                // This is a bit unsafe, but it's the most expedient option right now. If the child
                // has a serialize() method, call that; otherwise, include it as-is.
                var serializedList = new List<object>();
                for (int i = 0; i < l.Count; i += 1)
                {
                    var obj = l[i]!;
                    if (obj is IToJsonDictionary toDictionaryObj)
                    {
                        serializedList.Add(toDictionaryObj.ToJsonDictionary());
                    }
                    else
                    {
                        serializedList.Add(obj);
                    }
                }
                result[propInfo.Name] = serializedList;
            }
            else if (value is ITuple t)
            {
                // This is a bit unsafe, but it's the most expedient option right now. If the child
                // has a serialize() method, call that; otherwise, include it as-is.
                var serializedList = new List<object>();
                for (int i = 0; i < t.Length; i += 1)
                {
                    var obj = t[i]!;
                    if (obj is IToJsonDictionary toDictionaryObj)
                    {
                        serializedList.Add(toDictionaryObj.ToJsonDictionary());
                    }
                    else
                    {
                        serializedList.Add(obj);
                    }
                }
                result[propInfo.Name] = serializedList;
            }
            else if (value is FileId fileId)
            {
                result[propInfo.Name] = fileId.AsPosix();
            }
            else
            {
                throw new NotImplementedException(propInfo.Name);
            }
        }

        result.Remove("span");
        return result;
    }

    public void DeepCopyPositionTo(Node dest)
    {
        dest.Span = Span;
        if (dest is IParent<Node> parentNode)
        {
            foreach (var child in parentNode.Children)
            {
                DeepCopyPositionTo(child);
            }
        }
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization, IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(FootnoteReference), "footnote_reference")]
[JsonDerivedType(typeof(SubstitutionReference), "substitution_reference")]
[JsonDerivedType(typeof(TargetIdentifier), "target_identifier")]
[JsonDerivedType(typeof(InlineTarget), "inline_target")]
[JsonDerivedType(typeof(Line), "line")]
[JsonDerivedType(typeof(Reference), "reference")]
[JsonDerivedType(typeof(NamedReference), "named_reference")]
[JsonDerivedType(typeof(Role), "role")]
[JsonDerivedType(typeof(RefRole), "ref_role")]
[JsonDerivedType(typeof(Text), "text")]
[JsonDerivedType(typeof(Literal), "literal")]
[JsonDerivedType(typeof(Emphasis), "emphasis")]
[JsonDerivedType(typeof(Strong), "strong")]
public abstract class InlineNode : Node
{
    [JsonConstructor]
    public InlineNode(Span span) : base(span) { }
}


public class Code : Node
{
    public static string ClassType = "code";

    [JsonPropertyName("type")]
    public override string Type { get => Code.ClassType; }

    [JsonInclude]
    [JsonPropertyName("lang")]
    public string? Lang;

    [JsonInclude]
    [JsonPropertyName("caption")]
    public string? Caption;

    [JsonInclude]
    [JsonPropertyName("copyable")]
    public bool Copyable;

    [JsonInclude]
    [JsonPropertyName("emphasize_lines")]
    public List<(int, int)>? EmphasizeLines;

    [JsonInclude]
    [JsonPropertyName("value")]
    public string Value;

    [JsonInclude]
    [JsonPropertyName("linenos")]
    public bool Linenos;

    [JsonInclude]
    [JsonPropertyName("lineno_start")]
    public int? LinenoStart;

    public Code(Span span, string? lang, string value) : base(span)
    {
        Lang = lang;
        Value = value;
    }

    [JsonConstructorAttribute]
    public Code(Span span, string? lang, string? caption, bool copyable, string value, bool linenos, int? linenoStart) : base(span)
    {
        Lang = lang;
        Caption = caption;
        Copyable = copyable;
        Value = value;
        Linenos = linenos;
        LinenoStart = linenoStart;
    }
}

public interface IParent<N> where N : Node
{
    public List<N> Children { get; set; }

    public string GetText()
    {
        return string.Concat(Children.Select(child => child.GetText()));
    }
}

public interface IAgnosticParent
{
    public IReadOnlyList<Node> GetChildrenAgnostically();

    /// Return the first immediate child node with a given type, or None.
    public IEnumerable<T> GetChildOfType<T>() where T : N.Node
    {
        foreach (var child in GetChildrenAgnostically())
        {
            if (typeof(T).IsInstanceOfType(child))
            {
                yield return (T)child;
            }
        }
    }
}


[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization, IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(LineBlock), "line_block")]
[JsonDerivedType(typeof(Target), "target")]
public abstract class BlockParent<N> : Node, IParent<N>, IAgnosticParent where N : Node
{
    [JsonInclude]
    [JsonPropertyName("children")]
    public List<N> Children { get; set; } = new List<N>();

    public BlockParent(Span span) : base(span) { }
    public BlockParent(Span span, List<N> children) : base(span)
    {
        Children = children;
    }

    public IReadOnlyList<Node> GetChildrenAgnostically()
    {
        return Children;
    }

    public override Node DeepClone()
    {
        var clone = (BlockParent<N>)MemberwiseClone();
        clone.Children = Children.Select(child => (N)child.DeepClone()).ToList();
        return clone;
    }
}

public abstract class BlockParentOfInlineNodes : Node, IParent<InlineNode>, IAgnosticParent
{
    [JsonInclude]
    [JsonPropertyName("children")]
    public List<InlineNode> Children { get; set; } = new List<InlineNode>();

    public BlockParentOfInlineNodes(Span span) : base(span) { }
    public BlockParentOfInlineNodes(Span span, List<InlineNode> children) : base(span)
    {
        Children = children;
    }

    public override void Verify()
    {
        base.Verify();
        foreach (var child in Children)
        {
            Debug.Assert(child is InlineNode);
        }
    }

    public IReadOnlyList<Node> GetChildrenAgnostically()
    {
        return Children;
    }

    public override Node DeepClone()
    {
        var clone = (BlockParentOfInlineNodes)MemberwiseClone();
        clone.Children = Children.Select(child => (InlineNode)child.DeepClone()).ToList();
        return clone;
    }
}

public abstract class InlineParent : InlineNode, IParent<InlineNode>, IAgnosticParent
{
    [JsonInclude]
    [JsonPropertyName("children")]
    public List<InlineNode> Children { get; set; } = new List<InlineNode>();

    public InlineParent(Span span) : base(span) { }

    public override void Verify()
    {
        base.Verify();
        foreach (var child in Children)
        {
            Debug.Assert(child is InlineNode);
        }
    }

    public IReadOnlyList<Node> GetChildrenAgnostically()
    {
        return Children;
    }

    public override Node DeepClone()
    {
        var clone = (InlineParent)MemberwiseClone();
        clone.Children = Children.Select(child => (InlineNode)child.DeepClone()).ToList();
        return clone;
    }
}


public class Comment : BlockParent<Node>
{
    public static string ClassType = "comment";

    [JsonPropertyName("type")]
    public override string Type { get => Comment.ClassType; }

    public Comment(Span span) : base(span) { }
}



public class Label : BlockParent<Node>
{
    public static string ClassType = "label";

    [JsonPropertyName("type")]
    public override string Type { get => Label.ClassType; }

    public Label(Span span) : base(span) { }
    public Label(Span span, List<N.Node> children) : base(span, children) { }
}



public class Section : BlockParent<Node>
{
    public static string ClassType = "section";

    [JsonPropertyName("type")]
    public override string Type { get => Section.ClassType; }

    public Section(Span span) : base(span) { }

    [JsonConstructorAttribute]
    public Section(Span span, List<N.Node> children) : base(span, children) { }
}



public class Paragraph : BlockParent<Node>
{
    public static string ClassType = "paragraph";

    [JsonPropertyName("type")]
    public override string Type { get => Paragraph.ClassType; }

    [JsonConstructorAttribute]
    public Paragraph(Span span, List<Node> children) : base(span)
    {
        Children = children;
    }
}



public class Footnote : BlockParent<Node>
{
    public static string ClassType = "footnote";

    [JsonPropertyName("type")]
    public override string Type { get => Footnote.ClassType; }

    [JsonInclude]
    [JsonPropertyName("id")]
    public string? Id;

    [JsonInclude]
    [JsonPropertyName("name")]
    public string? Name;

    public Footnote(Span span, string? id, string? name) : base(span)
    {
        Id = id;
        Name = name;
    }

    [JsonConstructorAttribute]
    public Footnote(Span span, string? id, string? name, List<Node> children) : base(span, children)
    {
        Id = id;
        Name = name;
    }
}



public class FootnoteReference : InlineParent
{
    public static string ClassType = "footnote_reference";

    [JsonPropertyName("type")]
    public override string Type { get => FootnoteReference.ClassType; }

    [JsonInclude]
    [JsonPropertyName("id")]
    public string Id;

    [JsonInclude]
    [JsonPropertyName("ref_name")]
    public string RefName;

    public FootnoteReference(Span span, string id, string refname) : base(span)
    {
        Id = id;
        RefName = refname;
    }
}



public class SubstitutionDefinition : BlockParentOfInlineNodes
{
    public static string ClassType = "substitution_definition";

    [JsonPropertyName("type")]
    public override string Type { get => SubstitutionDefinition.ClassType; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string? Name;

    public SubstitutionDefinition(Span span) : base(span) { }
}

public interface ISubstitutionReference
{
    public string Name { get; }
}

public class SubstitutionReference : InlineParent, ISubstitutionReference
{
    public static string ClassType = "substitution_reference";

    [JsonPropertyName("type")]
    public override string Type { get => SubstitutionReference.ClassType; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    public SubstitutionReference(Span span, string name) : base(span)
    {
        Name = name;
    }
}



public class BlockSubstitutionReference : BlockParent<Node>, ISubstitutionReference
{
    public static string ClassType = "block_substitution_reference";

    [JsonPropertyName("type")]
    public override string Type { get => BlockSubstitutionReference.ClassType; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    public BlockSubstitutionReference(Span span, string name) : base(span)
    {
        Name = name;
    }
}



public class Root : BlockParent<Node>
{
    public static string ClassType = "root";

    [JsonPropertyName("type")]
    public override string Type { get { return Root.ClassType; } }

    [JsonInclude]
    [JsonPropertyName("fileid")]
    public FileId FileId;


    [JsonPropertyName("options")]
    public Dictionary<string, JsonNode> Options { get; set; } = new Dictionary<string, JsonNode>();

    public Root(Span span, FileId fileid) : base(span)
    {
        FileId = fileid;
    }

    [JsonConstructorAttribute]
    public Root(Span span, FileId fileid, List<Node> children) : base(span, children)
    {
        FileId = fileid;
    }

    public override Node DeepClone()
    {
        var clone = (Root)MemberwiseClone();
        clone.Children = Children.Select(child => child.DeepClone()).ToList();
        clone.Options = Options.ToDictionary(entry => entry.Key, entry => entry.Value);
        return clone;
    }
}

public class Heading : BlockParentOfInlineNodes
{
    public static string ClassType = "heading";

    [JsonPropertyName("type")]
    public override string Type { get => Heading.ClassType; }

    [JsonInclude]
    [JsonPropertyName("id")]
    public string? Id;

    public Heading(Span span, string? id) : base(span)
    {
        Id = id;
    }

    [JsonConstructorAttribute]
    public Heading(Span span, string? id, List<InlineNode> children) : base(span, children)
    {
        Id = id;
    }
}

public class DefinitionListItem : BlockParent<Node>
{
    public static string ClassType = "definitionListItem";

    [JsonPropertyName("type")]
    public override string Type { get => DefinitionListItem.ClassType; }

    [JsonInclude]
    [JsonPropertyName("term")]
    public List<InlineNode> Term = new List<InlineNode>();

    public DefinitionListItem(Span span) : base(span) { }

    [JsonConstructorAttribute]
    public DefinitionListItem(Span span, List<Node> children, List<InlineNode> term) : base(span, children)
    {
        Term = term;
    }

    public override void Verify()
    {
        base.Verify();
        foreach (var part in Term)
        {
            part.Verify();
        }
    }

    public override Node DeepClone()
    {
        var clone = (DefinitionListItem)MemberwiseClone();
        clone.Children = Children.Select(child => child.DeepClone()).ToList();
        clone.Term = Term.Select(child => (InlineNode)child.DeepClone()).ToList();
        return clone;
    }
}



public class DefinitionList : BlockParent<DefinitionListItem>
{
    public static string ClassType = "definitionList";

    [JsonPropertyName("type")]
    public override string Type { get => DefinitionList.ClassType; }

    public DefinitionList(Span span) : base(span) { }

    [JsonConstructorAttribute]
    public DefinitionList(Span span, List<DefinitionListItem> children) : base(span, children) { }
}



public class ListNodeItem : BlockParent<Node>
{
    public static string ClassType = "listItem";

    [JsonPropertyName("type")]
    public override string Type { get => ListNodeItem.ClassType; }

    public ListNodeItem(Span span) : base(span) { }

    [JsonConstructorAttribute]
    public ListNodeItem(Span span, List<Node> children) : base(span, children) { }
}



public class ListElement : BlockParent<ListNodeItem>
{
    public static string ClassType = "list";

    [JsonPropertyName("type")]
    public override string Type { get => ListElement.ClassType; }

    [JsonInclude]
    [JsonPropertyName("enumtype")]
    [JsonConverter(typeof(ListEnumTypeConverter))]
    public ListEnumType EnumType;

    [JsonInclude]
    [JsonPropertyName("startat")]
    public int? StartAt;

    public ListElement(Span span, ListEnumType enumType) : base(span)
    {
        EnumType = enumType;
    }

    [JsonConstructorAttribute]
    public ListElement(Span span, ListEnumType enumType, List<ListNodeItem> children) : base(span, children)
    {
        EnumType = enumType;
    }
}


public class Line : InlineParent
{
    public static string ClassType = "line";

    [JsonPropertyName("type")]
    public override string Type { get => Line.ClassType; }

    public Line(Span span) : base(span) { }
}



public class LineBlock : BlockParent<Node>
{
    public static string ClassType = "line_block";

    [JsonPropertyName("type")]
    public override string Type { get => LineBlock.ClassType; }

    public LineBlock(Span span) : base(span) { }
}



public class Directive : BlockParent<Node>
{
    public static string ClassType = "directive";

    [JsonPropertyName("type")]
    public override string Type { get => Directive.ClassType; }

    [JsonInclude]
    [JsonPropertyName("domain")]
    public string Domain;

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name;

    [JsonInclude]
    [JsonPropertyName("argument")]
    public List<InlineNode> Argument = new List<InlineNode>();

    [JsonInclude]
    [JsonPropertyName("options")]
    public Dictionary<string, JsonNode> Options { get; set; } = new Dictionary<string, JsonNode>();

    public Directive(Span span, string domain, string name) : base(span)
    {
        Domain = domain;
        Name = name;
    }

    [JsonConstructorAttribute]
    public Directive(Span span, string domain, string name, Dictionary<string, JsonNode>? options, List<InlineNode> argument, List<Node> children) : base(span, children)
    {
        Domain = domain;
        Name = name;

        if (options is null)
        {
            Options = new Dictionary<string, JsonNode>();
        }
        else
        {
            Options = options;
        }
        Argument = argument;
    }

    public override void Verify()
    {
        base.Verify();
        foreach (var arg in Argument)
        {
            arg.Verify();
        }
    }

    public override Node DeepClone()
    {
        var clone = (Directive)MemberwiseClone();
        clone.Children = Children.Select(child => child.DeepClone()).ToList();
        clone.Argument = Argument.Select(child => (InlineNode)child.DeepClone()).ToList();
        clone.Options = Options.ToDictionary(entry => entry.Key, entry => entry.Value);
        return clone;
    }
}


public record TocTreeDirectiveEntry
{
    [JsonInclude]
    [JsonPropertyName("title")]
    public string? Title;

    [JsonInclude]
    [JsonPropertyName("url")]
    public string? Url;

    [JsonInclude]
    [JsonPropertyName("slug")]
    public string? Slug;

    object Serialize()
    {
        var result = new Dictionary<string, string>();
        if (Title != null)
        {
            result["title"] = Title;
        }
        if (Url != null)
        {
            result["url"] = Url;
        }
        if (Slug != null)
        {
            result["slug"] = Slug;
        }
        return result;
    }
}



public class TocTreeDirective : Directive
{
    public static new string ClassType = "toctree_directive";

    [JsonPropertyName("type")]
    public override string Type { get => TocTreeDirective.ClassType; }

    [JsonInclude]
    [JsonPropertyName("entries")]
    public List<TocTreeDirectiveEntry>? Entries;

    public TocTreeDirective(Span span, string domain, string name) : base(span, domain, name) { }


    [JsonConstructorAttribute]
    public TocTreeDirective(Span span, string domain, string name, Dictionary<string, JsonNode> options, List<InlineNode> argument, List<Node> children, List<TocTreeDirectiveEntry> entries) : base(span, domain, name, options, argument, children)
    {
        Entries = entries;
    }


    public override Node DeepClone()
    {
        var clone = (TocTreeDirective)base.DeepClone();
        clone.Entries = (Entries is null) ? null : Entries.ToList();
        return clone;
    }
}



public class DirectiveArgument : BlockParentOfInlineNodes
{
    public static string ClassType = "directive_argument";

    [JsonPropertyName("type")]
    public override string Type { get => DirectiveArgument.ClassType; }

    public DirectiveArgument(Span span) : base(span) { }

    [JsonConstructorAttribute]
    public DirectiveArgument(Span span, List<InlineNode> children) : base(span, children)
    {
    }
}

public interface ITarget : INode
{
    public string Domain { get; set; }
    public string Name { get; set; }
    public string? HtmlId { get; set; }
}

public class Target : BlockParent<Node>, ITarget
{
    public static string ClassType = "target";

    [JsonPropertyName("type")]
    public override string Type { get => Target.ClassType; }

    [JsonInclude]
    [JsonPropertyName("domain")]
    public string Domain { get; set; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("html_id")]
    public string? HtmlId { get; set; }

    public Target(Span span, string domain, string name) : base(span)
    {
        Domain = domain;
        Name = name;
    }
}



public class TargetIdentifier : InlineParent
{
    public static string ClassType = "target_identifier";

    [JsonPropertyName("type")]
    public override string Type { get => TargetIdentifier.ClassType; }

    [JsonInclude]
    [JsonPropertyName("ids")]
    public List<string> Ids = new List<string>();

    public TargetIdentifier(Span span) : base(span) { }

    public override Node DeepClone()
    {
        var clone = (TargetIdentifier)base.DeepClone();
        clone.Ids = Ids.ToList();
        return clone;
    }
}



public class InlineTarget : InlineParent, ITarget
{
    public static string ClassType = "inline_target";

    [JsonPropertyName("type")]
    public override string Type { get => InlineTarget.ClassType; }

    [JsonInclude]
    [JsonPropertyName("domain")]
    public string Domain { get; set; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonInclude]
    [JsonPropertyName("html_id")]
    public string? HtmlId { get; set; }

    public InlineTarget(Span span, string domain, string name) : base(span)
    {
        Domain = domain;
        Name = name;
    }
}



public class Reference : InlineParent
{
    public static string ClassType = "reference";

    [JsonPropertyName("type")]
    public override string Type { get => Reference.ClassType; }

    [JsonInclude]
    [JsonPropertyName("refname")]
    public string RefName;

    [JsonInclude]
    [JsonPropertyName("refuri")]
    public string RefUri;

    public Reference(Span span, string refName, string refUri) : base(span)
    {
        RefName = refName;
        RefUri = refUri;
    }
}



public class NamedReference : InlineNode
{
    public static string ClassType = "named_reference";

    [JsonPropertyName("type")]
    public override string Type { get => NamedReference.ClassType; }

    [JsonInclude]
    [JsonPropertyName("refname")]
    public string RefName;

    [JsonInclude]
    [JsonPropertyName("refuri")]
    public string RefUri;

    public NamedReference(Span span, string refname, string refuri) : base(span)
    {
        RefName = refname;
        RefUri = refuri;
    }
}



public class Role : InlineParent
{
    public static string ClassType = "role";

    [JsonPropertyName("type")]
    public override string Type { get => Role.ClassType; }

    [JsonInclude]
    [JsonPropertyName("domain")]
    public string Domain;

    [JsonInclude]
    [JsonPropertyName("name")]
    public string Name;

    [JsonInclude]
    [JsonPropertyName("target")]
    public string? Target;

    [JsonInclude]
    [JsonPropertyName("flag")]
    public string? Flag;

    public Role(Span span, string domain, string name) : base(span)
    {
        Domain = domain;
        Name = name;
    }
}



public class RefRole : Role
{
    public static new string ClassType = "ref_role";

    [JsonPropertyName("type")]
    public override string Type { get => RefRole.ClassType; }

    [JsonInclude]
    [JsonPropertyName("fileid")]
    public (string, string)? FileId;

    [JsonInclude]
    [JsonPropertyName("url")]
    public string? Url;

    public RefRole(Span span, string domain, string name) : base(span, domain, name) { }

    public override void Verify()
    {
        Debug.Assert((
            FileId != null || Url != null
        ), "Missing required target field");
    }
}



public class Text : InlineNode
{
    public static string ClassType = "text";

    [JsonPropertyName("type")]
    public override string Type { get => Text.ClassType; }

    [JsonInclude]
    [JsonPropertyName("value")]
    public string Value;

    [JsonConstructorAttribute]
    public Text(Span span, string value) : base(span)
    {
        Value = value;
    }

    public override string GetText()
    {
        return Value;
    }
}



public class Literal : InlineParent
{
    public static string ClassType = "literal";

    [JsonPropertyName("type")]
    public override string Type { get => Literal.ClassType; }

    public Literal(Span span) : base(span) { }
}



public class Emphasis : InlineParent
{
    public static string ClassType = "emphasis";

    [JsonPropertyName("type")]
    public override string Type { get => Emphasis.ClassType; }

    public Emphasis(Span span) : base(span) { }
}



public class Field : BlockParent<Node>
{
    public static string ClassType = "field";

    [JsonPropertyName("type")]
    public override string Type { get => Field.ClassType; }

    [JsonInclude]
    [JsonPropertyName("name")]
    public string? Name;

    [JsonInclude]
    [JsonPropertyName("label")]
    public string? Label;

    public Field(Span span) : base(span) { }
}



public class FieldList : BlockParent<Node>
{
    public static string ClassType = "field_list";

    [JsonPropertyName("type")]
    public override string Type { get => FieldList.ClassType; }

    public FieldList(Span span) : base(span) { }
}



public class Strong : InlineParent
{
    public static string ClassType = "strong";

    [JsonPropertyName("type")]
    public override string Type { get => Strong.ClassType; }

    public Strong(Span span) : base(span) { }
}



public class Transition : Node
{
    public static string ClassType = "transition";

    [JsonPropertyName("type")]
    public override string Type { get => Transition.ClassType; }

    public Transition(Span span) : base(span) { }
}
