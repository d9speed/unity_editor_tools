using D9speed_BaseEditorUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone.Components;

// 素体のスキンウェイトに基づいてPhysBoneのコライダーを自動生成するエディタ拡張
// カプセルコライダーの軸合わせ、メッシュ表面オフセットなどを自動で計算します
// ウェイトが変なところに飛んでたらパーセンタイルで弾くのでウェイトの乱れがあっても多少許容できます



namespace D9speed_Test_Editor
{
    public sealed class PhysBoneWeightColliderGeneratorWindow : EditorWindow
    {
        private const string DefaultHolderSuffix = "_PB_collider";

        [SerializeField] private SkinnedMeshRenderer target_renderer;
        [SerializeField] private Transform target_bone;
        [SerializeField] private Transform holder_parent;
        private float weight_threshold = 0.2f;
        private float percentile_clip = 2f;
        private float radius_percentile = 95f;
        private float diameter_padding_meters = 0.01f;
        private string holder_name_suffix = DefaultHolderSuffix;
        private List<PhysBoneWeightColliderGenerator.BoneWeightGroup> bone_groups = new List<PhysBoneWeightColliderGenerator.BoneWeightGroup>();
        private string[] bone_group_labels = Array.Empty<string>();
        private SkinnedMeshRenderer cached_renderer;
        private int cached_bone_count = -1;
        private int selected_bone_index = -1;
        private VisualElement target_bone_container;
        private Button create_button;
        [SerializeField] private Animator humanoid_animator;
        [SerializeField] private VRCPhysBoneCollider selected_collider;
        private ObjectField animator_field;
        private ObjectField collider_field;
        private SliderInt radius_slider;
        private SliderInt length_slider;
        private Vector3Field position_field;
        private Vector3Field rotation_field;
        private Button align_rotation_button;
        private Button mirror_button;
        private HelpBox status_box;
        private static readonly HumanBodyBones[] paired_bones =
        {
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes
        };
        private readonly Dictionary<HumanBodyBones, Button> body_buttons = new Dictionary<HumanBodyBones, Button>();

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshColliderControls;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshColliderControls;
        }

        private void OnFocus()
        {
            RefreshColliderControls();
        }

