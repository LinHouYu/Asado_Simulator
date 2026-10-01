using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 全自动现代 UI 运行时生成与装配器 (ModernUIBuilder)
    /// 特性：
    /// 1. 零手动拖拽、零手动点击菜单，游戏启动 (Play 模式 / Standalone) 自动静默装配全套现代 UI！
    /// 2. 自动检测并生成 EventSystem、[NetworkCore] 网络单例、[UI_Canvas] (1920x1080 响应式)。
    /// 3. 生成 PerformanceHUD (平滑 FPS / Mirror TCP Ping)、PauseMenu (ESC 暂停与设置)、MultiplayerLobby (联机与穿透工具)。
    /// 4. 自动挂载与动态加载中文字体 Fallback，杜绝所有 TMP Unicode 缺失警告与方块字 (□)。
    /// 5. PauseMenuUI 挂载于主 Canvas 根节点，面板隐藏时绝不中断 Update 循环，ESC 键 100% 灵敏响应。
    /// </summary>
    public static class ModernUIBuilder
    {
        private static readonly Color CardBgColor = new Color(0.12f, 0.13f, 0.18f, 0.96f);
        private static readonly Color MaskBgColor = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color PrimaryBtnColor = new Color(0.20f, 0.23f, 0.30f, 1.0f);
        private static readonly Color AccentGoldColor = new Color(1.0f, 0.72f, 0.18f, 1.0f);
        private static readonly Color TextWhiteColor = new Color(0.95f, 0.95f, 0.95f, 1.0f);
        private static readonly Color InputBgColor = new Color(0.08f, 0.09f, 0.12f, 0.95f);

        private static TMP_FontAsset _cachedChineseFont;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoBootstrap()
        {
            BuildModernUI();
        }

        public static PauseMenuUI BuildModernUI()
        {
            // 如果已存在完整的 UI Canvas 和 PauseMenuUI，直接返回
            if (PauseMenuUI.Instance != null && GameObject.Find("[UI_Canvas]") != null)
            {
                return PauseMenuUI.Instance;
            }

            // 0. 准备中文字体
            EnsureChineseFontFallback();

            // 1. 确保 EventSystem 存在
            EnsureEventSystem();

            // 2. 确保 [NetworkCore] 核心网络单例存在
            EnsureNetworkCore();

            // 3. 创建 [UI_Canvas]
            GameObject canvasObj = GameObject.Find("[UI_Canvas]");
            if (canvasObj == null)
            {
                canvasObj = new GameObject("[UI_Canvas]");
                canvasObj.layer = LayerMask.NameToLayer("UI");

                Canvas canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                canvasObj.AddComponent<GraphicRaycaster>();
                UnityEngine.Object.DontDestroyOnLoad(canvasObj);
            }

            // 4. 创建 PerformanceHUD
            PerformanceHUD perfHud = CreatePerformanceHUD(canvasObj.transform);

            // 5. 创建 MultiplayerLobbyUI (含 TunnelManagerModal & AutoJoinModal)
            MultiplayerLobbyUI lobbyUI = CreateMultiplayerLobby(canvasObj.transform);

            // 6. 创建 PauseMenuUI (挂载于 [UI_Canvas] 保证 Update 永不休眠)
            PauseMenuUI pauseUI = CreatePauseMenu(canvasObj, lobbyUI, perfHud);

            Debug.Log("[ModernUIBuilder] 全套现代 UI 自动构建并绑定完成！ESC 暂停菜单与联机系统已完全就绪。");
            return pauseUI;
        }

        #region 1. 依赖物体检测

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                try
                {
                    esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                }
                catch
                {
                    esObj.AddComponent<StandaloneInputModule>();
                }
                UnityEngine.Object.DontDestroyOnLoad(esObj);
            }
        }

        private static void EnsureNetworkCore()
        {
            GameObject netCore = GameObject.Find("[NetworkCore]");
            if (netCore == null)
            {
                netCore = new GameObject("[NetworkCore]");
                UnityEngine.Object.DontDestroyOnLoad(netCore);
            }

            if (netCore.GetComponent<AsadoNetworkManager>() == null) netCore.AddComponent<AsadoNetworkManager>();
            if (netCore.GetComponent<NetworkGameManager>() == null) netCore.AddComponent<NetworkGameManager>();
            if (netCore.GetComponent<GitHubReleaseDownloader>() == null) netCore.AddComponent<GitHubReleaseDownloader>();
            if (netCore.GetComponent<LanguageManager>() == null) netCore.AddComponent<LanguageManager>();
            if (netCore.GetComponent<AutoJoinDetector>() == null) netCore.AddComponent<AutoJoinDetector>();
        }

        #endregion

        #region 2. 中文字体支持

        public static bool IsFontAssetValid(TMP_FontAsset font)
        {
            if (font == null) return false;
            try
            {
                if (font.atlasTextures == null || font.atlasTextures.Length == 0) return false;
                if (font.atlasTextures[0] == null) return false;
                if (font.material == null) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static TMP_FontAsset GetChineseFontAsset()
        {
            if (_cachedChineseFont != null && IsFontAssetValid(_cachedChineseFont)) 
                return _cachedChineseFont;

            _cachedChineseFont = null;

            // 1. 优先从 Resources 加载已生成的 MSYH SDF 资产 (无任何运行时警告)
            TMP_FontAsset resFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/MSYH SDF");
            if (IsFontAssetValid(resFont))
            {
                _cachedChineseFont = resFont;
            }

#if UNITY_EDITOR
            // 2. 编辑器环境下若未加载到，尝试通过 AssetDatabase 即时获取
            if (_cachedChineseFont == null)
            {
                TMP_FontAsset adFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/MSYH SDF.asset");
                if (IsFontAssetValid(adFont))
                {
                    _cachedChineseFont = adFont;
                }
            }
#endif

            // 3. 回退到 TMP Settings 默认字体 (避免在运行时调用任何抛出警告的外部系统路径或无数据动态字体)
            if (_cachedChineseFont == null)
            {
                _cachedChineseFont = TMP_Settings.defaultFontAsset;
            }

            EnsureChineseFontFallback();
            return _cachedChineseFont;
        }

        private static void EnsureChineseFontFallback()
        {
            if (_cachedChineseFont == null || !IsFontAssetValid(_cachedChineseFont)) return;

            if (TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset != _cachedChineseFont)
            {
                if (TMP_Settings.defaultFontAsset.fallbackFontAssetTable == null)
                {
                    TMP_Settings.defaultFontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                }

                // 清除可能存在的 null 或已失效的旧字体引用，防止 TMPro 解析时抛出 MissingReferenceException
                TMP_Settings.defaultFontAsset.fallbackFontAssetTable.RemoveAll(item => item == null || !IsFontAssetValid(item));

                if (!TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Contains(_cachedChineseFont))
                {
                    TMP_Settings.defaultFontAsset.fallbackFontAssetTable.Add(_cachedChineseFont);
                }
            }

            if (TMP_Settings.fallbackFontAssets != null)
            {
                TMP_Settings.fallbackFontAssets.RemoveAll(item => item == null || !IsFontAssetValid(item));
                if (!TMP_Settings.fallbackFontAssets.Contains(_cachedChineseFont))
                {
                    TMP_Settings.fallbackFontAssets.Add(_cachedChineseFont);
                }
            }
        }

        #endregion

        #region 3. 构造 PerformanceHUD

        private static PerformanceHUD CreatePerformanceHUD(Transform canvasTransform)
        {
            GameObject hudObj = new GameObject("PerformanceHUD");
            hudObj.transform.SetParent(canvasTransform, false);

            var rt = hudObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(24f, -20f);
            rt.sizeDelta = new Vector2(340f, 36f);

            var bgImg = hudObj.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.45f);

            var group = hudObj.AddComponent<CanvasGroup>();
            var hudComp = hudObj.AddComponent<PerformanceHUD>();

            GameObject textObj = new GameObject("HUDText");
            textObj.transform.SetParent(hudObj.transform, false);
            var textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.sizeDelta = new Vector2(-16f, 0f);
            textRt.anchoredPosition = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            var font = GetChineseFontAsset();
            if (font != null) tmp.font = font;
            tmp.fontSize = 16;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.text = "<color=#2ECC71>FPS: 60</color>";

            hudComp.InitializeReferences(tmp, group);
            return hudComp;
        }

        #endregion

        #region 4. 构造 PauseMenuUI

        private static PauseMenuUI CreatePauseMenu(GameObject canvasObj, MultiplayerLobbyUI lobbyUI, PerformanceHUD perfHud)
        {
            // PauseMenuUI 挂载在 canvasObj 上，自身 GameObject 永远不设为 Inactive！
            var pauseUI = canvasObj.GetComponent<PauseMenuUI>();
            if (pauseUI == null) pauseUI = canvasObj.AddComponent<PauseMenuUI>();

            // 根面板容器 PauseMenu
            GameObject pauseMenuRoot = new GameObject("PauseMenu");
            pauseMenuRoot.transform.SetParent(canvasObj.transform, false);

            var pmRt = pauseMenuRoot.AddComponent<RectTransform>();
            pmRt.anchorMin = Vector2.zero;
            pmRt.anchorMax = Vector2.one;
            pmRt.sizeDelta = Vector2.zero;
            pmRt.anchoredPosition = Vector2.zero;

            var pauseGroup = pauseMenuRoot.AddComponent<CanvasGroup>();

            // 1. 全屏半透明遮罩
            GameObject maskObj = new GameObject("BackgroundMask");
            maskObj.transform.SetParent(pauseMenuRoot.transform, false);
            var maskRt = maskObj.AddComponent<RectTransform>();
            maskRt.anchorMin = Vector2.zero;
            maskRt.anchorMax = Vector2.one;
            maskRt.sizeDelta = Vector2.zero;
            var maskImg = maskObj.AddComponent<Image>();
            maskImg.color = MaskBgColor;

            // 2. 悬浮主面板 MainPanel
            GameObject mainPanel = new GameObject("MainPanel");
            mainPanel.transform.SetParent(pauseMenuRoot.transform, false);
            var panelRt = mainPanel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(440f, 560f);
            panelRt.anchoredPosition = Vector2.zero;

            var panelImg = mainPanel.AddComponent<Image>();
            panelImg.color = CardBgColor;

            // 3. 主菜单内容容器 (打开系统设置时完全隐藏此类节点)
            GameObject pauseMenuContent = new GameObject("PauseMenuContent");
            pauseMenuContent.transform.SetParent(mainPanel.transform, false);
            var pmcRt = pauseMenuContent.AddComponent<RectTransform>();
            pmcRt.anchorMin = Vector2.zero;
            pmcRt.anchorMax = Vector2.one;
            pmcRt.sizeDelta = Vector2.zero;

            TextMeshProUGUI titleTmp = CreateText(pauseMenuContent.transform, "TitleText", "游戏暂停", 28, FontStyles.Bold, AccentGoldColor, new Vector2(0f, 220f), new Vector2(380f, 48f), TextAlignmentOptions.Center);

            Button resumeBtn = CreateButton(pauseMenuContent.transform, "ResumeButton", "继续游戏 (Resume)", new Vector2(320f, 50f), new Vector2(0f, 130f), PrimaryBtnColor, out var resumeTmp);
            Button restartBtn = CreateButton(pauseMenuContent.transform, "RestartButton", "重新开始 (Restart)", new Vector2(320f, 50f), new Vector2(0f, 65f), PrimaryBtnColor, out var restartTmp);
            Button mpBtn = CreateButton(pauseMenuContent.transform, "MultiplayerButton", "联机大厅 (Multiplayer)", new Vector2(320f, 50f), new Vector2(0f, 0f), PrimaryBtnColor, out var mpTmp);
            Button settingsBtn = CreateButton(pauseMenuContent.transform, "SettingsButton", "系统设置 (Settings)", new Vector2(320f, 50f), new Vector2(0f, -65f), PrimaryBtnColor, out var settingsTmp);
            Button quitBtn = CreateButton(pauseMenuContent.transform, "QuitButton", "退出游戏 (Quit)", new Vector2(320f, 50f), new Vector2(0f, -145f), new Color(0.4f, 0.15f, 0.15f, 1f), out var quitTmp);

            // 4. 设置子面板 SettingsPanel
            GameObject settingsPanel = new GameObject("SettingsPanel");
            settingsPanel.transform.SetParent(mainPanel.transform, false);
            var setRt = settingsPanel.AddComponent<RectTransform>();
            setRt.anchorMin = Vector2.zero;
            setRt.anchorMax = Vector2.one;
            setRt.sizeDelta = Vector2.zero;
            var setImg = settingsPanel.AddComponent<Image>();
            setImg.color = CardBgColor;

            TextMeshProUGUI setTitleTmp = CreateText(settingsPanel.transform, "SettingsTitle", "系统设置 (Settings)", 24, FontStyles.Bold, AccentGoldColor, new Vector2(0f, 210f), new Vector2(360f, 40f), TextAlignmentOptions.Center);
            TextMeshProUGUI langLblTmp = CreateText(settingsPanel.transform, "LangLabel", "语言选择 / Language", 18, FontStyles.Normal, TextWhiteColor, new Vector2(0f, 140f), new Vector2(320f, 30f), TextAlignmentOptions.Center);

            Button langCnBtn = CreateButton(settingsPanel.transform, "LangChineseBtn", "简体中文", new Vector2(100f, 42f), new Vector2(-110f, 85f), PrimaryBtnColor, out _);
            Button langEsBtn = CreateButton(settingsPanel.transform, "LangSpanishBtn", "Español", new Vector2(100f, 42f), new Vector2(0f, 85f), PrimaryBtnColor, out _);
            Button langEnBtn = CreateButton(settingsPanel.transform, "LangEnglishBtn", "English", new Vector2(100f, 42f), new Vector2(110f, 85f), PrimaryBtnColor, out _);

            Toggle perfToggle = CreateToggle(settingsPanel.transform, "PerfHudToggle", "显示性能监控 (FPS / Ping)", new Vector2(320f, 36f), new Vector2(0f, 15f), out var perfTglTmp);
            Button setBackBtn = CreateButton(settingsPanel.transform, "SettingsBackButton", "返回 (Back)", new Vector2(280f, 46f), new Vector2(0f, -180f), PrimaryBtnColor, out var setBackTmp);
            settingsPanel.SetActive(false);

            // 5. 初始化引用
            pauseUI.InitializeReferences(
                pauseMenuRoot,
                pauseGroup,
                panelRt,
                pauseMenuContent,
                resumeBtn,
                restartBtn,
                mpBtn,
                settingsBtn,
                quitBtn,
                titleTmp,
                resumeTmp,
                restartTmp,
                mpTmp,
                settingsTmp,
                quitTmp,
                settingsPanel,
                setTitleTmp,
                langLblTmp,
                setBackBtn,
                setBackTmp,
                langCnBtn,
                langEsBtn,
                langEnBtn,
                perfToggle,
                perfTglTmp,
                lobbyUI
            );

            return pauseUI;
        }

        #endregion

        #region 5. 构造 MultiplayerLobbyUI

        private static MultiplayerLobbyUI CreateMultiplayerLobby(Transform canvasTransform)
        {
            GameObject lobbyObj = new GameObject("MultiplayerLobby");
            lobbyObj.transform.SetParent(canvasTransform, false);

            var lobbyRt = lobbyObj.AddComponent<RectTransform>();
            lobbyRt.anchorMin = Vector2.zero;
            lobbyRt.anchorMax = Vector2.one;
            lobbyRt.sizeDelta = Vector2.zero;

            var lobbyGroup = lobbyObj.AddComponent<CanvasGroup>();
            var lobbyUI = lobbyObj.AddComponent<MultiplayerLobbyUI>();

            // 1. 半透明遮罩
            GameObject mask = new GameObject("BackgroundMask");
            mask.transform.SetParent(lobbyObj.transform, false);
            var mRt = mask.AddComponent<RectTransform>();
            mRt.anchorMin = Vector2.zero;
            mRt.anchorMax = Vector2.one;
            mRt.sizeDelta = Vector2.zero;
            mask.AddComponent<Image>().color = MaskBgColor;

            // 2. 主面板
            GameObject lobbyPanel = new GameObject("LobbyPanel");
            lobbyPanel.transform.SetParent(lobbyObj.transform, false);
            var panelRt = lobbyPanel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(500f, 600f);
            lobbyPanel.AddComponent<Image>().color = CardBgColor;

            TextMeshProUGUI titleTmp = CreateText(lobbyPanel.transform, "Title", "多人联机大厅 (TCP)", 26, FontStyles.Bold, AccentGoldColor, new Vector2(0f, 240f), new Vector2(420f, 40f), TextAlignmentOptions.Center);

            Button hostBtn = CreateButton(lobbyPanel.transform, "HostBtn", "创建房间 / 开服 (Host: 25565)", new Vector2(380f, 50f), new Vector2(0f, 155f), new Color(0.18f, 0.38f, 0.28f, 1f), out var hostTmp);

            // 客户端 IP 输入框 (宽 260) 与 端口输入框 (宽 110) 并排布局 (默认: 127.0.0.1 : 25566)
            TMP_InputField ipInput = CreateInputField(lobbyPanel.transform, "IPInput", "127.0.0.1", new Vector2(260f, 46f), new Vector2(-60f, 80f));
            TMP_InputField portInput = CreateInputField(lobbyPanel.transform, "PortInput", "25566", new Vector2(110f, 46f), new Vector2(135f, 80f));

            Button joinBtn = CreateButton(lobbyPanel.transform, "JoinBtn", "加入游戏 (连接 IP / 穿透通道)", new Vector2(380f, 50f), new Vector2(0f, 20f), PrimaryBtnColor, out var joinTmp);
            Button tunnelGuideBtn = CreateButton(lobbyPanel.transform, "TunnelGuideBtn", "联机穿透工具与使用教程", new Vector2(380f, 48f), new Vector2(0f, -50f), new Color(0.24f, 0.30f, 0.42f, 1f), out var tunnelGuideTmp);

            TextMeshProUGUI statusTmp = CreateText(lobbyPanel.transform, "StatusText", "准备就绪", 16, FontStyles.Normal, new Color(0.7f, 0.7f, 0.7f, 1f), new Vector2(0f, -120f), new Vector2(400f, 30f), TextAlignmentOptions.Center);
            Button backBtn = CreateButton(lobbyPanel.transform, "BackBtn", "返回 (Back)", new Vector2(300f, 46f), new Vector2(0f, -220f), PrimaryBtnColor, out var backTmp);

            // 3. 自动加入弹窗 AutoJoinModal
            GameObject autoJoinModal = new GameObject("AutoJoinModal");
            autoJoinModal.transform.SetParent(lobbyObj.transform, false);
            var ajRt = autoJoinModal.AddComponent<RectTransform>();
            ajRt.anchorMin = new Vector2(0.5f, 0.5f);
            ajRt.anchorMax = new Vector2(0.5f, 0.5f);
            ajRt.sizeDelta = new Vector2(420f, 260f);
            autoJoinModal.AddComponent<Image>().color = new Color(0.08f, 0.10f, 0.15f, 0.98f);
            var ajGroup = autoJoinModal.AddComponent<CanvasGroup>();

            TextMeshProUGUI ajTitle = CreateText(autoJoinModal.transform, "AJTitle", "检测到本地穿透客户端", 22, FontStyles.Bold, AccentGoldColor, new Vector2(0f, 75f), new Vector2(360f, 35f), TextAlignmentOptions.Center);
            TextMeshProUGUI ajDesc = CreateText(autoJoinModal.transform, "AJDesc", "检测到 127.0.0.1:25566 端口已开启！\n是否立即加入游戏？", 16, FontStyles.Normal, TextWhiteColor, new Vector2(0f, 15f), new Vector2(360f, 60f), TextAlignmentOptions.Center);
            Button ajConfirmBtn = CreateButton(autoJoinModal.transform, "AJConfirmBtn", "立即加入", new Vector2(150f, 44f), new Vector2(-90f, -65f), new Color(0.18f, 0.45f, 0.25f, 1f), out var ajConfirmTmp);
            Button ajIgnoreBtn = CreateButton(autoJoinModal.transform, "AJIgnoreBtn", "稍后", new Vector2(150f, 44f), new Vector2(90f, -65f), PrimaryBtnColor, out var ajIgnoreTmp);
            autoJoinModal.SetActive(false);

            // 4. 穿透教程与下载器弹窗 TunnelManagerModal
            TunnelManagerUI tunnelUI = CreateTunnelManagerModal(lobbyObj.transform);

            // 5. 初始化引用
            lobbyUI.InitializeReferences(
                lobbyGroup,
                panelRt,
                titleTmp,
                ipInput,
                portInput,
                hostBtn,
                hostTmp,
                joinBtn,
                joinTmp,
                tunnelGuideBtn,
                tunnelGuideTmp,
                backBtn,
                backTmp,
                statusTmp,
                autoJoinModal,
                ajGroup,
                ajRt,
                ajTitle,
                ajDesc,
                ajConfirmBtn,
                ajConfirmTmp,
                ajIgnoreBtn,
                ajIgnoreTmp,
                tunnelUI
            );

            lobbyObj.SetActive(false);
            return lobbyUI;
        }

        private static TunnelManagerUI CreateTunnelManagerModal(Transform lobbyTransform)
        {
            GameObject tunnelModal = new GameObject("TunnelManagerModal");
            tunnelModal.transform.SetParent(lobbyTransform, false);
            var tmRt = tunnelModal.AddComponent<RectTransform>();
            tmRt.anchorMin = new Vector2(0.5f, 0.5f);
            tmRt.anchorMax = new Vector2(0.5f, 0.5f);
            tmRt.sizeDelta = new Vector2(560f, 620f);
            tunnelModal.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.16f, 0.98f);
            var tmGroup = tunnelModal.AddComponent<CanvasGroup>();
            var tunnelUI = tunnelModal.AddComponent<TunnelManagerUI>();

            TextMeshProUGUI title = CreateText(tunnelModal.transform, "Title", "Cloudflared 联机穿透教程与下载", 22, FontStyles.Bold, AccentGoldColor, new Vector2(0f, 255f), new Vector2(500f, 40f), TextAlignmentOptions.Center);
            TextMeshProUGUI prompt = CreateText(tunnelModal.transform, "Prompt", "是否需要使用/下载联机辅助工具？", 16, FontStyles.Normal, TextWhiteColor, new Vector2(0f, 205f), new Vector2(480f, 30f), TextAlignmentOptions.Center);

            Button dlBtn = CreateButton(tunnelModal.transform, "DownloadPcBtn", "下载 PC 客户端 (自动拉起)", new Vector2(240f, 46f), new Vector2(-125f, 150f), new Color(0.2f, 0.4f, 0.6f, 1f), out _);
            Button webBtn = CreateButton(tunnelModal.transform, "OpenWebBtn", "使用网页版", new Vector2(200f, 46f), new Vector2(130f, 150f), PrimaryBtnColor, out _);

            TextMeshProUGUI step1 = CreateText(tunnelModal.transform, "Step1", "1. 首次使用：双方都必须去【杂项与设置】点击【安装 cloudflared】。", 14, FontStyles.Normal, TextWhiteColor, new Vector2(0f, 75f), new Vector2(480f, 50f), TextAlignmentOptions.TopLeft);
            TextMeshProUGUI step2 = CreateText(tunnelModal.transform, "Step2", "2. 开服方：点击【软件服务端】->【免配置临时通道】->【一键获取临时链接】-> 复制链接发给朋友。", 14, FontStyles.Normal, TextWhiteColor, new Vector2(0f, 5f), new Vector2(480f, 60f), TextAlignmentOptions.TopLeft);
            TextMeshProUGUI step3 = CreateText(tunnelModal.transform, "Step3", "3. 连接方：进入【软件客户端】-> 填写朋友给的临时链接 -> 点击【连接】。", 14, FontStyles.Normal, TextWhiteColor, new Vector2(0f, -65f), new Vector2(480f, 50f), TextAlignmentOptions.TopLeft);

            Slider progressSlider = CreateSlider(tunnelModal.transform, "DownloadSlider", new Vector2(460f, 16f), new Vector2(0f, -145f));
            TextMeshProUGUI status = CreateText(tunnelModal.transform, "Status", "准备下载...", 14, FontStyles.Normal, AccentGoldColor, new Vector2(0f, -180f), new Vector2(460f, 26f), TextAlignmentOptions.Center);
            Button closeBtn = CreateButton(tunnelModal.transform, "CloseBtn", "关闭 (Close)", new Vector2(220f, 44f), new Vector2(0f, -245f), PrimaryBtnColor, out _);

            tunnelUI.InitializeReferences(
                tmGroup,
                tmRt,
                title,
                prompt,
                step1,
                step2,
                step3,
                status,
                dlBtn,
                webBtn,
                closeBtn,
                progressSlider
            );

            tunnelModal.SetActive(false);
            return tunnelUI;
        }

        #endregion

        #region 6. 基础 UI 节点装配辅助

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float size, FontStyles style, Color color, Vector2 pos, Vector2 sizeDelta, TextAlignmentOptions alignment)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;

            var tmp = obj.AddComponent<TextMeshProUGUI>();
            var font = GetChineseFontAsset();
            if (font != null) tmp.font = font;

            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.text = content;
            return tmp;
        }

        private static Button CreateButton(Transform parent, string name, string labelText, Vector2 size, Vector2 pos, Color btnColor, out TextMeshProUGUI labelTmp)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var img = obj.AddComponent<Image>();
            img.color = btnColor;

            var btn = obj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = btnColor;
            colors.highlightedColor = btnColor * 1.25f;
            colors.pressedColor = btnColor * 0.85f;
            btn.colors = colors;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            var tRt = textObj.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.sizeDelta = Vector2.zero;

            labelTmp = textObj.AddComponent<TextMeshProUGUI>();
            var font = GetChineseFontAsset();
            if (font != null) labelTmp.font = font;

            labelTmp.fontSize = 18;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = TextWhiteColor;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.raycastTarget = false;
            labelTmp.text = labelText;

            return btn;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, string defaultText, Vector2 size, Vector2 pos)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var img = obj.AddComponent<Image>();
            img.color = InputBgColor;

            var input = obj.AddComponent<TMP_InputField>();

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            var tRt = textObj.AddComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero;
            tRt.anchorMax = Vector2.one;
            tRt.sizeDelta = new Vector2(-20f, 0f);

            var textTmp = textObj.AddComponent<TextMeshProUGUI>();
            var font = GetChineseFontAsset();
            if (font != null) textTmp.font = font;

            textTmp.fontSize = 18;
            textTmp.color = TextWhiteColor;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            input.textComponent = textTmp;
            input.text = defaultText;

            return input;
        }

        private static Toggle CreateToggle(Transform parent, string name, string label, Vector2 size, Vector2 pos, out TextMeshProUGUI labelTmp)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var toggle = obj.AddComponent<Toggle>();

            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(obj.transform, false);
            var bgRt = bgObj.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.5f);
            bgRt.anchorMax = new Vector2(0f, 0.5f);
            bgRt.pivot = new Vector2(0f, 0.5f);
            bgRt.sizeDelta = new Vector2(24f, 24f);
            bgRt.anchoredPosition = new Vector2(10f, 0f);
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = PrimaryBtnColor;

            GameObject checkObj = new GameObject("Checkmark");
            checkObj.transform.SetParent(bgObj.transform, false);
            var cRt = checkObj.AddComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.5f, 0.5f);
            cRt.anchorMax = new Vector2(0.5f, 0.5f);
            cRt.sizeDelta = new Vector2(16f, 16f);
            var checkImg = checkObj.AddComponent<Image>();
            checkImg.color = AccentGoldColor;

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(obj.transform, false);
            var lRt = labelObj.AddComponent<RectTransform>();
            lRt.anchorMin = Vector2.zero;
            lRt.anchorMax = Vector2.one;
            lRt.sizeDelta = new Vector2(-48f, 0f);
            lRt.anchoredPosition = new Vector2(24f, 0f);
            labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            var font = GetChineseFontAsset();
            if (font != null) labelTmp.font = font;

            labelTmp.fontSize = 16;
            labelTmp.color = TextWhiteColor;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;
            labelTmp.text = label;

            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            toggle.isOn = true;

            return toggle;
        }

        private static Slider CreateSlider(Transform parent, string name, Vector2 size, Vector2 pos)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var slider = obj.AddComponent<Slider>();
            slider.interactable = false;

            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(obj.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.12f, 0.9f);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(obj.transform, false);
            var faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero;
            faRt.anchorMax = Vector2.one;
            faRt.sizeDelta = new Vector2(-4f, -4f);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.sizeDelta = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = AccentGoldColor;

            slider.fillRect = fillRt;
            slider.targetGraphic = fillImg;
            slider.value = 0f;

            return slider;
        }

        #endregion
    }
}
