using BuildingSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot scene fixes: static batching / occlusion flags under <see cref="BuildingTool"/> floors,
/// and wiring <see cref="BuildingRuntimeFloorCuller"/>.
/// </summary>
public static class BuildingOptimizationEditorMenus
{
    private const StaticEditorFlags kAddStatic =
        StaticEditorFlags.OccluderStatic |
        StaticEditorFlags.OccludeeStatic |
        StaticEditorFlags.BatchingStatic;

    [MenuItem("Tools/Building/Mark floor geometry static (Occluder+Occludee+Batching)")]
    public static void MarkFloorsStatic()
    {
        BuildingTool[] tools = Object.FindObjectsByType<BuildingTool>(FindObjectsSortMode.None);
        if (tools.Length == 0)
        {
            Debug.LogWarning("[BuildingOptimization] No BuildingTool in loaded scenes.");
            return;
        }

        int updated = 0;
        foreach (BuildingTool tool in tools)
        {
            Transform root = tool.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform ch = root.GetChild(i);
                if (!BuildingFloorNaming.TryParseFloorLevelFromName(ch.name, out _))
                    continue;

                updated += VisitRecursive(ch);
            }
        }

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[BuildingOptimization] Set Occluder+Occludee+Batching static on {updated} GameObjects under Floor N roots.");
    }

    [MenuItem("Tools/Building/Ensure BuildingRuntimeFloorCuller on BuildingTool")]
    public static void EnsureFloorCuller()
    {
        BuildingTool[] tools = Object.FindObjectsByType<BuildingTool>(FindObjectsSortMode.None);
        if (tools.Length == 0)
        {
            Debug.LogWarning("[BuildingOptimization] No BuildingTool in loaded scenes.");
            return;
        }

        int added = 0;
        foreach (BuildingTool tool in tools)
        {
            if (tool.GetComponent<BuildingRuntimeFloorCuller>() != null)
                continue;

            Undo.AddComponent<BuildingRuntimeFloorCuller>(tool.gameObject);
            added++;
        }

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
        }

        Debug.Log($"[BuildingOptimization] Added BuildingRuntimeFloorCuller to {added} BuildingTool instance(s).");
    }

    private static int VisitRecursive(Transform t)
    {
        int n = 0;
        GameObject go = t.gameObject;
        if (!ShouldSkipStatic(go))
        {
            Undo.RecordObject(go, "Mark building static");
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
            GameObjectUtility.SetStaticEditorFlags(go, flags | kAddStatic);
            n++;
        }

        for (int i = 0; i < t.childCount; i++)
            n += VisitRecursive(t.GetChild(i));

        return n;
    }

    private static bool ShouldSkipStatic(GameObject go)
    {
        if (go.CompareTag("Player"))
            return true;

        if (go.GetComponent<ParticleSystem>() != null)
            return true;

        if (go.GetComponent<HingeJoint>() != null)
            return true;

        if (go.GetComponent<Animator>() != null &&
            go.name.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }
}
