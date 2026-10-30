using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

// Compiles every community script against the reference DLLs the Android app ships and lists the API
// errors (types or members the port is missing), grouped by message.
// usage: ScriptCompileCheck <scriptsDir> <refsDir>
string scriptsDir = args[0];
string refsDir = args[1];

string[] namespaces =
{
    "System", "System.Collections", "System.Collections.Generic", "System.Diagnostics", "System.Drawing",
    "System.Dynamic", "System.Globalization", "System.IO", "System.Linq", "System.Net", "System.Net.Http",
    "System.Reflection", "System.Runtime.CompilerServices", "System.Text", "System.Text.RegularExpressions",
    "System.Threading", "System.Threading.Tasks", "System.Timers", "System.Windows.Forms", "Microsoft.Win32",
    "Skua.Core", "Skua.Core.Interfaces", "Skua.Core.Models", "Skua.Core.Models.Items", "Skua.Core.Models.Monsters",
    "Skua.Core.Models.Players", "Skua.Core.Models.Quests", "Skua.Core.Models.Servers", "Skua.Core.Models.Shops",
    "Skua.Core.Models.Skills", "Skua.Core.Models.Auras", "Skua.Core.Models.Factions", "Skua.Core.Options",
    "Skua.Core.Utils", "CommunityToolkit.Mvvm.DependencyInjection", "Newtonsoft.Json", "Newtonsoft.Json.Linq",
};

CSharpParseOptions parse = new(LanguageVersion.Latest, DocumentationMode.None);
List<SyntaxTree> trees = new()
{
    CSharpSyntaxTree.ParseText(string.Join("\n", namespaces.Select(n => $"global using {n};")), parse, "__usings.cs"),
};
foreach (string file in Directory.EnumerateFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
{
    if (file.Contains("Cached-Scripts"))
        continue;
    trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(file), parse, Path.GetRelativePath(scriptsDir, file)));
}

List<MetadataReference> refs = Directory.EnumerateFiles(refsDir, "*.dll")
    .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

CSharpCompilation comp = CSharpCompilation.Create("check", trees, refs,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, concurrentBuild: true, allowUnsafe: true));

// Only "this API does not exist" style errors matter; scripts clash with each other on names when
// they are all compiled together, which never happens in the app.
HashSet<string> wanted = new() { "CS0246", "CS0103", "CS0117", "CS1061", "CS0234", "CS0012", "CS0426", "CS1503", "CS7036", "CS0029", "CS0266", "CS1729", "CS0619", "CS0122", "CS0104", "CS0433", "CS0101" };
var errors = comp.GetDiagnostics()
    .Where(d => d.Severity == DiagnosticSeverity.Error && wanted.Contains(d.Id))
    .ToList();

Console.WriteLine($"{trees.Count - 1} files, {errors.Count} API errors");
foreach (var group in errors.GroupBy(e => e.Id + " " + e.GetMessage()).OrderByDescending(g => g.Count()))
{
    var first = group.First();
    Console.WriteLine($"{group.Count(),5}  {group.Key}   [{first.Location.SourceTree?.FilePath}:{first.Location.GetLineSpan().StartLinePosition.Line + 1}]");
}
