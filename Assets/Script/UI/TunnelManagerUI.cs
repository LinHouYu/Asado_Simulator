using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 联机穿透工具引导与教程界面 (TunnelManagerUI)
    /// 功能：
    /// 1. 弹出询问：“是否需要使用/下载联机工具？”
    /// 2. 提供【下载 PC 版客户端】与【使用网页版】两大入口
    /// 3. 详细硬核的图文/文字分步骤联机指南
    /// 4. 实时展示 GitHub 最新客户端下载进度条与状态
    /// </summary>
    public class TunnelManagerUI : MonoBehaviour
    {
        [Header("根节点与动画")]
        [SerializeField] private CanvasGroup mainCanvasGroup;
        [SerializeField] private RectTransform windowRect;

        [Header("UI 文本组件")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI promptText;
        [SerializeField] private TextMeshProUGUI guideStep1Text;
        [SerializeField] private TextMeshProUGUI guideStep2Text;
        [SerializeField] private TextMeshProUGUI guideStep3Text;
        [SerializeField] private TextMeshProUGUI downloadStatusText;

        [Header("交互按钮与进度条")]
        [SerializeField] private Button downloadPcButton;
        [SerializeField] private Button openWebButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Slider downloadProgressBar;

        private bool _listenersBound = false;

        public void InitializeReferences(
            CanvasGroup canvasGroup,
            RectTransform windowRt,
            TextMeshProUGUI titleTxt,
            TextMeshProUGUI promptTxt,
            TextMeshProUGUI step1Txt,
            TextMeshProUGUI step2Txt,
            TextMeshProUGUI step3Txt,
            TextMeshProUGUI statusTxt,
            Button dlBtn,
            Button webBtn,
            Button closeBtn,
            Slider slider)
        {
            mainCanvasGroup = canvasGroup;
            windowRect = windowRt;
            titleText = titleTxt;
            promptText = promptTxt;
            guideStep1Text = step1Txt;
            guideStep2Text = step2Txt;
            guideStep3Text = step3Txt;
            downloadStatusText = statusTxt;
            downloadPcButton = dlBtn;
            openWebButton = webBtn;
            closeButton = closeBtn;
            downloadProgressBar = slider;

            BindListeners();
        }

        private void BindListeners()
        {
            if (_listenersBound) return;
            _listenersBound = true;

            // 按钮事件绑定
            if (downloadPcButton != null) downloadPcButton.onClick.AddListener(OnDownloadPcClicked);
            if (openWebButton != null) openWebButton.onClick.AddListener(OnOpenWebClicked);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);

            // 隐藏进度条初始状态
            if (downloadProgressBar != null) downloadProgressBar.gameObject.SetActive(false);
            if (downloadStatusText != null) downloadStatusText.gameObject.SetActive(false);
        }

        private void Awake()
        {
            if (mainCanvasGroup == null) mainCanvasGroup = GetComponent<CanvasGroup>();
            if (windowRect == null) windowRect = GetComponent<RectTransform>();

            BindListeners();
        }

        private void Start()
        {
            LanguageManager.OnLanguageChanged += RefreshLocalizedTexts;
            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);

            // 监听下载器事件
            if (GitHubReleaseDownloader.Instance != null)
            {
                GitHubReleaseDownloader.Instance.OnDownloadProgress += HandleDownloadProgress;
                GitHubReleaseDownloader.Instance.OnStatusChanged += HandleStatusChanged;
                GitHubReleaseDownloader.Instance.OnDownloadFinished += HandleDownloadFinished;
                GitHubReleaseDownloader.Instance.OnDownloadFailed += HandleDownloadFailed;
            }
        }

        private void OnDestroy()
        {
            LanguageManager.OnLanguageChanged -= RefreshLocalizedTexts;

            if (GitHubReleaseDownloader.Instance != null)
            {
                GitHubReleaseDownloader.Instance.OnDownloadProgress -= HandleDownloadProgress;
                GitHubReleaseDownloader.Instance.OnStatusChanged -= HandleStatusChanged;
                GitHubReleaseDownloader.Instance.OnDownloadFinished -= HandleDownloadFinished;
                GitHubReleaseDownloader.Instance.OnDownloadFailed -= HandleDownloadFailed;
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);
            RefreshLocalizedTexts(LanguageManager.Instance != null ? LanguageManager.Instance.CurrentLanguage : GameLanguage.Chinese);
            StartCoroutine(ModernUIAnimationHelper.AnimatePopIn(windowRect, mainCanvasGroup, 0.28f));
        }

        public void Hide()
        {
            StartCoroutine(ModernUIAnimationHelper.AnimatePopOut(windowRect, mainCanvasGroup, 0.18f, () =>
            {
                gameObject.SetActive(false);
            }));
        }

        private void OnDownloadPcClicked()
        {
            if (GitHubReleaseDownloader.Instance == null)
            {
                GameObject obj = new GameObject("GitHubReleaseDownloader");
                obj.AddComponent<GitHubReleaseDownloader>();
            }

            if (downloadProgressBar != null)
            {
                downloadProgressBar.gameObject.SetActive(true);
                downloadProgressBar.value = 0f;
            }

            if (downloadStatusText != null)
            {
                downloadStatusText.gameObject.SetActive(true);
                downloadStatusText.text = "正在准备从 GitHub 下载最新版本...";
            }

            if (downloadPcButton != null) downloadPcButton.interactable = false;

            GitHubReleaseDownloader.Instance.StartDownloadAndRun();
        }

        private void OnOpenWebClicked()
        {
            Application.OpenURL("https://linhouyu.github.io/cloudflared-tunnel-gui/");
        }

        private void HandleDownloadProgress(float progress, string status)
        {
            if (downloadProgressBar != null)
            {
                downloadProgressBar.value = progress;
            }
            if (downloadStatusText != null)
            {
                downloadStatusText.text = status;
            }
        }

        private void HandleStatusChanged(string status)
        {
            if (downloadStatusText != null)
            {
                downloadStatusText.text = status;
            }
        }

        private void HandleDownloadFinished(string filePath)
        {
            if (downloadStatusText != null)
            {
                downloadStatusText.text = "✅ 客户端已启动！请在软件内连接或创建通道。";
            }
            if (downloadPcButton != null) downloadPcButton.interactable = true;
        }

        private void HandleDownloadFailed(string error)
        {
            if (downloadStatusText != null)
            {
                downloadStatusText.text = $"❌ {error}";
            }
            if (downloadPcButton != null) downloadPcButton.interactable = true;
        }

        private void RefreshLocalizedTexts(GameLanguage lang)
        {
            if (titleText != null) titleText.text = LanguageManager.T("tunnel_modal_title");
            if (promptText != null) promptText.text = LanguageManager.T("tunnel_modal_prompt");
            if (guideStep1Text != null) guideStep1Text.text = LanguageManager.T("tunnel_guide_step1");
            if (guideStep2Text != null) guideStep2Text.text = LanguageManager.T("tunnel_guide_step2");
            if (guideStep3Text != null) guideStep3Text.text = LanguageManager.T("tunnel_guide_step3");
        }
    }
}
