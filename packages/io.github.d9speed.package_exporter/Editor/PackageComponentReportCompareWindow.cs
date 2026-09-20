using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace D9speedBaseEditorUtil
{
    public sealed class PackageComponentReportCompareWindow : EditorWindow
    {
        private const string menu_path = "D9speed/ExportBatch/Package Component Report Compare";
        private const float row_height = 22f;
        private const float header_height = 22f;
        private const float resize_handle_width = 6f;

        private static readonly Color header_color = new Color(0.20f, 0.20f, 0.20f, 1f);
        private static readonly Color even_row_color = new Color(0.24f, 0.24f, 0.24f, 0.35f);
        private static readonly Color odd_row_color = new Color(0.12f, 0.12f, 0.12f, 0.35f);
        private static readonly Color selected_row_color = new Color(0.25f, 0.42f, 0.75f, 0.55f);
        private static readonly Color diff_row_color = new Color(0.80f, 0.55f, 0.15f, 0.22f);
        private static readonly Color location_diff_row_color = new Color(0.55f, 0.45f, 0.95f, 0.22f);
        private static readonly Color missing_row_color = new Color(0.75f, 0.18f, 0.18f, 0.24f);
        private static readonly Color extra_row_color = new Color(0.18f, 0.60f, 0.28f, 0.20f);
        private static readonly Color normal_text_color = new Color(0.86f, 0.86f, 0.86f, 1f);
        private static readonly Color diff_text_color = new Color(1.00f, 0.42f, 0.42f, 1f);
        private static readonly Color extra_text_color = new Color(0.45f, 1.00f, 0.58f, 1f);
        private static readonly Color location_diff_text_color = new Color(0.70f, 0.62f, 1.00f, 1f);

        [SerializeField] private string reference_json_path = "";
        [SerializeField] private List<string> target_json_paths = new List<string>();
        [SerializeField] private string filter_text = "";
        [SerializeField] private bool only_differences = false;
        [SerializeField] private Vector2 table_scroll;
        [SerializeField] private Vector2 detail_scroll;
        [SerializeField] private Vector2 summary_scroll;
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
        private GUIStyle header_style;
        private GUIStyle cell_style;
        private GUIStyle small_cell_style;
        private GUIStyle summary_style;
        private int resizing_column = -1;

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

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();
            DrawSummary();
            DrawTable();
            DrawSelectedDetail();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawPathRow("Reference JSON", reference_json_path, SelectReferenceJson);

                for (int i = 0; i < target_json_paths.Count; i++)
                {
                    int index = i;
                    DrawTargetPathRow(index);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    filter_text = EditorGUILayout.TextField("Filter", filter_text);
                    only_differences = EditorGUILayout.ToggleLeft("差分のみ", only_differences, GUILayout.Width(90f));

                    if (GUILayout.Button("Add Target", GUILayout.Width(100f)))
                    {
                        AddTargetJson();
                    }

                    if (GUILayout.Button("Reload", GUILayout.Width(90f)))
                    {
                        ReloadReports();
                    }

                    if (GUILayout.Button("Clear", GUILayout.Width(70f)))
                    {
                        reference_json_path = "";
                        target_json_paths.Clear();
                        reference_report = new PackageReport();
                        target_reports.Clear();
                        rows.Clear();
                        visible_rows.Clear();
                        selected_row = -1;
                    }
                }
            }
        }

        private void DrawPathRow(string label, string path, Action select_action)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(110f));
                EditorGUILayout.SelectableLabel(path, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Select", GUILayout.Width(70f)))
                {
                    select_action();
                }
            }
        }

        private void DrawTargetPathRow(int index)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Target " + (index + 1) + " JSON", GUILayout.Width(110f));
                EditorGUILayout.SelectableLabel(target_json_paths[index], EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("Select", GUILayout.Width(70f)))
                {
                    SelectTargetJson(index);
                }

                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    target_json_paths.RemoveAt(index);
                    ReloadReports();
                }
            }
        }

        private void DrawSummary()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(118f)))
            {
                summary_scroll = EditorGUILayout.BeginScrollView(summary_scroll, GUILayout.Height(108f));
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawSummaryPane("Reference", reference_report);
                    for (int i = 0; i < target_reports.Count; i++)
                    {
                        DrawSummaryPane("Target " + (i + 1), target_reports[i]);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSummaryPane(string title, PackageReport report)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(320f)))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(report.package_file, small_cell_style);
                EditorGUILayout.TextArea(report.summary, summary_style, GUILayout.ExpandHeight(true));
            }
        }

        private void DrawTable()
        {
            RebuildColumns();
            UpdateVisibleRows();

            Rect header_rect = GUILayoutUtility.GetRect(GetTableWidth(), header_height, GUILayout.ExpandWidth(false));
            DrawHeader(header_rect);

            table_scroll = EditorGUILayout.BeginScrollView(table_scroll, GUILayout.ExpandHeight(true));
            float table_width = GetTableWidth();
            Rect content_rect = GUILayoutUtility.GetRect(table_width, visible_rows.Count * row_height, GUILayout.ExpandWidth(false));

            for (int i = 0; i < visible_rows.Count; i++)
            {
                Rect row_rect = new Rect(content_rect.x, content_rect.y + i * row_height, table_width, row_height);
                DrawRow(row_rect, visible_rows[i], i);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader(Rect rect)
        {
            EditorGUI.DrawRect(rect, header_color);
            float x = rect.x - table_scroll.x;
            for (int i = 0; i < columns.Count; i++)
            {
                Rect cell_rect = new Rect(x, rect.y, column_widths[i], rect.height);
                string label = columns[i].label;
                if (sort_column == i)
                {
                    label += sort_descending ? " ▼" : " ▲";
                }

                if (GUI.Button(cell_rect, label, header_style))
                {
                    if (sort_column == i)
                    {
                        sort_descending = !sort_descending;
                    }
                    else
                    {
                        sort_column = i;
                        sort_descending = false;
                    }
                }

                DrawColumnResizeHandle(i, cell_rect);
                x += column_widths[i];
            }
        }

        private void RebuildColumns()
        {
            columns.Clear();
            columns.Add(new TableColumn("Status", ColumnKind.Status, -1, 90f));
            columns.Add(new TableColumn("Category", ColumnKind.Category, -1, 130f));
            columns.Add(new TableColumn("Component", ColumnKind.Component, -1, 220f));
            columns.Add(new TableColumn("Reference", ColumnKind.ReferenceCount, -1, 80f));
            columns.Add(new TableColumn("Reference Locations", ColumnKind.ReferenceLocations, -1, 260f));

            int target_count = Math.Max(target_json_paths.Count, target_reports.Count);
            for (int i = 0; i < target_count; i++)
            {
                string target_label = GetTargetColumnLabel(i);
                columns.Add(new TableColumn(target_label, ColumnKind.TargetCount, i, 100f));
                columns.Add(new TableColumn("Delta " + (i + 1), ColumnKind.TargetDelta, i, 70f));
                columns.Add(new TableColumn("Locations " + (i + 1), ColumnKind.TargetLocations, i, 260f));
            }

            EnsureColumnWidths();
        }

        private string GetTargetColumnLabel(int index)
        {
            if (index >= 0 && index < target_reports.Count && !string.IsNullOrEmpty(target_reports[index].package_file))
            {
                return "T" + (index + 1) + " " + target_reports[index].package_file;
            }

            return "Target " + (index + 1);
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

        private void DrawColumnResizeHandle(int column_index, Rect cell_rect)
        {
            Rect handle_rect = new Rect(cell_rect.xMax - resize_handle_width * 0.5f, cell_rect.y, resize_handle_width, cell_rect.height);
            EditorGUIUtility.AddCursorRect(handle_rect, MouseCursor.ResizeHorizontal);

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && handle_rect.Contains(evt.mousePosition))
            {
                resizing_column = column_index;
                evt.Use();
            }

            if (resizing_column == column_index && evt.type == EventType.MouseDrag)
            {
                column_widths[column_index] = Mathf.Max(45f, evt.mousePosition.x - cell_rect.x);
                Repaint();
                evt.Use();
            }

            if (evt.type == EventType.MouseUp)
            {
                resizing_column = -1;
            }
        }

        private void DrawRow(Rect row_rect, CompareRow row, int visible_index)
        {
            Color base_color = visible_index % 2 == 0 ? even_row_color : odd_row_color;
            EditorGUI.DrawRect(row_rect, base_color);

            if (row.status == CompareStatus.Missing)
            {
                EditorGUI.DrawRect(row_rect, missing_row_color);
            }
            else if (row.status == CompareStatus.Extra)
            {
                EditorGUI.DrawRect(row_rect, extra_row_color);
            }
            else if (row.status == CompareStatus.CountDiff)
            {
                EditorGUI.DrawRect(row_rect, diff_row_color);
            }
            else if (row.status == CompareStatus.LocationDiff)
            {
                EditorGUI.DrawRect(row_rect, location_diff_row_color);
            }

            int row_index = rows.IndexOf(row);
            if (selected_row == row_index)
            {
                EditorGUI.DrawRect(row_rect, selected_row_color);
            }

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && row_rect.Contains(evt.mousePosition))
            {
                selected_row = row_index;
                Repaint();
            }

            float x = row_rect.x;
            for (int i = 0; i < columns.Count; i++)
            {
                TableColumn column = columns[i];
                Rect cell_rect = new Rect(x, row_rect.y, column_widths[i], row_rect.height);
                DrawCell(cell_rect, GetCellText(row, column), GetCellTextColor(row, column));
                x += column_widths[i];
            }
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

        private Color GetCellTextColor(CompareRow row, TableColumn column)
        {
            if (column.kind == ColumnKind.Category)
            {
                return normal_text_color;
            }

            if (column.kind == ColumnKind.Status || column.kind == ColumnKind.Component)
            {
                return row.status == CompareStatus.Ok ? normal_text_color : GetStatusTextColor(row.status);
            }

            if (column.kind == ColumnKind.ReferenceCount)
            {
                return row.status == CompareStatus.CountDiff || row.status == CompareStatus.Missing || row.status == CompareStatus.Extra
                    ? GetStatusTextColor(row.status)
                    : normal_text_color;
            }

            if (column.kind == ColumnKind.ReferenceLocations)
            {
                return row.status == CompareStatus.LocationDiff ? GetStatusTextColor(row.status) : normal_text_color;
            }

            TargetCompareCell target_cell = GetTargetCell(row, column.target_index);
            if (column.kind == ColumnKind.TargetLocations)
            {
                return target_cell.status == CompareStatus.LocationDiff ? GetStatusTextColor(target_cell.status) : normal_text_color;
            }

            return target_cell.status == CompareStatus.CountDiff || target_cell.status == CompareStatus.Missing || target_cell.status == CompareStatus.Extra
                ? GetStatusTextColor(target_cell.status)
                : normal_text_color;
        }

        private TargetCompareCell GetTargetCell(CompareRow row, int target_index)
        {
            if (target_index >= 0 && target_index < row.targets.Count)
            {
                return row.targets[target_index];
            }

            return TargetCompareCell.Empty;
        }

        private void DrawCell(Rect rect, string text, Color text_color)
        {
            Rect padded_rect = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, rect.height - 4f);
            Color old_color = cell_style.normal.textColor;
            cell_style.normal.textColor = text_color;
            GUI.Label(padded_rect, text, cell_style);
            cell_style.normal.textColor = old_color;
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(0.5f, 0.5f, 0.5f, 0.18f));
        }

        private void DrawSelectedDetail()
        {
            CompareRow row = selected_row >= 0 && selected_row < rows.Count ? rows[selected_row] : null;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(150f)))
            {
                EditorGUILayout.LabelField("Selected Component Detail", EditorStyles.boldLabel);
                if (row == null)
                {
                    EditorGUILayout.LabelField("行を選択してください。", small_cell_style);
                    return;
                }

                detail_scroll = EditorGUILayout.BeginScrollView(detail_scroll);
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawLocationList("Reference", row.reference_locations);
                    for (int i = 0; i < row.targets.Count; i++)
                    {
                        DrawLocationList("Target " + (i + 1), row.targets[i].locations);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawLocationList(string title, List<string> locations)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(320f)))
            {
                EditorGUILayout.LabelField(title + " (" + locations.Count + ")", EditorStyles.boldLabel);
                if (locations.Count == 0)
                {
                    EditorGUILayout.LabelField("-", small_cell_style);
                    return;
                }

                foreach (string location in locations)
                {
                    EditorGUILayout.SelectableLabel(location, small_cell_style, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }
        }

        private void SelectReferenceJson()
        {
            string selected_path = EditorUtility.OpenFilePanel("Reference component report JSON", GetInitialDirectory(reference_json_path), "json");
            if (string.IsNullOrEmpty(selected_path)) return;
            reference_json_path = selected_path;
            ReloadReports();
        }

        private void AddTargetJson()
        {
            string selected_path = EditorUtility.OpenFilePanel("Target component report JSON", GetInitialDirectory(GetLastTargetPath()), "json");
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

            string selected_path = EditorUtility.OpenFilePanel("Target component report JSON", GetInitialDirectory(target_json_paths[index]), "json");
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
            reference_report = LoadReport(reference_json_path);
            target_reports = target_json_paths.Select(LoadReport).ToList();
            rows = BuildCompareRows(reference_report, target_reports);
            selected_row = rows.Count > 0 ? 0 : -1;
            SortRows();
            RebuildColumns();
            UpdateVisibleRows();
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
                case CompareStatus.Missing: return "Missing";
                case CompareStatus.Extra: return "Extra";
                case CompareStatus.CountDiff: return "Count Diff";
                case CompareStatus.LocationDiff: return "Location Diff";
                default: return status.ToString();
            }
        }

        private Color GetStatusTextColor(CompareStatus status)
        {
            switch (status)
            {
                case CompareStatus.Ok: return normal_text_color;
                case CompareStatus.Extra: return extra_text_color;
                case CompareStatus.LocationDiff: return location_diff_text_color;
                case CompareStatus.Missing:
                case CompareStatus.CountDiff:
                    return diff_text_color;
                default:
                    return normal_text_color;
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

        private float GetTableWidth()
        {
            return column_widths.Sum();
        }

        private void EnsureStyles()
        {
            if (header_style != null) return;

            header_style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 0, 0)
            };
            cell_style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(2, 2, 0, 0)
            };
            small_cell_style = new GUIStyle(EditorStyles.label)
            {
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            summary_style = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false
            };
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
