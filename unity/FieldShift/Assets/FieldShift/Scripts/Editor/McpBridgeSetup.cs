using UnityEditor;

namespace FieldShift.EditorTools
{
    /// <summary>
    /// The team drives this editor from Claude Code through the "MCP for Unity" package, registered in Claude as
    /// a stdio server. The package defaults to HTTP, so switch it to stdio once (its bridge then listens on port
    /// 6400 whenever this project is open).
    /// </summary>
    [InitializeOnLoad]
    static class McpBridgeSetup
    {
        const string Key = "MCPForUnity.UseHttpTransport";
        const string Done = "FieldShift.McpStdioConfigured";

        static McpBridgeSetup()
        {
            if (UnityEngine.Application.isBatchMode || EditorPrefs.GetBool(Done, false)) return;
            EditorPrefs.SetBool(Done, true);
            if (EditorPrefs.HasKey(Key) && !EditorPrefs.GetBool(Key)) return;
            EditorPrefs.SetBool(Key, false);
            // reload once so the bridge starts in stdio mode, but never in the middle of a Play session
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorUtility.RequestScriptReload();
            };
        }
    }
}
