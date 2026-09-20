using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using D9speed_BaseEditorUtils;

namespace D9speedBaseEditorUtil
{
    public sealed class PackageComponentReportCompareWindow : EditorWindow
    {
        private const string menu_path = "D9speed/ExportBatch/Package Component Report Compare";
        [SerializeField] private string reference_json_path = "";
        [SerializeField] private List<string> target_json_paths = new List<string>();
        [SerializeField] private string filter_text = "";
        [SerializeField] private bool only_differences = false;
        [SerializeField] private int sort_column = 0;
        [SerializeField] private bool sort_descending = false;
        [SerializeField] private int selected_row = -1;
        [SerializeField] private float[] column_widths =
        {
            90f, 130f, 220f, 80f, 260f
        };

        private PackageReport reference_report = new PackageReport();
        private List<PackageReport> target_reports = new List<PackageReport>();
        private List<CompareRow> rows = new List<CompareRow>();
        private List<CompareRow> visible_rows = new List<CompareRow>();
        private List<TableColumn> columns = new List<TableColumn>();
        private MultiColumnListView report_table;
        private VisualElement paths_container;
        private ScrollView summary_container;
        private ScrollView detail_container;
        private Label result_count;
        private Label empty_state;
        private bool rebuilding_table;

        [MenuItem(menu_path, false, 101)]
        public static void Open()
        {
            var window = GetWindow<PackageComponentReportCompareWindow>();
            window.titleContent = new GUIContent("Component Report Compare");
            window.minSize = new Vector2(920f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            if (target_json_paths == null) target_json_paths = new List<string>();
            if (target_reports == null) target_reports = new List<PackageReport>();
            if (rows == null) rows = new List<CompareRow>();
            if (visible_rows == null) visible_rows = new List<CompareRow>();
            if (columns == null) columns = new List<TableColumn>();
            RebuildColumns();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            EditorUiTheme.Apply(root);
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/io.github.d9speed.package_exporter/Editor/report_compare.uss");
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("report_compare");
            minSize = new Vector2(760, 600);

            var content = new VisualElement();
            content.AddToClassList("d9_content");
            content.AddToClassList("d9_grow");
            content.AddToClassList("d9_window_body");
            root.Add(content);
            content.Add(EditorUiControls.Header("Component Report Compare", "出力レポートを比較し、コンポーネントの数と配置の違いを確認します。"));
            var setup_scroll = new ScrollView(ScrollViewMode.Vertical);
            setup_scroll.AddToClassList("report_setup");
            content.Add(setup_scroll);
            var setup = EditorUiControls.Section(setup_scroll, "比較するレポート");
            var paths = new ScrollView(ScrollViewMode.Vertical);
            paths.AddToClassList("report_paths");
            paths_container = paths.contentContainer;
            setup.Add(paths);
            var actions = EditorUiControls.Row();
            actions.Add(EditorUiControls.Button("比較対象を追加…", AddTargetJson, true));
            actions.Add(EditorUiControls.Button("再読み込み", ReloadReports));
            actions.Add(EditorUiControls.Button("クリア", () =>
            {
                reference_json_path = "";
                target_json_paths.Clear();
                ReloadReports();
            }));
            setup.Add(actions);

            var summary = EditorUiControls.Foldout("レポートの概要");
            summary.viewDataKey = "report_summary";
            summary_container = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            summary_container.AddToClassList("report_summary");
            summary.Add(summary_container);
            setup.Add(summary);

            var filter_row = EditorUiControls.Row(false);
            var filter = EditorUiControls.Field(new TextField("絞り込み（種類・名前・配置場所）") { value = filter_text, name = "report_filter" });
            filter.RegisterValueChangedCallback(evt => { filter_text = evt.newValue; RefreshTable(); });
            filter_row.Add(filter);
            filter_row.Add(EditorUiControls.Toggle("差分のみ", only_differences, value => { only_differences = value; RefreshTable(); }));
            content.Add(filter_row);
            result_count = EditorUiControls.Label("");
            content.Add(result_count);

            var table_container = new VisualElement();
            table_container.AddToClassList("d9_grow");
            table_container.AddToClassList("d9_table_container");
            report_table = new MultiColumnListView
            {
                name = "report_table",
                fixedItemHeight = 32,
                selectionType = SelectionType.Single,
                sortingEnabled = true,
                reorderable = false
            };
            report_table.AddToClassList("d9_table");
            report_table.columns.reorderable = false;
            report_table.selectionChanged += selected =>
            {
                if (rebuilding_table) return;
                var row = selected.OfType<CompareRow>().FirstOrDefault();
                selected_row = row == null ? -1 : rows.IndexOf(row);
                RefreshDetails();
            };
            report_table.columnSortingChanged += () =>
            {
                if (rebuilding_table) return;
                var sorted = report_table.sortedColumns.FirstOrDefault();
                if (sorted != null)
                {
                    sort_column = sorted.columnIndex;
                    sort_descending = sorted.direction == SortDirection.Descending;
                }
                RefreshTable();
            };
            table_container.Add(report_table);
            empty_state = EditorUiControls.Label("", "d9_empty_state");
            empty_state.pickingMode = PickingMode.Ignore;
            table_container.Add(empty_state);
            content.Add(table_container);

            var detail = EditorUiControls.Foldout("選択したコンポーネントの配置場所", true);
            detail.AddToClassList("report_detail_foldout");
            detail.viewDataKey = "report_details";
            detail_container = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            detail_container.AddToClassList("report_details");
            detail.Add(detail_container);
            content.Add(detail);
            ReloadReports();
        }

        private void OnInspectorUpdate()
        {
            EditorUiTheme.RefreshTheme(rootVisualElement);
        }

        private void OnDisable()
        {
            CaptureColumnWidths();
        }

        private void CaptureColumnWidths()
        {
            if (report_table == null || column_widths == null) return;
            for (int i = 0; i < Math.Min(report_table.columns.Count, column_widths.Length); i++)
                column_widths[i] = report_table.columns[i].width.value;
        }

        private void RefreshComparisonUI()
        {
            if (paths_container == null) return;
            paths_container.Clear();
            AddPathRow("基準 JSON", reference_json_path, SelectReferenceJson, null);
            for (int i = 0; i < target_json_paths.Count; i++)
            {
                int index = i;
                AddPathRow("比較対象 " + (i + 1), target_json_paths[i], () => SelectTargetJson(index), () =>
                {
                    CaptureColumnWidths();
                    target_json_paths.RemoveAt(index);
                    ReloadReports();
                });
            }
            summary_container.Clear();
            var summaries = EditorUiControls.Row(false);
            AddReportPane(summaries, "基準", reference_report);
            for (int i = 0; i < target_reports.Count; i++) AddReportPane(summaries, "比較対象 " + (i + 1), target_reports[i]);
            summary_container.Add(summaries);

            rebuilding_table = true;
            report_table.columns.Clear();
            for (int i = 0; i < columns.Count; i++)
            {
                var descriptor = columns[i];
                report_table.columns.Add(new Column
                {
                    name = "column_" + i,
                    title = descriptor.label,
                    width = column_widths[i],
                    minWidth = 60,
                    resizable = true,
                    sortable = true,
                    makeCell = () => EditorUiControls.Label("", "d9_table_cell"),
                    bindCell = (element, index) =>
                    {
                        if (index < 0 || index >= visible_rows.Count) return;
                        var row = visible_rows[index];
                        var label = (Label)element;
                        label.text = GetCellText(row, descriptor);
                        label.tooltip = label.text;
                        var status = descriptor.target_index >= 0 ? GetTargetCell(row, descriptor.target_index).status : row.status;
                        foreach (CompareStatus kind in Enum.GetValues(typeof(CompareStatus)))
                            label.EnableInClassList("d9_status_" + kind.ToString().ToLowerInvariant(), kind == status && descriptor.kind != ColumnKind.Category);
                    }
                });
            }
            report_table.sortColumnDescriptions.Clear();
            report_table.sortColumnDescriptions.Add(new SortColumnDescription(sort_column, sort_descending ? SortDirection.Descending : SortDirection.Ascending));
            rebuilding_table = false;
            RefreshTable();
        }

        private void AddPathRow(string title, string path, Action choose, Action remove)
        {
            var row = EditorUiControls.Row(false);
            var field = EditorUiControls.Field(new TextField(title) { value = path, isReadOnly = true, tooltip = path });
            row.Add(field);
            row.Add(EditorUiControls.Button("参照…", choose));
            if (remove != null) row.Add(EditorUiControls.Button("削除", remove));
            paths_container.Add(row);
        }

        private void AddReportPane(VisualElement parent, string title, PackageReport report)
        {
            var pane = new VisualElement();
            pane.AddToClassList("report_pane");
            pane.Add(EditorUiControls.Label(title, "d9_section_title"));
            pane.Add(EditorUiControls.Label(report.package_file));
            var text = EditorUiControls.Label(string.IsNullOrEmpty(report.summary) ? "レポート未選択" : report.summary);
            text.selection.isSelectable = true;
            pane.Add(text);
            parent.Add(pane);
        }

        private void RefreshTable()
        {
            UpdateVisibleRows();
            if (report_table == null) return;
            rebuilding_table = true;
            report_table.itemsSource = visible_rows;
            report_table.Rebuild();
            int visible_index = selected_row >= 0 && selected_row < rows.Count ? visible_rows.IndexOf(rows[selected_row]) : -1;
            report_table.SetSelectionWithoutNotify(visible_index < 0 ? Array.Empty<int>() : new[] { visible_index });
            if (visible_index < 0) selected_row = -1;
            rebuilding_table = false;
            empty_state.text = rows.Count == 0 ? "基準と比較対象のコンポーネントレポート JSON を選択してください。" : "条件に一致するコンポーネントはありません。";
            empty_state.EnableInClassList("d9_hidden", visible_rows.Count > 0);
            result_count.text = $"{visible_rows.Count} / {rows.Count} 件を表示  ·  差分 {rows.Count(row => row.status != CompareStatus.Ok)} 件";
            RefreshDetails();
        }

        private void RefreshDetails()
        {
            if (detail_container == null) return;
            detail_container.Clear();
            var row = selected_row >= 0 && selected_row < rows.Count ? rows[selected_row] : null;
            if (row == null)
            {
                detail_container.Add(EditorUiControls.Label("表の行を選択すると、配置場所の一覧を表示します。"));
                return;
            }
            var details = EditorUiControls.Row(false);
            AddLocations(details, "基準", row.reference_locations);
            for (int i = 0; i < row.targets.Count; i++) AddLocations(details, "比較対象 " + (i + 1), row.targets[i].locations);
            detail_container.Add(details);
        }

        private void AddLocations(VisualElement parent, string title, List<string> locations)
        {
            var pane = new VisualElement();
            pane.AddToClassList("report_pane");
            pane.Add(EditorUiControls.Label($"{title}（{locations.Count}）", "d9_section_title"));
            var text = EditorUiControls.Label(locations.Count == 0 ? "該当なし" : string.Join("\n", locations));
            text.selection.isSelectable = true;
            pane.Add(text);
            parent.Add(pane);
        }

        private void RebuildColumns()
        {
            columns.Clear();
            columns.Add(new TableColumn("状態", ColumnKind.Status, -1, 90f));
            columns.Add(new TableColumn("種類", ColumnKind.Category, -1, 130f));
            columns.Add(new TableColumn("コンポーネント", ColumnKind.Component, -1, 220f));
            columns.Add(new TableColumn("基準数", ColumnKind.ReferenceCount, -1, 80f));
            columns.Add(new TableColumn("基準の配置場所", ColumnKind.ReferenceLocations, -1, 260f));

            int target_count = Math.Max(target_json_paths.Count, target_reports.Count);
            for (int i = 0; i < target_count; i++)
            {
                string target_label = GetTargetColumnLabel(i);
                columns.Add(new TableColumn(target_label, ColumnKind.TargetCount, i, 100f));
                columns.Add(new TableColumn("差分 " + (i + 1), ColumnKind.TargetDelta, i, 70f));
                columns.Add(new TableColumn("配置場所 " + (i + 1), ColumnKind.TargetLocations, i, 260f));
            }

            EnsureColumnWidths();
        }

        private string GetTargetColumnLabel(int index)
        {
            if (index >= 0 && index < target_reports.Count && !string.IsNullOrEmpty(target_reports[index].package_file))
            {
                return "T" + (index + 1) + " " + target_reports[index].package_file;
            }

            return "比較対象 " + (index + 1);
        }

        private void EnsureColumnWidths()
        {
            if (column_widths != null && column_widths.Length == columns.Count)
            {
                return;
            }

            float[] old_widths = column_widths ?? new float[0];
            float[] new_widths = new float[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                new_widths[i] = i < old_widths.Length ? old_widths[i] : columns[i].default_width;
            }

            column_widths = new_widths;
            sort_column = Mathf.Clamp(sort_column, 0, Math.Max(0, columns.Count - 1));
        }

        private string GetCellText(CompareRow row, TableColumn column)
        {
            TargetCompareCell target_cell = GetTargetCell(row, column.target_index);
            switch (column.kind)
            {
                case ColumnKind.Status: return row.status_label;
                case ColumnKind.Category: return row.category;
                case ColumnKind.Component: return row.component_label;
                case ColumnKind.ReferenceCount: return row.reference_count.ToString();
                case ColumnKind.ReferenceLocations: return row.reference_locations_preview;
                case ColumnKind.TargetCount: return target_cell.target_count.ToString();
                case ColumnKind.TargetDelta: return target_cell.delta.ToString("+0;-0;0");
                case ColumnKind.TargetLocations: return target_cell.locations_preview;
                default: return "";
            }
        }

        private TargetCompareCell GetTargetCell(CompareRow row, int target_index)
        {
            if (target_index >= 0 && target_index < row.targets.Count)
            {
                return row.targets[target_index];
            }

            return TargetCompareCell.Empty;
        }

        private void SelectReferenceJson()
        {
            string selected_path = EditorUtility.OpenFilePanel("基準のコンポーネントレポート JSON", GetInitialDirectory(reference_json_path), "json");
            if (string.IsNullOrEmpty(selected_path)) return;
            reference_json_path = selected_path;
            ReloadReports();
        }

        private void AddTargetJson()
        {
            string selected_path = EditorUtility.OpenFilePanel("比較対象のコンポーネントレポート JSON", GetInitialDirectory(GetLastTargetPath()), "json");
            if (string.IsNullOrEmpty(selected_path)) return;
            target_json_paths.Add(selected_path);
            ReloadReports();
        }

        private void SelectTargetJson(int index)
        {
            if (index < 0 || index >= target_json_paths.Count)
            {
                return;
            }

            string selected_path = EditorUtility.OpenFilePanel("比較対象のコンポーネントレポート JSON", GetInitialDirectory(target_json_paths[index]), "json");
            if (string.IsNullOrEmpty(selected_path)) return;
            target_json_paths[index] = selected_path;
            ReloadReports();
        }

        private string GetLastTargetPath()
        {
            for (int i = target_json_paths.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(target_json_paths[i]))
                {
                    return target_json_paths[i];
                }
            }

            return reference_json_path;
        }

