// using System.Text.Json;
// using System.Text.Json.Serialization;
using System.Text;
using StreamJsonRpc;
using Tomlyn;

class MainClass
{
    static public void Main(string[] args)
    {
        var toml = File.ReadAllText(args[0], Encoding.UTF8);
        var model = Toml.ToModel<Spec>(toml);

        // while (true)
        // {
        //     JsonRpc rpc = JsonRpc.Attach(Console.OpenStandardOutput(), Console.OpenStandardInput(), new ParserServer());
        //     await rpc.Completion;
        // }
    }
}
