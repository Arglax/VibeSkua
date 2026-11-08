using Skua.Core.Interfaces;
public class MvsTestScriptBug
{
    public void ScriptMain(IScriptInterface bot)
    {
        string name = null;
        bot.Log("length " + name.Length); // a mistake in the script: should be the script's fault
    }
}
