// Assets/Editor/SkinnedMeshRendererEditor.cs

using D9speed_BaseEditorUtils;
using UnityEngine;
using UnityEditor;
using Unity.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor.Animations;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

[CustomEditor(typeof(SkinnedMeshRenderer))]
public class SkinnedMeshRendererEditorExtra : Editor
{
    // キャッシュ用
    private HashSet<Transform> weightedBones;   
    private bool weighted_bones_scanning;
    private int weighted_bones_scan_version;
    private VisualElement bones_container;
    private string blendShapeSearch = "";
    private bool DisplayNonZeroBool ;
    private bool show_blend_shape_bounds;
    private const string ShowBlendShapeBoundsSessionKey = nameof(SkinnedMeshRendererEditorExtra) + ".show_blend_shape_bounds";
    private const string BoundsColorSessionKey = nameof(SkinnedMeshRendererEditorExtra) + ".bounds_color";
    private Color blend_shape_bounds_color = Color.cyan;
    private readonly Dictionary<int, Bounds> blend_shape_delta_bounds = new Dictionary<int, Bounds>();
    private long cached_mesh_instance_id;
    private int active_blend_shape_bounds_index = -1;
    private static readonly Regex blend_shape_group_header_regex = new Regex(
        @"^\s*(?<separator>[^\p{L}\p{N}\s])\k<separator>{2,}\s*(?<group_name>.*?)\s*\k<separator>{2,}\s*$",
        RegexOptions.Compiled);

    [System.Serializable]
    private class BlendShapeGroupFoldoutStateEntry
    {
        public string key;
        public bool is_expanded;
    }

    [FilePath("ProjectSettings/SkinnedMeshRendererEditorExtraState.asset", FilePathAttribute.Location.ProjectFolder)]
    private class BlendShapeEditorState : ScriptableSingleton<BlendShapeEditorState>
    {
        [SerializeField] private List<BlendShapeGroupFoldoutStateEntry> blend_shape_group_foldout_states = new List<BlendShapeGroupFoldoutStateEntry>();

        public bool GetGroupFoldoutState(string key, bool default_value)
        {
            var entry = blend_shape_group_foldout_states.FirstOrDefault(x => x.key == key);
            return entry != null ? entry.is_expanded : default_value;
        }

        public void SetGroupFoldoutState(string key, bool is_expanded)
        {
            var entry = blend_shape_group_foldout_states.FirstOrDefault(x => x.key == key);
            if (entry == null)
            {
                blend_shape_group_foldout_states.Add(new BlendShapeGroupFoldoutStateEntry
                {
                    key = key,
                    is_expanded = is_expanded
                });
            }
            else
            {
                entry.is_expanded = is_expanded;
            }

            Save(true);
        }
    }

    //折りたたみ記録
    private string _SMRObjKey;
    private static string GetObjectKey(UnityEngine.Object obj)
    {
        // オブジェクトの一意のキー
        var gid = GlobalObjectId.GetGlobalObjectIdSlow(obj);
        return gid.ToString();
    }
    private bool _customInpectorFoldOutKey;

    
    
    // デフォルト(内部)インスペクター
    private Editor _defaultEditor;

    

    void OnEnable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;

        // 初回スキャン
        ScheduleCalculateWeightedBones();

        // SessionSate用キー
        _SMRObjKey = GetObjectKey(target);
        show_blend_shape_bounds = SessionState.GetBool(ShowBlendShapeBoundsSessionKey, false);
        blend_shape_bounds_color = ParseColor(SessionState.GetString(BoundsColorSessionKey, ColorUtility.ToHtmlStringRGBA(Color.cyan)), Color.cyan);

