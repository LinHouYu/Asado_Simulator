using System;
using System.Collections;
using System.Net.Sockets;
using UnityEngine;

namespace AsadoSimulator.Multiplayer
{
    /// <summary>
    /// 客户端本地隧道自动检测与一键加入器 (AutoJoinDetector)
    /// 功能：
    /// 1. 在联机大厅/菜单状态下，后台每隔 2 秒非阻塞检测 127.0.0.1:25566 端口连通性 (客户端默认 25566)
    /// 2. 当检测到端口开放（即玩家已在穿透软件连接朋友的临时通道），自动弹出确认加入弹窗
    /// 3. 当本地已处于 Host 或已连接状态时，绝对不发起探测，防止向自身端口发送虚假握手
    /// </summary>
    public class AutoJoinDetector : MonoBehaviour
    {
        public static AutoJoinDetector Instance { get; private set; }

        [Header("检测配置")]
        [Tooltip("目标检测的主机地址 (默认为穿透客户端本地监听的 127.0.0.1)")]
        [SerializeField] private string targetHost = "127.0.0.1";

        [Tooltip("目标检测的端口 (默认客户端穿透端口 25566)")]
        [SerializeField] private int targetPort = 25566;

        [Tooltip("检测间隔时间（秒）")]
        [SerializeField] private float checkInterval = 2.0f;

        [Tooltip("是否在启用时自动开始检测")]
        [SerializeField] private bool autoStartOnEnable = true;

        public int TargetPort { get => targetPort; set => targetPort = value; }
        public string TargetHost { get => targetHost; set => targetHost = value; }

        // 事件通知
        public event Action<string, int> OnServerDetected; // (host, port)
        public event Action OnServerLost;

        private Coroutine _detectCoroutine;
        private bool _isServerAvailable = false;
        private bool _hasPromptedThisSession = false; // 避免重复频繁弹窗打扰玩家

        public bool IsServerAvailable => _isServerAvailable;
        public bool IsDetecting => _detectCoroutine != null;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnEnable()
        {
            if (autoStartOnEnable)
            {
                StartDetection();
            }
        }

        private void OnDisable()
        {
            StopDetection();
        }

        public void StartDetection()
        {
            if (_detectCoroutine != null) StopCoroutine(_detectCoroutine);
            _hasPromptedThisSession = false;
            _detectCoroutine = StartCoroutine(DetectPortLoop());
        }

        public void StopDetection()
        {
            if (_detectCoroutine != null)
            {
                StopCoroutine(_detectCoroutine);
                _detectCoroutine = null;
            }
        }

        public void ResetPromptState()
        {
            _hasPromptedThisSession = false;
        }

        private IEnumerator DetectPortLoop()
        {
            while (true)
            {
                // 如果本地已在运行网络服务 (Host 或已连接 Client)，绝不发起探测，避免向自身端口发送假 TCP 连接
                if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
                {
                    yield return new WaitForSecondsRealtime(checkInterval);
                    continue;
                }

                bool isPortOpen = false;

                // 在后台异步测试 TCP 连接，绝不阻塞 Unity 主线程
                int port = targetPort;
                string host = targetHost;
                var testTask = System.Threading.Tasks.Task.Run(() => CheckPortOpen(host, port, 1200));

                while (!testTask.IsCompleted)
                {
                    yield return null;
                }

                if (testTask.IsFaulted)
                {
                    isPortOpen = false;
                }
                else
                {
                    isPortOpen = testTask.Result;
                }

                // 再次检查网络状态，防止在等待期间玩家已经开服
                if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
                {
                    yield return new WaitForSecondsRealtime(checkInterval);
                    continue;
                }

                if (isPortOpen)
                {
                    if (!_isServerAvailable && !_hasPromptedThisSession)
                    {
                        _isServerAvailable = true;
                        _hasPromptedThisSession = true;
                        Debug.Log($"[AutoJoinDetector] ✅ 成功检测到本地隧道客户端开放: {host}:{port}");
                        OnServerDetected?.Invoke(host, port);
                    }
                }
                else
                {
                    if (_isServerAvailable)
                    {
                        _isServerAvailable = false;
                        OnServerLost?.Invoke();
                    }
                }

                yield return new WaitForSecondsRealtime(checkInterval);
            }
        }

        private static bool CheckPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(host, port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (success && client.Connected)
                    {
                        client.EndConnect(result);
                        client.Close();
                        return true;
                    }
                }
            }
            catch
            {
                // 端口未开放为正常预期，忽略异常
            }
            return false;
        }

        /// <summary>
        /// 执行一键加入
        /// </summary>
        public void ExecuteJoin()
        {
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
            {
                Debug.LogWarning("[AutoJoinDetector] 网络已处于激活状态，忽略重复加入请求");
                StopDetection();
                return;
            }

            Debug.Log($"[AutoJoinDetector] 玩家确认加入本地隧道服务器: {targetHost}:{targetPort}");
            StopDetection();

            // 调用联机管理器启动连接
            if (AsadoNetworkManager.Instance != null)
            {
                AsadoNetworkManager.Instance.StartClientGame(targetHost, (ushort)targetPort);
            }
            else
            {
                Debug.LogWarning("[AutoJoinDetector] 未找到 AsadoNetworkManager 实例！");
            }
        }
    }
}
