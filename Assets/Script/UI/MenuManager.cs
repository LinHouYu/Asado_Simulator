using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 游戏主菜单核心管理组件 (MenuManager)
    /// 挂载于 Hierarchy 根节点的 [MenuManager] 物体上，统一管理场景内所有菜单逻辑：
    /// 1. 开始游戏 (跳转至 Game 场景)、游戏设置、退出游戏。
    /// 2. 独立背景图片插槽 (backgroundImage / customBackgroundSprite)，可在 Inspector 自由拖拽替换壁纸。
    /// 3. 设置面板内嵌 3 种语言动态切换 (中文 / 西班牙语 / 英语)，全界面文本即时无缝重绘并高亮当前语言。
    /// 4. 严格配置 Scale With Screen Size (1920x1080 响应式设计)，适配所有屏幕。
    /// 5. 零限制布局：完全移除强制锁定锚点的 LayoutGroup，所有按钮与文本具备独立 RectTransform，用户可在 Scene 视口自由拖动位姿与修改锚点！
    /// 6. 设置面板默认隐藏，彻底杜绝文字与按钮重叠挤在一起。
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuManager : MonoBehaviour
    {
        public static MenuManager Instance { get; private set; }

        [Header("🖼️ 背景图片配置 (在此直接拖拽你的菜单壁纸)")]
        [Tooltip("主菜单全屏背景 Image 组件")]
        [SerializeField] private Image backgroundImage;

        [Tooltip("自定义背景图片 Sprite (拖拽你的图片到此处，Inspector 即时预览生效)")]
        [SerializeField] private Sprite customBackgroundSprite;

        [Header("🎮 主菜单操作按钮")]
        [SerializeField] private Button startGameButton;
        [SerializeField] private TextMeshProUGUI startButtonText;

        [SerializeField] private Button settingsButton;
        [SerializeField] private TextMeshProUGUI settingsButtonText;

        [SerializeField] private Button quitButton;
        [SerializeField] private TextMeshProUGUI quitButtonText;

        [Header("🚀 场景跳转目标")]
        [Tooltip("点击开始游戏时加载的目标场景名")]
        [SerializeField] private string gameSceneName = "Game";

        [Header("⚙️ 设置子面板 (Settings Modal - 默认隐藏)")]
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private CanvasGroup settingsCanvasGroup;
        [SerializeField] private RectTransform settingsWindowRect;
        [SerializeField] private Button settingsBackButton;
        [SerializeField] private TextMeshProUGUI settingsBackText;

        [Header("🌐 多语言切换按钮 (三种语言)")]
        [SerializeField] private Button langChineseButton;
        [SerializeField] private TextMeshProUGUI langChineseText;
        [SerializeField] private Image langChineseBg;

        [SerializeField] private Button langSpanishButton;
        [SerializeField] private TextMeshProUGUI langSpanishText;
        [SerializeField] private Image langSpanishBg;

        [SerializeField] private Button langEnglishButton;
        [SerializeField] private TextMeshProUGUI langEnglishText;
        [SerializeField] private Image langEnglishBg;

        [Header("🏷️ 标题与文本组件")]
        [SerializeField] private TextMeshProUGUI gameTitleText;
        [SerializeField] private TextMeshProUGUI gameSubtitleText;
        [SerializeField] private TextMeshProUGUI versionText;
        [SerializeField] private TextMeshProUGUI settingsTitleText;
        [SerializeField] private TextMeshProUGUI langLabelText;

        // 颜色主题定义
        private static readonly Color AccentGold = new Color(1.0f, 0.75f, 0.20f, 1f);
        private static readonly Color BtnNormalColor = new Color(0.15f, 0.18f, 0.25f, 0.95f);
        private static readonly Color BtnActiveColor = new Color(0.95f, 0.70f, 0.16f, 1.0f);
        private static readonly Color TextWhite = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color TextDark = new Color(0.10f, 0.10f, 0.14f, 1f);

        private bool _listenersBound = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapMenu()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene != null && string.Equals(activeScene.name, "Menu", StringComparison.OrdinalIgnoreCase))
            {
                if (FindAnyObjectByType<MenuManager>() == null)
                {
                    GameObject managerObj = new GameObject("MenuManager");
                    var manager = managerObj.AddComponent<MenuManager>();
                    manager.EnsureUIHierarchy();
                }
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

            // 确保多语言单例正常就绪
            LanguageManager.EnsureInstance();

            // 确保主菜单鼠标始终解锁可用
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 检查并自动装配 UI（若 Hierarchy 尚未配置）
            EnsureUIHierarchy();

            BindListeners();
        }

        private void Start()
        {
            // 确保背景图片生效
            ApplyBackgroundSprite();

            // 多语言监听
            LanguageManager.OnLanguageChanged += HandleLanguageChanged;
            GameLanguage initialLang = LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese;
            RefreshLocalizedTexts(initialLang);
            UpdateLanguageButtonHighlights(initialLang);

            // 关键：初始时保证设置弹窗严格隐藏，杜绝重叠显示
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            LanguageManager.OnLanguageChanged -= HandleLanguageChanged;
        }

        private void OnValidate()
        {
            // 编辑器下修改 customBackgroundSprite 时实时反映到 backgroundImage 上
            ApplyBackgroundSprite();
        }

        /// <summary>
        /// 外部或 Inspector 动态设置背景壁纸
        /// </summary>
        public void SetBackgroundSprite(Sprite newSprite)
        {
            customBackgroundSprite = newSprite;
            ApplyBackgroundSprite();
        }

        private void ApplyBackgroundSprite()
        {
            if (backgroundImage == null) return;

            if (customBackgroundSprite != null)
            {
                backgroundImage.sprite = customBackgroundSprite;
                backgroundImage.color = Color.white;
            }
            else if (backgroundImage.sprite == null)
            {
                // 生成高质量深色炭火渐变暗角底图，防止白板
                backgroundImage.sprite = GenerateProceduralMenuBackground();
                backgroundImage.color = Color.white;
            }
        }

        private void BindListeners()
        {
            if (_listenersBound) return;
            _listenersBound = true;

            if (startGameButton != null) startGameButton.onClick.AddListener(StartGame);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);

            if (settingsBackButton != null) settingsBackButton.onClick.AddListener(CloseSettings);

            if (langChineseButton != null) langChineseButton.onClick.AddListener(SelectChinese);
            if (langSpanishButton != null) langSpanishButton.onClick.AddListener(SelectSpanish);
            if (langEnglishButton != null) langEnglishButton.onClick.AddListener(SelectEnglish);
        }

        #region 游戏菜单业务逻辑 (支持 UnityEvent 检查器绑定)

        /// <summary>
        /// 开始游戏 (跳转目标场景)
        /// </summary>
        public void StartGame()
        {
            Debug.Log($"[MenuManager] 🎮 开始游戏，正在加载场景: {gameSceneName}...");
            StartCoroutine(LoadSceneWithFade(gameSceneName));
        }

        private IEnumerator LoadSceneWithFade(string sceneName)
        {
            yield return new WaitForSecondsRealtime(0.1f);
            SceneManager.LoadScene(sceneName);
        }

        /// <summary>
        /// 打开设置弹窗
        /// </summary>
        public void OpenSettings()
        {
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(true);
                if (settingsWindowRect != null && settingsCanvasGroup != null)
                {
                    StartCoroutine(ModernUIAnimationHelper.AnimatePopIn(settingsWindowRect, settingsCanvasGroup, 0.2f));
                }
            }

            GameLanguage currentLang = LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese;
            UpdateLanguageButtonHighlights(currentLang);
        }

        /// <summary>
        /// 关闭设置弹窗
        /// </summary>
        public void CloseSettings()
        {
            if (settingsPanel == null) return;

            if (settingsWindowRect != null && settingsCanvasGroup != null)
            {
                StartCoroutine(ModernUIAnimationHelper.AnimatePopOut(settingsWindowRect, settingsCanvasGroup, 0.15f, () =>
                {
                    settingsPanel.SetActive(false);
                }));
            }
            else
            {
                settingsPanel.SetActive(false);
            }
        }

        public void SelectChinese() => SelectLanguage(GameLanguage.Chinese);
        public void SelectSpanish() => SelectLanguage(GameLanguage.Spanish);
        public void SelectEnglish() => SelectLanguage(GameLanguage.English);

        /// <summary>
        /// 选择语言并即时刷新界面全部文本
        /// </summary>
        public void SelectLanguage(GameLanguage lang)
        {
            LanguageManager.ChangeLanguage(lang);
            RefreshLocalizedTexts(lang);
            UpdateLanguageButtonHighlights(lang);
        }

        /// <summary>
        /// 退出游戏
        /// </summary>
        public void QuitGame()
        {
            Debug.Log("[MenuManager] 🚪 退出游戏程序。");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleLanguageChanged(GameLanguage lang)
        {
            RefreshLocalizedTexts(lang);
            UpdateLanguageButtonHighlights(lang);
        }

        /// <summary>
        /// 动态刷新主菜单与设置的所有本地化文本
        /// </summary>
        public void RefreshLocalizedTexts(GameLanguage lang)
        {
            if (gameTitleText != null) gameTitleText.text = LanguageManager.T("menu_game_title");
            if (gameSubtitleText != null) gameSubtitleText.text = LanguageManager.T("menu_subtitle");

            if (startButtonText != null) startButtonText.text = $"🍖  {LanguageManager.T("menu_start")}";
            if (settingsButtonText != null) settingsButtonText.text = $"⚙️   {LanguageManager.T("menu_settings")}";
            if (quitButtonText != null) quitButtonText.text = $"🚪  {LanguageManager.T("menu_quit")}";

            if (settingsTitleText != null) settingsTitleText.text = $"⚙️ {LanguageManager.T("settings_title")}";
            if (langLabelText != null) langLabelText.text = LanguageManager.T("lang_label");
            if (settingsBackText != null) settingsBackText.text = LanguageManager.T("btn_back");

            if (langChineseText != null) langChineseText.text = LanguageManager.T("lang_chinese");
            if (langSpanishText != null) langSpanishText.text = LanguageManager.T("lang_spanish");
            if (langEnglishText != null) langEnglishText.text = LanguageManager.T("lang_english");
        }

        /// <summary>
        /// 更新语言按钮的高亮选定状态
        /// </summary>
        private void UpdateLanguageButtonHighlights(GameLanguage currentLang)
        {
            SetButtonActiveStyle(langChineseBg, langChineseText, currentLang == GameLanguage.Chinese);
            SetButtonActiveStyle(langSpanishBg, langSpanishText, currentLang == GameLanguage.Spanish);
            SetButtonActiveStyle(langEnglishBg, langEnglishText, currentLang == GameLanguage.English);
        }

        private void SetButtonActiveStyle(Image bg, TextMeshProUGUI txt, bool isActive)
        {
            if (bg != null)
            {
                bg.color = isActive ? BtnActiveColor : BtnNormalColor;
            }
            if (txt != null)
            {
                txt.color = isActive ? TextDark : TextWhite;
                txt.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        #endregion

        #region 全自动 UI 节点检测与动态构建 (完全无锁定 LayoutGroup)

        /// <summary>
        /// 确保场景内存在完整的现代化 UI 层级结构
        /// 彻底移除所有锁定 RectTransform 的 LayoutGroup，使所有按钮与文本均可独立调位与设置锚点！
        /// </summary>
        public void EnsureUIHierarchy()
        {
            // 1. 确保 EventSystem 存在
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                try
                {
                    es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                }
                catch
                {
                    es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                }
            }

            // 2. 查找或创建独立根节点 [Menu_Canvas]
            GameObject canvasObj = GameObject.Find("[Menu_Canvas]");
            Canvas canvas = null;
            if (canvasObj == null)
            {
                canvasObj = new GameObject("[Menu_Canvas]");
                canvasObj.layer = LayerMask.NameToLayer("UI");

                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 10;

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                canvasObj.AddComponent<GraphicRaycaster>();
            }
            else
            {
                canvas = canvasObj.GetComponent<Canvas>();
            }

            var font = ModernUIBuilder.GetChineseFontAsset();

            // 3. 全屏背景图片 (BackgroundImage)
            Transform bgTrans = canvasObj.transform.Find("BackgroundImage");
            if (bgTrans == null)
            {
                GameObject bgObj = new GameObject("BackgroundImage");
                bgObj.transform.SetParent(canvasObj.transform, false);
                var rt = bgObj.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                backgroundImage = bgObj.AddComponent<Image>();
                backgroundImage.raycastTarget = false;
            }
            else
            {
                backgroundImage = bgTrans.GetComponent<Image>();
            }
            ApplyBackgroundSprite();

            // 4. 暗色微暗角遮罩 (DarkOverlay)
            Transform overlayTrans = canvasObj.transform.Find("DarkOverlay");
            if (overlayTrans == null)
            {
                GameObject olObj = new GameObject("DarkOverlay");
                olObj.transform.SetParent(canvasObj.transform, false);
                var rt = olObj.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var olImg = olObj.AddComponent<Image>();
                olImg.color = new Color(0f, 0f, 0f, 0.40f);
                olImg.raycastTarget = false;
            }

            // 5. 左侧主菜单容器 (MainMenuPanel)
            Transform menuContentTrans = canvasObj.transform.Find("MainMenuPanel");
            GameObject menuContentObj;
            if (menuContentTrans == null)
            {
                menuContentObj = new GameObject("MainMenuPanel");
                menuContentObj.transform.SetParent(canvasObj.transform, false);
                var rt = menuContentObj.AddComponent<RectTransform>();
                // 左侧靠边垂直撑满，宽度 500
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(120f, 0f);
                rt.sizeDelta = new Vector2(500f, 0f);
            }
            else
            {
                menuContentObj = menuContentTrans.gameObject;
            }

            // 游戏主标题 (GameTitle)
            Transform titleTrans = menuContentObj.transform.Find("GameTitle");
            if (titleTrans == null)
            {
                GameObject tObj = new GameObject("GameTitle");
                tObj.transform.SetParent(menuContentObj.transform, false);
                var rt = tObj.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -140f);
                rt.sizeDelta = new Vector2(520f, 65f);

                gameTitleText = tObj.AddComponent<TextMeshProUGUI>();
                if (font != null) gameTitleText.font = font;
                gameTitleText.fontSize = 44f;
                gameTitleText.fontStyle = FontStyles.Bold;
                gameTitleText.color = AccentGold;
                gameTitleText.text = "ASADO SIMULATOR";
                gameTitleText.alignment = TextAlignmentOptions.Left;
                gameTitleText.textWrappingMode = TextWrappingModes.NoWrap;
                gameTitleText.raycastTarget = false;
            }
            else
            {
                gameTitleText = titleTrans.GetComponent<TextMeshProUGUI>();
            }

            // 游戏副标题 (GameSubtitle)
            Transform subTrans = menuContentObj.transform.Find("GameSubtitle");
            if (subTrans == null)
            {
                GameObject sObj = new GameObject("GameSubtitle");
                sObj.transform.SetParent(menuContentObj.transform, false);
                var rt = sObj.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -215f);
                rt.sizeDelta = new Vector2(520f, 36f);

                gameSubtitleText = sObj.AddComponent<TextMeshProUGUI>();
                if (font != null) gameSubtitleText.font = font;
                gameSubtitleText.fontSize = 20f;
                gameSubtitleText.color = new Color(0.85f, 0.88f, 0.95f, 0.9f);
                gameSubtitleText.text = "阿根廷烤肉模拟器";
                gameSubtitleText.alignment = TextAlignmentOptions.Left;
                gameSubtitleText.textWrappingMode = TextWrappingModes.NoWrap;
                gameSubtitleText.raycastTarget = false;
            }
            else
            {
                gameSubtitleText = subTrans.GetComponent<TextMeshProUGUI>();
            }

            // 金色装饰线条 (TitleDivider)
            Transform divTrans = menuContentObj.transform.Find("TitleDivider");
            if (divTrans == null)
            {
                GameObject dObj = new GameObject("TitleDivider");
                dObj.transform.SetParent(menuContentObj.transform, false);
                var rt = dObj.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -260f);
                rt.sizeDelta = new Vector2(320f, 3f);
                var img = dObj.AddComponent<Image>();
                img.color = AccentGold;
                img.raycastTarget = false;
            }

            // 1. 开始游戏按钮 (独立绝对位置与锚点，绝对不挤在一起)
            Transform startBtnTrans = menuContentObj.transform.Find("StartGameButton");
            if (startBtnTrans == null)
            {
                startGameButton = CreateModernMenuButton(
                    menuContentObj.transform, 
                    "StartGameButton", 
                    "🍖  开始游戏", 
                    font, 
                    new Vector2(0f, -315f), 
                    new Vector2(340f, 62f), 
                    out startButtonText);
            }
            else
            {
                startGameButton = startBtnTrans.GetComponent<Button>();
                startButtonText = startBtnTrans.GetComponentInChildren<TextMeshProUGUI>();
            }

            // 2. 游戏设置按钮 (Y = -395f，与上一按钮相距 80 像素，清晰分明)
            Transform settingsBtnTrans = menuContentObj.transform.Find("SettingsButton");
            if (settingsBtnTrans == null)
            {
                settingsButton = CreateModernMenuButton(
                    menuContentObj.transform, 
                    "SettingsButton", 
                    "⚙️   游戏设置", 
                    font, 
                    new Vector2(0f, -395f), 
                    new Vector2(340f, 62f), 
                    out settingsButtonText);
            }
            else
            {
                settingsButton = settingsBtnTrans.GetComponent<Button>();
                settingsButtonText = settingsBtnTrans.GetComponentInChildren<TextMeshProUGUI>();
            }

            // 3. 退出游戏按钮 (Y = -475f，间距充裕)
            Transform quitBtnTrans = menuContentObj.transform.Find("QuitButton");
            if (quitBtnTrans == null)
            {
                quitButton = CreateModernMenuButton(
                    menuContentObj.transform, 
                    "QuitButton", 
                    "🚪  退出游戏", 
                    font, 
                    new Vector2(0f, -475f), 
                    new Vector2(340f, 62f), 
                    out quitButtonText);
            }
            else
            {
                quitButton = quitBtnTrans.GetComponent<Button>();
                quitButtonText = quitBtnTrans.GetComponentInChildren<TextMeshProUGUI>();
            }

            // 版本号小标签 (位于左下方)
            Transform verTrans = menuContentObj.transform.Find("VersionText");
            if (verTrans == null)
            {
                GameObject vObj = new GameObject("VersionText");
                vObj.transform.SetParent(menuContentObj.transform, false);
                var rt = vObj.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(0f, 35f);
                rt.sizeDelta = new Vector2(400f, 25f);

                versionText = vObj.AddComponent<TextMeshProUGUI>();
                if (font != null) versionText.font = font;
                versionText.fontSize = 14f;
                versionText.color = new Color(0.6f, 0.65f, 0.75f, 0.7f);
                versionText.text = "v1.0.0 · Argentine BBQ Simulator";
                versionText.raycastTarget = false;
            }
            else
            {
                versionText = verTrans.GetComponent<TextMeshProUGUI>();
            }

            // 6. 设置模态窗口 (SettingsModal - 默认在层级里设为隐藏，杜绝遮盖或文字混乱)
            Transform settingsModalTrans = canvasObj.transform.Find("SettingsModal");
            if (settingsModalTrans == null)
            {
                BuildSettingsModal(canvasObj.transform, font);
            }
            else
            {
                settingsPanel = settingsModalTrans.gameObject;
                settingsCanvasGroup = settingsPanel.GetComponent<CanvasGroup>();
                settingsWindowRect = settingsPanel.transform.Find("SettingsCard") as RectTransform;

                var backTrans = settingsPanel.transform.Find("SettingsCard/BackButton");
                if (backTrans != null)
                {
                    settingsBackButton = backTrans.GetComponent<Button>();
                    settingsBackText = backTrans.GetComponentInChildren<TextMeshProUGUI>();
                }
                var zhTrans = settingsPanel.transform.Find("SettingsCard/LangChineseButton");
                if (zhTrans != null)
                {
                    langChineseButton = zhTrans.GetComponent<Button>();
                    langChineseBg = zhTrans.GetComponent<Image>();
                    langChineseText = zhTrans.GetComponentInChildren<TextMeshProUGUI>();
                }
                var esTrans = settingsPanel.transform.Find("SettingsCard/LangSpanishButton");
                if (esTrans != null)
                {
                    langSpanishButton = esTrans.GetComponent<Button>();
                    langSpanishBg = esTrans.GetComponent<Image>();
                    langSpanishText = esTrans.GetComponentInChildren<TextMeshProUGUI>();
                }
                var enTrans = settingsPanel.transform.Find("SettingsCard/LangEnglishButton");
                if (enTrans != null)
                {
                    langEnglishButton = enTrans.GetComponent<Button>();
                    langEnglishBg = enTrans.GetComponent<Image>();
                    langEnglishText = enTrans.GetComponentInChildren<TextMeshProUGUI>();
                }
            }

            // 始终确保设置弹窗初始状态隐藏
            if (settingsPanel != null)
            {
                settingsPanel.SetActive(false);
            }
        }

        private Button CreateModernMenuButton(
            Transform parent, 
            string name, 
            string label, 
            TMP_FontAsset font, 
            Vector2 anchoredPos, 
            Vector2 size, 
            out TextMeshProUGUI labelTmp)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = obj.AddComponent<Image>();
            img.color = new Color(0.13f, 0.16f, 0.22f, 0.95f);

            var btn = obj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.14f, 0.17f, 0.24f, 0.95f);
            colors.highlightedColor = new Color(0.25f, 0.30f, 0.42f, 1.0f);
            colors.pressedColor = new Color(0.95f, 0.72f, 0.18f, 1.0f);
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;
            btn.targetGraphic = img;

            // 按钮文本 (居中清晰对齐)
            GameObject tObj = new GameObject("Text");
            tObj.transform.SetParent(obj.transform, false);
            var tRt = tObj.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(20f, 0f);
            tRt.offsetMax = new Vector2(-20f, 0f);

            labelTmp = tObj.AddComponent<TextMeshProUGUI>();
            if (font != null) labelTmp.font = font;
            labelTmp.fontSize = 21f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = TextWhite;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.textWrappingMode = TextWrappingModes.NoWrap;
            labelTmp.raycastTarget = false;
            labelTmp.text = label;

            return btn;
        }

        private void BuildSettingsModal(Transform canvasTrans, TMP_FontAsset font)
        {
            GameObject modalObj = new GameObject("SettingsModal");
            modalObj.transform.SetParent(canvasTrans, false);
            settingsPanel = modalObj;

            var modalRt = modalObj.AddComponent<RectTransform>();
            modalRt.anchorMin = Vector2.zero;
            modalRt.anchorMax = Vector2.one;
            modalRt.offsetMin = Vector2.zero;
            modalRt.offsetMax = Vector2.zero;

            settingsCanvasGroup = modalObj.AddComponent<CanvasGroup>();

            // 半透明暗黑遮罩
            var maskImg = modalObj.AddComponent<Image>();
            maskImg.color = new Color(0f, 0f, 0f, 0.65f);

            // 卡片容器 (居中 600x420)
            GameObject cardObj = new GameObject("SettingsCard");
            cardObj.transform.SetParent(modalObj.transform, false);
            settingsWindowRect = cardObj.AddComponent<RectTransform>();
            settingsWindowRect.anchorMin = new Vector2(0.5f, 0.5f);
            settingsWindowRect.anchorMax = new Vector2(0.5f, 0.5f);
            settingsWindowRect.pivot = new Vector2(0.5f, 0.5f);
            settingsWindowRect.sizeDelta = new Vector2(600f, 420f);
            settingsWindowRect.anchoredPosition = Vector2.zero;

            var cardImg = cardObj.AddComponent<Image>();
            cardImg.color = new Color(0.10f, 0.12f, 0.17f, 0.98f);

            // 标题
            GameObject titleObj = new GameObject("SettingsTitle");
            titleObj.transform.SetParent(cardObj.transform, false);
            var titleRt = titleObj.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -25f);
            titleRt.sizeDelta = new Vector2(0f, 40f);

            settingsTitleText = titleObj.AddComponent<TextMeshProUGUI>();
            if (font != null) settingsTitleText.font = font;
            settingsTitleText.fontSize = 24f;
            settingsTitleText.fontStyle = FontStyles.Bold;
            settingsTitleText.color = AccentGold;
            settingsTitleText.alignment = TextAlignmentOptions.Center;
            settingsTitleText.text = "⚙️ 系统设置";
            settingsTitleText.raycastTarget = false;

            // 分割线
            GameObject sep = new GameObject("Separator");
            sep.transform.SetParent(cardObj.transform, false);
            var sepRt = sep.AddComponent<RectTransform>();
            sepRt.anchorMin = new Vector2(0f, 1f);
            sepRt.anchorMax = new Vector2(1f, 1f);
            sepRt.pivot = new Vector2(0.5f, 1f);
            sepRt.anchoredPosition = new Vector2(0f, -75f);
            sepRt.sizeDelta = new Vector2(540f, 2f);
            var sImg = sep.AddComponent<Image>();
            sImg.color = new Color(1f, 1f, 1f, 0.12f);
            sImg.raycastTarget = false;

            // 语言标签
            GameObject langLblObj = new GameObject("LanguageLabel");
            langLblObj.transform.SetParent(cardObj.transform, false);
            var llRt = langLblObj.AddComponent<RectTransform>();
            llRt.anchorMin = new Vector2(0f, 1f);
            llRt.anchorMax = new Vector2(1f, 1f);
            llRt.pivot = new Vector2(0f, 1f);
            llRt.anchoredPosition = new Vector2(35f, -100f);
            llRt.sizeDelta = new Vector2(400f, 30f);

            langLabelText = langLblObj.AddComponent<TextMeshProUGUI>();
            if (font != null) langLabelText.font = font;
            langLabelText.fontSize = 17f;
            langLabelText.color = new Color(0.85f, 0.88f, 0.95f, 1f);
            langLabelText.text = "语言选择 / Language";
            langLabelText.raycastTarget = false;

            // 三种语言按钮 (各自拥有绝对独立的 RectTransform，横向整齐排列，绝不使用 restrictive LayoutGroup)
            // 按钮宽度 165，高度 50，间隙 15
            langChineseButton = CreateLangPillButton(cardObj.transform, "LangChineseButton", "🇨🇳 简体中文", font, new Vector2(-180f, -170f), out langChineseText, out langChineseBg);
            langSpanishButton = CreateLangPillButton(cardObj.transform, "LangSpanishButton", "🇦🇷 Español", font, new Vector2(0f, -170f), out langSpanishText, out langSpanishBg);
            langEnglishButton = CreateLangPillButton(cardObj.transform, "LangEnglishButton", "🇬🇧 English", font, new Vector2(180f, -170f), out langEnglishText, out langEnglishBg);

            // 返回按钮 (位于弹窗底部)
            GameObject backBtnObj = new GameObject("BackButton");
            backBtnObj.transform.SetParent(cardObj.transform, false);
            var bbRt = backBtnObj.AddComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0.5f, 0f);
            bbRt.anchorMax = new Vector2(0.5f, 0f);
            bbRt.pivot = new Vector2(0.5f, 0f);
            bbRt.anchoredPosition = new Vector2(0f, 35f);
            bbRt.sizeDelta = new Vector2(220f, 50f);

            var bImg = backBtnObj.AddComponent<Image>();
            bImg.color = new Color(0.18f, 0.22f, 0.30f, 1f);

            settingsBackButton = backBtnObj.AddComponent<Button>();
            settingsBackButton.targetGraphic = bImg;

            GameObject bTxtObj = new GameObject("Text");
            bTxtObj.transform.SetParent(backBtnObj.transform, false);
            var btRt = bTxtObj.AddComponent<RectTransform>();
            btRt.anchorMin = Vector2.zero;
            btRt.anchorMax = Vector2.one;
            btRt.offsetMin = Vector2.zero;
            btRt.offsetMax = Vector2.zero;

            settingsBackText = bTxtObj.AddComponent<TextMeshProUGUI>();
            if (font != null) settingsBackText.font = font;
            settingsBackText.fontSize = 18f;
            settingsBackText.fontStyle = FontStyles.Bold;
            settingsBackText.color = TextWhite;
            settingsBackText.alignment = TextAlignmentOptions.Center;
            settingsBackText.textWrappingMode = TextWrappingModes.NoWrap;
            settingsBackText.raycastTarget = false;
            settingsBackText.text = "返回";

            // 初始在层级中设为非激活
            modalObj.SetActive(false);
        }

        private Button CreateLangPillButton(
            Transform parent, 
            string name, 
            string label, 
            TMP_FontAsset font, 
            Vector2 pos, 
            out TextMeshProUGUI textTmp, 
            out Image bgImage)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(165f, 50f);

            bgImage = obj.AddComponent<Image>();
            bgImage.color = BtnNormalColor;

            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = bgImage;

            GameObject tObj = new GameObject("Text");
            tObj.transform.SetParent(obj.transform, false);
            var tRt = tObj.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.offsetMin = Vector2.zero;
            tRt.offsetMax = Vector2.zero;

            textTmp = tObj.AddComponent<TextMeshProUGUI>();
            if (font != null) textTmp.font = font;
            textTmp.fontSize = 16f;
            textTmp.alignment = TextAlignmentOptions.Center;
            textTmp.color = TextWhite;
            textTmp.textWrappingMode = TextWrappingModes.NoWrap;
            textTmp.raycastTarget = false;
            textTmp.text = label;

            return btn;
        }

        /// <summary>
        /// 动态生成 256x256 高质感炭火深色暗角壁纸
        /// </summary>
        private static Sprite GenerateProceduralMenuBackground()
        {
            const int width = 256;
            const int height = 256;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;

            Color topColor = new Color(0.06f, 0.07f, 0.09f, 1f);
            Color bottomColor = new Color(0.14f, 0.08f, 0.06f, 1f);
            Color fireGlow = new Color(0.32f, 0.12f, 0.05f, 1f);

            for (int y = 0; y < height; y++)
            {
                float v = (float)y / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (float)x / width;
                    float dist = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.4f));
                    float vignette = Mathf.Clamp01(1.0f - dist * 0.85f);

                    Color baseCol = Color.Lerp(bottomColor, topColor, v);
                    float fireDist = Vector2.Distance(new Vector2(u, v), new Vector2(0.6f, 0.1f));
                    float glow = Mathf.Clamp01(1.0f - fireDist * 1.5f);

                    Color col = Color.Lerp(baseCol, fireGlow, glow * 0.45f) * (0.5f + 0.5f * vignette);
                    col.a = 1f;
                    tex.SetPixel(x, y, col);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        }

        #endregion
    }
}
