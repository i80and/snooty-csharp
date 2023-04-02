namespace tinydocutils;

using System.Collections;
using System.Text;

public interface INodeVisitor
{
    void DispatchVisit(Node node);
    void DispatchDeparture(Node node);
}

public class SkipChildren : Exception { }
public class SkipNode : Exception { }
public class StopTraversal : Exception { }
public class SkipDeparture : Exception { }
public class SkipSiblings : Exception { }

public abstract class Node
{
    public abstract string RawSource { get; set; }
    public Element? Parent { get; set; }
    public string? Source { get; set; }
    public int? Line { get; set; }
    public Dictionary<string, object> Attributes { get; init; } = new Dictionary<string, object>();

    private Document? _document;
    public Document? Document
    {
        get
        {
            if (_document is not null)
            {
                return _document;
            }

            if (Parent is not null)
            {
                return Parent.Document;
            }

            return null;
        }
        set => _document = value;
    }

    public bool WalkAbout(INodeVisitor visitor)
    {
        bool call_depart = true;
        bool stop = false;

        try
        {
            try
            {
                visitor.DispatchVisit(this);
            }
            catch (SkipNode)
            {
                stop = true;
            }
            catch (SkipDeparture)
            {
                call_depart = false;
            }

            if (this is Element element)
            {
                var children = element.Children.ToList();
                try
                {
                    foreach (var child in children)
                    {
                        if (child.WalkAbout(visitor))
                        {
                            stop = true;
                            break;
                        }
                    }
                }
                catch (SkipSiblings)
                {

                }
            }
        }
        catch (SkipChildren)
        {

        }
        catch (StopTraversal)
        {
            stop = true;
        }

        if (call_depart)
        {
            visitor.DispatchDeparture(this);
        }

        return stop;
    }

    public IEnumerable<T> Traverse<T>() where T : Node
    {
        if (this is T)
        {
            yield return (T)this;
        }
        if (this is Element element)
        {
            foreach (var child in element.Children)
            {
                foreach (var subnode in child.Traverse<T>())
                {
                    yield return subnode;
                }
            }
        }
    }

    public abstract string AsText();
}

public class Text : Node
{
    public string Value { get; set; }
    public override string RawSource { get; set; }

    public Text(string value, string rawSource = "")
    {
        Value = value;
        RawSource = rawSource;
    }

    public override string AsText()
    {
        return Util.Unescape(Value);
    }

    public override string ToString()
    {
        var className = this.GetType().Name;
        return $"<{className}>{AsText()}</{className}>";
    }
}

public class Element : Node, IEnumerable<Node>
{
    public override string RawSource { get; set; }
    public List<Node> Children { get; set; } = new List<Node>();
    public List<string> Ids { get; init; } = new List<string>();
    public List<string> Names = new List<string>();
    public List<string> DupNames = new List<string>();

    public Element(string rawsource = "")
    {
        RawSource = rawsource;
    }

    public Node this[int index]
    {
        get
        {
            return Children[index];
        }

        set
        {
            Children[index] = value;
        }
    }

    public void Add(Node node)
    {
        SetupChild(node);
        Children.Add(node);
    }

    public void AddRange(IEnumerable<Node> nodes)
    {
        foreach (var node in nodes)
        {
            Add(node);
        }
    }

    public void RemoveAt(int index)
    {
        Children.RemoveAt(index);
    }

    private void SetupChild(Node child)
    {
        child.Parent = this;
        var ourDocument = Document;
        if (ourDocument is not null)
        {
            child.Document = ourDocument;
            if (child.Source is null)
            {
                child.Source = ourDocument.CurrentSource;
            }
            if (child.Line is null)
            {
                child.Line = ourDocument.CurrentLine;
            }
        }
    }

    public int Count
    {
        get { return Children.Count; }
    }

    public IEnumerator<Node> GetEnumerator()
    {
        return Children.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void DupName(string name)
    {
        DupNames.Add(name);
        Names.Remove(name);
    }

    public virtual string ChildTextSeperator { get => "\n\n"; }

    public override string AsText()
    {
        return String.Join(ChildTextSeperator, Children.Select(child => child.AsText()));
    }

    public override string ToString()
    {
        var className = this.GetType().Name;
        var sb = new StringBuilder($"<{className}>");

        foreach (var child in Children)
        {
            sb.Append(child.ToString());
        }

        sb.Append($"</{className}>");
        return sb.ToString();
    }
}

public class Reporter
{
    public Func<int?, (string?, int)>? GetSourceAndLine;