        private string GetInitialDirectory(string current_path)
        {
            if (!string.IsNullOrEmpty(current_path) && File.Exists(current_path))
            {
                return Path.GetDirectoryName(current_path) ?? Application.dataPath;
            }

            return Application.dataPath;
        }

        private void ReloadReports()
        {
            CaptureColumnWidths();
            reference_report = LoadReport(reference_json_path);
            target_reports = target_json_paths.Select(LoadReport).ToList();
            rows = BuildCompareRows(reference_report, target_reports);
            selected_row = rows.Count > 0 ? 0 : -1;
            SortRows();
            RebuildColumns();
            UpdateVisibleRows();
            RefreshComparisonUI();
        }

        private PackageReport LoadReport(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return new PackageReport();
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                var report = new PackageReport
                {
                    path = path,
                    package_file = ReadString(root, "package_file"),
                    summary = ReadString(root, "summary")
                };

                JArray instances = root["component_instances"] as JArray;
                if (instances == null)
                {
                    return report;
                }

                foreach (JToken token in instances)
                {
                    var instance = new ComponentInstance
                    {
                        category = ReadString(token, "category"),
                        type_name = ReadString(token, "type_name"),
                        type_full_name = ReadString(token, "type_full_name"),
                        prefab_path = ReadString(token, "prefab_path"),
                        object_path = ReadString(token, "object_path"),
                        parent_path = ReadString(token, "parent_path")
                    };

                    report.instances.Add(instance);
                }

                return report;
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load component report json: " + path + "\n" + ex.Message);
                return new PackageReport { path = path, package_file = Path.GetFileName(path), summary = "読み込み失敗" };
            }
        }

