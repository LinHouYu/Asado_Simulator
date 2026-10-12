using System.IO;
using UnityEditor;
using UnityEngine;

public static class SetupModelPrefabs
{
    [MenuItem("Tools/Generate Model Prefabs")]
    [InitializeOnLoadMethod]
    public static void GeneratePrefabs()
    {
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        // 1. Setup Materials
        Material matEmber = GetOrCreateMaterial("Assets/Models/Charcoal_Embers/Materials/M_Charcoal_Ember.mat", litShader, "Assets/Models/Charcoal_Embers/asado_charcoal_ember.png", true, new Color(1.0f, 0.4f, 0.1f));
        Material matHotcore = GetOrCreateMaterial("Assets/Models/Charcoal_Embers/Materials/M_Charcoal_Hotcore.mat", litShader, "Assets/Models/Charcoal_Embers/asado_charcoal_hotcore.png", true, new Color(1.0f, 0.6f, 0.15f));
        Material matCrust = GetOrCreateMaterial("Assets/Models/Charcoal_Embers/Materials/M_Charcoal_Crust.mat", litShader, "Assets/Models/Charcoal_Embers/asado_charcoal_crust.png", false, Color.black);

        Material matShovelWorn = GetOrCreateMaterial("Assets/Models/Charcoal_Shovel/Materials/M_Shovel_Worn.mat", litShader, "Assets/Models/Charcoal_Shovel/shovel_iron_worn.png", false, Color.black);
        Material matShovelHeated = GetOrCreateMaterial("Assets/Models/Charcoal_Shovel/Materials/M_Shovel_Heated.mat", litShader, "Assets/Models/Charcoal_Shovel/shovel_iron_heated.png", true, new Color(0.8f, 0.2f, 0.05f));

        Material matFork = GetOrCreateMaterial("Assets/Models/Roasting_Fork/Materials/M_Roasting_Fork.mat", litShader, "Assets/Models/Roasting_Fork/roasting_fork_texture.png", false, Color.black);

        // 2. Build Prefabs from OBJ Models
        CreatePrefabFromObj("Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage1.obj", "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage1.prefab", new Material[] { matHotcore, matEmber, matCrust });
        CreatePrefabFromObj("Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage2.obj", "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage2.prefab", new Material[] { matHotcore, matEmber, matCrust });
        CreatePrefabFromObj("Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage3.obj", "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage3.prefab", new Material[] { matHotcore, matEmber, matCrust });

        CreatePrefabFromObj("Assets/Models/Charcoal_Shovel/iron_charcoal_shovel.obj", "Assets/Models/Charcoal_Shovel/iron_charcoal_shovel.prefab", new Material[] { matShovelWorn });
        CreatePrefabFromObj("Assets/Models/Charcoal_Shovel/loaded_charcoal_shovel.obj", "Assets/Models/Charcoal_Shovel/loaded_charcoal_shovel.prefab", new Material[] { matShovelHeated, matHotcore, matEmber });

        CreatePrefabFromObj("Assets/Models/Roasting_Fork/barbecue_roasting_fork.obj", "Assets/Models/Roasting_Fork/barbecue_roasting_fork.prefab", new Material[] { matFork });

        AssetDatabase.SaveAssets();
        Debug.Log("[SetupModelPrefabs] All materials and prefabs successfully generated!");
    }

    private static Material GetOrCreateMaterial(string matPath, Shader shader, string texturePath, bool emissive, Color emissionColor)
    {
        string dir = Path.GetDirectoryName(matPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (tex != null)
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
        }

        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emissionColor * 2.0f);
            if (tex != null) mat.SetTexture("_EmissionMap", tex);
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void CreatePrefabFromObj(string objPath, string prefabPath, Material[] materials)
    {
        GameObject objModel = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
        if (objModel == null)
        {
            Debug.LogWarning($"[SetupModelPrefabs] OBJ model not found at {objPath}");
            return;
        }

        GameObject instance = Object.Instantiate(objModel);
        instance.name = Path.GetFileNameWithoutExtension(prefabPath);

        // Assign materials to renderers
        MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in renderers)
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = materials[Mathf.Min(i, materials.Length - 1)];
            }
            r.sharedMaterials = mats;
        }

        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);
        Debug.Log($"[SetupModelPrefabs] Created prefab: {prefabPath}");
    }
}
