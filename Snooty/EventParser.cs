public class CancelledException : Exception { }

public class FileIdStack
{
    private List<FileId> _stack;

    public FileIdStack()
    {
        _stack = new List<FileId>();
    }
    public FileIdStack(List<FileId> initial_stack)
    {
        _stack = new List<FileId>(initial_stack);
    }

    public void Pop()
    {
        _stack.RemoveAt(_stack.Count - 1);
    }

    public void Push(FileId fileid)
    {
        _stack.Add(fileid);
    }

    public void Clear()
    {
        _stack.Clear();
    }

    public FileId Root
    {
        get
        {
            return _stack[0];
        }
    }


    public FileId Current
    {
        get
        {
            return _stack[^1];
        }
    }
}

public class Context
{
    private Dictionary<Type, object> _ctx = new Dictionary<Type, object>();
    public Dictionary<FileId, List<Diagnostic>> Diagnostics = new Dictionary<FileId, List<Diagnostic>>();
    public Dictionary<FileId, Page> Pages;

    public Context(Dictionary<FileId, Page> pages)
    {
        Pages = pages;
    }

    public void Add(object val)
    {
        _ctx[val.GetType()] = val;
    }

    public T Get<T>() where T : class
    {
        var result = _ctx[typeof(T)] as T;
        if (result != null)
        {
            return result;
        }

        throw new KeyNotFoundException(typeof(T).ToString());
    }
}


public class Handler
{
    public Context Context;

    public Handler(Context context)
    {
        Context = context;
    }

    virtual public void EnterNode(FileIdStack fileid_stack, N.Node node) { }

    virtual public void ExitNode(FileIdStack fileid_stack, N.Node node) { }

    virtual public void EnterPage(FileIdStack fileid_stack, Page page) { }

    virtual public void ExitPage(FileIdStack fileid_stack, Page page) { }
}


class EventParser
{
    private List<Handler> _handlers = new List<Handler>();
    private FileIdStack fileIdStack = new FileIdStack();
    private CancellationToken _cancellationToken;

    public EventParser(CancellationToken cancellation_token)
    {
        _cancellationToken = cancellation_token;
    }

    public void AddHandler(Handler handler)
    {
        _handlers.Add(handler);
    }

    public void Consume(IEnumerable<(FileId, Page)> d)
    {
        foreach (var (filename, page) in d)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                throw new CancelledException();
            }

            foreach (var handler in _handlers)
            {
                handler.EnterPage(new FileIdStack(new List<FileId> { filename }), page);
            }

            _iterate(page.Ast, filename);

            foreach (var handler in _handlers)
            {
                handler.ExitPage(new FileIdStack(new List<FileId> { filename }), page);
            }

            fileIdStack.Clear();
        }
    }

    private void _iterate(N.Node d, FileId filename)
    {
        if (d is N.Root rootNode)
        {
            fileIdStack.Push(rootNode.FileId);
        }

        foreach (var handler in _handlers)
        {
            handler.EnterNode(fileIdStack, d);
        }

        if (d is N.IAgnosticParent parentNode)
        {
            if (d is N.DefinitionListItem definitionListNode)
            {
                foreach (var child in definitionListNode.Term)
                {
                    _iterate(child, filename);
                }
            }

            if (d is N.Directive directiveNode)
            {
                foreach (var arg in directiveNode.Argument)
                {
                    _iterate(arg, filename);
                }
            }

            foreach (var child in parentNode.GetChildrenAgnostically())
            {
                _iterate(child, filename);
            }
        }

        foreach (var handler in _handlers)
        {
            handler.ExitNode(fileIdStack, d);
        }

        if (d is N.Root)
        {
            fileIdStack.Pop();
        }
    }
}
