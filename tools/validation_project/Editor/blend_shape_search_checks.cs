using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Search;
using UnityEngine;

public static class blend_shape_search_checks
{
    private static GameObject smile_object;
    private static GameObject happy_object;
    private static SearchContext current_context;
    private static int query_index;
    private static double started_at;
    private static readonly string[] queries =
    {
        "bs:smile", "bs:SMILE", "bs:\"happy face\"", "bs:missing",
        "h:t:SkinnedMeshRenderer bs:smile", "h:t:SkinnedMeshRenderer bs:SMILE",
        "h:t:SkinnedMeshRenderer bs:\"happy face\"", "h:t:SkinnedMeshRenderer bs:missing",
        "h:t:Skinnedmeshrenderer bs:smile"
    };

    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            smile_object = create_renderer("smile_object", "Smile_Left");
            happy_object = create_renderer("happy_object", "Happy Face");
            create_renderer("frown_object", "Frown");
            var empty_object = new GameObject("empty_object");
            empty_object.AddComponent<SkinnedMeshRenderer>();

            require(SearchService.GetProvider("d9speed_blend_shapes") != null, "Provider was not registered");
            started_at = EditorApplication.timeSinceStartup;
            EditorApplication.update += check_timeout;
            run_next_query();
        }
        catch (Exception error)
        {
            fail(error);
        }
    }

    private static void run_next_query()
    {
        current_context = SearchService.CreateContext(queries[query_index]);
        var expected_filter = query_index < 4 ? "bs:" : "h:";
        require(current_context.filterId == expected_filter, "Unexpected provider for " + queries[query_index]);
        SearchService.Request(current_context, on_search_completed, SearchFlags.WantsMore);
    }

    private static void on_search_completed(SearchContext context, IList<SearchItem> results)
    {
        try
        {
            if (query_index == 0 || query_index == 1 || query_index == 4 || query_index == 5 || query_index == 8)
            {
                require(results.Count == 1, "Expected one smile result, got " + results.Count);
                require(results[0].ToObject<GameObject>() == smile_object, "Wrong GameObject returned");
                if (query_index < 4)
                    require(results[0].GetDescription(context).Contains("Smile_Left"), "Matched shape name is missing");
            }
            else if (query_index == 2 || query_index == 6)
            {
                require(results.Count == 1 && results[0].ToObject<GameObject>() == happy_object,
                    "Quoted shape name match failed");
            }
            else
            {
                require(results.Count == 0, "Unexpected result for missing shape");
            }

            current_context.Dispose();
            query_index++;
            if (query_index < queries.Length)
                run_next_query();
            else
            {
                EditorApplication.update -= check_timeout;
                Debug.Log("BLEND_SHAPE_SEARCH_PASS");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception error)
        {
            fail(error);
        }
    }

    private static void check_timeout()
    {
        if (EditorApplication.timeSinceStartup - started_at > 30)
            fail(new TimeoutException("Search callback did not complete"));
    }

    private static void fail(Exception error)
    {
        EditorApplication.update -= check_timeout;
        current_context?.Dispose();
        Debug.LogException(error);
        EditorApplication.Exit(1);
    }

    private static GameObject create_renderer(string object_name, string shape_name)
    {
        var game_object = new GameObject(object_name);
        var renderer = game_object.AddComponent<SkinnedMeshRenderer>();
        var mesh = new Mesh();
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        var deltas = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
        mesh.AddBlendShapeFrame(shape_name, 100f, deltas, deltas, deltas);
        renderer.sharedMesh = mesh;
        return game_object;
    }

    private static void require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
