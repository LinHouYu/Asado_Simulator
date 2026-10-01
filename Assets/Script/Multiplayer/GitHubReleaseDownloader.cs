using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace AsadoSimulator.Multiplayer
{
    /// <summary>
    /// GitHub 最新 Release 自动检测、下载与运行器 (GitHubReleaseDownloader)
    /// 功能：
    /// 1. 请求 GitHub API 获取 LinHouYu/cloudflared-tunnel-gui 最新 Release 资产
    /// 2. 解析 .exe 下载链接，在后台自动流式下载到临时目录 Application.temporaryCachePath
    /// 3. 提供下载进度与下载速度回调 (0.0 ~ 1.0)
    /// 4. 下载完毕后自动通过系统进程拉起该软件 (Process.Start)
    /// </summary>
    public class GitHubReleaseDownloader : MonoBehaviour
    {
        public static GitHubReleaseDownloader Instance { get; private set; }

        [Header("GitHub 仓库配置")]
        [SerializeField] private string repoOwner = "LinHouYu";
        [SerializeField] private string repoName = "cloudflared-tunnel-gui";
        [SerializeField] private string targetExtension = ".exe";

        // 回调事件
        public event Action<string> OnStatusChanged;
        public event Action<float, string> OnDownloadProgress; // progress (0-1), display text
        public event Action<string> OnDownloadFinished;       // exe file path
        public event Action<string> OnDownloadFailed;         // error message

        private bool _isDownloading = false;
        public bool IsDownloading => _isDownloading;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 一键启动：获取最新 Release -> 自动下载 -> 自动运行
        /// </summary>
        public void StartDownloadAndRun()
        {
            if (_isDownloading)
            {
                Debug.LogWarning("[Downloader] 下载已在进行中...");
                return;
            }

            StartCoroutine(DownloadAndLaunchRoutine());
        }

        private IEnumerator DownloadAndLaunchRoutine()
        {
            _isDownloading = true;
            OnStatusChanged?.Invoke("正在检查 GitHub 最新版本...");
            OnDownloadProgress?.Invoke(0f, "连接 GitHub API...");

            string apiUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";
            string downloadUrl = null;
            string fileName = "cloudflared-tunnel-gui.exe";

            // 1. 请求 GitHub API 获取 Release JSON
            using (UnityWebRequest apiReq = UnityWebRequest.Get(apiUrl))
            {
                apiReq.SetRequestHeader("User-Agent", "AsadoSimulator-GameClient");
                apiReq.SetRequestHeader("Accept", "application/vnd.github.v3+json");
                apiReq.timeout = 10;

                yield return apiReq.SendWebRequest();

                if (apiReq.result != UnityWebRequest.Result.Success)
                {
                    string err = $"获取版本信息失败: {apiReq.error}";
                    Debug.LogError($"[Downloader] {err}");
                    OnDownloadFailed?.Invoke(err);
                    _isDownloading = false;
                    yield break;
                }

                string json = apiReq.downloadHandler.text;
                downloadUrl = ExtractExeDownloadUrl(json, out fileName);

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    string err = "未在最新 Release 中找到可执行程序 (.exe) 下载包！";
                    Debug.LogError($"[Downloader] {err}");
                    OnDownloadFailed?.Invoke(err);
                    _isDownloading = false;
                    yield break;
                }
            }

            // 2. 准备下载路径
            string saveDir = Application.temporaryCachePath;
            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }
            string saveFilePath = Path.Combine(saveDir, fileName);

            OnStatusChanged?.Invoke($"正在下载 {fileName}...");
            Debug.Log($"[Downloader] 开始下载: {downloadUrl} -> {saveFilePath}");

            // 3. 开始流式下载文件
            using (UnityWebRequest fileReq = new UnityWebRequest(downloadUrl, UnityWebRequest.kHttpVerbGET))
            {
                fileReq.downloadHandler = new DownloadHandlerFile(saveFilePath) { removeFileOnAbort = true };
                fileReq.timeout = 120; // 2分钟超时

                var asyncOp = fileReq.SendWebRequest();

                while (!asyncOp.isDone)
                {
                    float progress = fileReq.downloadProgress;
                    ulong downloadedBytes = fileReq.downloadedBytes;
                    float mb = downloadedBytes / (1024f * 1024f);

                    string progressText = progress > 0f 
                        ? $"已下载 {mb:F1} MB ({Mathf.RoundToInt(progress * 100f)}%)"
                        : $"已下载 {mb:F1} MB...";

                    OnDownloadProgress?.Invoke(progress, progressText);
                    yield return new WaitForSeconds(0.05f);
                }

                if (fileReq.result != UnityWebRequest.Result.Success)
                {
                    string err = $"文件下载失败: {fileReq.error}";
                    Debug.LogError($"[Downloader] {err}");
                    OnDownloadFailed?.Invoke(err);
                    _isDownloading = false;
                    yield break;
                }
            }

            // 4. 下载完成并拉起进程
            _isDownloading = false;
            OnDownloadProgress?.Invoke(1.0f, "下载完成 (100%)");
            OnStatusChanged?.Invoke("下载完成，正在启动软件...");
            OnDownloadFinished?.Invoke(saveFilePath);

            LaunchExecutable(saveFilePath);
        }

        /// <summary>
        /// 从 Release JSON 中提取目标 .exe 的 browser_download_url
        /// </summary>
        private string ExtractExeDownloadUrl(string json, out string foundFileName)
        {
            foundFileName = "cloudflared-tunnel-gui.exe";

            string extPattern = Regex.Escape(!string.IsNullOrEmpty(targetExtension) ? targetExtension : ".exe");
            // 正则匹配 "browser_download_url": "(https://...ext)"
            var match = Regex.Match(json, $@"""browser_download_url""\s*:\s*""(https:[^""]+{extPattern})""", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string url = match.Groups[1].Value;
                foundFileName = Path.GetFileName(url);
                return url;
            }

            // 备用正则匹配
            var nameMatch = Regex.Match(json, $@"""name""\s*:\s*""([^""]+{extPattern})""", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                foundFileName = nameMatch.Groups[1].Value;
            }

            return null;
        }

        /// <summary>
        /// 启动外部 EXE 进程
        /// </summary>
        private void LaunchExecutable(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Debug.Log($"[Downloader] 启动程序: {filePath}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Debug.LogError($"[Downloader] 文件不存在: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Downloader] 自动运行失败: {ex.Message}");
            }
        }
    }
}
