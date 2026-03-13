using System.IO.Compression;
using System.Text;

// xnafiddle-encode snippet '{"IsGum":true,"initialize":"..."}'
// xnafiddle-encode snippet --file mysnippet.json
// xnafiddle-encode code 'using System; public class MyGame : Game { ... }'
// xnafiddle-encode code --file MyGame.cs
//
// Multiple items in one call (outputs one URL per item):
// xnafiddle-encode code 'cs1' code 'cs2' snippet 'json1'
// xnafiddle-encode code --file a.cs snippet --file b.json code 'inline cs'
//
// Always outputs one URL per item, each on its own line.

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  xnafiddle-encode snippet '<json>'");
    Console.Error.WriteLine("  xnafiddle-encode snippet --file <path>");
    Console.Error.WriteLine("  xnafiddle-encode code '<csharp>'");
    Console.Error.WriteLine("  xnafiddle-encode code --file <path>");
    Console.Error.WriteLine("");
    Console.Error.WriteLine("Multiple items (one URL per item):");
    Console.Error.WriteLine("  xnafiddle-encode code 'cs1' snippet 'json1' code --file a.cs");
    return 1;
}

// Parse all mode+value pairs from args
var items = new List<(string mode, string input)>();
int i = 0;
while (i < args.Length)
{
    string mode = args[i].ToLowerInvariant();
    if (mode != "snippet" && mode != "code")
    {
        Console.Error.WriteLine($"Unknown mode '{args[i]}'. Use 'snippet' or 'code'.");
        return 1;
    }
    i++;

    if (i >= args.Length)
    {
        Console.Error.WriteLine($"Mode '{mode}' requires a value or '--file <path>'.");
        return 1;
    }

    string input;
    if (args[i] == "--file")
    {
        i++;
        if (i >= args.Length)
        {
            Console.Error.WriteLine("--file requires a path argument.");
            return 1;
        }
        string path = args[i];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }
        input = File.ReadAllText(path, Encoding.UTF8);
        i++;
    }
    else
    {
        input = args[i];
        i++;
    }

    items.Add((mode, input));
}

foreach (var (mode, input) in items)
{
    string encoded = Encode(input);
    string param   = mode == "snippet" ? "snippet" : "code";
    Console.WriteLine($"https://xnafiddle.net/#{param}={encoded}");
}
return 0;

static string Encode(string text)
{
    byte[] bytes = Encoding.UTF8.GetBytes(text);
    using var ms = new MemoryStream();
    using (var gz = new GZipStream(ms, CompressionLevel.Optimal))
        gz.Write(bytes);
    return Convert.ToBase64String(ms.ToArray())
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
