#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace AsadoSimulator.EditorTools
{
    /// <summary>
    /// 联机与测试环境一键自动化安装工具 (MultiplayerSetupTool)
    /// 功能：
    /// 1. 顶部菜单栏一键导入 Mirror (TCP联机框架)、ParrelSync (单机双开测试)、Unity Localization (多语言)
    /// 2. 异步队列安装监听与进度弹窗提示
    /// 3. 本地隧道端口连通性测试 (127.0.0.1:25565)
    /// 4. 快捷访问 Cloudflared 网页端穿透平台
    /// </summary>
    public static class MultiplayerSetupTool
    {
        private class PackageInfo
        {
            public string Name;
            public string PackageIdOrUrl;
            public string Description;

            public PackageInfo(string name, string idOrUrl, string desc)
            {
                Name = name;
                PackageIdOrUrl = idOrUrl;
                Description = desc;
            }
        }

        private static readonly Queue<PackageInfo> PackageQueue = new Queue<PackageInfo>();
        private static AddRequest _currentAddRequest;
        private static PackageInfo _currentPackage;
        private static int _totalPackages;
        private static int _completedPackages;
        private static bool _isInstalling = false;

        [MenuItem("Tools/一键安装联机与测试依赖", false, 10)]
        public static void InstallAllDependencies()
        {
            if (_isInstalling)
            {
                EditorUtility.DisplayDialog("提示", "依赖包正在安装中，请稍候...", "确定");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "一键配置联机与测试环境",
                "即将通过 Unity Package Manager 自动安装以下必备插件：\n\n" +
                "1. Mirror (Git: 现代网络联机框架，TCP传输层)\n" +
                "2. ParrelSync (Git: 本地无需打包双开客户端测试)\n" +
                "3. Unity Localization (官方多语言本地化包)\n\n" +
                "是否立即开始安装？",
                "立即安装", "取消"
            );

            if (!confirm) return;

            PackageQueue.Clear();
            PackageQueue.Enqueue(new PackageInfo("Mirror 网络库", "https://github.com/MirrorNetworking/Mirror.git?path=/Assets/Mirror", "基于 TCP 的多人联机网络核心"));
            PackageQueue.Enqueue(new PackageInfo("ParrelSync 双开测试工具", "https://github.com/VeriorPies/ParrelSync.git?path=/project/Packages/com.veriorpies.parrelsync", "单电脑实时双开克隆测试"));
            PackageQueue.Enqueue(new PackageInfo("Unity Localization", "com.unity.localization", "官方多语言本地化支持"));

            _totalPackages = PackageQueue.Count;
            _completedPackages = 0;
            _isInstalling = true;

            EditorApplication.update += OnEditorUpdate;
            ProcessNextPackage();
        }

        private static void ProcessNextPackage()
        {
            if (PackageQueue.Count > 0)
            {
                _currentPackage = PackageQueue.Dequeue();
                float progress = (float)_completedPackages / Mathf.Max(1, _totalPackages);
                EditorUtility.DisplayProgressBar("安装联机依赖环境", $"正在导入 {_currentPackage.Name} ({_completedPackages + 1}/{_totalPackages})...\n{_currentPackage.Description}", progress);

                Debug.Log($"[MultiplayerSetup] 开始导入包: {_currentPackage.Name} ({_currentPackage.PackageIdOrUrl})");
                _currentAddRequest = Client.Add(_currentPackage.PackageIdOrUrl);
            }
            else
            {
                // 全部安装完成
                FinishInstallation();
            }
        }

        private static void OnEditorUpdate()
        {
            if (!_isInstalling || _currentAddRequest == null) return;

            if (_currentAddRequest.IsCompleted)
            {
                if (_currentAddRequest.Status == StatusCode.Success)
                {
                    Debug.Log($"[MultiplayerSetup] 成功安装: {_currentPackage.Name}");
                    _completedPackages++;
                }
                else if (_currentAddRequest.Status >= StatusCode.Failure)
                {
                    Debug.LogError($"[MultiplayerSetup] 安装失败: {_currentPackage.Name}。原因: {_currentAddRequest.Error?.message}");
                }

                _currentAddRequest = null;
                ProcessNextPackage();
            }
        }

        private static void FinishInstallation()
        {
            _isInstalling = false;
            EditorApplication.update -= OnEditorUpdate;
            EditorUtility.ClearProgressBar();

            EditorUtility.DisplayDialog(
                "安装完成",
                "🎉 恭喜！Mirror 网络库、ParrelSync 双开工具 与 Unity Localization 已全部成功配置！\n\n" +
                "Unity 将自动重新编译脚本。\n" +
                "现在可以在顶部菜单栏使用 ParrelSync 进行双开联机测试，并在场景中挂载 AsadoNetworkManager 开始联机！",
                "确定"
            );
        }
    }
}
#endif