    public SystemMessage MakeSystemMesage(DiagnosticLevel level, string message, int? line = null)
    {
        return new SystemMessage();
    }

    public SystemMessage Info(string message, int? line = null)
    {
        return new SystemMessage();
    }

    public SystemMessage Warning(string message, int? line = null)
    {
        return new SystemMessage();
    }

    public SystemMessage Error(string message, int? line = null)
    {
        return new SystemMessage();
    }

    public SystemMessage Severe(string message, int? line = null)
    {
        return new SystemMessage();
    }
}

public class TextElement : Element
{
    public override string ChildTextSeperator { get { return ""; } }

    public TextElement(string rawsource = "", string text = "") : base(rawsource)
    {
        if (text != "")
        {
            Add(new Text(text));
        }
    }
}

public class FixedTextElement : TextElement
{
    public FixedTextElement(string rawsource = "", string source = "") : base(rawsource, source) { }
}

////////////////////////
// Element Categories //
////////////////////////

public interface IRoot { }

public interface ITitular { }

public interface IPreBibliographic { }

public interface IStructural { }

public interface IBody { }

public interface IGeneral : IBody { }

public interface IInline { }

//////////////////
// Root Element //
//////////////////

public class Document : Element
{
    public int? CurrentLine { get; set; }
    public string? CurrentSource { get; set; }
    public OptionParser Settings { get; set; }

    public Dictionary<string, List<Node>> RefNames { get; init; } = new Dictionary<string, List<Node>>();
    public Dictionary<string, string?> NameIds { get; init; } = new Dictionary<string, string?>();
    public Dictionary<string, Element> IdToElement { get; init; } = new Dictionary<string, Element>();
    public List<Target> IndirectTargets = new List<Target>();
    public Dictionary<string, List<CitationReference>> CitationRefs = new Dictionary<string, List<CitationReference>>();
    public Dictionary<string, bool> NameTypes = new Dictionary<string, bool>();
    public List<FootnoteReference> AutofootnoteRefs = new List<FootnoteReference>();
    public List<FootnoteReference> SymbolFootnoteRefs = new List<FootnoteReference>();
    public Dictionary<string, List<FootnoteReference>> FootnoteRefs = new Dictionary<string, List<FootnoteReference>>();
    public List<Citation> Citations = new List<Citation>();
    public List<Footnote> AutoFootnotes = new List<Footnote>();
    public List<Footnote> SymbolFootnotes = new List<Footnote>();
    public List<Footnote> Footnotes = new List<Footnote>();
    public int IdStart { get; set; } = 1;

    public Reporter Reporter { get; init; }
    public Document(OptionParser settings, Reporter reporter)
    {
        Settings = settings;
        Reporter = reporter;
    }

    public string SetId(Element node, Element? msgnode = null)
    {
        string id = "";

        foreach (var currentId in node.Ids)
        {
            if (IdToElement.ContainsKey(currentId) && IdToElement[currentId] != node)
            {
                var msg = Reporter.Severe($"Duplicate ID: '{currentId}'.");
                if (msgnode is not null)
                {
                    msgnode.Add(msg);
                }
            }
        }

        if (node.Ids.Count == 0)
        {
            bool didBreak = false;
            foreach (var name in node.Names)
            {
                id = Settings.id_prefix + Util.MakeId(name);
                if (id.Length > 0 && !IdToElement.ContainsKey(id))
                {
                    didBreak = true;
                    break;
                }
            }

            if (!didBreak)
            {
                id = "";
                while ((id.Length == 0) || IdToElement.ContainsKey(id))
                {
                    id = (
                        Settings.id_prefix
                        + Settings.auto_id_prefix
                        + IdStart.ToString()
                    );
                    IdStart += 1;
                }
            }
            node.Ids.Add(id);
        }
        IdToElement[id] = node;
        return id;
    }

