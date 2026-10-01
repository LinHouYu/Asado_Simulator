using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 现代化联机大厅主界面 (MultiplayerLobbyUI)
    /// 功能：
    /// 1. 创建房间 (Host: 默认 25565，可自定)、加入游戏 (Client: 默认 25566，支持自定义端口及 IP:Port)
    /// 2. 呼出 Cloudflared 联机辅助工具与分步使用教程
    /// 3. 集成 127.0.0.1:25566 穿透通道客户端自动侦测弹窗 (一键立即加入)
    /// 4. 实时网络连接状态与异常 Toast 提示
    /// </summary>
    public class MultiplayerLobbyUI : MonoBehaviour
    {
        [Header("大厅根节点与动画")]
        [SerializeField] private CanvasGroup lobbyCanvasGroup;
        [SerializeField] private RectTransform lobbyWindowRect;

        [Header("大厅标题与按钮")]
        [SerializeField] private TextMeshProUGUI lobbyTitleText;
        [SerializeField] private TMP_InputField ipInputField;
        [SerializeField] private TMP_InputField portInputField;
        [SerializeField] private Button hostButton;
        [SerializeField] private TextMeshProUGUI hostButtonText;
        [SerializeField] private Button joinButton;
        [SerializeField] private TextMeshProUGUI joinButtonText;
        [SerializeField] private Button openTunnelGuideButton;
        [SerializeField] private TextMeshProUGUI openTunnelGuideButtonText;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI backButtonText;
        [SerializeField] private TextMeshProUGUI statusLabelText;

        [Header("自动检测一键加入弹窗 (Auto-Join Modal)")]
        [SerializeField] private GameObject autoJoinModalObj;
        [SerializeField] private CanvasGroup autoJoinCanvasGroup;
        [SerializeField] private RectTransform autoJoinRect;
        [SerializeField] private TextMeshProUGUI autoJoinTitleText;
        [SerializeField] private TextMeshProUGUI autoJoinDescText;
        [SerializeField] private Button autoJoinConfirmButton;
        [SerializeField] private TextMeshProUGUI autoJoinConfirmText;
        [SerializeField] private Button autoJoinIgnoreButton;
        [SerializeField] private TextMeshProUGUI autoJoinIgnoreText;

        [Header("子界面引用")]
        [SerializeField] private TunnelManagerUI tunnelManagerUI;

        private bool _listenersBound = false;

        public void InitializeReferences(
            CanvasGroup canvasGroup,
            RectTransform windowRect,
            TextMeshProUGUI titleTxt,
            TMP_InputField ipInput,
            TMP_InputField portInput,
            Button hostBtn,
            TextMeshProUGUI hostTxt,
            Button joinBtn,
            TextMeshProUGUI joinTxt,
            Button openTunnelBtn,
            TextMeshProUGUI openTunnelTxt,
            Button backBtn,
            TextMeshProUGUI backTxt,
            TextMeshProUGUI statusLabel,
            GameObject autoJoinModal,
            CanvasGroup autoJoinGroup,
            RectTransform autoJoinRTransform,
            TextMeshProUGUI ajTitle,
            TextMeshProUGUI ajDesc,
            Button ajConfirmBtn,
            TextMeshProUGUI ajConfirmTxt,
            Button ajIgnoreBtn,
            TextMeshProUGUI ajIgnoreTxt,
            TunnelManagerUI tunnelUI)
        {
            lobbyCanvasGroup = canvasGroup;
            lobbyWindowRect = windowRect;
            lobbyTitleText = titleTxt;
            ipInputField = ipInput;
            portInputField = portInput;
            hostButton = hostBtn;
            hostButtonText = hostTxt;
            joinButton = joinBtn;
            joinButtonText = joinTxt;
            openTunnelGuideButton = openTunnelBtn;
            openTunnelGuideButtonText = openTunnelTxt;
            backButton = backBtn;
            backButtonText = backTxt;
            statusLabelText = statusLabel;

            autoJoinModalObj = autoJoinModal;
            autoJoinCanvasGroup = autoJoinGroup;
            autoJoinRect = autoJoinRTransform;
            autoJoinTitleText = ajTitle;
            autoJoinDescText = ajDesc;
            autoJoinConfirmButton = ajConfirmBtn;
            autoJoinConfirmText = ajConfirmTxt;
            autoJoinIgnoreButton = ajIgnoreBtn;
            autoJoinIgnoreText = ajIgnoreTxt;
            tunnelManagerUI = tunnelUI;

            BindListeners();
        }

        private void BindListeners()
        {
            if (_listenersBound) return;
            _listenersBound = true;

            // 绑定大厅操作
            if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
            if (openTunnelGuideButton != null) openTunnelGuideButton.onClick.AddListener(OnOpenTunnelGuideClicked);
            if (backButton != null) backButton.onClick.AddListener(Hide);

            // 绑定自动加入弹窗
            if (autoJoinConfirmButton != null) autoJoinConfirmButton.onClick.AddListener(OnAutoJoinConfirmClicked);
            if (autoJoinIgnoreButton != null) autoJoinIgnoreButton.onClick.AddListener(OnAutoJoinIgnoreClicked);

            if (autoJoinModalObj != null) autoJoinModalObj.SetActive(false);
        }

        private void Awake()
        {
            if (lobbyCanvasGroup == null) lobbyCanvasGroup = GetComponent<CanvasGroup>();
            if (lobbyWindowRect == null) lobbyWindowRect = GetComponent<RectTransform>();

            BindListeners();
        }

        private void Start()
        {
            LanguageManager.OnLanguageChanged += RefreshLocalizedTexts;
            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);

            // 订阅自动端口探测事件
            if (AutoJoinDetector.Instance != null)
            {
                AutoJoinDetector.Instance.OnServerDetected += HandleLocalServerDetected;
            }

            // 订阅网络连接事件
            AsadoNetworkManager.OnClientConnectedEvent += HandleClientConnected;
            AsadoNetworkManager.OnClientDisconnectedEvent += HandleClientDisconnected;
            AsadoNetworkManager.OnNetworkErrorEvent += HandleNetworkError;
            AsadoNetworkManager.OnWorldSyncCompletedEvent += HandleWorldSyncCompleted;
        }

        private void OnDestroy()
        {
            LanguageManager.OnLanguageChanged -= RefreshLocalizedTexts;

            if (AutoJoinDetector.Instance != null)
            {
                AutoJoinDetector.Instance.OnServerDetected -= HandleLocalServerDetected;
            }

            AsadoNetworkManager.OnClientConnectedEvent -= HandleClientConnected;
            AsadoNetworkManager.OnClientDisconnectedEvent -= HandleClientDisconnected;
            AsadoNetworkManager.OnNetworkErrorEvent -= HandleNetworkError;
            AsadoNetworkManager.OnWorldSyncCompletedEvent -= HandleWorldSyncCompleted;
        }

        private System.Action _onCloseCallback;

        public void Show(System.Action onClose = null)
        {
            _onCloseCallback = onClose;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (lobbyCanvasGroup != null)
            {
                lobbyCanvasGroup.alpha = 1f;
                lobbyCanvasGroup.blocksRaycasts = true;
                lobbyCanvasGroup.interactable = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);

            // 打开大厅时，根据当前实际网络状态刷新状态文本，杜绝残留‘正在同步’
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
            {
                UpdateStatus(AsadoNetworkManager.Instance.IsHost ? "✅ 正在作为房主运行" : "✅ 已经连入房间");
            }
            else
            {
                UpdateStatus(LanguageManager.T("mp_ready"));
            }

            StartCoroutine(ModernUIAnimationHelper.AnimatePopIn(lobbyWindowRect, lobbyCanvasGroup, 0.22f));

            if (AutoJoinDetector.Instance != null && (AsadoNetworkManager.Instance == null || !AsadoNetworkManager.Instance.IsNetworkActive))
            {
                AutoJoinDetector.Instance.StartDetection();
            }
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(Hide));
        }

        public void Hide()
        {
            CancelInvoke(nameof(Hide));

            if (lobbyCanvasGroup != null)
            {
                lobbyCanvasGroup.blocksRaycasts = false;
                lobbyCanvasGroup.interactable = false;
            }

            if (!gameObject.activeInHierarchy)
            {
                gameObject.SetActive(false);
                var cb = _onCloseCallback;
                _onCloseCallback = null;
                cb?.Invoke();
                return;
            }

            StartCoroutine(ModernUIAnimationHelper.AnimatePopOut(lobbyWindowRect, lobbyCanvasGroup, 0.15f, () =>
            {
                gameObject.SetActive(false);
                var cb = _onCloseCallback;
                _onCloseCallback = null;
                cb?.Invoke();
            }));
        }

        private void OnHostClicked()
        {
            if (AutoJoinDetector.Instance != null)
            {
                AutoJoinDetector.Instance.StopDetection();
            }
            if (autoJoinModalObj != null) autoJoinModalObj.SetActive(false);

            ushort serverPort = 25565; // 服务端默认端口 25565
            if (portInputField != null && ushort.TryParse(portInputField.text.Trim(), out ushort p) && p > 0 && p != 25566)
            {
                serverPort = p;
            }

            UpdateStatus($"正在创建本地 TCP 房间 (Host 端口: {serverPort})...");
            if (AsadoNetworkManager.Instance != null)
            {
                AsadoNetworkManager.Instance.StartHostGame(serverPort);
            }
            else
            {
                UpdateStatus("⚠️ 未找到 AsadoNetworkManager 实例！");
            }
        }

        private void OnJoinClicked()
        {
            if (AutoJoinDetector.Instance != null)
            {
                AutoJoinDetector.Instance.StopDetection();
            }
            if (autoJoinModalObj != null) autoJoinModalObj.SetActive(false);

            string ip = ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text) 
                ? ipInputField.text.Trim() 
                : "127.0.0.1";

            ushort clientPort = 25566; // 客户端默认端口 25566
            if (ip.Contains(":"))
            {
                var parts = ip.Split(':');
                ip = parts[0];
                if (ushort.TryParse(parts[1], out ushort parsedPort) && parsedPort > 0)
                {
                    clientPort = parsedPort;
                }
            }
            else if (portInputField != null && ushort.TryParse(portInputField.text.Trim(), out ushort p) && p > 0)
            {
                clientPort = p;
            }

            UpdateStatus($"正在连接目标服务器: {ip}:{clientPort}...");
            if (AsadoNetworkManager.Instance != null)
            {
                AsadoNetworkManager.Instance.StartClientGame(ip, clientPort);
            }
            else
            {
                UpdateStatus("⚠️ 未找到 AsadoNetworkManager 实例！");
            }
        }

        private void OnOpenTunnelGuideClicked()
        {
            if (tunnelManagerUI != null)
            {
                tunnelManagerUI.transform.SetAsLastSibling();
                tunnelManagerUI.Show();
            }
        }

        private void HandleLocalServerDetected(string host, int port)
        {
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
            {
                return;
            }

            if (autoJoinModalObj != null)
            {
                if (autoJoinDescText != null)
                {
                    autoJoinDescText.text = $"检测到 {host}:{port} 端口已开启！\n是否立即加入游戏？";
                }
                autoJoinModalObj.SetActive(true);
                if (autoJoinCanvasGroup != null && autoJoinRect != null)
                {
                    StartCoroutine(ModernUIAnimationHelper.AnimatePopIn(autoJoinRect, autoJoinCanvasGroup, 0.25f));
                }
            }
        }

        private void OnAutoJoinConfirmClicked()
        {
            if (autoJoinModalObj != null) autoJoinModalObj.SetActive(false);
            if (AutoJoinDetector.Instance != null)
            {
                AutoJoinDetector.Instance.StopDetection();
                UpdateStatus("正在通过本地穿透隧道加入游戏...");
                AutoJoinDetector.Instance.ExecuteJoin();
            }
        }

        private void OnAutoJoinIgnoreClicked()
        {
            if (autoJoinModalObj != null)
            {
                StartCoroutine(ModernUIAnimationHelper.AnimatePopOut(autoJoinRect, autoJoinCanvasGroup, 0.15f, () =>
                {
                    autoJoinModalObj.SetActive(false);
                }));
            }
        }

        private void HandleClientConnected()
        {
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsHost)
            {
                UpdateStatus("✅ 房间创建成功！等待玩家加入...");
                CancelInvoke(nameof(Hide));
                if (gameObject.activeInHierarchy)
                {
                    Invoke(nameof(Hide), 0.8f);
                }
            }
            else
            {
                UpdateStatus("✅ 已成功连入房间！正在同步游戏世界...");
                // 备用超时保护：若2.5秒内未收到WorldState包，也自动隐藏大厅，绝不永久锁死
                CancelInvoke(nameof(Hide));
                if (gameObject.activeInHierarchy)
                {
                    Invoke(nameof(Hide), 2.5f);
                }
            }
        }

        private void HandleWorldSyncCompleted()
        {
            UpdateStatus("✅ 世界同步完成！游戏已就绪");
            CancelInvoke(nameof(Hide));
            if (gameObject.activeInHierarchy)
            {
                Invoke(nameof(Hide), 0.5f);
            }
        }

        private void HandleClientDisconnected()
        {
            UpdateStatus("⚠️ 已与房间断开连接。");
        }

        private void HandleNetworkError(string err)
        {
            UpdateStatus($"❌ 网络错误: {err}");
        }

        private void UpdateStatus(string text)
        {
            if (statusLabelText != null)
            {
                statusLabelText.text = text;
            }
            Debug.Log($"[LobbyUI] {text}");
        }

        private void RefreshLocalizedTexts(GameLanguage lang)
        {
            if (lobbyTitleText != null) lobbyTitleText.text = LanguageManager.T("mp_title");
            if (hostButtonText != null) hostButtonText.text = LanguageManager.T("mp_host");
            if (joinButtonText != null) joinButtonText.text = LanguageManager.T("mp_join");
            if (openTunnelGuideButtonText != null) openTunnelGuideButtonText.text = LanguageManager.T("mp_tool_guide");
            if (backButtonText != null) backButtonText.text = LanguageManager.T("btn_back");

            if (autoJoinTitleText != null) autoJoinTitleText.text = LanguageManager.T("auto_join_title");
            if (autoJoinConfirmText != null) autoJoinConfirmText.text = LanguageManager.T("auto_join_confirm");
            if (autoJoinIgnoreText != null) autoJoinIgnoreText.text = LanguageManager.T("auto_join_ignore");

            if (statusLabelText != null && (statusLabelText.text == "准备就绪" || statusLabelText.text == "Listo" || statusLabelText.text == "Ready"))
            {
                statusLabelText.text = LanguageManager.T("mp_ready");
            }
        }
    }
}
