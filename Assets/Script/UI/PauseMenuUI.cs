using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 现代化游戏暂停菜单 (PauseMenuUI)
    /// 功能：
    /// 1. 按 ESC 键平滑呼出/隐藏，支持新旧输入系统 (Input System & Legacy Input)
    /// 2. 具备严格的空指针保护与 Debug 追踪报错
    /// 3. 单机模式暂停时间 (timeScale=0)，联机模式保持世界时间流逝 (timeScale=1)
    /// 4. 提供 继续游戏、重新开始、联机大厅、系统设置、退出游戏 功能
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        public static PauseMenuUI Instance { get; private set; }

        [Header("UI 容器与动画")]
        [Tooltip("暂停菜单的根容器 GameObject（必须在 Inspector 中拖拽赋值，例如 PauseMenu 面板）")]
        [SerializeField] private GameObject menuContentRoot;

        [Tooltip("控制透明度与交互的 CanvasGroup（必须在 Inspector 中拖拽赋值）")]
        [SerializeField] private CanvasGroup pauseCanvasGroup;

        [Tooltip("控制弹性缩放动画的 RectTransform（例如 MainPanel 或 PauseMenu）")]
        [SerializeField] private RectTransform pauseWindowRect;

        [Header("主菜单内容容器 (打开设置时隐藏)")]
        [SerializeField] private GameObject pauseMenuContent;

        [Header("主菜单按钮")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button multiplayerButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        [Header("主菜单文本")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI resumeText;
        [SerializeField] private TextMeshProUGUI restartText;
        [SerializeField] private TextMeshProUGUI multiplayerText;
        [SerializeField] private TextMeshProUGUI settingsText;
        [SerializeField] private TextMeshProUGUI quitText;

        [Header("设置子面板 (Settings Panel)")]
        [SerializeField] private GameObject settingsPanelObj;
        [SerializeField] private TextMeshProUGUI settingsTitleText;
        [SerializeField] private TextMeshProUGUI langLabelText;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private TextMeshProUGUI settingsBackText;
        [SerializeField] private Button langChineseButton;
        [SerializeField] private Button langSpanishButton;
        [SerializeField] private Button langEnglishButton;
        [SerializeField] private Toggle perfHudToggle;
        [SerializeField] private TextMeshProUGUI perfToggleText;

        [Header("外部 UI 引用")]
        [SerializeField] private MultiplayerLobbyUI multiplayerLobbyUI;

        private bool _isPaused = false;
        public bool IsPaused => _isPaused;

        private bool _listenersBound = false;
        private int _lastToggleFrame = -1;

        public static PauseMenuUI EnsureInstance()
        {
            if (Instance != null) return Instance;

            var found = FindAnyObjectByType<PauseMenuUI>();
            if (found != null)
            {
                Instance = found;
                return Instance;
            }

            ModernUIBuilder.BuildModernUI();
            return Instance;
        }

        public void InitializeReferences(
            GameObject menuRoot,
            CanvasGroup canvasGroup,
            RectTransform windowRect,
            GameObject menuContent,
            Button resumeBtn,
            Button restartBtn,
            Button multiplayerBtn,
            Button settingsBtn,
            Button quitBtn,
            TextMeshProUGUI titleTxt,
            TextMeshProUGUI resumeTxt,
            TextMeshProUGUI restartTxt,
            TextMeshProUGUI multiplayerTxt,
            TextMeshProUGUI settingsTxt,
            TextMeshProUGUI quitTxt,
            GameObject settingsPanel,
            TextMeshProUGUI setTileTxt,
            TextMeshProUGUI langLblTxt,
            Button settingsBackBtn,
            TextMeshProUGUI setBackTxt,
            Button langCnBtn,
            Button langEsBtn,
            Button langEnBtn,
            Toggle perfToggle,
            TextMeshProUGUI perfTglTxt,
            MultiplayerLobbyUI lobbyUI)
        {
            menuContentRoot = menuRoot;
            pauseCanvasGroup = canvasGroup;
            pauseWindowRect = windowRect;
            pauseMenuContent = menuContent;

            resumeButton = resumeBtn;
            restartButton = restartBtn;
            multiplayerButton = multiplayerBtn;
            settingsButton = settingsBtn;
            quitButton = quitBtn;

            titleText = titleTxt;
            resumeText = resumeTxt;
            restartText = restartTxt;
            multiplayerText = multiplayerTxt;
            settingsText = settingsTxt;
            quitText = quitTxt;

            settingsPanelObj = settingsPanel;
            settingsTitleText = setTileTxt;
            langLabelText = langLblTxt;
            settingsBackButton = settingsBackBtn;
            settingsBackText = setBackTxt;
            langChineseButton = langCnBtn;
            langSpanishButton = langEsBtn;
            langEnglishButton = langEnBtn;
            perfHudToggle = perfToggle;
            perfToggleText = perfTglTxt;
            multiplayerLobbyUI = lobbyUI;

            BindListeners();
            HideImmediate();
        }

        private void BindListeners()
        {
            if (_listenersBound) return;
            _listenersBound = true;

            // 绑定菜单按钮
            if (resumeButton != null) resumeButton.onClick.AddListener(ResumeGame);
            if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
            if (multiplayerButton != null) multiplayerButton.onClick.AddListener(OpenMultiplayerLobby);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);

            // 绑定设置面板
            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(CloseSettings);
            if (langChineseButton != null) langChineseButton.onClick.AddListener(() => ChangeLanguage(GameLanguage.Chinese));
            if (langSpanishButton != null) langSpanishButton.onClick.AddListener(() => ChangeLanguage(GameLanguage.Spanish));
            if (langEnglishButton != null) langEnglishButton.onClick.AddListener(() => ChangeLanguage(GameLanguage.English));

            if (perfHudToggle != null)
            {
                bool isVisible = PerformanceHUD.Instance != null 
                    ? PerformanceHUD.Instance.IsVisible 
                    : (PlayerPrefs.GetInt("AsadoSim_ShowPerfHUD", 1) == 1);
                perfHudToggle.SetIsOnWithoutNotify(isVisible);
                perfHudToggle.onValueChanged.AddListener(OnPerfHudToggleChanged);
            }
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            // 自动容错获取组件（当开发者未在 Inspector 手动拖拽时尝试自愈）
            if (pauseCanvasGroup == null)
            {
                pauseCanvasGroup = GetComponent<CanvasGroup>() ?? GetComponentInChildren<CanvasGroup>(true);
            }
            if (pauseWindowRect == null)
            {
                pauseWindowRect = GetComponent<RectTransform>();
            }
            if (menuContentRoot == null)
            {
                if (pauseWindowRect != null && pauseWindowRect.gameObject != gameObject)
                {
                    menuContentRoot = pauseWindowRect.gameObject;
                }
            }

            BindListeners();

            if (settingsPanelObj != null) settingsPanelObj.SetActive(false);

            // 初始隐藏 UI 视觉（但保持当前脚本 GameObject 处于激活状态以接收 Update 输入！）
            HideImmediate();
        }

        private void Start()
        {
            LanguageManager.OnLanguageChanged += RefreshLocalizedTexts;
            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);
        }

        private void OnDestroy()
        {
            LanguageManager.OnLanguageChanged -= RefreshLocalizedTexts;
        }

        public void TogglePause()
        {
            // 防同一帧由多个输入源（如 PlayerController 与 PauseMenuUI.Update）重复触发
            if (_lastToggleFrame == Time.frameCount) return;
            _lastToggleFrame = Time.frameCount;

            if (_isPaused)
            {
                // 如果当前正在联机大厅子界面，ESC 优先退回到暂停主面板
                if (multiplayerLobbyUI != null && multiplayerLobbyUI.gameObject.activeSelf)
                {
                    multiplayerLobbyUI.Hide();
                }
                else if (settingsPanelObj != null && settingsPanelObj.activeSelf)
                {
                    CloseSettings();
                }
                else
                {
                    ResumeGame();
                }
            }
            else
            {
                PauseGame();
            }
        }

        private void Update()
        {
            // 严格使用 Unity Input System 避免 InvalidOperationException 异常
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                TogglePause();
            }
        }

        /// <summary>
        /// 唤出暂停菜单
        /// </summary>
        public void PauseGame()
        {
            _isPaused = true;
            Debug.Log("[PauseMenuUI] 触发暂停，开始显示UI");

            // 1. 操作 menuContentRoot 并提供明确的空指针报错
            if (menuContentRoot != null)
            {
                menuContentRoot.SetActive(true);
            }
            else
            {
                Debug.LogError("[PauseMenuUI] 严重错误：PauseMenuUI 缺少 Inspector 引用赋值！menuContentRoot 为空，请在 Inspector 中为 PauseMenuUI 拖拽分配 menuContentRoot（如 PauseMenu 面板 GameObject）！");
            }

            // 2. 操作 pauseCanvasGroup 并提供明确的空指针报错
            if (pauseCanvasGroup != null)
            {
                pauseCanvasGroup.alpha = 1f;
                pauseCanvasGroup.blocksRaycasts = true;
                pauseCanvasGroup.interactable = true;
            }
            else
            {
                Debug.LogError("[PauseMenuUI] 严重错误：PauseMenuUI 缺少 Inspector 引用赋值！pauseCanvasGroup 为空，请在 Inspector 中为 PauseMenuUI 拖拽分配 CanvasGroup 组件！");
            }

            // 3. 通知 NetworkGameManager / 设置时间缩放与光标解锁
            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.Instance.SetPauseState(true);
            }
            else
            {
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);

            if (pauseMenuContent != null) pauseMenuContent.SetActive(true);
            if (settingsPanelObj != null) settingsPanelObj.SetActive(false);

            // 4. 播放平滑弹性弹出动画（严格使用 unscaledDeltaTime，暂停时绝不卡住）
            if (pauseWindowRect != null && pauseCanvasGroup != null)
            {
                StartCoroutine(ModernUIAnimationHelper.AnimatePopIn(pauseWindowRect, pauseCanvasGroup, 0.22f));
            }
        }

        /// <summary>
        /// 恢复游戏
        /// </summary>
        public void ResumeGame()
        {
            _isPaused = false;
            _lastToggleFrame = Time.frameCount;
            Debug.Log("[PauseMenuUI] 恢复游戏，隐藏UI");

            if (NetworkGameManager.Instance != null)
            {
                NetworkGameManager.Instance.SetPauseState(false);
            }
            else
            {
                Time.timeScale = 1.0f;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (pauseCanvasGroup != null)
            {
                pauseCanvasGroup.blocksRaycasts = false;
                pauseCanvasGroup.interactable = false;
            }

            if (pauseWindowRect != null && pauseCanvasGroup != null)
            {
                StartCoroutine(ModernUIAnimationHelper.AnimatePopOut(pauseWindowRect, pauseCanvasGroup, 0.16f, () =>
                {
                    if (menuContentRoot != null && menuContentRoot != gameObject) menuContentRoot.SetActive(false);
                    if (pauseMenuContent != null) pauseMenuContent.SetActive(false);
                    if (settingsPanelObj != null) settingsPanelObj.SetActive(false);
                }));
            }
            else
            {
                if (menuContentRoot != null && menuContentRoot != gameObject) menuContentRoot.SetActive(false);
                if (pauseMenuContent != null) pauseMenuContent.SetActive(false);
                if (settingsPanelObj != null) settingsPanelObj.SetActive(false);
            }
        }

        private void HideImmediate()
        {
            _isPaused = false;
            if (pauseCanvasGroup != null)
            {
                pauseCanvasGroup.alpha = 0f;
                pauseCanvasGroup.blocksRaycasts = false;
                pauseCanvasGroup.interactable = false;
            }

            if (menuContentRoot != null && menuContentRoot != gameObject)
            {
                menuContentRoot.SetActive(false);
            }
            if (pauseMenuContent != null) pauseMenuContent.SetActive(false);
            if (settingsPanelObj != null) settingsPanelObj.SetActive(false);
        }

        public void RestartGame()
        {
            Time.timeScale = 1.0f;
            var net = AsadoNetworkManager.Instance;
            if (net != null && net.IsNetworkActive)
            {
                if (!net.IsHost)
                {
                    // 客户端队员点击：安全退出联机房间，绝不重置房主世界！
                    Debug.Log("[PauseMenuUI] 客户端队员退出房间，断开网络连接回到本地状态。");
                    net.StopGame();
                    ResumeGame();
                    return;
                }
                else
                {
                    // 房主点击：关闭网络服务后重载当前场景
                    net.StopGame();
                }
            }

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public void OpenMultiplayerLobby()
        {
            if (multiplayerLobbyUI != null)
            {
                // 1. 完全隐藏暂停菜单根节点与遮罩，彻底杜绝遮挡和拦截点击！
                if (menuContentRoot != null)
                {
                    menuContentRoot.SetActive(false);
                }
                if (pauseCanvasGroup != null)
                {
                    pauseCanvasGroup.blocksRaycasts = false;
                    pauseCanvasGroup.interactable = false;
                }

                // 2. 将联机大厅移至 Canvas 最前层并展示
                multiplayerLobbyUI.transform.SetAsLastSibling();
                multiplayerLobbyUI.Show(() =>
                {
                    // 3. 联机大厅关闭返回时，平滑重新显示暂停主面板
                    if (menuContentRoot != null)
                    {
                        menuContentRoot.SetActive(true);
                    }
                    if (pauseCanvasGroup != null)
                    {
                        pauseCanvasGroup.blocksRaycasts = true;
                        pauseCanvasGroup.interactable = true;
                        pauseCanvasGroup.alpha = 1f;
                    }
                    if (pauseMenuContent != null)
                    {
                        pauseMenuContent.SetActive(true);
                    }
                    if (settingsPanelObj != null)
                    {
                        settingsPanelObj.SetActive(false);
                    }
                    if (pauseWindowRect != null && pauseCanvasGroup != null)
                    {
                        StartCoroutine(ModernUIAnimationHelper.AnimatePopIn(pauseWindowRect, pauseCanvasGroup, 0.18f));
                    }
                });
            }
        }

        public void OpenSettings()
        {
            // 关键逻辑：点击系统设置时，彻底隐藏主菜单内容（暂停标题与各个按钮），绝不重叠！
            if (pauseMenuContent != null)
            {
                pauseMenuContent.SetActive(false);
            }
            if (settingsPanelObj != null)
            {
                settingsPanelObj.SetActive(true);
            }

            // 同步性能悬浮窗开关状态
            if (perfHudToggle != null)
            {
                bool isVisible = PerformanceHUD.Instance != null 
                    ? PerformanceHUD.Instance.IsVisible 
                    : (PlayerPrefs.GetInt("AsadoSim_ShowPerfHUD", 1) == 1);
                perfHudToggle.SetIsOnWithoutNotify(isVisible);
            }

            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);
        }

        public void CloseSettings()
        {
            // 返回时：隐藏设置面板，重新显示主菜单内容
            if (settingsPanelObj != null)
            {
                settingsPanelObj.SetActive(false);
            }
            if (pauseMenuContent != null)
            {
                pauseMenuContent.SetActive(true);
            }

            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);
        }

        private void ChangeLanguage(GameLanguage lang)
        {
            if (LanguageManager.Instance != null)
            {
                LanguageManager.Instance.SetLanguage(lang);
            }
        }

        private void OnPerfHudToggleChanged(bool isOn)
        {
            PlayerPrefs.SetInt("AsadoSim_ShowPerfHUD", isOn ? 1 : 0);
            PlayerPrefs.Save();

            if (PerformanceHUD.Instance != null)
            {
                PerformanceHUD.Instance.SetVisible(isOn);
            }
        }

        public void QuitGame()
        {
            Debug.Log("[PauseMenu] 退出游戏...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void RefreshLocalizedTexts(GameLanguage lang)
        {
            var net = AsadoNetworkManager.Instance;
            bool isMultiplayer = net != null && net.IsNetworkActive;
            bool isHost = isMultiplayer && net.IsHost;
            bool isClient = isMultiplayer && !isHost;

            if (titleText != null)
            {
                string baseTitle = LanguageManager.T("pause_title");
                if (isHost)
                {
                    titleText.text = $"{baseTitle} <size=65%><color=#F1C40F>[👑 {LanguageManager.T("role_host")}]</color></size>";
                }
                else if (isClient)
                {
                    titleText.text = $"{baseTitle} <size=65%><color=#3498DB>[🎮 {LanguageManager.T("role_client")} #{net.LocalPlayerId}]</color></size>";
                }
                else
                {
                    titleText.text = baseTitle;
                }
            }

            if (resumeText != null) resumeText.text = LanguageManager.T("pause_resume");

            if (restartText != null)
            {
                // 若为联机客户端队员，显示“离开房间”以防误触导致世界重置
                restartText.text = isClient ? LanguageManager.T("pause_leave_room") : LanguageManager.T("pause_restart");
            }

            if (multiplayerText != null) multiplayerText.text = LanguageManager.T("pause_multiplayer");
            if (settingsText != null) settingsText.text = LanguageManager.T("pause_settings");
            if (quitText != null) quitText.text = LanguageManager.T("pause_quit");

            if (settingsTitleText != null) settingsTitleText.text = LanguageManager.T("settings_title");
            if (langLabelText != null) langLabelText.text = LanguageManager.T("lang_label");
            if (settingsBackText != null) settingsBackText.text = LanguageManager.T("btn_back");
            if (perfToggleText != null) perfToggleText.text = LanguageManager.T("settings_hud_toggle");
        }
    }
}