    public void SetNameIdMap(
        Element node,
        string id,
        Element? msgnode = null,
        bool isExplicit = false
    )
    {
        // `NameIds` maps names to IDs, while `self.nametypes` maps names to
        // booleans representing hyperlink type (True==explicit,
        // False==implicit).  This method updates the mappings.

        // The following state transition table shows how `NameIds` ("ids")
        // and `self.nametypes` ("types") change with new input (a call to this
        // method), and what actions are performed ("implicit"-type system
        // messages are INFO/1, and "explicit"-type system messages are ERROR/3):

        // ====  =====  ========  ========  =======  ====  =====  =====
        //  Old State    Input          Action        New State   Notes
        // -----------  --------  -----------------  -----------  -----
        // ids   types  new type  sys.msg.  dupname  ids   types
        // ====  =====  ========  ========  =======  ====  =====  =====
        // -     -      explicit  -         -        new   True
        // -     -      implicit  -         -        new   False
        // None  False  explicit  -         -        new   True
        // old   False  explicit  implicit  old      new   True
        // None  True   explicit  explicit  new      None  True
        // old   True   explicit  explicit  new,old  None  True   [#]_
        // None  False  implicit  implicit  new      None  False
        // old   False  implicit  implicit  new,old  None  False
        // None  True   implicit  implicit  new      None  True
        // old   True   implicit  implicit  new      old   True
        // ====  =====  ========  ========  =======  ====  =====  =====

        // .. [#] Do not clear the name-to-id map or invalidate the old target if
        //    both old and new targets are external and refer to identical URIs.
        //    The new target is invalidated regardless.

        foreach (var name in node.Names)
        {
            if (NameIds.ContainsKey(name))
            {
                SetDuplicateNameId(node, id, name, msgnode, isExplicit);
            }
            else
            {
                NameIds[name] = id;
                NameTypes[name] = isExplicit;
            }
        }
    }

    public void SetDuplicateNameId(
        Element node,
        string id,
        string name,
        Element? msgnode,
        bool isExplicit
    )
    {
        var old_id = NameIds[name];
        var old_explicit = NameTypes[name];
        NameTypes[name] = old_explicit || isExplicit;
        if (isExplicit)
        {
            if (old_explicit)
            {
                DiagnosticLevel level = DiagnosticLevel.Warning;
                if (old_id is not null)
                {
                    var old_node = IdToElement[old_id];
                    if (node.Attributes.ContainsKey("refuri"))
                    {
                        var refuri = node.Attributes["refuri"];
                        if (
                            old_node.Names.Count > 0
                            && old_node.Attributes.ContainsKey("refuri")
                            && old_node.Attributes["refuri"] == refuri
                        )
                        {
                            level = DiagnosticLevel.Info;  // just inform if refuri's identical
                        }
                    }
                    if ((int)level > 1)
                    {
                        old_node.DupName(name);
                        NameIds[name] = null;
                    }
                }
                var msg = Reporter.MakeSystemMesage(
                    level,
                    $"Duplicate explicit target name: '{name}'."
                );
                if (msgnode is not null)
                {
                    msgnode.Add(msg);
                }
                node.DupName(name);
            }
            else
            {
                NameIds[name] = id;
                if (old_id is not null)
                {
                    var old_node = IdToElement[old_id];
                    old_node.DupName(name);
                }
            }
        }
        else
        {
            if (old_id is not null && !old_explicit)
            {
                NameIds[name] = null;
                var old_node = IdToElement[old_id!];
                old_node.DupName(name);
            }
            node.DupName(name);
        }
        if (!isExplicit || (!old_explicit && old_id is not null))
        {
            var msg = Reporter.Info(
                $"Duplicate implicit target name: '{name}'."
            );
            if (msgnode is not null)
            {
                msgnode.Add(msg);
            }
        }
    }

    public void NoteSource(string? source, int? offset)
    {
        CurrentSource = source;
        if (offset is null)
        {
            CurrentLine = offset;
        }
        else
        {
            CurrentLine = offset + 1;
        }
    }

    public void NoteRefname(Element node)
    {
        List<Node>? nodes;
        var key = (string)node.Attributes["refname"];
        if (RefNames.TryGetValue(key, out nodes))
        {
            nodes.Add(node);
        }
        else
        {
            RefNames[key] = new List<Node> { node };
        }
    }

    public void NoteIndirectTarget(Target target)
    {
        IndirectTargets.Add(target);
        if (target.Names.Count > 0)
        {
            NoteRefname(target);
        }

    }

    public void NoteExplicitTarget(Element target, Element msgnode)
    {
        var id = SetId(target, msgnode);
        SetNameIdMap(target, id, msgnode, isExplicit: true);
    }

