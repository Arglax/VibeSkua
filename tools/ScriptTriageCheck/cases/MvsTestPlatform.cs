using Skua.Core.Interfaces;
public class MvsTestPlatform
{
    public void ScriptMain(IScriptInterface bot)
    {
        throw new System.PlatformNotSupportedException("Only on Windows.");
    }
}