        [MenuItem("D9speed/Tools/PhysBone Weight Collider Generator")]
        private static void Open()
        {
            GetWindow<PhysBoneWeightColliderGeneratorWindow>("PB Weight Collider");
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            minSize = new Vector2(380, 600);
            var root = new ScrollView();
            rootVisualElement.Add(root);
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;
            D9speedEditorFontUtility.Apply(root);

            root.Add(CreateTitle("VRC PhysBone Collider Generator"));

            var target_renderer_field = CreateObjectField<SkinnedMeshRenderer>("Target Renderer", target_renderer, true);
            target_renderer_field.RegisterValueChangedCallback(evt =>
            {
                target_renderer = evt.newValue as SkinnedMeshRenderer;
                selected_collider = null;
                humanoid_animator = target_renderer != null ? target_renderer.GetComponentInParent<Animator>() : null;
                animator_field?.SetValueWithoutNotify(humanoid_animator);
                RebuildBoneGroups();
                RefreshTargetBoneField();
                UpdateCreateButton();
                RefreshBodyButtons();
                RefreshColliderControls();
            });
            root.Add(target_renderer_field);

            animator_field = CreateObjectField<Animator>("Humanoid Animator", humanoid_animator, true);
            animator_field.RegisterValueChangedCallback(evt =>
            {
                humanoid_animator = evt.newValue as Animator;
                selected_collider = null;
                RefreshBodyButtons();
                RefreshColliderControls();
            });
            root.Add(animator_field);
            root.Add(new HelpBox("人体図をクリックして生成／選択。左右はアバター基準です。灰色の部位には対応ボーンまたはウェイトがありません。", HelpBoxMessageType.Info));
            root.Add(CreateBodyDiagram());

            target_bone_container = new VisualElement();
            root.Add(target_bone_container);
            RefreshTargetBoneField();

            var holder_parent_field = CreateObjectField<Transform>("Holder Parent", holder_parent, true);
            holder_parent_field.RegisterValueChangedCallback(evt => holder_parent = evt.newValue as Transform);
            root.Add(holder_parent_field);

            root.Add(CreateSlider("Weight Threshold", weight_threshold, 0.001f, 1f, value => weight_threshold = value));
            root.Add(CreateSlider("Position Clip %", percentile_clip, 0f, 20f, value => percentile_clip = value));
            root.Add(CreateSlider("Radius Percentile", radius_percentile, 50f, 100f, value => radius_percentile = value));

            var diameter_padding_field = new FloatField("Diameter Padding m") { value = diameter_padding_meters };
            diameter_padding_field.RegisterValueChangedCallback(evt => diameter_padding_meters = evt.newValue);
            root.Add(diameter_padding_field);

            var holder_name_suffix_field = new TextField("Name Suffix") { value = holder_name_suffix };
            holder_name_suffix_field.RegisterValueChangedCallback(evt => holder_name_suffix = evt.newValue ?? string.Empty);
            root.Add(holder_name_suffix_field);

            create_button = new Button(CreateFromWindow) { text = "Create Capsule Collider" };
            create_button.style.marginTop = 6;
            create_button.style.height = 28;
            root.Add(create_button);

            collider_field = CreateObjectField<VRCPhysBoneCollider>("調整するコライダー", selected_collider, true);
            collider_field.style.marginTop = 12;
            collider_field.RegisterValueChangedCallback(evt =>
            {
                selected_collider = evt.newValue as VRCPhysBoneCollider;
                RefreshColliderControls();
            });
            root.Add(collider_field);
            radius_slider = CreateMillimeterSlider("半径 (mm)", true);
            length_slider = CreateMillimeterSlider("長さ / Height (mm)", false);
            root.Add(radius_slider);
            root.Add(length_slider);
            position_field = new Vector3Field("位置 XYZ (mm)");
            position_field.RegisterValueChangedCallback(evt => EditSelectedCollider("Move PhysBone Collider",
                collider => collider.position = evt.newValue / 1000f));
            root.Add(position_field);
            rotation_field = new Vector3Field("回転 XYZ (度)");
            rotation_field.RegisterValueChangedCallback(evt => EditSelectedCollider("Rotate PhysBone Collider",
                collider => collider.rotation = Quaternion.Euler(evt.newValue)));
            root.Add(rotation_field);
            align_rotation_button = new Button(() => EditSelectedCollider("Align PhysBone Collider To Bone",
                collider => collider.rotation = Quaternion.identity)) { text = "ボーンの軸に回転を合わせる" };
            align_rotation_button.tooltip = "Root Transformのローカル回転オフセットを0にし、コライダーの軸をボーンの軸に合わせます。位置は保持します。";
            root.Add(align_rotation_button);
            root.Add(new HelpBox("半径・長さ：10～500mm・1mm刻み。長さは両端を含む全長です。直径より短い場合は球形になります。位置・回転はRoot Transform基準、寸法はスケール1で実寸です。", HelpBoxMessageType.Info));
            status_box = new HelpBox("部位を選択してください。", HelpBoxMessageType.Info);
            root.Add(status_box);
            root.Add(new HelpBox("ミラー：現在の姿勢でAnimatorの中央面（ローカルX=0）を基準に、位置・回転・サイズを反対側の四肢へ複製します。既存コライダーは上書きしません。", HelpBoxMessageType.Info));
            mirror_button = new Button(CreateMirroredCollider) { text = "選択したコライダーの左右ミラーを作成" };
            mirror_button.style.height = 28;
            root.Add(mirror_button);
            UpdateCreateButton();
            RefreshBodyButtons();
            RefreshColliderControls();
        }

