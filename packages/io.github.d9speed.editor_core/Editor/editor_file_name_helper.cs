#if UNITY_EDITOR
using System.IO;

namespace D9speed_BaseEditorUtils
{
    public static class EditorFileNameHelper
    {
        /// <summary>OSで使用できない文字を置換。空文字の扱いとTrimは呼び出し側で決める。</summary>
        public static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value;
        }
    }
}
#endif
