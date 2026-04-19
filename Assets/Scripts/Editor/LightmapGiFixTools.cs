using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Corrige lightmaps sem UV2 (ModelImporter.generateSecondaryUV) e aplica definições de bake recomendadas.
/// Executar no Editor: menu <c>Tools/Lighting/...</c> com a cena de jogo aberta e guardada depois.
/// </summary>
public static class LightmapGiFixTools
{
    private const string MenuRoot = "Tools/Lighting/";

    [MenuItem(MenuRoot + "Run all: UV2 on models + LightingSettings + stitch seams", false, 0)]
    public static void RunAllLightmapFixes()
    {
        EnableLightmapUvsForGiContributors();
        ApplyRecommendedLightingSettings();
        EnableStitchSeamsOnGiMeshRenderers();
    }

    [MenuItem(MenuRoot + "Enable Lightmap UVs on models (GI contributors with MeshRenderer)", false, 10)]
    public static void EnableLightmapUvsForGiContributors()
    {
        var meshRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        var pathsToReimport = new HashSet<string>();
        int meshesConsidered = 0;
        int importersToggled = 0;

        foreach (MeshRenderer mr in meshRenderers)
        {
            GameObject go = mr.gameObject;
            if (!GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.ContributeGI))
                continue;

            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
                continue;

            Mesh mesh = mf.sharedMesh;
            string assetPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(assetPath))
                continue;

            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                continue;

            meshesConsidered++;
            if (importer.generateSecondaryUV)
                continue;

            Undo.RecordObject(importer, "Enable Lightmap UVs");
            importer.generateSecondaryUV = true;
            EditorUtility.SetDirty(importer);
            pathsToReimport.Add(assetPath);
            importersToggled++;
        }

        foreach (string path in pathsToReimport)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        AssetDatabase.SaveAssets();
        Debug.Log(
            $"[LightmapGiFix] GI contributors with MeshFilter: {meshesConsidered} mesh(es); " +
            $"generateSecondaryUV enabled on {importersToggled} ModelImporter(s); reimported {pathsToReimport.Count} path(s).");
    }

    [MenuItem(MenuRoot + "Enable Stitch Lightmap Seams on GI MeshRenderers", false, 11)]
    public static void EnableStitchSeamsOnGiMeshRenderers()
    {
        int updated = 0;
        foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            GameObject go = mr.gameObject;
            if (!GameObjectUtility.AreStaticEditorFlagsSet(go, StaticEditorFlags.ContributeGI))
                continue;

            if (go.GetComponent<MeshFilter>() == null)
                continue;

            if (mr.stitchLightmapSeams)
                continue;

            Undo.RecordObject(mr, "Stitch lightmap seams");
            mr.stitchLightmapSeams = true;
            updated++;
        }

        MarkOpenScenesDirty();
        Debug.Log($"[LightmapGiFix] stitchLightmapSeams enabled on {updated} MeshRenderer(s).");
    }

    [MenuItem(MenuRoot + "Apply LightingSettings (GPU if possible, resolution 40, padding 5)", false, 20)]
    public static void ApplyRecommendedLightingSettings()
    {
        LightingSettings ls = Lightmapping.lightingSettings;
        if (ls == null)
        {
            ls = new LightingSettings { name = "SceneLightingSettings" };
            Lightmapping.lightingSettings = ls;
        }

        Undo.RecordObject(ls, "Lighting bake settings");

        if (TryUseProgressiveGpu(ls))
            Debug.Log("[LightmapGiFix] Lightmapper: Progressive GPU.");
        else
        {
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            Debug.Log("[LightmapGiFix] Lightmapper: Progressive CPU (GPU not selected or unavailable).");
        }

        ls.lightmapResolution = 40f;
        ls.lightmapPadding = 5;

        EditorUtility.SetDirty(ls);
        MarkOpenScenesDirty();
        Debug.Log("[LightmapGiFix] lightmapResolution=40, lightmapPadding=5. Save the scene to persist.");
    }

    private static bool TryUseProgressiveGpu(LightingSettings ls)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            return false;

        if (!SystemInfo.supportsComputeShaders)
            return false;

        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        return true;
    }

    private static void MarkOpenScenesDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isLoaded)
                EditorSceneManager.MarkSceneDirty(s);
        }
    }
}