    public void NoteImplicitTarget(Element target, Element msgnode)
    {
        var id = SetId(target, msgnode);
        SetNameIdMap(target, id, msgnode, isExplicit: false);
    }

    public void NoteAnonymousTarget(Target target)
    {
        var id = SetId(target);
    }

    public void NoteCitation(Citation citation)
    {
        Citations.Add(citation);
    }

    public void NoteCitationRef(CitationReference node)
    {
        SetId(node);

        List<CitationReference>? nodes;
        var key = (string)node.Attributes["refname"];
        if (CitationRefs.TryGetValue(key, out nodes))
        {
            nodes.Add(node);
        }
        else
        {
            CitationRefs[key] = new List<CitationReference> { node };
        }

        NoteRefname(node);
    }

    public void NoteAutofootnote(Footnote node)
    {
        SetId(node);
        AutoFootnotes.Add(node);
    }

    public void NoteSymbolFootnote(Footnote node)
    {
        SetId(node);
        SymbolFootnotes.Add(node);
    }

    public void NoteFootnote(Footnote node)
    {
        SetId(node);
        Footnotes.Add(node);
    }

    public void NoteAutofootnoteRef(FootnoteReference node)
    {
        SetId(node);
        AutofootnoteRefs.Add(node);
    }

    public void NoteSymbolFootnoteRef(FootnoteReference node)
    {
        SetId(node);
        SymbolFootnoteRefs.Add(node);
    }

    public void NoteFootnoteRef(FootnoteReference node)
    {
        SetId(node);

        List<FootnoteReference>? nodes;
        var key = (string)node.Attributes["refname"];
        if (FootnoteRefs.TryGetValue(key, out nodes))
        {
            nodes.Add(node);
        }
        else
        {
            FootnoteRefs[key] = new List<FootnoteReference> { node };
        }

        NoteRefname(node);
    }

    public void NoteSubstitutionRef(SubstitutionReference node, string refname)
    {
        node.Attributes["refname"] = Util.WhitespaceNormalizeName(refname);
    }

    public static Document New(string sourcePath, OptionParser settings)
    {
        // Return a new empty document object.

        // :Parameters:
        //     `source_path` : string
        //         The path to or description of the source text of the document.
        //     `settings` : optparse.Values object
        //         Runtime settings.  If none are provided, a default core set will
        //         be used.  If you will use the document object with any Docutils
        //         components, you must provide their default settings as well.  For
        //         example, if parsing rST, at least provide the rst-parser settings,
        //         obtainable as follows::

        //             settings = docutils.frontend.OptionParser(
        //                 components=(docutils.parsers.rst.Parser,)
        //                 ).get_default_values()

        var reporter = new Reporter();
        var doc = new Document(settings, reporter);
        doc.Source = sourcePath;
        doc.NoteSource(sourcePath, -1);
        return doc;
    }

}


////////////////////
// Title ELements //
////////////////////

public class Title : TextElement, ITitular, IPreBibliographic
{
    public Title(string rawsource, string text) : base(rawsource, text) { }
}

/////////////////////////
// Structural Elements //
/////////////////////////

public class Section : Element, IStructural { }

public class Transition : Element, IStructural
{
    public Transition(string rawsource) : base(rawsource) { }
}

///////////////////
// Body Elements //
///////////////////

public class Paragraph : TextElement, IGeneral
{
    public Paragraph(string rawSource, string source) : base(rawSource, source) { }
}

public class BulletList : Element { }

public class EnumeratedList : Element { }

public class ListItem : Element
{
    public ListItem(string text) : base(text) { }
}

public class DefinitionList : Element { }

public class DefinitionListItem : Element
{
    public DefinitionListItem(string rawsource) : base(rawsource) { }
}

public class Term : TextElement
{
    public Term(string rawsource) : base(rawsource) { }
}

public class Classifier : TextElement
{
    public Classifier(string rawsource, string text) : base(rawsource, text) { }
}

public class Definition : Element
{
    public Definition(string rawsource) : base(rawsource) { }
}

public class FieldList : Element { }

public class Field : Element { }

public class FieldName : TextElement
{
    public FieldName(string rawsource, string text) : base(rawsource, text) { }
}

public class FieldBody : Element
{
    public FieldBody(string rawtext) : base(rawtext) { }
}

public class Option : Element
{
    public override string ChildTextSeperator { get { return ""; } }

