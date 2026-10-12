using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CheckModels
{
    [InitializeOnLoadMethod]
    private static void VerifyImportedModels()
    {
        string[] modelPaths = new string[]
        {
            "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage1.fbx",
            "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage2.fbx",
            "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage3.fbx",
            "Assets/Models/Charcoal_Shovel/iron_charcoal_shovel.fbx",
            "Assets/Models/Charcoal_Shovel/loaded_charcoal_shovel.fbx",
            "Assets/Models/Roasting_Fork/barbecue_roasting_fork.fbx",
            "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage1.obj",
            "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage2.obj",
            "Assets/Models/Charcoal_Embers/Charcoal_Embers_Stage3.obj",
            "Assets/Models/Charcoal_Shovel/iron_charcoal_shovel.obj",
            "Assets/Models/Charcoal_Shovel/loaded_charcoal_shovel.obj",
            "Assets/Models/Roasting_Fork/barbecue_roasting_fork.obj"
        };

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== MODEL VERIFICATION REPORT ===");

        foreach (string path in modelPaths)
        {
            if (!File.Exists(path))
            {
                sb.AppendLine($"[FILE MISSING] {path}");
                continue;
            }

            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            int meshCount = 0;
            int totalVerts = 0;
            int totalTris = 0;

            foreach (var asset in subAssets)
            {
                if (asset is Mesh m)
                {
                    meshCount++;
                    totalVerts += m.vertexCount;
                    totalTris += m.triangles.Length / 3;
                }
            }

            GameObject mainGo = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int childCount = mainGo != null ? mainGo.transform.childCount : -1;

            sb.AppendLine($"[ASSET] {path} | Meshes: {meshCount} | Vertices: {totalVerts} | Triangles: {totalTris} | Children: {childCount}");
        }

        string report = sb.ToString();
        Debug.Log(report);
        File.WriteAllText("Assets/Models/import_verification.txt", report);
    }
}
