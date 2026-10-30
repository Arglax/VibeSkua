// Desktop harness for the Ruffle bridge: serves the page, loads the client when asked and
// exercises a few ExternalInterface calls. Open the printed URL in a browser.
using Skua.Flash.Ruffle;
using System.Diagnostics;

Trace.Listeners.Add(new ConsoleTraceListener());
int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 8787;
using RuffleHost host = new(new RuffleHostOptions { Port = port });
host.Start();
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "url.txt"), host.PageUrl);
Console.WriteLine($"PAGE {host.PageUrl}");

host.Control.Connected += () => Console.WriteLine("[ctl] page connected");
host.Control.Disconnected += () => Console.WriteLine("[ctl] page disconnected");
host.Control.ExternalCall += (name, argsJson) =>
{
    string shown = argsJson.Length > 200 ? argsJson[..200] + "..." : argsJson;
    if (name != "pext" && name != "packetFromServer")
        Console.WriteLine($"[ext] {name} {shown}");
    if (name == "requestLoadGame")
        Console.WriteLine($"[call] loadClient -> {host.Control.Call("loadClient", "[]")}");
    if (name == "loaded")
    {
        Console.WriteLine($"[call] isTrue -> {host.Control.Call("isTrue", "[]")}");
        Console.WriteLine($"[call] isNull(world) -> {host.Control.Call("isNull", "[\"world\"]")}");
        Console.WriteLine($"[call] getGameObject(params.sURL) -> {host.Control.Call("getGameObject", "[\"params.sURL\"]")}");
    }
};

string? line;
while ((line = Console.ReadLine()) != null)
{
    // "fn arg1 arg2" -> call with string args
    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) continue;
    string json = FlashValues.SerializeArgs(parts.Skip(1).Cast<object?>().ToArray());
    try { Console.WriteLine($"=> {host.Control.Call(parts[0], json)}"); }
    catch (Exception ex) { Console.WriteLine($"!! {ex.Message}"); }
}
Thread.Sleep(Timeout.Infinite);
