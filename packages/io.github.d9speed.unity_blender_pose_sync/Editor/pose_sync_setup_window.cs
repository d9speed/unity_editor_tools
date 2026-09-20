using System.IO;
using UnityEditor;
using UnityEngine;
using UnityBlenderPoseSync.World;

namespace UnityBlenderPoseSync.Setup
{
    public sealed class PoseSyncSetupWindow : EditorWindow
    {
        [MenuItem("D9speed/Animation/Pose Sync Setup")]
        public static void Open() => GetWindow<PoseSyncSetupWindow>("Pose Sync Setup");

        public static string ReceiverPath
        {
            get
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PoseSyncManager).Assembly);
                return package == null ? "" : Path.Combine(package.resolvedPath, "Blender~", "blender_pose_receiver_world.py");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Unity Blender Pose Sync", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Unity側の準備は完了しています。追加ライブラリのインストールは不要です。", MessageType.Info);
            EditorGUILayout.LabelField("1. 同梱アドオンをBlenderの「ディスクからインストール」で追加して有効化します。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("2. BlenderのPose SyncパネルでArmatureを選び、受信を開始します。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("3. UnityのPose Sync Managerで対象と接続先を設定し、Play Modeを開始します。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!File.Exists(ReceiverPath)))
                if (GUILayout.Button("Blender用アドオンの場所を開く")) EditorUtility.RevealInFinder(ReceiverPath);
            if (GUILayout.Button("Pose Sync Managerを開く"))
                EditorApplication.ExecuteMenuItem("D9speed/Animation/Pose Sync Manager");
            if (GUILayout.Button("使い方を開く"))
                Application.OpenURL("https://github.com/d9speed/unity_editor_tools/tree/main/packages/io.github.d9speed.unity_blender_pose_sync");
        }
    }
}