        private List<CompareRow> BuildCompareRows(PackageReport reference, List<PackageReport> targets)
        {
            var reference_groups = GroupInstances(reference.instances);
            var target_groups = targets.Select(target => GroupInstances(target.instances)).ToList();
            var keys = new HashSet<string>(reference_groups.Keys);
            foreach (var groups in target_groups)
            {
                keys.UnionWith(groups.Keys);
            }

            return keys
                .Select(key => CreateCompareRow(key, reference_groups, target_groups))
                .OrderBy(row => row.category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.component_label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Dictionary<string, List<ComponentInstance>> GroupInstances(List<ComponentInstance> instances)
        {
            return instances
                .GroupBy(instance => GetComponentKey(instance))
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        private CompareRow CreateCompareRow(
            string key,
            Dictionary<string, List<ComponentInstance>> reference_groups,
            List<Dictionary<string, List<ComponentInstance>>> target_groups)
        {
            reference_groups.TryGetValue(key, out List<ComponentInstance> reference_instances);
            if (reference_instances == null) reference_instances = new List<ComponentInstance>();

            ComponentInstance sample = reference_instances.FirstOrDefault()
                ?? target_groups.Select(groups =>
                {
                    groups.TryGetValue(key, out List<ComponentInstance> instances);
                    return instances != null ? instances.FirstOrDefault() : null;
                }).FirstOrDefault(instance => instance != null)
                ?? new ComponentInstance();

            var row = new CompareRow
            {
                category = sample.category,
                component_label = GetDisplayTypeName(sample),
                type_full_name = sample.type_full_name,
                reference_count = reference_instances.Count,
                reference_locations = BuildLocationList(reference_instances)
            };

            row.reference_locations_preview = BuildLocationPreview(row.reference_locations);
            foreach (var groups in target_groups)
            {
                groups.TryGetValue(key, out List<ComponentInstance> target_instances);
                if (target_instances == null) target_instances = new List<ComponentInstance>();
                List<string> target_locations = BuildLocationList(target_instances);
                var target_cell = new TargetCompareCell
                {
                    target_count = target_instances.Count,
                    delta = target_instances.Count - row.reference_count,
                    locations = target_locations,
                    locations_preview = BuildLocationPreview(target_locations)
                };
                target_cell.status = GetCompareStatus(row.reference_count, target_cell.target_count, row.reference_locations, target_cell.locations);
                row.targets.Add(target_cell);
            }

            row.status = GetOverallStatus(row.targets);
            row.status_label = GetStatusLabel(row.status);
            return row;
        }

        private string GetComponentKey(ComponentInstance instance)
        {
            string type_key = string.IsNullOrEmpty(instance.type_full_name) ? instance.type_name : instance.type_full_name;
            return instance.category + "|" + type_key;
        }

        private List<string> BuildLocationList(List<ComponentInstance> instances)
        {
            return instances
                .Select(instance => string.IsNullOrEmpty(instance.prefab_path)
                    ? instance.object_path
                    : Path.GetFileNameWithoutExtension(instance.prefab_path) + "/" + instance.object_path)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string BuildLocationPreview(List<string> locations)
        {
            if (locations.Count == 0) return "-";
            string preview = string.Join(", ", locations.Take(2));
            if (locations.Count > 2)
            {
                preview += " ... +" + (locations.Count - 2);
            }

            return preview;
        }

        private CompareStatus GetCompareStatus(
            int reference_count,
            int target_count,
            List<string> reference_locations,
            List<string> target_locations)
        {
            if (reference_count > 0 && target_count == 0) return CompareStatus.Missing;
            if (reference_count == 0 && target_count > 0) return CompareStatus.Extra;
            if (reference_count != target_count) return CompareStatus.CountDiff;
            return HasSameLocations(reference_locations, target_locations) ? CompareStatus.Ok : CompareStatus.LocationDiff;
        }

        private bool HasSameLocations(List<string> reference_locations, List<string> target_locations)
        {
            return new HashSet<string>(reference_locations, StringComparer.OrdinalIgnoreCase)
                .SetEquals(target_locations);
        }

        private CompareStatus GetOverallStatus(List<TargetCompareCell> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                return CompareStatus.Ok;
            }

            if (targets.Any(target => target.status == CompareStatus.Missing))
            {
                return CompareStatus.Missing;
            }

            if (targets.Any(target => target.status == CompareStatus.CountDiff))
            {
                return CompareStatus.CountDiff;
            }

            if (targets.Any(target => target.status == CompareStatus.Extra))
            {
                return CompareStatus.Extra;
            }

            if (targets.Any(target => target.status == CompareStatus.LocationDiff))
            {
                return CompareStatus.LocationDiff;
            }

            return CompareStatus.Ok;
        }

        private string GetStatusLabel(CompareStatus status)
        {
            switch (status)
            {
                case CompareStatus.Ok: return "OK";
                case CompareStatus.Missing: return "不足";
                case CompareStatus.Extra: return "追加";
                case CompareStatus.CountDiff: return "個数差";
                case CompareStatus.LocationDiff: return "配置差";
                default: return status.ToString();
            }
        }

        private string GetDisplayTypeName(ComponentInstance instance)
        {
            if (instance.category == "modular_avatar" && instance.type_name.StartsWith("ModularAvatar", StringComparison.Ordinal))
            {
                return instance.type_name.Substring("ModularAvatar".Length);
            }

            return instance.type_name;
        }

        private void UpdateVisibleRows()
        {
            IEnumerable<CompareRow> query = rows;
            if (only_differences)
            {
                query = query.Where(row => row.status != CompareStatus.Ok);
            }

            if (!string.IsNullOrWhiteSpace(filter_text))
            {
                string filter = filter_text.Trim();
                query = query.Where(row =>
                    Contains(row.category, filter) ||
                    Contains(row.component_label, filter) ||
                    Contains(row.type_full_name, filter) ||
                    row.reference_locations.Any(location => Contains(location, filter)) ||
                    row.targets.Any(target => target.locations.Any(location => Contains(location, filter))));
            }

            visible_rows = query.ToList();
            SortVisibleRows();
        }

        private void SortRows()
        {
            rows = rows
                .OrderBy(row => row.category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.component_label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void SortVisibleRows()
        {
            TableColumn column = sort_column >= 0 && sort_column < columns.Count
                ? columns[sort_column]
                : columns.FirstOrDefault();

            if (column == null)
            {
                return;
            }

            switch (column.kind)
            {
                case ColumnKind.ReferenceCount:
                    visible_rows = SortVisibleRowsByNumber(row => row.reference_count);
                    return;
                case ColumnKind.TargetCount:
                    visible_rows = SortVisibleRowsByNumber(row => GetTargetCell(row, column.target_index).target_count);
                    return;
                case ColumnKind.TargetDelta:
                    visible_rows = SortVisibleRowsByNumber(row => GetTargetCell(row, column.target_index).delta);
                    return;
                default:
                    visible_rows = SortVisibleRowsByText(row => GetSortText(row, column));
                    return;
            }
        }

        private List<CompareRow> SortVisibleRowsByText(Func<CompareRow, string> selector)
        {
            return sort_descending
                ? visible_rows.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase).ToList()
                : visible_rows.OrderBy(selector, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<CompareRow> SortVisibleRowsByNumber(Func<CompareRow, int> selector)
        {
            return sort_descending
                ? visible_rows.OrderByDescending(selector).ToList()
                : visible_rows.OrderBy(selector).ToList();
        }

        private string GetSortText(CompareRow row, TableColumn column)
        {
            switch (column.kind)
            {
                case ColumnKind.Status: return row.status_label;
                case ColumnKind.Category: return row.category;
                case ColumnKind.Component: return row.component_label;
                case ColumnKind.ReferenceLocations: return row.reference_locations_preview;
                case ColumnKind.TargetLocations: return GetTargetCell(row, column.target_index).locations_preview;
                default: return row.component_label;
            }
        }

        private bool Contains(string text, string filter)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ReadString(JToken token, string key)
        {
            return token?[key]?.Value<string>() ?? "";
        }

        private enum CompareStatus
        {
            Ok,
            Missing,
            Extra,
            CountDiff,
            LocationDiff
        }

        private enum ColumnKind
        {
            Status,
            Category,
            Component,
            ReferenceCount,
            ReferenceLocations,
            TargetCount,
            TargetDelta,
            TargetLocations
        }

        private class TableColumn
        {
            public string label;
            public ColumnKind kind;
            public int target_index;
            public float default_width;

            public TableColumn(string label, ColumnKind kind, int target_index, float default_width)
            {
                this.label = label;
                this.kind = kind;
                this.target_index = target_index;
                this.default_width = default_width;
            }
        }

        private class PackageReport
        {
            public string path = "";
            public string package_file = "";
            public string summary = "";
            public List<ComponentInstance> instances = new List<ComponentInstance>();
        }

        private class ComponentInstance
        {
            public string category = "";
            public string type_name = "";
            public string type_full_name = "";
            public string prefab_path = "";
            public string object_path = "";
            public string parent_path = "";
        }

        private class CompareRow
        {
            public CompareStatus status = CompareStatus.Ok;
            public string status_label = "";
            public string category = "";
            public string component_label = "";
            public string type_full_name = "";
            public int reference_count = 0;
            public string reference_locations_preview = "";
            public List<string> reference_locations = new List<string>();
            public List<TargetCompareCell> targets = new List<TargetCompareCell>();
        }

        private class TargetCompareCell
        {
            public static readonly TargetCompareCell Empty = new TargetCompareCell();

            public CompareStatus status = CompareStatus.Ok;
            public int target_count = 0;
            public int delta = 0;
            public string locations_preview = "-";
            public List<string> locations = new List<string>();
        }
    }
}
