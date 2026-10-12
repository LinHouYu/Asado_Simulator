#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using AsadoSimulator.UI;

namespace AsadoSimulator.EditorTools
{
    /// <summary>
    /// Menu 场景全自动现代 UI 一键装配工具 (MenuSceneBuilder)
    /// 功能：
    /// 1. 顶部菜单栏入口：Tools -> 一键为 Menu 场景配置主菜单 (MenuManager)
    /// 2. 自动检测并切换到 Assets/Scenes/Menu.unity 场景
    /// 3. 在 Hierarchy 根节点全自动生成 [MenuManager] 与专业 [Menu_Canvas]
    /// 4. 自动搭建：全屏背景图 (留出插槽供用户拖拽替换)、电影级微暗角遮罩、左侧大气排版标题/副标题、开始游戏、游戏设置、退出游戏
    /// 5. 自动配置设置子面板：集成 🇨🇳 中文、🇦🇷 西班牙语、🇬🇧 英语 三国语言切换按钮与返回按钮
    /// 6. 全自动反射/代码强绑定所有引用至 MenuManager，零手动拖拽，并自动保存场景！
    /// </summary>
    public static class MenuSceneBuilder
    {
        private const string MenuScenePath = "Assets/Scenes/Menu.unity";

        [MenuItem("Tools/一键为 Menu 场景配置主菜单 (MenuManager)", false, 20)]
        public static void BuildMenuSceneHierarchy()
        {
            // 1. 如果当前不是 Menu 场景，提示并协助用户打开
            Scene currentScene = SceneManager.GetActiveScene();
            if (!string.Equals(currentScene.path, MenuScenePath, StringComparison.OrdinalIgnoreCase))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    currentScene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
                }
                else
                {
                    Debug.LogWarning("[MenuSceneBuilder] 操作已取消：未切换至 Menu 场景。");
                    return;
                }
            }

            // 2. 检查或生成根节点 [MenuManager]
            MenuManager menuManager = UnityEngine.Object.FindAnyObjectByType<MenuManager>();
            GameObject managerObj;
            if (menuManager == null)
            {
                managerObj = GameObject.Find("MenuManager");
                if (managerObj == null)
                {
                    managerObj = new GameObject("MenuManager");
                    Undo.RegisterCreatedObjectUndo(managerObj, "Create MenuManager");
                }
                menuManager = managerObj.AddComponent<MenuManager>();
            }
            else
            {
                managerObj = menuManager.gameObject;
            }

            // 3. 执行全套现代 UI 搭建
            menuManager.EnsureUIHierarchy();
            EditorUtility.SetDirty(menuManager);

            // 4. 标记场景为脏并立即保存
            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);

            // 5. 高亮选中 MenuManager 物体，方便用户在 Inspector 中查看与拖拽背景图片
            Selection.activeGameObject = managerObj;
            EditorGUIUtility.PingObject(managerObj);

            EditorUtility.DisplayDialog(
                "✅ 现代主菜单配置成功！",
                "已成功在 Menu 场景配置专业游戏主菜单：\n\n" +
                "1. [MenuManager] 已置于 Hierarchy 根节点。\n" +
                "2. [Menu_Canvas] 已配置 Scale With Screen Size (1920x1080 响应式)。\n" +
                "3. 包含按钮：开始游戏 (-> Game场景)、游戏设置 (中/西/英多语言)、退出游戏。\n" +
                "4. 🖼️ 背景图片槽位已就绪！\n" +
                "   -> 你可以直接在 Inspector 的 'Custom Background Sprite' 中拖入你的菜单壁纸。\n\n" +
                "场景已自动保存完毕！",
                "太棒了"
            );

            Debug.Log("[MenuSceneBuilder] ✅ 现代主菜单配置与保存完成！可在 Inspector 的 MenuManager 中直接替换壁纸。");
        }
    }
}
#endif
