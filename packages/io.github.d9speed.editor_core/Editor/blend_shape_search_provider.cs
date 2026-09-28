using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace D9speed_BaseEditorUtils
{
    internal static class blend_shape_search_provider
    {
        private const string provider_id = "d9speed_blend_shapes";

        [SearchItemProvider]
        internal static SearchProvider create_provider()
        {
            return new SearchProvider(provider_id, "Blend Shapes")
            {
                filterId = "bs:",
                isExplicitProvider = true,
                priority = 99999,
                showDetailsOptions = ShowDetailsOptions.Inspector,
                fetchItems = (context, items, provider) => fetch_items(context, provider),
                toObject = (item, type) => item.data as GameObject,
                trackSelection = (item, context) =>
                {
                    var game_object = item.data as GameObject;
                    if (game_object != null)
                        EditorGUIUtility.PingObject(game_object);
                }
            };
        }

        [SearchActionsProvider]
        internal static IEnumerable<SearchAction> action_handlers()
        {
            yield return new SearchAction(provider_id, "select", new GUIContent("Select", "Select the GameObject in the Hierarchy"))
            {
                handler = item =>
                {
                    var game_object = item.data as GameObject;
                    if (game_object == null)
                        return;

                    Selection.activeGameObject = game_object;
                    EditorGUIUtility.PingObject(game_object);
                }
            };
        }

        private static IEnumerable<SearchItem> fetch_items(SearchContext context, SearchProvider provider)
        {
            var search_text = context.searchQuery.Trim();
            if (search_text.Length == 0)
                yield break;

            if (search_text.Length >= 2 && search_text[0] == '"' && search_text[search_text.Length - 1] == '"')
                search_text = search_text.Substring(1, search_text.Length - 2);

            var scene_provider = SearchService.GetProvider("scene");
            if (scene_provider == null)
                yield break;

            using (var scene_context = SearchService.CreateContext(scene_provider, "t:SkinnedMeshRenderer"))
            using (var scene_results = SearchService.Request(scene_context, SearchFlags.WantsMore))
            {
                foreach (var scene_item in scene_results)
                {
                    if (scene_item == null)
                    {
                        yield return null;
                        continue;
                    }

                    var game_object = scene_item.ToObject<GameObject>();
                    if (game_object == null)
                        continue;

                    var matched_names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var renderer in game_object.GetComponents<SkinnedMeshRenderer>())
                    {
                        var mesh = renderer.sharedMesh;
                        if (mesh == null)
                            continue;

                        for (var index = 0; index < mesh.blendShapeCount; index++)
                        {
                            var shape_name = mesh.GetBlendShapeName(index);
                            if (shape_name.IndexOf(search_text, StringComparison.OrdinalIgnoreCase) >= 0)
                                matched_names.Add(shape_name);
                        }
                    }

                    if (matched_names.Count == 0)
                        continue;

                    var scene_description = scene_item.GetDescription(scene_context, true);
                    var description = "BlendShape: " + string.Join(", ", matched_names);
                    if (!string.IsNullOrEmpty(scene_description))
                        description += " | " + scene_description;

                    yield return provider.CreateItem(context, scene_item.id, game_object.name, description, null, game_object);
                }
            }
        }
    }
}
