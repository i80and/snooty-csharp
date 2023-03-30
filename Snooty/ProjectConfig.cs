public record ParsedBannerConfig(List<string> targets, N.Directive node);

public class ProjectConfig
{
    public string root;
    public string source = "source";
    public string[] intersphinx = new string[] { };
    public List<ParsedBannerConfig> banner_nodes = new List<ParsedBannerConfig>();
    public Dictionary<string, List<N.InlineNode>> substitution_nodes = new Dictionary<string, List<N.InlineNode>>();

    public ProjectConfig(string r)
    {
        root = r;
    }

    public string SourcePath
    {
        get
        {
            return Path.Join(root, source);
        }
    }
}
