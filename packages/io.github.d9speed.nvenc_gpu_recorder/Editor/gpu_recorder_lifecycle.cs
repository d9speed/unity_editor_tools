using UnityEditor;

namespace D9speed.NvencGpu.Editor
{
    [InitializeOnLoad]
    static class gpu_recorder_lifecycle
    {
        static gpu_recorder_lifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += stop_alpha;
            EditorApplication.quitting += stop_alpha;
            AssemblyReloadEvents.beforeAssemblyReload += gpu_recorder_session.stop_all;
            EditorApplication.quitting += gpu_recorder_session.stop_all;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) { stop_alpha(); gpu_recorder_session.stop_all(); }
            };
        }
        static void stop_alpha()
        {
            D9speed.Recording.AlphaCaptureRecorder.StopAll();
            D9speed.Recording.prores_encoding.finish_exports();
        }
    }
}
