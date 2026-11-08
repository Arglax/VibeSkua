using Skua.Core.Interfaces;
public class MvsTestBadId
{
    // The script asks a helper for something that is not there: the helper (not the script) throws,
    // but it was the script's call, so the script's makers should look at it.
    public void ScriptMain(IScriptInterface bot)
    {
        new System.Collections.Generic.Dictionary<string, int>()["no such quest"].ToString();
    }
}