        private void CreateFromWindow()
        {
            if (target_renderer == null || target_bone == null || EditorApplication.isPlaying ||
                EditorUtility.IsPersistent(target_renderer) || EditorUtility.IsPersistent(target_bone) ||
                (holder_parent != null && EditorUtility.IsPersistent(holder_parent)))
                return;

            var parent = holder_parent != null ? holder_parent : target_renderer.transform;
            selected_collider = parent.GetComponentsInChildren<VRCPhysBoneCollider>(true)
                .FirstOrDefault(item => item.rootTransform == target_bone &&
                    item.gameObject.name == target_bone.name + holder_name_suffix &&
                    item.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule);
            if (selected_collider != null)
            {
                Selection.activeObject = selected_collider.gameObject;
                RefreshColliderControls();
                return;
            }

            var settings = new PhysBoneWeightColliderSettings
            {
                weight_threshold = weight_threshold,
                percentile_clip = percentile_clip,
                radius_percentile = radius_percentile,
                diameter_padding_meters = diameter_padding_meters,
                holder_name = target_bone.name + holder_name_suffix,
                holder_parent = holder_parent != null ? holder_parent : target_renderer.transform
            };

            Undo.IncrementCurrentGroup();
            var undo_group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create PhysBone Collider");
            try
            {
                var result = PhysBoneWeightColliderGenerator.CreateCapsule(target_renderer, target_bone, settings, true);
                selected_collider = result.collider;
                Selection.activeObject = result.holder;
                Debug.Log(result.ToLogString());
                RefreshColliderControls();
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undo_group);
                status_box.text = exception.Message;
                status_box.messageType = HelpBoxMessageType.Error;
            }
            finally
            {
                Undo.CollapseUndoOperations(undo_group);
            }
        }

        private VisualElement CreateBodyDiagram()
        {
            body_buttons.Clear();
            var diagram = new VisualElement { name = "humanoid_diagram" };
            diagram.style.height = 340;
            diagram.style.width = 310;
            diagram.style.alignSelf = Align.Center;
            diagram.style.flexShrink = 0;
            diagram.generateVisualContent += context =>
            {
                var painter = context.painter2D;
                painter.strokeColor = new Color(0.25f, 0.65f, 0.55f);
                painter.lineWidth = 18;
                painter.lineCap = LineCap.Round;
                DrawBodyLine(painter, new Vector2(155, 45), new Vector2(155, 187));
                for (var side = -1; side <= 1; side += 2)
                {
                    DrawBodyLine(painter, new Vector2(155, 88), new Vector2(155 + side * 48, 88));
                    DrawBodyLine(painter, new Vector2(155 + side * 48, 88), new Vector2(155 + side * 101, 205));
                    DrawBodyLine(painter, new Vector2(155, 187), new Vector2(155 + side * 27, 208));
                    DrawBodyLine(painter, new Vector2(155 + side * 27, 208), new Vector2(155 + side * 35, 308));
                }
            };
            AddBodyButton(diagram, HumanBodyBones.Head, "頭", 155, 26, 42, 40);
            AddBodyButton(diagram, HumanBodyBones.Neck, "首", 155, 64);
            AddBodyButton(diagram, HumanBodyBones.UpperChest, "上胸", 155, 94);
            AddBodyButton(diagram, HumanBodyBones.Chest, "胸", 155, 123);
            AddBodyButton(diagram, HumanBodyBones.Spine, "腹", 155, 152);
            AddBodyButton(diagram, HumanBodyBones.Hips, "腰", 155, 183, 54);
            AddBodySide(diagram, -1, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, "右");
            AddBodySide(diagram, 1, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, "左");
            return diagram;
        }

        private static void DrawBodyLine(Painter2D painter, Vector2 start, Vector2 end)
        {
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
        }

        private void AddBodySide(VisualElement diagram, int side, HumanBodyBones shoulder,
            HumanBodyBones upper_arm, HumanBodyBones lower_arm, HumanBodyBones hand,
            HumanBodyBones upper_leg, HumanBodyBones lower_leg, HumanBodyBones foot, HumanBodyBones toes, string label)
        {
            AddBodyButton(diagram, shoulder, label + "肩", 155 + side * 47, 88);
            AddBodyButton(diagram, upper_arm, label + "上腕", 155 + side * 63, 120, 48);
            AddBodyButton(diagram, lower_arm, label + "前腕", 155 + side * 84, 164, 48);
            AddBodyButton(diagram, hand, label + "手", 155 + side * 103, 206);
            AddBodyButton(diagram, upper_leg, label + "太腿", 155 + side * 29, 219, 50, 38);
            AddBodyButton(diagram, lower_leg, label + "すね", 155 + side * 32, 264, 50, 38);
            AddBodyButton(diagram, foot, label + "足", 155 + side * 35, 300);
            AddBodyButton(diagram, toes, label + "爪先", 155 + side * 40, 327, 50, 22);
        }

        private void AddBodyButton(VisualElement diagram, HumanBodyBones part, string label,
            float x, float y, float width = 40, float height = 25)
        {
            var button = new Button(() =>
            {
                target_bone = ResolveHumanoidBone(part);
                RefreshTargetBoneField();
                CreateFromWindow();
            }) { text = label, name = "body_" + part };
            button.style.position = Position.Absolute;
            button.style.left = x - width / 2;
            button.style.top = y - height / 2;
            button.style.width = width;
            button.style.height = height;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.marginTop = button.style.marginBottom = 0;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = 10;
            button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 10;
            diagram.Add(button);
            body_buttons.Add(part, button);
        }

        private Transform ResolveHumanoidBone(HumanBodyBones part)
        {
            return humanoid_animator != null && humanoid_animator.avatar != null && humanoid_animator.isHuman
                ? humanoid_animator.GetBoneTransform(part) : null;
        }

        private void RefreshBodyButtons()
        {
            EnsureBoneGroups();
            foreach (var pair in body_buttons)
            {
                var bone = ResolveHumanoidBone(pair.Key);
                var available = bone != null && bone_groups.Any(group => group.bone == bone);
                pair.Value.SetEnabled(available && !EditorApplication.isPlaying && !EditorUtility.IsPersistent(target_renderer));
                pair.Value.tooltip = available ? bone.name + "：クリックで生成／選択" : "対応するHumanoidボーンまたはメッシュウェイトがありません";
                pair.Value.style.backgroundColor = selected_collider != null && selected_collider.rootTransform == bone
                    ? new Color(0.15f, 0.55f, 0.45f) : StyleKeyword.Null;
            }
        }

        private SliderInt CreateMillimeterSlider(string label, bool is_radius)
        {
            var slider = new SliderInt(label, 10, 500) { showInputField = true };
            slider.RegisterValueChangedCallback(evt =>
            {
                var millimeters = Mathf.Clamp(evt.newValue, 10, 500);
                slider.SetValueWithoutNotify(millimeters);
                EditSelectedCollider("Resize PhysBone Collider", collider =>
                {
                    if (is_radius) collider.radius = millimeters / 1000f;
                    else collider.height = millimeters / 1000f;
                });
            });
            return slider;
        }

        private void RefreshColliderControls()
        {
            if (radius_slider == null || status_box == null) return;
            var editable = CanEditSelectedCollider();
            collider_field.SetValueWithoutNotify(selected_collider);
            radius_slider.SetEnabled(editable);
            length_slider.SetEnabled(editable);
            position_field.SetEnabled(editable);
            rotation_field.SetEnabled(editable);
            align_rotation_button.SetEnabled(editable);
            mirror_button.SetEnabled(editable && TryGetMirrorBones(out _, out _, out _));
            status_box.messageType = HelpBoxMessageType.Info;
            status_box.text = "人体図をクリック、またはシーンのカプセルコライダーを指定してください。";
            if (selected_collider != null)
            {
                radius_slider.SetValueWithoutNotify(Mathf.RoundToInt(selected_collider.radius * 1000));
                length_slider.SetValueWithoutNotify(Mathf.RoundToInt(selected_collider.height * 1000));
                position_field.SetValueWithoutNotify(selected_collider.position * 1000f);
                rotation_field.SetValueWithoutNotify(selected_collider.rotation.eulerAngles);
                status_box.text = $"{selected_collider.name}：半径 {selected_collider.radius * 1000:F1} mm / 長さ {selected_collider.height * 1000:F1} mm";
                if (selected_collider.radius < 0.01f || selected_collider.radius > 0.5f ||
                    selected_collider.height < 0.01f || selected_collider.height > 0.5f)
                    status_box.text += "\n自動計算値は保持しています。スライダー操作時は10～500mmに制限されます。";
            }
            RefreshBodyButtons();
            UpdateCreateButton();
        }

        private bool CanEditSelectedCollider()
        {
            return selected_collider != null && !EditorUtility.IsPersistent(selected_collider) &&
                selected_collider.shapeType == VRCPhysBoneColliderBase.ShapeType.Capsule && !EditorApplication.isPlaying;
        }

        private void EditSelectedCollider(string undo_name, Action<VRCPhysBoneCollider> edit)
        {
            if (!CanEditSelectedCollider()) return;
            Undo.RecordObject(selected_collider, undo_name);
            edit(selected_collider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(selected_collider);
            EditorUtility.SetDirty(selected_collider);
            RefreshColliderControls();
            SceneView.RepaintAll();
        }

        private bool TryGetMirrorBones(out Animator animator, out Transform source_bone, out Transform opposite_bone)
        {
            source_bone = selected_collider != null ? selected_collider.rootTransform : null;
            opposite_bone = null;
            animator = source_bone != null ? source_bone.GetComponentInParent<Animator>() : null;
            if (humanoid_animator != null && source_bone != null && source_bone.IsChildOf(humanoid_animator.transform))
                animator = humanoid_animator;
            if (animator == null || animator.avatar == null || !animator.isHuman) return false;
            for (var i = 0; i < paired_bones.Length; i++)
            {
                if (animator.GetBoneTransform(paired_bones[i]) != source_bone) continue;
                opposite_bone = animator.GetBoneTransform(paired_bones[i ^ 1]);
                return opposite_bone != null && !EditorUtility.IsPersistent(opposite_bone);
            }
            return false;
        }

        private void CreateMirroredCollider()
        {
            if (!CanEditSelectedCollider() || !TryGetMirrorBones(out var animator, out var source_bone, out var opposite_bone)) return;
            Undo.IncrementCurrentGroup();
            var undo_group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Mirror PhysBone Collider");
            try
            {
                var source = selected_collider;
                var scale_ratio = GetUniformBoneScale(source_bone) / GetUniformBoneScale(opposite_bone);
                var normal = animator.transform.right;
                var world_position = source_bone.TransformPoint(source.position);
                var mirrored_position = animator.transform.position +
                    ReflectVector(world_position - animator.transform.position, normal);
                var world_rotation = source_bone.rotation * source.rotation;
                var mirrored_rotation = Quaternion.LookRotation(
                    ReflectVector(world_rotation * Vector3.forward, normal),
                    ReflectVector(world_rotation * Vector3.up, normal));
                var parent = source.transform.parent;
                var name = GameObjectUtility.GetUniqueNameForSibling(parent, opposite_bone.name + holder_name_suffix);
                var holder = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(holder, "Mirror PhysBone Collider");
                SceneManager.MoveGameObjectToScene(holder, source.gameObject.scene);
                Undo.SetTransformParent(holder.transform, parent, "Parent Mirrored Collider");
                holder.transform.localPosition = Vector3.zero;
                holder.transform.localRotation = Quaternion.identity;
                holder.transform.localScale = Vector3.one;
                var mirrored = Undo.AddComponent<VRCPhysBoneCollider>(holder);
                Undo.RecordObject(mirrored, "Configure Mirrored Collider");
                EditorUtility.CopySerialized(source, mirrored);
                mirrored.rootTransform = opposite_bone;
                mirrored.position = opposite_bone.InverseTransformPoint(mirrored_position);
                mirrored.rotation = Quaternion.Inverse(opposite_bone.rotation) * mirrored_rotation;
                mirrored.radius = source.radius * scale_ratio;
                mirrored.height = source.height * scale_ratio;
                EditorUtility.SetDirty(mirrored);
                selected_collider = mirrored;
                Selection.activeObject = holder;
                RefreshColliderControls();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undo_group);
                status_box.text = exception.Message;
                status_box.messageType = HelpBoxMessageType.Error;
            }
            finally
            {
                Undo.CollapseUndoOperations(undo_group);
            }
        }

        private static Vector3 ReflectVector(Vector3 vector, Vector3 normal)
        {
            return vector - 2f * Vector3.Dot(vector, normal) * normal;
        }

        private static float GetUniformBoneScale(Transform bone)
        {
            var scale = bone.lossyScale;
            if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f ||
                !Mathf.Approximately(scale.x, scale.y) || !Mathf.Approximately(scale.x, scale.z))
                throw new InvalidOperationException("正確にミラーするため、左右ボーンのワールドスケールは正の均一スケールにしてください。");
            return scale.x;
        }

        private void RefreshTargetBoneField()
        {
            if (target_bone_container == null)
            {
                return;
            }

            EnsureBoneGroups();
            target_bone_container.Clear();

            if (target_renderer == null || bone_groups.Count == 0)
            {
                var target_bone_field = CreateObjectField<Transform>("Target Bone", target_bone, true);
                target_bone_field.RegisterValueChangedCallback(evt =>
                {
                    target_bone = evt.newValue as Transform;
                    selected_bone_index = bone_groups.FindIndex(group => group.bone == target_bone);
                    UpdateCreateButton();
                });
                target_bone_container.Add(target_bone_field);
                return;
            }

            selected_bone_index = Mathf.Max(0, bone_groups.FindIndex(group => group.bone == target_bone));
            var choices = bone_group_labels.ToList();
            var popup = new PopupField<string>("Target Bone", choices, selected_bone_index);
            popup.RegisterValueChangedCallback(evt =>
            {
                var next_index = choices.IndexOf(evt.newValue);
                if (next_index >= 0 && next_index < bone_groups.Count)
                {
                    selected_bone_index = next_index;
                    target_bone = bone_groups[next_index].bone;
                    UpdateCreateButton();
                }
            });
            target_bone_container.Add(popup);
        }

        private void EnsureBoneGroups()
        {
            var bone_count = target_renderer != null && target_renderer.bones != null ? target_renderer.bones.Length : -1;
            if (target_renderer == cached_renderer && bone_count == cached_bone_count)
            {
                return;
            }

            RebuildBoneGroups();
        }

        private void RebuildBoneGroups()
        {
            cached_renderer = target_renderer;
            cached_bone_count = target_renderer != null && target_renderer.bones != null ? target_renderer.bones.Length : -1;
            bone_groups = PhysBoneWeightColliderGenerator.GetWeightedBoneGroups(target_renderer);
            bone_group_labels = bone_groups.Select(FormatBoneGroupLabel).ToArray();

            selected_bone_index = bone_groups.FindIndex(group => group.bone == target_bone);
            if (selected_bone_index < 0 && bone_groups.Count > 0)
            {
                selected_bone_index = 0;
                target_bone = bone_groups[0].bone;
            }
        }

        private static string FormatBoneGroupLabel(PhysBoneWeightColliderGenerator.BoneWeightGroup group)
        {
            return $"{group.bone.name}  ({group.weighted_vertex_count} verts, max {group.max_weight:F2})";
        }

        private static Label CreateTitle(string text)
        {
            return new Label(text)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginBottom = 4
                }
            };
        }

        private static ObjectField CreateObjectField<T>(string label, UnityEngine.Object value, bool allow_scene_objects)
            where T : UnityEngine.Object
        {
            return new ObjectField(label)
            {
                objectType = typeof(T),
                allowSceneObjects = allow_scene_objects,
                value = value
            };
        }

        private static Slider CreateSlider(string label, float value, float low_value, float high_value, Action<float> on_change)
        {
            var slider = new Slider(label, low_value, high_value)
            {
                value = value,
                showInputField = true
            };
            slider.RegisterValueChangedCallback(evt => on_change(evt.newValue));
            return slider;
        }

        private void UpdateCreateButton()
        {
            create_button?.SetEnabled(target_renderer != null && target_bone != null &&
                !EditorApplication.isPlaying && !EditorUtility.IsPersistent(target_renderer) &&
                !EditorUtility.IsPersistent(target_bone));
        }
    }

    public sealed class PhysBoneWeightColliderSettings
    {
        public float weight_threshold = 0.2f;
        public float percentile_clip = 2f;
        public float radius_percentile = 95f;
        public float diameter_padding_meters = 0.01f;
        public string holder_name = "auto_physbone_collider";
        public Transform holder_parent;
    }

    public readonly struct PhysBoneWeightColliderResult
    {
        public readonly GameObject holder;
        public readonly VRCPhysBoneCollider collider;
        public readonly int vertex_count;
        public readonly float radius;
        public readonly float height;
        public readonly Vector3 position;

        public PhysBoneWeightColliderResult(GameObject holder, VRCPhysBoneCollider collider, int vertex_count, float radius, float height, Vector3 position)
        {
            this.holder = holder;
            this.collider = collider;
            this.vertex_count = vertex_count;
            this.radius = radius;
            this.height = height;
            this.position = position;
        }

        public string ToLogString()
        {
            return $"[PhysBoneWeightColliderGenerator] holder={holder.name}, root={collider.rootTransform.name}, vertices={vertex_count}, radius={radius:F4}, height={height:F4}, position={position}";
        }
    }

    public static class PhysBoneWeightColliderGenerator
    {
        public static PhysBoneWeightColliderResult CreateCapsule(
            SkinnedMeshRenderer skinned_mesh_renderer,
            Transform target_bone,
            PhysBoneWeightColliderSettings settings,
            bool use_undo)
        {
            if (skinned_mesh_renderer == null)
            {
                throw new ArgumentNullException(nameof(skinned_mesh_renderer));
            }

            if (target_bone == null)
            {
                throw new ArgumentNullException(nameof(target_bone));
            }

            var mesh = skinned_mesh_renderer.sharedMesh;
            if (mesh == null)
            {
                throw new InvalidOperationException("Target renderer has no shared mesh.");
            }

            var bone_index = Array.IndexOf(skinned_mesh_renderer.bones, target_bone);
            if (bone_index < 0)
            {
                throw new InvalidOperationException($"Bone '{target_bone.name}' is not included in renderer '{skinned_mesh_renderer.name}'.");
            }

            var points = CollectWeightedBonePoints(skinned_mesh_renderer, mesh, bone_index, settings.weight_threshold);
            if (points.Count < 2)
            {
                throw new InvalidOperationException($"Not enough vertices found for '{target_bone.name}' at threshold {settings.weight_threshold}.");
            }

            var capsule = CalculateCapsule(points, settings);
            var parent = settings.holder_parent != null ? settings.holder_parent : skinned_mesh_renderer.transform;
            var holder = new GameObject(string.IsNullOrWhiteSpace(settings.holder_name) ? target_bone.name + "_PB_collider" : settings.holder_name);

            if (use_undo)
            {
                Undo.RegisterCreatedObjectUndo(holder, "Create PhysBone Collider Holder");
                Undo.SetTransformParent(holder.transform, parent, "Parent PhysBone Collider Holder");
            }
            else
            {
                holder.transform.SetParent(parent, false);
            }

            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;

            var collider = use_undo
                ? Undo.AddComponent<VRCPhysBoneCollider>(holder)
                : holder.AddComponent<VRCPhysBoneCollider>();

            if (use_undo)
            {
                Undo.RecordObject(collider, "Configure PhysBone Collider");
            }

            collider.rootTransform = target_bone;
            collider.shapeType = VRCPhysBoneColliderBase.ShapeType.Capsule;
            collider.insideBounds = false;
            collider.radius = capsule.radius;
            collider.height = capsule.height;
            collider.position = capsule.position;
            collider.rotation = Quaternion.identity;
            collider.bonesAsSpheres = false;
            EditorUtility.SetDirty(collider);

            return new PhysBoneWeightColliderResult(holder, collider, capsule.vertex_count, capsule.radius, capsule.height, capsule.position);
        }

        private static List<Vector3> CollectWeightedBonePoints(
            SkinnedMeshRenderer skinned_mesh_renderer,
            Mesh mesh,
            int bone_index,
            float weight_threshold)
        {
            var points = new List<Vector3>();
            var vertices = mesh.vertices;
            var weights = mesh.boneWeights;
            var bindposes = mesh.bindposes;
            var bone = skinned_mesh_renderer.bones[bone_index];
            var has_bindpose = bindposes != null && bone_index < bindposes.Length;
            var bindpose = has_bindpose ? bindposes[bone_index] : Matrix4x4.identity;

            for (var i = 0; i < vertices.Length && i < weights.Length; i++)
            {
                var weight = GetWeight(weights[i], bone_index);
                if (weight < weight_threshold)
                {
                    continue;
                }

                if (has_bindpose)
                {
                    points.Add(bindpose.MultiplyPoint3x4(vertices[i]));
                }
                else
                {
                    var world_point = skinned_mesh_renderer.transform.TransformPoint(vertices[i]);
                    points.Add(bone.InverseTransformPoint(world_point));
                }
            }

            return points;
        }

        public static List<BoneWeightGroup> GetWeightedBoneGroups(SkinnedMeshRenderer skinned_mesh_renderer)
        {
            var groups = new List<BoneWeightGroup>();
            if (skinned_mesh_renderer == null || skinned_mesh_renderer.sharedMesh == null || skinned_mesh_renderer.bones == null)
            {
                return groups;
            }

            var bones = skinned_mesh_renderer.bones;
            var weights = skinned_mesh_renderer.sharedMesh.boneWeights;
            var weight_sums = new float[bones.Length];
            var max_weights = new float[bones.Length];
            var vertex_counts = new int[bones.Length];

            foreach (var bone_weight in weights)
            {
                AddBoneWeightGroupStats(bone_weight.boneIndex0, bone_weight.weight0, bones.Length, weight_sums, max_weights, vertex_counts);
                AddBoneWeightGroupStats(bone_weight.boneIndex1, bone_weight.weight1, bones.Length, weight_sums, max_weights, vertex_counts);
                AddBoneWeightGroupStats(bone_weight.boneIndex2, bone_weight.weight2, bones.Length, weight_sums, max_weights, vertex_counts);
                AddBoneWeightGroupStats(bone_weight.boneIndex3, bone_weight.weight3, bones.Length, weight_sums, max_weights, vertex_counts);
            }

            for (var i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || vertex_counts[i] == 0)
                {
                    continue;
                }

                groups.Add(new BoneWeightGroup(i, bones[i], vertex_counts[i], weight_sums[i], max_weights[i]));
            }

            return groups
                .OrderBy(group => group.bone.name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(group => EditorObjectHelper.GetObjectId(group.bone))
                .ToList();
        }

        private static void AddBoneWeightGroupStats(
            int bone_index,
            float weight,
            int bone_count,
            float[] weight_sums,
            float[] max_weights,
            int[] vertex_counts)
        {
            if (bone_index < 0 || bone_index >= bone_count || weight <= 0f)
            {
                return;
            }

            weight_sums[bone_index] += weight;
            max_weights[bone_index] = Mathf.Max(max_weights[bone_index], weight);
            vertex_counts[bone_index]++;
        }

        private static float GetWeight(BoneWeight bone_weight, int bone_index)
        {
            var weight = 0f;
            if (bone_weight.boneIndex0 == bone_index) weight = Mathf.Max(weight, bone_weight.weight0);
            if (bone_weight.boneIndex1 == bone_index) weight = Mathf.Max(weight, bone_weight.weight1);
            if (bone_weight.boneIndex2 == bone_index) weight = Mathf.Max(weight, bone_weight.weight2);
            if (bone_weight.boneIndex3 == bone_index) weight = Mathf.Max(weight, bone_weight.weight3);
            return weight;
        }

        private static CapsuleData CalculateCapsule(List<Vector3> raw_points, PhysBoneWeightColliderSettings settings)
        {
            var clip = Mathf.Clamp(settings.percentile_clip, 0f, 49f);
            var y_values = raw_points.Select(point => point.y).OrderBy(value => value).ToArray();
            var min_y = Percentile(y_values, clip);
            var max_y = Percentile(y_values, 100f - clip);
            var points = raw_points.Where(point => point.y >= min_y && point.y <= max_y).ToList();

            if (points.Count < 2)
            {
                points = raw_points;
                min_y = y_values.First();
                max_y = y_values.Last();
            }

            var radial_values = points
                .Select(point => Mathf.Sqrt(point.x * point.x + point.z * point.z))
                .OrderBy(value => value)
                .ToArray();
            var radius_padding = Mathf.Max(0f, settings.diameter_padding_meters) * 0.5f;
            var radius = Percentile(radial_values, Mathf.Clamp(settings.radius_percentile, 1f, 100f)) + radius_padding;
            var height = Mathf.Max(max_y - min_y + radius * 2f, radius * 2f);
            var position = new Vector3(0f, (min_y + max_y) * 0.5f, 0f);

            return new CapsuleData(points.Count, radius, height, position);
        }

        private static float Percentile(float[] sorted_values, float percentile)
        {
            if (sorted_values == null || sorted_values.Length == 0)
            {
                return 0f;
            }

            if (sorted_values.Length == 1)
            {
                return sorted_values[0];
            }

            var position = Mathf.Clamp01(percentile / 100f) * (sorted_values.Length - 1);
            var lower = Mathf.FloorToInt(position);
            var upper = Mathf.CeilToInt(position);
            if (lower == upper)
            {
                return sorted_values[lower];
            }

            return Mathf.Lerp(sorted_values[lower], sorted_values[upper], position - lower);
        }

        private readonly struct CapsuleData
        {
            public readonly int vertex_count;
            public readonly float radius;
            public readonly float height;
            public readonly Vector3 position;

            public CapsuleData(int vertex_count, float radius, float height, Vector3 position)
            {
                this.vertex_count = vertex_count;
                this.radius = radius;
                this.height = height;
                this.position = position;
            }
        }

        public readonly struct BoneWeightGroup
        {
            public readonly int bone_index;
            public readonly Transform bone;
            public readonly int weighted_vertex_count;
            public readonly float weight_sum;
            public readonly float max_weight;

            public BoneWeightGroup(int bone_index, Transform bone, int weighted_vertex_count, float weight_sum, float max_weight)
            {
                this.bone_index = bone_index;
                this.bone = bone;
                this.weighted_vertex_count = weighted_vertex_count;
                this.weight_sum = weight_sum;
                this.max_weight = max_weight;
            }
        }
    }

}
