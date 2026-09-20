using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityBlenderPoseSync.World;
using Object = UnityEngine.Object;

namespace UnityBlenderPoseSync.World.Editor
{
    /// <summary>
    /// The whole Pose Sync front end: pick the Animators to stream, and Play.
    /// Nothing is added to the scene - the settings live in EditorPrefs and
    /// <see cref="PoseSyncManagerBootstrap"/> spawns a throwaway manager on Play.
    /// </summary>
    public sealed class PoseSyncManagerWindow : EditorWindow
    {
        private const string MenuPath = "D9speed/Animations/Pose Sync Manager";

        private PoseSyncSettings settings;
        private VisualElement avatar_list;
        private VisualElement cloth_list;
        private Label status_label;

        [MenuItem(MenuPath, false, 11)]
        private static void Open()
        {
            var window = GetWindow<PoseSyncManagerWindow>("Pose Sync Manager");
            window.minSize = new Vector2(380, 320);
        }

        public void CreateGUI()
        {
            settings = PoseSyncSettingsStore.Load();

            var root = rootVisualElement;
            root.style.paddingLeft = 8;
            root.style.paddingRight = 8;
            root.style.paddingTop = 8;
            root.style.paddingBottom = 8;

            var header = new Label("送信するアバターの Animator を並べてください。");
            header.style.whiteSpace = WhiteSpace.Normal;
            header.style.marginBottom = 6;
            root.Add(header);

            // ---- avatar rows ----
            var box = new VisualElement
            {
                style =
                {
                    borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftColor = Color.gray, borderRightColor = Color.gray,
                    borderTopColor = Color.gray, borderBottomColor = Color.gray,
                    paddingLeft = 6, paddingRight = 6, paddingTop = 6, paddingBottom = 6,
                    marginBottom = 6,
                },
            };
            avatar_list = new ScrollView { style = { flexGrow = 1, minHeight = 140 } };
            box.Add(avatar_list);

            var row_buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            row_buttons.Add(new Button(AddEmptyRow) { text = "＋ アバターを追加" });
            row_buttons.Add(new Button(AddFromScene) { text = "シーンから収集" });
            box.Add(row_buttons);
            root.Add(box);

            // ---- Merge Armature Tracking (for Clothes) ----
            var cloth_box = new VisualElement
            {
                style =
                {
                    borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftColor = Color.gray, borderRightColor = Color.gray,
                    borderTopColor = Color.gray, borderBottomColor = Color.gray,
                    paddingLeft = 6, paddingRight = 6, paddingTop = 6, paddingBottom = 6,
                    marginBottom = 6,
                },
            };
            var cloth_title = new Label("Merge Armature Tracking (for Clothes)");
            cloth_title.style.unityFontStyleAndWeight = FontStyle.Bold;
            cloth_box.Add(cloth_title);
            cloth_list = new ScrollView { style = { minHeight = 80, maxHeight = 220 } };
            cloth_box.Add(cloth_list);
            cloth_box.Add(new Button(AddCloth) { text = "＋ 衣装を追加" });
            root.Add(cloth_box);

            // ---- camera ----
            var camera_box = new VisualElement
            {
                style =
                {
                    borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopWidth = 1, borderBottomWidth = 1,
                    borderLeftColor = Color.gray, borderRightColor = Color.gray,
                    borderTopColor = Color.gray, borderBottomColor = Color.gray,
                    paddingLeft = 6, paddingRight = 6, paddingTop = 6, paddingBottom = 6,
                    marginBottom = 6,
                },
            };
            var camera_toggle = new Toggle("カメラ同期") { value = settings.send_camera };
            var camera_source_field = new EnumField("Unity側ソース", settings.camera_source);
            var camera_field = new ObjectField("Camera")
            {
                objectType = typeof(Camera),
                allowSceneObjects = true,
                value = PoseSyncSettingsStore.ResolveCamera(settings),
            };
            camera_box.Add(camera_toggle);
            camera_box.Add(camera_source_field);
            camera_box.Add(camera_field);
            root.Add(camera_box);

            void RefreshCameraFields()
            {
                camera_source_field.SetEnabled(settings.send_camera);
                camera_field.SetEnabled(settings.send_camera);
                camera_field.style.display = settings.camera_source == PoseSyncCameraSource.CameraObject
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            camera_toggle.RegisterValueChangedCallback(e =>
            {
                settings.send_camera = e.newValue;
                RefreshCameraFields();
                Save();
            });
            camera_source_field.RegisterValueChangedCallback(e =>
            {
                settings.camera_source = (PoseSyncCameraSource)e.newValue;
                RefreshCameraFields();
                Save();
            });
            camera_field.RegisterValueChangedCallback(e =>
            {
                PoseSyncSettingsStore.RecordCamera(settings, e.newValue as Camera);
                Save();
            });
            RefreshCameraFields();

            // ---- connection ----
            var host_field = new TextField("Host") { value = settings.host };
            host_field.RegisterValueChangedCallback(e => { settings.host = e.newValue; Save(); });
            var port_field = new IntegerField("Port") { value = settings.port };
            port_field.RegisterValueChangedCallback(e => { settings.port = e.newValue; Save(); });
            var interval_field = new IntegerField("Send Interval Frames") { value = settings.send_interval_frames };
            interval_field.tooltip = "1 = 毎フレーム送信。2 にすると 30fps 相当になり負荷が半分になります。";
            interval_field.RegisterValueChangedCallback(e =>
            {
                settings.send_interval_frames = Mathf.Max(1, e.newValue);
                Save();
            });
            var auto_toggle = new Toggle("Play時に自動で開始") { value = settings.auto_spawn };
            auto_toggle.RegisterValueChangedCallback(e => { settings.auto_spawn = e.newValue; Save(); });

            root.Add(host_field);
            root.Add(port_field);
            root.Add(interval_field);
            root.Add(auto_toggle);

            var announce_button = new Button(() =>
            {
                AnnounceToBlender();
                status_label.text = "Blender へアバター一覧を送信しました（受信側が Listening のときのみ届きます）。";
            })
            { text = "Blender へスロット通知" };
            announce_button.tooltip = "登録済みのアバター/衣装の名前を Blender の Pose Sync パネルへ送ります。\n" +
                                      "Blender 側の Start を後から押した場合などに使ってください。\n" +
                                      "（一覧の変更時とウィンドウを開いた時は自動で送信されます）";
            announce_button.style.marginTop = 4;
            root.Add(announce_button);

            status_label = new Label();
            status_label.style.whiteSpace = WhiteSpace.Normal;
            status_label.style.marginTop = 8;
            root.Add(status_label);

            RebuildRows();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            // Announce on open, so just opening the manager is enough for the
            // Blender panel to offer the right slots.
            QueueAnnounce();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                settings = PoseSyncSettingsStore.Load();
                RebuildRows();
            }
        }

