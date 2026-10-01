using UnityEditor;
using UnityEngine;
using TMPro;
using UnityEngine.TextCore.LowLevel;

namespace AsadoSimulator.EditorTools
{
    /// <summary>
    /// 编辑器中文字体资产自动生成与配置工具 (ChineseFontSetup)
    /// 确保工程中的 MSYH.TTC 被预先生成为完整、合法的 TMP_FontAsset，
    /// 包含内置的 Atlas 纹理与 Material，彻底避免缺失子资源引用与 FreeType 警告。
    /// </summary>
    [InitializeOnLoad]
    public static class ChineseFontSetup
    {
        static ChineseFontSetup()
        {
            EditorApplication.delayCall += EnsureChineseFontAsset;
        }

        [MenuItem("Tools/重新生成中文字体资产 (MSYH SDF)")]
        public static void EnsureChineseFontAsset()
        {
            string targetFolder = "Assets/TextMesh Pro/Resources/Fonts & Materials";
            string targetPath = targetFolder + "/MSYH SDF.asset";

            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetPath);
            if (existing != null)
            {
                // 校验现有资产是否完全健康（必须有有效的 Atlas 纹理和 Material）
                if (existing.atlasTextures != null && existing.atlasTextures.Length > 0 &&
                    existing.atlasTextures[0] != null && existing.material != null)
                {
                    return; // 资产完好，无需重复生成
                }

                Debug.LogWarning("[ChineseFontSetup] 检测到旧的 MSYH SDF 资产损坏或缺少 Atlas/Material，正在重建...");
                AssetDatabase.DeleteAsset(targetPath);
            }

            if (!System.IO.Directory.Exists(targetFolder))
            {
                System.IO.Directory.CreateDirectory(targetFolder);
                AssetDatabase.Refresh();
            }

            string sourceFontPath = "Assets/TextMesh Pro/Fonts/MSYH.TTC";
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
            if (sourceFont == null)
            {
                sourceFontPath = "Assets/TextMesh Pro/Fonts/MSYHBD.TTC";
                sourceFont = AssetDatabase.LoadAssetAtPath<Font>(sourceFontPath);
            }

            if (sourceFont == null)
            {
                Debug.LogWarning("[ChineseFontSetup] 未能在 Assets/TextMesh Pro/Fonts/ 找到中文字体源文件。");
                return;
            }

            try
            {
                TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                    sourceFont,
                    40,
                    4,
                    GlyphRenderMode.SDFAA,
                    512,
                    512,
                    AtlasPopulationMode.Dynamic,
                    true
                );

                if (fontAsset != null)
                {
                    fontAsset.name = "MSYH SDF";

                    // 1. 保存主资产 (TMP_FontAsset)
                    AssetDatabase.CreateAsset(fontAsset, targetPath);

                    // 2. 将内部生成的 Texture2D 与 Material 注册为 Sub-Asset 保存到同一个 .asset 文件中！
                    // 彻底避免从硬盘重新加载时 m_AtlasTextures 或 m_Material 变成丢失引用 (MissingReferenceException)
                    if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
                    {
                        fontAsset.atlasTextures[0].name = "MSYH SDF Atlas";
                        AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
                    }

                    if (fontAsset.material != null)
                    {
                        fontAsset.material.name = "MSYH SDF Material";
                        AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
                    }

                    EditorUtility.SetDirty(fontAsset);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    Debug.Log($"[ChineseFontSetup] ✅ 成功生成完整中文字体资产 (包含内置 Atlas 与 Material): {targetPath}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChineseFontSetup] 生成中文字体资产异常: {ex}");
            }
        }
    }
}