    public Option(string rawsource) : base(rawsource) { }
}

public class OptionArgument : TextElement
{
    public OptionArgument(string rawsource, string source, string delimiter) : base(rawsource, source)
    {
        Attributes["delimiter"] = delimiter;
    }

    public override string AsText()
    {
        string delimiter = " ";
        try
        {
            delimiter = (string)Attributes["delimiter"];
        }
        catch (IndexOutOfRangeException) { }
        return delimiter + base.AsText();
    }
}


public class OptionGroup : Element
{
    public override string ChildTextSeperator { get { return ", "; } }

    public OptionGroup(string rawsource) : base(rawsource) { }
}

public class OptionList : Element { }

public class OptionListItem : Element
{
    public override string ChildTextSeperator { get { return "  "; } }

    public OptionListItem(string rawsource, OptionGroup group, Description description) : base(rawsource)
    {
        Add(group);
        Add(description);
    }
}

public class OptionString : TextElement
{
    public OptionString(string rawsource, string text) : base(rawsource, text) { }
}

public class Description : Element
{
    public Description(string rawsource) : base(rawsource) { }
}

public class LiteralBlock : FixedTextElement, IGeneral
{
    public LiteralBlock(string rawsource, string source) : base(rawsource, source) { }
}

public class DoctestBlock : FixedTextElement, IGeneral
{
    public DoctestBlock(string rawsource, string source) : base(rawsource, source) { }
}

public class LineBlock : Element, IGeneral
{
    public IEnumerable<Line> Lines()
    {
        foreach (var child in Children)
        {
            if (child is Line line)
            {
                yield return line;
            }
        }
    }
}

public class Line : TextElement
{
    public int? Indent = null;

    public Line(string rawsource, string text) : base(rawsource, text) { }
}

public class BlockQuote : Element, IGeneral { }


public class Error : Element { }


public class Note : Element { }


public class Hint : Element { }


public class Warning : Element { }


public class Comment : FixedTextElement
{
    public Comment() : base() { }
    public Comment(string rawsource, string text) : base(rawsource, text) { }
}


public class SubstitutionDefinition : TextElement
{
    public SubstitutionDefinition(string rawsource) : base(rawsource) { }
}


public class Target : TextElement, IInline
{
    public static readonly Func<string, string, Target> Make = (rawsource, text) => new Target(rawsource, text);

    public Target(string rawsource, string text) : base(rawsource, text) { }
}


public class Footnote : Element, IGeneral
{
    public Footnote(string rawsource) : base(rawsource) { }
}


public class Citation : Element, IGeneral
{
    public Citation(string rawsource) : base(rawsource) { }
}


public class Label : TextElement
{
    public Label(string rawsource, string text) : base(rawsource, text) { }
}


public class Table : Element, IGeneral { }


public class Caption : TextElement { }


public class Entry : Element { }

public class SystemMessage : Element, IPreBibliographic
{
    public override string AsText()
    {
        return "SystemMessage";
    }
}


//////////////////////
//  Inline Elements //
//////////////////////


public class Emphasis : TextElement, IInline
{
    public static readonly Func<string, string, Emphasis> Make = (rawsource, text) => new Emphasis(rawsource, text);

    public Emphasis(string rawsource, string text) : base(rawsource, text) { }
}


public class Strong : TextElement, IInline
{
    public static readonly Func<string, string, Strong> Make = (rawsource, text) => new Strong(rawsource, text);

    public Strong(string rawsource, string text) : base(rawsource, text) { }
}


public class Literal : TextElement, IInline
{
    public static readonly Func<string, string, Literal> Make = (rawsource, text) => new Literal(rawsource, text);

    public Literal(string rawsource, string text) : base(rawsource, text) { }
}


public class Reference : TextElement, IGeneral, IInline
{
    public Reference(string rawsource, string value) : base(rawsource, value) { }
}


public class FootnoteReference : TextElement, IInline
{
    public FootnoteReference(string rawsource) : base(rawsource) { }
}


public class CitationReference : TextElement, IInline
{
    public CitationReference(string rawsource) : base(rawsource) { }
}


public class SubstitutionReference : TextElement, IInline
{
    public static readonly Func<string, string, SubstitutionReference> Make = (rawsource, text) => new SubstitutionReference(rawsource, text);

    public SubstitutionReference(string rawsource, string text) : base(rawsource, text) { }
}
