using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityBlenderPoseSync.Setup
{
    public sealed class PoseSyncSetupWindow : EditorWindow
    {
        public const string MenuPath = "D9speed/Animations/PoseSync Setup";
        private static readonly Color DoneColor = new Color(0.16f, 0.85f, 0.36f);
        private static readonly Color BusyColor = new Color(0.30f, 0.30f, 0.30f);
        private static readonly Color IdleColor = new Color(0.24f, 0.24f, 0.24f);
        private VisualElement step_strip;
        private VisualElement checklist;
        private Label message_label;
        private Button action_button;
        private Button manager_button;
        public static string ReceiverPath => PoseSyncDependencies.ReceiverPath;

        [MenuItem(MenuPath, false, 1)]
        public static PoseSyncSetupWindow Open()
        {
            var window = GetWindow<PoseSyncSetupWindow>(true, "PoseSync Setup");
            window.minSize = new Vector2(520, 390);
            return window;
        }

        private void OnEnable() => PoseSyncDependencies.Changed += Refresh;
        private void OnDisable() => PoseSyncDependencies.Changed -= Refresh;

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 14; root.style.paddingRight = 14;
            root.style.paddingTop = 12; root.style.paddingBottom = 12;
            var title = new Label("Unity Blender Pose Sync のセットアップ");
            title.style.fontSize = 15; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(title);
            var subtitle = new Label("同期に必要な依存パッケージを導入・更新します。");
            subtitle.style.opacity = .7f; subtitle.style.marginBottom = 12;
            root.Add(subtitle);
            step_strip = new VisualElement { name = "setup_steps", style = { flexDirection = FlexDirection.Row, marginBottom = 16 } };
            root.Add(step_strip);
            checklist = new VisualElement { name = "setup_checks", style = { marginBottom = 12 } };
            root.Add(checklist);
            message_label = new Label { name = "setup_message" };
            message_label.style.whiteSpace = WhiteSpace.Normal;
            message_label.style.marginBottom = 10;
            root.Add(message_label);
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            action_button = new Button(() => { if (PoseSyncDependencies.Ready) Close(); else PoseSyncDependencies.Begin(); Refresh(); })
                { name = "setup_action", text = "セットアップ" };
            action_button.style.height = 28; action_button.style.flexGrow = 1;
            buttons.Add(action_button);
            var later = new Button(Close) { text = "後で" };
            later.style.width = 80;
            buttons.Add(later); root.Add(buttons);
            var blender = new Button(() => EditorUtility.RevealInFinder(ReceiverPath)) { text = "Blender用アドオンの場所を開く" };
            blender.style.marginTop = 12; blender.SetEnabled(File.Exists(ReceiverPath)); root.Add(blender);
            manager_button = new Button(() => EditorApplication.ExecuteMenuItem("D9speed/Animations/Pose Sync Manager")) { text = "Pose Sync Managerを開く" };
            root.Add(manager_button);
            Refresh();
        }

        private void Refresh()
        {
            if (step_strip == null) return;
            var step = PoseSyncDependencies.CurrentStep;
            var nuget = PoseSyncDependencies.HasNuGet;
            var core = PoseSyncDependencies.HasCore;
            var unity = PoseSyncDependencies.HasUnitySupport;
            var ready = PoseSyncDependencies.Ready;
            var busy = PoseSyncDependencies.IsBusy;
            step_strip.Clear();
            step_strip.Add(MakeStepBox("NuGet for Unity", nuget ? StepState.Done : step == PoseSyncDependencies.Step.InstallingNuGet ? StepState.Busy : StepState.Idle));
            step_strip.Add(MakeStepBox("MessagePack Core", core ? StepState.Done : step == PoseSyncDependencies.Step.InstallingMessagePack ? StepState.Busy : StepState.Idle));
            step_strip.Add(MakeStepBox("Unity Support", unity ? StepState.Done : step == PoseSyncDependencies.Step.InstallingMessagePackUnity ? StepState.Busy : StepState.Idle));
            step_strip.Add(MakeStepBox("完了", ready ? StepState.Done : step == PoseSyncDependencies.Step.Compiling ? StepState.Busy : StepState.Idle));
            checklist.Clear();
            checklist.Add(MakeCheckRow("NuGet For Unity 4.5.x", nuget, step == PoseSyncDependencies.Step.InstallingNuGet));
            checklist.Add(MakeCheckRow("MessagePack + Analyzer 3.1.9", core, step == PoseSyncDependencies.Step.InstallingMessagePack));
            checklist.Add(MakeCheckRow("MessagePack.Unity 3.1.9", unity, step == PoseSyncDependencies.Step.InstallingMessagePackUnity));
            var error = PoseSyncDependencies.LastError;
            message_label.style.color = error.Length > 0 ? new StyleColor(new Color(1f,.5f,.35f)) : new StyleColor(StyleKeyword.Null);
            message_label.text = error.Length > 0 ? error : ready ? "すべての依存パッケージが揃いました。Pose Sync Managerから利用できます。"
                : busy ? "依存パッケージを準備しています。コンパイル後も自動で続行します。"
                : "「セットアップ」を押すと、不足パッケージを導入し、MessagePack 3.1.xの旧版を3.1.9へ更新します。";
            action_button.SetEnabled(!busy);
            action_button.text = ready ? "閉じる" : busy ? "インストール中…" : error.Length > 0 ? "再試行" : "セットアップ";
            manager_button.SetEnabled(ready);
        }

        private enum StepState { Idle, Busy, Done }

        private static VisualElement MakeStepBox(string label, StepState state)
        {
            var box = new VisualElement
            {
                style =
                {
                    flexGrow = 1, height = 46, marginRight = 6,
                    justifyContent = Justify.Center, alignItems = Align.Center,
                    flexDirection = FlexDirection.Row,
                    backgroundColor = state == StepState.Done ? DoneColor
                                    : state == StepState.Busy ? BusyColor : IdleColor,
                    borderTopLeftRadius = 3, borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
                },
            };

            var mark = new Label(state == StepState.Done ? "✔" : state == StepState.Busy ? "…" : "");
            mark.style.fontSize = state == StepState.Done ? 20 : 16;
            mark.style.unityFontStyleAndWeight = FontStyle.Bold;
            mark.style.color = state == StepState.Done ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            mark.style.marginRight = 6;
            box.Add(mark);

            if (state != StepState.Done)
            {
                var text = new Label(state == StepState.Busy ? "Installing" : label);
                text.style.color = new Color(0.85f, 0.85f, 0.85f);
                text.style.unityFontStyleAndWeight = FontStyle.Bold;
                box.Add(text);
            }
            return box;
        }

        private static VisualElement MakeCheckRow(string label, bool done, bool busy)
        {
            var row = new VisualElement
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 },
            };

            var mark = new Label(done ? "✔" : busy ? "…" : "○");
            mark.style.width = 26;
            mark.style.fontSize = done ? 16 : 13;
            mark.style.unityFontStyleAndWeight = FontStyle.Bold;
            mark.style.color = done ? DoneColor : new Color(0.6f, 0.6f, 0.6f);
            row.Add(mark);

            var text = new Label(done ? label : busy ? $"{label} Installing" : label);
            text.style.fontSize = 13;
            text.style.opacity = done ? 1f : 0.75f;
            row.Add(text);
            return row;
        }
    }

}
