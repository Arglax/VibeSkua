using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MoVibeSkua.Android.Diagnostics;
using Skua.Core.Models;

// usage: dotnet run -- [casesDir]   (expected verdict per case is in Expected below)
string dir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "cases");
Dictionary<string, ScriptVerdicts.Who> expected = new()
{
    ["MvsTestScriptBug"] = ScriptVerdicts.Who.Script,
    ["MvsTestSyntax"] = ScriptVerdicts.Who.Script,
    ["MvsTestBadId"] = ScriptVerdicts.Who.Script,
    ["MvsTestWindows"] = ScriptVerdicts.Who.Mvs,
    ["MvsTestPlatform"] = ScriptVerdicts.Who.Mvs,
};

List<MetadataReference> refs = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => !a.IsDynamic && a.Location.Length > 0)
    .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)).ToList();
refs.Add(MetadataReference.CreateFromFile(typeof(Skua.Core.Interfaces.IScriptInterface).Assembly.Location));
string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
foreach (string name in new[] { "System.Runtime.dll", "System.Collections.dll", "netstandard.dll" })
    refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, name)));

int failed = 0;
foreach (string file in Directory.GetFiles(dir, "*.cs").OrderBy(f => f))
{
    string cls = Path.GetFileNameWithoutExtension(file);
    Exception? error = null;
    // Skua: CSharpCompilation.Create(GeneratedClassName + ".cs"), errors as Diagnostic.ToString() lines.
    CSharpCompilation comp = CSharpCompilation.Create(cls + ".cs",
        new[] { CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: cls + ".cs") }, refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using MemoryStream ms = new();
    var result = comp.Emit(ms);
    if (!result.Success)
    {
        StringBuilder sb = new();
        foreach (Diagnostic d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            sb.AppendLine(d.ToString());
        error = new ScriptCompileException(sb.ToString(), string.Empty);
    }
    else
    {
        Assembly asm = Assembly.Load(ms.ToArray());
        object script = Activator.CreateInstance(asm.GetType(cls)!)!;
        try
        {
            script.GetType().GetMethod("ScriptMain")!.Invoke(script, new object?[] { null });
        }
        catch (Exception e)
        {
            error = e; // Skua sends the TargetInvocationException as is.
        }
    }
    if (error is null)
    {
        Console.WriteLine($"FAIL {cls}: ran without an error");
        failed++;
        continue;
    }
    ScriptVerdicts.Verdict v = ScriptVerdicts.Classify(error);
    bool ok = !expected.TryGetValue(cls, out ScriptVerdicts.Who want) || v.Who == want;
    if (!ok)
        failed++;
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {cls}: {v.Who} | {v.Reason} | {v.Short}");
}
Console.WriteLine(failed == 0 ? "all cases as expected" : failed + " case(s) wrong");
return failed;