        private double next_status_refresh;

        private void OnEditorUpdate()
        {
            // Only while playing, and only a few times a second.
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next_status_refresh)
            {
                return;
            }
            next_status_refresh = EditorApplication.timeSinceStartup + 0.5;
            UpdateStatus();
        }

        private void Save()
        {
            PoseSyncSettingsStore.Save(settings);
            UpdateStatus();
            QueueAnnounce();
        }

        // ---- edit-mode announce ------------------------------------------------
        // Blender's slot picker ("Slots From Received Avatars") only knows names it
        // has been told about, which used to require entering Play Mode once. This
        // pushes the configured names over a short-lived connection whenever the
        // list changes, so the slots can be assigned before Play ever runs.
        //
        // Deliberately NOT periodic: the receiver hands its connection over to
        // whichever client connected most recently, so a polling announce from this
        // project would kick a live stream from another Unity instance every tick.

        private bool announce_queued;

        /// <summary>Coalesce the per-keystroke Save() calls into one announce per tick.</summary>
        private void QueueAnnounce()
        {
            if (announce_queued)
            {
                return;
            }
            announce_queued = true;
            EditorApplication.delayCall += () =>
            {
                announce_queued = false;
                AnnounceToBlender();
            };
        }

        private void AnnounceToBlender()
        {
            // While playing, the spawned manager owns the connection; a second
            // connection would trigger the receiver's takeover and kick it.
            if (settings == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            var names = new List<string>();
            foreach (var avatar in settings.avatars)
            {
                if (avatar == null || !avatar.send || string.IsNullOrEmpty(avatar.animator_id))
                {
                    continue;
                }
                var name = avatar.avatar_name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    var animator = PoseSyncSettingsStore.Resolve(avatar);
                    name = animator != null ? animator.gameObject.name : "";
                }
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
            foreach (var cloth in settings.clothes)
            {
                if (cloth == null || !cloth.send || string.IsNullOrEmpty(cloth.root_name)
                    || string.IsNullOrWhiteSpace(cloth.avatar_name))
                {
                    continue;
                }
                if (!names.Contains(cloth.avatar_name))
                {
                    names.Add(cloth.avatar_name);
                }
            }

            var packet = PoseSyncMessagePack.Serialize(new AvatarListMessage
            {
                schema = "unity_blender_pose_sync.world",
                avatars = names.ToArray(),
            });
            var host = settings.host;
            var port = settings.port;

            // Fire-and-forget off the UI thread: when Blender is not running the
            // loopback connect fails fast, and either way the editor must not hitch.
            Task.Run(() =>
            {
                try
                {
                    using (var client = new TcpClient { NoDelay = true, SendTimeout = 1000 })
                    {
                        if (!client.ConnectAsync(host, port).Wait(500))
                        {
                            return;
                        }
                        var stream = client.GetStream();
                        var size = packet.Length + 1;
                        var header = new byte[5]
                        {
                            (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size,
                            PoseSyncManager.PacketKindControl,
                        };
                        stream.Write(header, 0, header.Length);
                        stream.Write(packet, 0, packet.Length);
                        stream.Flush();
                    }
                }
                catch
                {
                    // No receiver listening - nothing to announce to.
                }
            });
        }

        private void AddEmptyRow()
        {
            settings.avatars.Add(new PoseSyncAvatarSetting());
            Save();
            RebuildRows();
        }

        private void AddFromScene()
        {
#if UNITY_2022_2_OR_NEWER
            var animators = Object.FindObjectsByType<Animator>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
#else
            var animators = Object.FindObjectsOfType<Animator>(true);
#endif
            var known = new HashSet<string>(settings.avatars.Select(a => a.animator_id));
            var added = 0;
            foreach (var animator in animators)
            {
                if (!animator.isHuman)
                {
                    continue;
                }
                var id = PoseSyncSettingsStore.IdOf(animator);
                if (string.IsNullOrEmpty(id) || known.Contains(id))
                {
                    continue;
                }
                var setting = new PoseSyncAvatarSetting { avatar_name = animator.gameObject.name };
                PoseSyncSettingsStore.Record(setting, animator);
                settings.avatars.Add(setting);
                known.Add(id);
                added++;
            }
            Save();
            RebuildRows();
            status_label.text = added > 0
                ? $"{added} 体を追加しました。"
                : "追加できる Humanoid Animator は見つかりませんでした。";
        }

        private void RebuildRows()
        {
            avatar_list.Clear();

            if (settings.avatars.Count == 0)
            {
                avatar_list.Add(new Label("アバターが未登録です。「シーンから収集」を押してください。"));
            }

            for (var i = 0; i < settings.avatars.Count; i++)
            {
                var index = i;
                var avatar = settings.avatars[index];

                var entry_box = new VisualElement { style = { marginBottom = 8 } };

                var top = new VisualElement
                {
                    style = { flexDirection = FlexDirection.Row, alignItems = Align.Center },
                };
                top.Add(new Label($"{index + 1}.") { style = { width = 22, unityFontStyleAndWeight = FontStyle.Bold } });

                var field = new ObjectField
                {
                    objectType = typeof(Animator),
                    allowSceneObjects = true,
                    value = PoseSyncSettingsStore.Resolve(avatar),
                    style = { flexGrow = 1 },
                };
                field.RegisterValueChangedCallback(e =>
                {
                    var animator = e.newValue as Animator;
                    PoseSyncSettingsStore.Record(avatar, animator);
                    if (animator != null && string.IsNullOrWhiteSpace(avatar.avatar_name))
                    {
                        avatar.avatar_name = animator.gameObject.name;
                    }
                    Save();
                    RebuildRows();
                });
                top.Add(field);

                var remove = new Button(() =>
                {
                    settings.avatars.RemoveAt(index);
                    Save();
                    RebuildRows();
                })
                { text = "✕", style = { width = 24 } };
                top.Add(remove);
                entry_box.Add(top);

                var detail = new VisualElement { style = { marginLeft = 22 } };

                var name_field = new TextField("Blender側の名前") { value = avatar.avatar_name };
                name_field.tooltip = "Blender の Pose Sync パネルにある Avatars スロットと一致させます。";
                name_field.RegisterValueChangedCallback(e => { avatar.avatar_name = e.newValue; Save(); });
                detail.Add(name_field);

                var phys = new Toggle("Include All VRCPhysbone") { value = avatar.include_all_phys_bones };
                phys.tooltip = "スカート・髪・尻尾などの揺れボーンも送信します。";
                phys.RegisterValueChangedCallback(e => { avatar.include_all_phys_bones = e.newValue; Save(); });
                detail.Add(phys);

                var send = new Toggle("送信する") { value = avatar.send };
                send.RegisterValueChangedCallback(e => { avatar.send = e.newValue; Save(); });
                detail.Add(send);

                detail.Add(BuildExtraRootsSection(avatar));

                var resolved = PoseSyncSettingsStore.Resolve(avatar);
                if (resolved == null && !string.IsNullOrEmpty(avatar.animator_id))
                {
                    var warn = new Label("このシーンで見つかりません。選び直してください。");
                    warn.style.color = new Color(1f, 0.6f, 0.3f);
                    detail.Add(warn);
                }
                else if (resolved != null && !resolved.isHuman)
                {
                    var warn = new Label("Humanoid ではありません（PhysBoneのみ送信されます）。");
                    warn.style.color = new Color(1f, 0.8f, 0.3f);
                    detail.Add(warn);
                }

                entry_box.Add(detail);
                avatar_list.Add(entry_box);
            }

            RebuildClothRows();
            UpdateStatus();
        }

        private void AddCloth()
        {
            settings.clothes.Add(new PoseSyncClothSetting
            {
                owner_avatar_name = settings.avatars.Count > 0
                    ? PoseSyncSettingsStore.DisplayName(settings.avatars[0])
                    : "",
            });
            Save();
            RebuildRows();
        }

        /// <summary>
        /// Clothing merged in by Modular Avatar. The Transform is picked here in Edit
        /// Mode; what gets stored is the authored names of the PhysBone chain roots
        /// under it, because MA renames and reparents them the moment Play starts.
        /// </summary>
        private void RebuildClothRows()
        {
            if (cloth_list == null)
            {
                return;
            }
            cloth_list.Clear();

            if (settings.clothes.Count == 0)
            {
                var hint = new Label("MAでマージされる衣装のアーマチュア階層を追加します。\n" +
                                     "Play前のTransformを指定すると、配下のVRCPhysBoneを検知して追跡します。");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.opacity = 0.6f;
                cloth_list.Add(hint);
                return;
            }

            // Owner links match on the ANNOUNCED name (avatar_name, else the
            // Animator's object name) - the same resolution Spawn uses.
            var avatar_names = settings.avatars
                .Select(PoseSyncSettingsStore.DisplayName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            for (var i = 0; i < settings.clothes.Count; i++)
            {
                var index = i;
                var cloth = settings.clothes[index];

                var entry_box = new VisualElement { style = { marginBottom = 8 } };

                var top = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
                top.Add(new Label($"{index + 1}") { style = { width = 20, unityFontStyleAndWeight = FontStyle.Bold } });

                var field = new ObjectField
                {
                    objectType = typeof(Transform),
                    allowSceneObjects = true,
                    value = PoseSyncSettingsStore.ResolveClothRoot(cloth),
                    style = { flexGrow = 1 },
                };
                field.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
                field.RegisterValueChangedCallback(e =>
                {
                    PoseSyncSettingsStore.RecordCloth(cloth, e.newValue as Transform);
                    Save();
                    RebuildRows();
                });
                top.Add(field);
                top.Add(new Button(() =>
                {
                    settings.clothes.RemoveAt(index);
                    Save();
                    RebuildRows();
                })
                { text = "✕", style = { width = 24 } });
                entry_box.Add(top);

                var detail = new VisualElement { style = { marginLeft = 20 } };

                var name_field = new TextField("Blender側の名前") { value = cloth.avatar_name };
                name_field.tooltip = "Blender の Avatars スロットに、この名前で衣装アーマチュアを割り当てます。";
                name_field.RegisterValueChangedCallback(e => { cloth.avatar_name = e.newValue; Save(); });
                detail.Add(name_field);

                if (avatar_names.Contains(cloth.avatar_name))
                {
                    // Same name as a streamed avatar = one Blender runtime fought over
                    // by two defs. Spawn refuses these, so surface it right here.
                    var dup = new Label("アバターと同じ名前です。衝突するため送信されません。別名にしてください。");
                    dup.style.color = new Color(1f, 0.45f, 0.35f);
                    dup.style.whiteSpace = WhiteSpace.Normal;
                    detail.Add(dup);
                }

                if (avatar_names.Count > 0)
                {
                    var owner_index = avatar_names.IndexOf(cloth.owner_avatar_name);
                    if (owner_index < 0)
                    {
                        // The stored owner is not in the list any more (renamed or
                        // removed avatar). The popup is about to DISPLAY the first
                        // entry, so make the data say what the UI shows - leaving the
                        // stale value in place made Spawn skip the cloth while the
                        // window looked perfectly fine.
                        owner_index = 0;
                        cloth.owner_avatar_name = avatar_names[0];
                        PoseSyncSettingsStore.Save(settings);
                    }
                    var owner = new PopupField<string>("マージ先アバター", avatar_names, owner_index);
                    owner.tooltip = "この衣装がMAでマージされるアバター。Play後はここを探索します。";
                    owner.RegisterValueChangedCallback(e => { cloth.owner_avatar_name = e.newValue; Save(); });
                    detail.Add(owner);
                }
                else
                {
                    var warn = new Label("先に上のアバターを登録してください。");
                    warn.style.color = new Color(1f, 0.6f, 0.3f);
                    detail.Add(warn);
                }

                var humanoid = new Toggle("Humanoidボーンも送る") { value = cloth.include_humanoid };
                humanoid.tooltip = "衣装自体がヒューマノイドリグで、アバターと階層を一致させている場合に有効。\n" +
                                   "MAでマージされると衣装のHumanoidボーンはアバター側に統合されるため、\n" +
                                   "アバターのHumanoidボーンを衣装アーマチュアにも送ります。";
                humanoid.RegisterValueChangedCallback(e => { cloth.include_humanoid = e.newValue; Save(); });
                detail.Add(humanoid);

                var send = new Toggle("送信する") { value = cloth.send };
                send.RegisterValueChangedCallback(e => { cloth.send = e.newValue; Save(); });
                detail.Add(send);

                var tracked = new Label(cloth.tracked_bone_names.Count > 0
                    ? $"検知したPhysBoneチェーン: {cloth.tracked_bone_names.Count} 本 " +
                      $"({string.Join(", ", cloth.tracked_bone_names.Take(4))}" +
                      (cloth.tracked_bone_names.Count > 4 ? ", …)" : ")")
                    : cloth.include_humanoid
                        ? "VRCPhysBone なし（Humanoidボーンのみ送信）"
                        : "VRCPhysBone が見つかりません。Transform を指定してください。");
                tracked.style.whiteSpace = WhiteSpace.Normal;
                tracked.style.opacity = 0.7f;
                detail.Add(tracked);

                var refresh = new Button(() =>
                {
                    PoseSyncSettingsStore.RefreshClothTracking(settings);
                    Save();
                    RebuildRows();
                }) { text = "PhysBoneを再検知" };
                refresh.tooltip = "衣装の現在のPhysBoneを取り直します。Play開始直前にも自動で再検知します。";
                refresh.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
                detail.Add(refresh);

                entry_box.Add(detail);
                cloth_list.Add(entry_box);
            }
        }

        /// <summary>
        /// "任意階層の送信" section: hierarchies the user adds by hand on top of the
        /// automatic collection. Picked as a Transform here, but stored by name and
        /// avatar-relative path, because Merge Armature reparents and renames these
        /// objects the moment Play Mode starts.
        /// </summary>
        private VisualElement BuildExtraRootsSection(PoseSyncAvatarSetting avatar)
        {
            var animator = PoseSyncSettingsStore.Resolve(avatar);

            var box = new VisualElement
            {
                style =
                {
                    marginTop = 4, marginBottom = 2,
                    paddingLeft = 6, paddingTop = 4, paddingBottom = 4,
                    borderLeftWidth = 2, borderLeftColor = new Color(0.4f, 0.5f, 0.6f),
                },
            };

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            header.Add(new Label("任意階層の送信") { style = { flexGrow = 1 } });
            header.Add(new Button(() =>
            {
                avatar.extra_roots.Add(new PoseSyncExtraRoot());
                Save();
                RebuildRows();
            })
            { text = "＋" });
            box.Add(header);

            if (avatar.extra_roots.Count == 0)
            {
                var hint = new Label("自動収集で拾えない階層をここに追加します（例: Chest/Ribbon_Root）。");
                hint.style.whiteSpace = WhiteSpace.Normal;
                hint.style.opacity = 0.6f;
                box.Add(hint);
                return box;
            }

            for (var i = 0; i < avatar.extra_roots.Count; i++)
            {
                var index = i;
                var extra = avatar.extra_roots[index];

                var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

                var field = new ObjectField
                {
                    objectType = typeof(Transform),
                    allowSceneObjects = true,
                    value = animator != null
                        ? PoseSyncSettingsStore.ResolveExtraRoot(animator, extra)
                        : null,
                    style = { flexGrow = 1 },
                };
                field.RegisterValueChangedCallback(e =>
                {
                    PoseSyncSettingsStore.RecordExtraRoot(extra, PoseSyncSettingsStore.Resolve(avatar),
                                                          e.newValue as Transform);
                    Save();
                    RebuildRows();
                });
                row.Add(field);

                row.Add(new Button(() =>
                {
                    avatar.extra_roots.RemoveAt(index);
                    Save();
                    RebuildRows();
                })
                { text = "✕", style = { width = 24 } });
                box.Add(row);

                var opts = new VisualElement { style = { flexDirection = FlexDirection.Row, marginLeft = 4 } };
                var desc = new Toggle("配下も含む") { value = extra.include_descendants };
                desc.RegisterValueChangedCallback(e => { extra.include_descendants = e.newValue; Save(); });
                var on = new Toggle("送信") { value = extra.send };
                on.RegisterValueChangedCallback(e => { extra.send = e.newValue; Save(); });
                opts.Add(desc);
                opts.Add(on);
                box.Add(opts);

                if (!string.IsNullOrEmpty(extra.bone_name))
                {
                    var info = new Label($"Blender側のボーン名: {extra.bone_name}");
                    info.style.opacity = 0.6f;
                    info.style.marginLeft = 4;
                    box.Add(info);
                }
            }
            return box;
        }

        private void UpdateStatus()
        {
            if (status_label == null)
            {
                return;
            }

            var lines = new List<string>();
            var configured = settings.avatars.Count(a => a.send && !string.IsNullOrEmpty(a.animator_id));
            lines.Add($"登録: {settings.avatars.Count} 体 / 送信対象: {configured} 体");

            if (EditorApplication.isPlaying)
            {
                var manager = PoseSyncManagerBootstrap.FindSpawned();
                if (manager == null)
                {
                    lines.Add("実行中: マネージャーが生成されていません。");
                }
                else
                {
                    lines.Add($"実行中: {(manager.IsStreaming ? "送信中" : "接続待ち")}   " +
                              $"アバター {manager.ActiveAvatarCount} / 合計ボーン {manager.TotalBoneCount}");
                    foreach (var pair in manager.DescribeAvatars())
                    {
                        lines.Add($"　・{pair.Key}: {pair.Value} bones");
                    }
                }
            }
            else
            {
                lines.Add("Play を押すと自動でマネージャーが生成されます（シーンには何も残りません）。");
            }

            lines.Add("Blender側: Pose Sync パネルの Avatars で「Slots From Received Avatars」を押すと自動登録されます。");
            status_label.text = string.Join("\n", lines);
        }
    }
}