        // internal class UnityEditor.SkinnedMeshRendererEditor を取得
        var internalType = typeof(Editor).Assembly
                          .GetType("UnityEditor.SkinnedMeshRendererEditor");
        // 型が見つからない場合に自身のCustomEditorを再生成する再帰を避ける。
        _defaultEditor = internalType == null ? null : CreateEditor(targets, internalType);
    }
    
    void OnDisable()
    {
        weighted_bones_scan_version++;
        weighted_bones_scanning = false;
        SceneView.duringSceneGui -= OnSceneGUI;
        if (_defaultEditor) DestroyImmediate(_defaultEditor);
    }

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        
        // デフォルトのインスペクター内容をIMGUIContainerで描画
        var imgui = new IMGUIContainer(() =>
        {
            if (_defaultEditor) _defaultEditor.OnInspectorGUI();
            else DrawDefaultInspector();
        });
        // 既存 UI の Repaint を継承
        imgui.style.flexGrow = 0;
        root.Add(imgui);

        // ヘッダー
        var header = new Label("Extra Tools") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 15, marginBottom = 10 } };
        root.Add(header);

        var showBoundsToggle = new Toggle("ブレンドシェイプの影響部位を表示") { value = show_blend_shape_bounds };
        showBoundsToggle.RegisterValueChangedCallback(evt =>
        {
            show_blend_shape_bounds = evt.newValue;
            SessionState.SetBool(ShowBlendShapeBoundsSessionKey, show_blend_shape_bounds);
            if (show_blend_shape_bounds)
            {
                EnsureDeltaBoundsCache();
            }
            else
            {
                active_blend_shape_bounds_index = -1;
            }

            SceneView.RepaintAll();
        });
        root.Add(showBoundsToggle);

        var boundsSettingsFoldout = new Foldout { text = "影響部位表示設定", value = false };
        boundsSettingsFoldout.style.marginLeft = 12;
        var boundsColorField = new ColorField("Bounds Color") { value = blend_shape_bounds_color };
        boundsColorField.RegisterValueChangedCallback(evt =>
        {
            blend_shape_bounds_color = evt.newValue;
            SessionState.SetString(BoundsColorSessionKey, ColorUtility.ToHtmlStringRGBA(blend_shape_bounds_color));
            SceneView.RepaintAll();
        });
        boundsSettingsFoldout.Add(boundsColorField);
        root.Add(boundsSettingsFoldout);

        // ボーンセクション
        var bonesFoldout = new Foldout { text = "Bones", value = false };
        root.Add(bonesFoldout);

        // Create the bones container first
        bones_container = new VisualElement();
        bones_container.AddToClassList("bones-list");
        bones_container.style.marginTop = 5;
        bones_container.style.marginBottom = 10;
        bonesFoldout.Add(bones_container);

        var refreshButton = new Button(() => {
            ScheduleCalculateWeightedBones();
            UpdateBonesUI(bones_container);
        }) { text = "Refresh Bones" };
        bonesFoldout.Add(refreshButton);

        // 初期ボーン表示
        UpdateBonesUI(bones_container);

        // BlendShapesセクション ->デフォルトで閉じている状態に変更

        var blendShapesFoldout = new Foldout { text = "BlendShapes", value = _customInpectorFoldOutKey };

        // SessionSate
        blendShapesFoldout.SetValueWithoutNotify(
            SessionState.GetBool($"{nameof(SkinnedMeshRendererEditorExtra)}{nameof(_customInpectorFoldOutKey)}" + _SMRObjKey, false));

        blendShapesFoldout.RegisterValueChangedCallback(evt =>
        {
            SessionState.SetBool($"{nameof(SkinnedMeshRendererEditorExtra)}{nameof(_customInpectorFoldOutKey)}" + _SMRObjKey, evt.newValue);
        });

        // Drag And Drop
        blendShapesFoldout.RegisterCallback<DragPerformEvent>(OnDragPerform);
        blendShapesFoldout.RegisterCallback<DragUpdatedEvent>(OnDragUpdate);


        root.Add(blendShapesFoldout);



        // Create the blend shapes container first
        var blendShapesContainer = new VisualElement();
        blendShapesContainer.style.marginTop = 5;
        blendShapesContainer.style.marginBottom = 10;

        // Display BlendShape 0<
        var DisplayNonZero = new Toggle("0より大きいものだけ表示") { value = DisplayNonZeroBool };
        DisplayNonZero.style.marginBottom = 4;
        DisplayNonZero.RegisterValueChangedCallback(evt =>
        {
            DisplayNonZeroBool = evt.newValue;
            UpdateBlendShapesUI(blendShapesContainer);
        });

        blendShapesFoldout.Add(DisplayNonZero);

        // 検索行
        var searchRow = new VisualElement();
        searchRow.style.flexDirection = FlexDirection.Row;
        searchRow.style.alignItems = Align.FlexEnd;
        searchRow.style.marginBottom = 10;
        searchRow.style.marginTop = 5;
        searchRow.style.display = DisplayStyle.Flex;
        blendShapesFoldout.Add(searchRow);

        // search
        var searchField = new TextField("Search");
        searchField.value = blendShapeSearch;
        searchField.style.flexGrow = 1;
        searchField.style.marginRight = 5;
        searchRow.Add(searchField);

        // 検索フィールドの変更イベント
        searchField.RegisterValueChangedCallback(evt => {
            blendShapeSearch = evt.newValue;
            UpdateBlendShapesUI(blendShapesContainer);
        });

        // Reset All Button
        var resetAllButtonUpper = new Button(() => {
            var smr = (SkinnedMeshRenderer)target;
            ResetAllBlendShapes(smr);
            UpdateBlendShapesUI(blendShapesContainer);
        }) { text = "全BlendShapeを0にリセット" };
        resetAllButtonUpper.style.alignSelf = Align.Stretch;
        resetAllButtonUpper.style.height = 24;
        resetAllButtonUpper.style.marginTop = 0;
        resetAllButtonUpper.style.marginBottom = 8;
        resetAllButtonUpper.style.display = DisplayStyle.Flex;
        blendShapesFoldout.Add(resetAllButtonUpper);

        blendShapesFoldout.Add(blendShapesContainer);

        
        // 初期BlendShape表示
        UpdateBlendShapesUI(blendShapesContainer);
        
        // アニメーション保存ボタン
        var saveAnimButton = new Button(() => {
            var smr = (SkinnedMeshRenderer)target;
            SaveCurrentBlendShapeValuesAsAnimation(smr);
        }) { text = "Save BlendShape Animation" };
        saveAnimButton.style.marginTop = 10;
        saveAnimButton.style.height = 25;
        blendShapesFoldout.Add(saveAnimButton);
        
        // アニメーションロードボタン
        var loadAnimButton = new Button(() => {
            var smr = (SkinnedMeshRenderer)target;
            LoadAndApplyAnimationClip(smr, blendShapesContainer);
        }) { text = "Load Anim Clip" };
        loadAnimButton.style.marginTop = 5;
        loadAnimButton.style.height = 25;
        blendShapesFoldout.Add(loadAnimButton);

        return root;
    }

    

    private void UpdateBonesUI(VisualElement container)
    {
        if (container == null)
            return;

        container.Clear();
        var smr = (SkinnedMeshRenderer)target;

        if (weighted_bones_scanning)
        {
            var helpBox = new HelpBox("ボーンウェイトをスキャン中...", HelpBoxMessageType.Info);
            helpBox.style.marginTop = 5;
            helpBox.style.marginBottom = 5;
            container.Add(helpBox);
            return;
        }

        if (weightedBones != null && weightedBones.Count > 0)
        {
            foreach (var bone in weightedBones.OrderBy(bone => bone != null ? bone.name : string.Empty))
            {
                if (bone != null)
                {
                    var boneField = new ObjectField
                    {
                        objectType = typeof(GameObject),
                        value = bone.gameObject,
                        allowSceneObjects = true
                    };
                    boneField.SetEnabled(false); // Read-only
                    container.Add(boneField);
                }
                else
                {
                    var label = new Label("<Null Bone>");
                    container.Add(label);
                }
            }
        }
        else
        {
            var helpBox = new HelpBox("ウェイトを持つボーンがありません。", HelpBoxMessageType.Info);
            //helpBox.style.padding = 10;
            helpBox.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            //helpBox.style.borderRadius = 3;
            helpBox.style.marginTop = 5;
            helpBox.style.marginBottom = 5;
            container.Add(helpBox);
        }
    }



    private void OnDragPerform(DragPerformEvent evt)
    {
#if !UNITY_2023_2_OR_NEWER
        evt.PreventDefault();
#endif
        evt.StopPropagation();
        Debug.Log("ドロップされた時の処理");

        // ドロップを受け入れる
        DragAndDrop.AcceptDrag();

        if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0 || DragAndDrop.objectReferences[0] == null)
        {
            Debug.LogWarning("[Drop] オブジェクトがありません。");
            return;
        }

        // 検証
        //var Animclip = (AnimationClip)DragAndDrop.objectReferences[0];

        if (DragAndDrop.objectReferences[0] is not AnimationClip clip)
        {
            var p = AssetDatabase.GetAssetPath(DragAndDrop.objectReferences[0]);
            Debug.LogWarning($"[Drop] AnimationClipではありません: type={DragAndDrop.objectReferences[0]?.GetType().Name}, name={DragAndDrop.objectReferences[0]?.name}, path={p}");
            return;
        }
        // Assets or Temporally object
        if (!EditorUtility.IsPersistent(clip))
        {
            Debug.LogWarning($"[Drop] アセットではないためスキップ: name={clip.name}（Scene内・一時参照の可能性）");
            return;
        }


        // Is Assets?
        var assetPath = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/"))
        {
            Debug.LogWarning($"[Drop] Assets/ 配下でないためスキップ: {assetPath}");
            return;
        }

        ApplyFirstFrameBlendShapes((AnimationClip)DragAndDrop.objectReferences[0],(SkinnedMeshRenderer)target);

    }
    
    private void OnDragUpdate(DragUpdatedEvent evt)
    {
        // ドラッグ中の処理
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
    }

    private void UpdateBlendShapesUI(VisualElement container)
    {
        container.Clear();
        var smr = (SkinnedMeshRenderer)target;
        var mesh = smr.sharedMesh;

        if (mesh != null && mesh.blendShapeCount > 0)
        {
            string low = blendShapeSearch.ToLower();
            bool use_group_foldout = string.IsNullOrEmpty(low);
            string current_group_name = null;
            string current_group_state_key = null;
            Foldout current_group_foldout = null;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (use_group_foldout && TryParseBlendShapeGroupHeader(name, out var group_name))
                {
                    current_group_name = group_name;
                    current_group_state_key = BuildBlendShapeGroupStateKey(name);
                    current_group_foldout = null;
                    continue;
                }

                float weight = smr.GetBlendShapeWeight(i);
                if (!IsBlendShapeVisible(name, weight, low))
                    continue;

                if (use_group_foldout && current_group_name != null && current_group_foldout == null)
                {
                    current_group_foldout = CreateBlendShapeGroupFoldout(current_group_name, current_group_state_key);
                    container.Add(current_group_foldout);
                }

                // Create a row for each blend shape
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 0;
                row.style.marginBottom = 0;

                string Truncate(string s, int max)
                {
                    return (s.Length > max) ? s.Substring(0, max) + "…" : s;
                }

                var label = new Label(Truncate(name, 20));
                label.style.minWidth = 150;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.tooltip = name;



                if (!TryGetBlendShapeWeightProperty(i, out var weightProperty))
                    continue;

                var slider = CreateBlendShapeSlider(weightProperty, weight);
                UpdateBlendShapeRowVisibility(row, weight);
                int blendShapeIndex = i;
                slider.RegisterValueChangedCallback(_ =>
                {
                    if (show_blend_shape_bounds)
                    {
                        ShowBlendShapeBounds(blendShapeIndex);
                    }
                });
                row.TrackPropertyValue(weightProperty, trackedProperty =>
                {
                    UpdateBlendShapeRowVisibility(row, trackedProperty.floatValue);
                });



                // 右クリックでクリップボードにコピーする機能関連
                label.RegisterCallback<MouseDownEvent>(evt =>
                {

                    // 右クリックメニューイベント
                    if (evt.button == (int)MouseButton.RightMouse)
                    {
                        var menu = new GenericMenu();
                        float copiedweight = slider.value;

                        // ブレンドシェイプ名をコピー
                        menu.AddItem(new GUIContent("ブレンドシェイプ名をコピー"), false, () =>
                        {
                            GUIUtility.systemCopyBuffer = name;
                            // Debug.Log($"{name}");
                        });

                        // 値をコピー
                        menu.AddItem(new GUIContent("値をコピー"), false, () =>
                        {
                            GUIUtility.systemCopyBuffer = copiedweight.ToString("F2");
                            // Debug.Log($"{copiedweight}");
                        });

                        // 両方コピー
                        menu.AddItem(new GUIContent("ブレンドシェイプ名と値をコピー"), false, () =>
                        {
                            GUIUtility.systemCopyBuffer = $"{name}: {copiedweight:F2}";
                            // Debug.Log($"{name}: {copiedweight:F2}");
                        });

                        // マウス位置に表示
                        menu.ShowAsContext();
                        evt.StopPropagation();
                    }
                });

                row.Add(label);
                row.Add(slider);

                if (use_group_foldout && current_group_foldout != null)
                {
                    current_group_foldout.Add(row);
                    continue;
                }

                container.Add(row);
            }
        }
        else
        {
            var helpBox = new HelpBox("No blend shapes found.", HelpBoxMessageType.Info);
            //helpBox.style.padding = 10;
            helpBox.style.backgroundColor = new Color(0, 0, 0, 0.1f);
            //helpBox.style.borderRadius = 3;
            helpBox.style.marginTop = 5;
            helpBox.style.marginBottom = 5;
            container.Add(helpBox);
        }
    }


    private bool IsBlendShapeVisible(string name, float weight, string search_text_lower)
    {
        if (!string.IsNullOrEmpty(search_text_lower) && !name.ToLower().Contains(search_text_lower))
            return false;

        if (DisplayNonZeroBool && Mathf.Approximately(weight, 0f))
            return false;

        return true;
    }

    private bool TryParseBlendShapeGroupHeader(string name, out string group_name)
    {
        group_name = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var match = blend_shape_group_header_regex.Match(name);
        if (!match.Success)
            return false;

        group_name = match.Groups["group_name"].Value.Trim();
        return !string.IsNullOrEmpty(group_name);
    }

    private string BuildBlendShapeGroupStateKey(string raw_group_header_name)
    {
        return $"{_SMRObjKey}:blend_shape_group:{raw_group_header_name}";
    }

    private Foldout CreateBlendShapeGroupFoldout(string group_name, string state_key)
    {
        var foldout = new Foldout
        {
            text = group_name,
            value = BlendShapeEditorState.instance.GetGroupFoldoutState(state_key, false)
        };
        foldout.style.marginTop = 4;
        foldout.style.marginBottom = 2;
        foldout.style.marginLeft = 6;
        foldout.RegisterValueChangedCallback(evt =>
        {
            BlendShapeEditorState.instance.SetGroupFoldoutState(state_key, evt.newValue);
        });

        return foldout;
    }

    private static string GetBlendShapeWeightPropertyPath(int blendShapeIndex)
    {
        return $"m_BlendShapeWeights.Array.data[{blendShapeIndex}]";
    }

    private static EditorCurveBinding CreateBlendShapeCurveBinding(string relativePath, string shapeName)
    {
        return EditorCurveBinding.FloatCurve(relativePath, typeof(SkinnedMeshRenderer), $"blendShape.{shapeName}");
    }

    private static string FormatAnimationFloatValue(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateBlendShapeRowVisibility(VisualElement row, float weight)
    {
        row.style.display = !DisplayNonZeroBool || !Mathf.Approximately(weight, 0f)
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private Slider CreateBlendShapeSlider(SerializedProperty weightProperty, float weight)
    {
        var slider = new Slider(0f, 100f);
        slider.style.flexBasis = 0;
        slider.style.flexShrink = 1;
        slider.style.minWidth = 0;
        slider.value = weight;
        slider.showInputField = true;
        slider.style.flexGrow = 1;
        slider.BindProperty(weightProperty);

        return slider;
    }

    private void ShowBlendShapeBounds(int blendShapeIndex)
    {
        if (!show_blend_shape_bounds)
            return;

        if (!EnsureDeltaBoundsCache())
            return;

        EnsureBlendShapeDeltaBoundsCache(blendShapeIndex);
        active_blend_shape_bounds_index = blendShapeIndex;
        SceneView.RepaintAll();
    }

    private bool EnsureDeltaBoundsCache()
    {
        var smr = (SkinnedMeshRenderer)target;
        if (smr == null || smr.sharedMesh == null)
            return false;

        long meshInstanceId = EditorObjectHelper.GetObjectId(smr.sharedMesh);
        if (cached_mesh_instance_id == meshInstanceId)
            return true;

        blend_shape_delta_bounds.Clear();
        active_blend_shape_bounds_index = -1;
        cached_mesh_instance_id = meshInstanceId;
        return true;
    }

    private void EnsureBlendShapeDeltaBoundsCache(int blendShapeIndex)
    {
        if (blend_shape_delta_bounds.ContainsKey(blendShapeIndex))
            return;

        var smr = (SkinnedMeshRenderer)target;
        if (smr == null || smr.sharedMesh == null)
        {
            blend_shape_delta_bounds[blendShapeIndex] = new Bounds(Vector3.zero, Vector3.zero);
            return;
        }

        var mesh = smr.sharedMesh;
        if (blendShapeIndex < 0 || blendShapeIndex >= mesh.blendShapeCount)
        {
            blend_shape_delta_bounds[blendShapeIndex] = new Bounds(Vector3.zero, Vector3.zero);
            return;
        }

        int vertexCount = mesh.vertexCount;
        Vector3[] vertices = mesh.vertices;
        var deltaVertices = new Vector3[vertexCount];
        int frameIndex = Mathf.Max(0, mesh.GetBlendShapeFrameCount(blendShapeIndex) - 1);
        mesh.GetBlendShapeFrameVertices(blendShapeIndex, frameIndex, deltaVertices, null, null);

        const float deltaEpsilonSqr = 0.0000000001f;
        bool hasBounds = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        for (int vertexIndex = 0; vertexIndex < deltaVertices.Length; vertexIndex++)
        {
            if (deltaVertices[vertexIndex].sqrMagnitude <= deltaEpsilonSqr)
                continue;

            if (!hasBounds)
            {
                bounds = new Bounds(vertices[vertexIndex], Vector3.zero);
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(vertices[vertexIndex]);
        }

        blend_shape_delta_bounds[blendShapeIndex] = hasBounds ? bounds : new Bounds(Vector3.zero, Vector3.zero);
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        if (!show_blend_shape_bounds || active_blend_shape_bounds_index < 0)
            return;

        var smr = (SkinnedMeshRenderer)target;
        if (smr == null)
            return;

        Bounds bounds;
        if (!blend_shape_delta_bounds.TryGetValue(active_blend_shape_bounds_index, out bounds) || bounds.size == Vector3.zero)
            return;

        Matrix4x4 previousMatrix = Handles.matrix;
        Color previousColor = Handles.color;
        Handles.matrix = smr.transform.localToWorldMatrix;
        Handles.color = blend_shape_bounds_color;

        Handles.DrawWireCube(bounds.center, bounds.size);

        Handles.matrix = previousMatrix;
        Handles.color = previousColor;
    }

    private static Color ParseColor(string htmlRgba, Color fallback)
    {
        Color color;
        if (ColorUtility.TryParseHtmlString("#" + htmlRgba, out color))
            return color;

        return fallback;
    }

    private bool TryGetBlendShapeWeightProperty(int blendShapeIndex, out SerializedProperty weightProperty)
    {
        weightProperty = null;

        if (serializedObject == null || blendShapeIndex < 0)
            return false;

        weightProperty = serializedObject.FindProperty(GetBlendShapeWeightPropertyPath(blendShapeIndex));
        return weightProperty != null;
    }

    private bool TrySetBlendShapeWeightInAnimationMode(SkinnedMeshRenderer smr, int blendShapeIndex, float value, string relativePath)
    {
        if (!AnimationMode.InAnimationMode() || smr == null || smr.sharedMesh == null)
            return false;

        if (!TryGetBlendShapeWeightProperty(blendShapeIndex, out var weightProperty))
            return false;

        string shapeName = smr.sharedMesh.GetBlendShapeName(blendShapeIndex);
        var binding = CreateBlendShapeCurveBinding(relativePath, shapeName);

        AnimationMode.AddEditorCurveBinding(smr.gameObject, binding);

        weightProperty.floatValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        AnimationMode.AddPropertyModification(binding, new PropertyModification
        {
            target = smr,
            propertyPath = weightProperty.propertyPath,
            value = FormatAnimationFloatValue(value)
        }, true);

        EditorUtility.SetDirty(smr);
        return true;
    }

    private void SetBlendShapeWeight(SkinnedMeshRenderer smr, int blendShapeIndex, float value, string undoName, bool recordUndo = true, string relativePath = null)
    {
        if (smr == null || smr.sharedMesh == null)
            return;

        if (blendShapeIndex < 0 || blendShapeIndex >= smr.sharedMesh.blendShapeCount)
            return;

        if (Mathf.Approximately(smr.GetBlendShapeWeight(blendShapeIndex), value))
            return;

        relativePath ??= GetRelativePath(FindClosestModelPrefabInParents(smr.transform), smr.transform);
        if (TrySetBlendShapeWeightInAnimationMode(smr, blendShapeIndex, value, relativePath))
            return;

        if (recordUndo)
            Undo.RecordObject(smr, undoName);

        smr.SetBlendShapeWeight(blendShapeIndex, value);
        PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
        EditorUtility.SetDirty(smr);
    }


    private void ScheduleCalculateWeightedBones()
    {
        int scanVersion = ++weighted_bones_scan_version;
        weighted_bones_scanning = true;
        weightedBones = null;
        UpdateBonesUI(bones_container);

        EditorApplication.delayCall += () =>
        {
            if (scanVersion != weighted_bones_scan_version)
                return;

            StartCalculateWeightedBonesAsync(scanVersion);
        };
    }

    private async void StartCalculateWeightedBonesAsync(int scanVersion)
    {
        try
        {
            var smr = (SkinnedMeshRenderer)target;
            var mesh = smr != null ? smr.sharedMesh : null;
            var bones = smr != null ? smr.bones : null;

            if (mesh == null || bones == null)
            {
                ApplyWeightedBonesScanResult(scanVersion, new HashSet<int>(), bones);
                return;
            }

            byte[] bonesPerVertexArray;
            BoneWeight1[] boneWeightsArray;
            var bonesPerVertex = mesh.GetBonesPerVertex();
            var boneWeights = mesh.GetAllBoneWeights();
            try
            {
                bonesPerVertexArray = bonesPerVertex.ToArray();
                boneWeightsArray = boneWeights.ToArray();
            }
            finally
            {
                boneWeights.Dispose();
                bonesPerVertex.Dispose();
            }

            HashSet<int> boneIndexSet = await Task.Run(() =>
            {
                var result = new HashSet<int>();
                int weightOffset = 0;
                for (int vertexIndex = 0; vertexIndex < bonesPerVertexArray.Length; vertexIndex++)
                {
                    int count = bonesPerVertexArray[vertexIndex];
                    for (int weightIndex = 0; weightIndex < count; weightIndex++)
                    {
                        BoneWeight1 boneWeight = boneWeightsArray[weightOffset + weightIndex];
                        if (boneWeight.weight > 0f)
                        {
                            result.Add(boneWeight.boneIndex);
                        }
                    }

                    weightOffset += count;
                }

                return result;
            });

            EditorApplication.delayCall += () => ApplyWeightedBonesScanResult(scanVersion, boneIndexSet, bones);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.delayCall += () => ApplyWeightedBonesScanResult(scanVersion, new HashSet<int>(), null);
        }
    }

    private void ApplyWeightedBonesScanResult(int scanVersion, HashSet<int> boneIndexSet, Transform[] bones)
    {
        if (scanVersion != weighted_bones_scan_version)
            return;

        weightedBones = new HashSet<Transform>();
        if (bones != null && boneIndexSet != null)
        {
            foreach (var idx in boneIndexSet)
            {
                if (idx >= 0 && idx < bones.Length)
                {
                    weightedBones.Add(bones[idx]);
                }
            }
        }

        weighted_bones_scanning = false;
        UpdateBonesUI(bones_container);
    }


    // 全BlendShapeを0にリセット
    private void ResetAllBlendShapes(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null) return;

        string relativePath = GetRelativePath(FindClosestModelPrefabInParents(smr.transform), smr.transform);
        if (!AnimationMode.InAnimationMode())
            Undo.RecordObject(smr, "Reset All BlendShapes");

        for (int i = 0; i < mesh.blendShapeCount; i++)
            SetBlendShapeWeight(smr, i, 0f, "Reset All BlendShapes", false, relativePath);

        if (!AnimationMode.InAnimationMode())
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);

        EditorUtility.SetDirty(smr);
    }

    // 最も近いAnimatorを持つ親プレハブを見つける
    private Transform FindClosestModelPrefabInParents(Transform transform)
    {
        if (transform == null) return null;
        
        // 自身を含む親階層を上に辿る
        Transform current = transform;
        while (current != null)
        {
            // Animatorコンポーネントを持つオブジェクトを探す
            if (current.GetComponent<Animator>() != null)
            {
                return current;
            }
            
            current = current.parent;
        }
        
        // 見つからなかった場合は元のオブジェクトを返す
        return transform;
    }
    
    // 指定されたTransformからのパスを取得
    private string GetRelativePath(Transform root, Transform target)
    {
        return HierarchyPathHelper.GetRelativePath(root, target) ?? string.Empty;
    }
    
    // BlendShape→AnimationClip 保存ロジック
    private void SaveCurrentBlendShapeValuesAsAnimation(SkinnedMeshRenderer smr)
    {
        var mesh = smr.sharedMesh;
        if (mesh == null) return;

        var clip = new AnimationClip { frameRate = 60f };
        
        // 最も近いAnimatorを持つ親を見つける
        Transform rootTransform = FindClosestModelPrefabInParents(smr.transform);
        string relativePath = GetRelativePath(rootTransform, smr.transform);
        
        Debug.Log($"Animation root: {rootTransform.name}, Relative path: {relativePath}");

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            string shapeName = mesh.GetBlendShapeName(i);
            float weight = smr.GetBlendShapeWeight(i);

            // ウェイトが 0 ならスキップ
            if (Mathf.Approximately(weight, 0f))
                continue;

            var binding = new EditorCurveBinding
            {
                path         = relativePath,
                type         = typeof(SkinnedMeshRenderer),
                propertyName = $"blendShape.{shapeName}"
            };

            var curve = new AnimationCurve(new Keyframe(0f, weight));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save BlendShape Animation",
            $"{rootTransform.name}_BlendShapeAnim",
            "anim",
            "Enter a filename for the animation clip."
        );
        if (string.IsNullOrEmpty(path)) return;

        AssetDatabase.CreateAsset(clip, path);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = clip;
    }

    // アニメーションクリップをロードして適用する
    private void LoadAndApplyAnimationClip(SkinnedMeshRenderer smr, VisualElement blendShapesContainer)
    {
        string path = EditorUtility.OpenFilePanelWithFilters(
            "Load Animation Clip",
            "Assets",
            new string[] { "Animation Files", "anim", "All Files", "*" }
        );
        
        if (string.IsNullOrEmpty(path)) return;
        
        // プロジェクトパスに変換
        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Please select an animation clip from your project.", "OK");
            return;
        }
        
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            EditorUtility.DisplayDialog("Error", "Failed to load animation clip.", "OK");
            return;
        }
        
        ApplyFirstFrameBlendShapes(clip, smr);
        
        // ブレンドシェイプUIを更新
        UpdateBlendShapesUI(blendShapesContainer);
    }
    
    // アニメーションクリップの最初のフレームのBlendShapeを適用する
    private void ApplyFirstFrameBlendShapes(AnimationClip clip, SkinnedMeshRenderer smr)
    {
        if (clip == null || smr == null || smr.sharedMesh == null) return;

        if (!AnimationMode.InAnimationMode())
            Undo.RecordObject(smr, "Apply BlendShapes From Animation");
        
        // "1フレーム目" を時間で指定したい場合は以下のように。
        // float sampleTime = 1f / clip.frameRate;
        float sampleTime = 0f;

        var bindings = AnimationUtility.GetCurveBindings(clip);
        int count = 0;
        
        // 最も近いAnimatorを持つ親を見つける
        Transform rootTransform = FindClosestModelPrefabInParents(smr.transform);
        string relativePath = GetRelativePath(rootTransform, smr.transform);
        
        Debug.Log($"Animation root: {rootTransform.name}, Relative path: {relativePath}");

        foreach (var b in bindings)
        {
            // SkinnedMeshRenderer のブレンドシェイプカーブだけを対象に
            if (b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape."))
            {
                // アニメーションのパスとオブジェクトのパスが一致するか確認
                // 空のパスの場合はルートオブジェクトに直接適用されるアニメーション
                if (!string.IsNullOrEmpty(b.path) && b.path != relativePath)
                {
                    // パスが一致しない場合はスキップ（別のSkinnedMeshRenderer向けのカーブ）
                    continue;
                }
                
                string shapeName = b.propertyName.Substring("blendShape.".Length);
                int shapeIndex = smr.sharedMesh.GetBlendShapeIndex(shapeName);

                if (shapeIndex >= 0)
                {
                    var curve = AnimationUtility.GetEditorCurve(clip, b);
                    float value = curve.Evaluate(sampleTime);
                    SetBlendShapeWeight(smr, shapeIndex, value, "Apply BlendShapes From Animation", false, relativePath);
                    count++;
                }
            }
        }

        if (!AnimationMode.InAnimationMode())
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);

        EditorUtility.SetDirty(smr);
        Debug.Log($"[{clip.name}] から {count} 件の BlendShape を '{smr.name}' に適用しました。");
    }
    
    // 内部エディターが常時 Repaint 要求しているかを反映
    public override bool RequiresConstantRepaint() =>
        _defaultEditor && _defaultEditor.RequiresConstantRepaint();

}
