using UnityEngine;
using TMPro;
using AsadoSimulator.Multiplayer;

namespace AsadoSimulator.UI
{
    /// <summary>
    /// 性能监控 HUD 悬浮窗 (PerformanceHUD)
    /// 功能：
    /// 1. 平滑计算实时 FPS 帧率
    /// 2. 实时监测 Mirror TCP 往返网络延迟 (Ping / RTT)，支持红黄绿三色状态预警
    /// 3. 支持在设置菜单中自由开启/关闭，偏好自动持久化保存 (PlayerPrefs)
    /// </summary>
    public class PerformanceHUD : MonoBehaviour
    {
        public static PerformanceHUD Instance { get; private set; }

        private const string PrefKey = "AsadoSim_ShowPerfHUD";

        [Header("UI 组件")]
        [SerializeField] private TextMeshProUGUI hudText;
        [SerializeField] private CanvasGroup hudCanvasGroup;

        [Header("刷新配置")]
        [Tooltip("HUD 数值刷新频率（秒）")]
        [SerializeField] private float updateInterval = 0.25f;

        private float _accumulatedDeltaTime = 0f;
        private int _accumulatedFrames = 0;
        private float _timeUntilNextUpdate = 0f;
        private float _currentFps = 60f;
        private bool _isVisible = true;

        public bool IsVisible => _isVisible;

        public void InitializeReferences(TextMeshProUGUI text, CanvasGroup canvasGroup)
        {
            hudText = text;
            hudCanvasGroup = canvasGroup;
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

            if (hudCanvasGroup == null) hudCanvasGroup = GetComponent<CanvasGroup>();
            _isVisible = PlayerPrefs.GetInt(PrefKey, 1) == 1;
            SetVisible(_isVisible);
        }

        private void Start()
        {
            LanguageManager.OnLanguageChanged += HandleLanguageChanged;
        }

        private void OnDestroy()
        {
            LanguageManager.OnLanguageChanged -= HandleLanguageChanged;
        }

        private void HandleLanguageChanged(GameLanguage lang)
        {
            UpdateDisplay();
        }

        private void Update()
        {
            if (!_isVisible) return;

            // 统计平滑帧率
            _accumulatedDeltaTime += Time.unscaledDeltaTime;
            _accumulatedFrames++;
            _timeUntilNextUpdate -= Time.unscaledDeltaTime;

            if (_timeUntilNextUpdate <= 0f)
            {
                _currentFps = _accumulatedDeltaTime > 0f ? (_accumulatedFrames / _accumulatedDeltaTime) : 60f;
                _timeUntilNextUpdate = updateInterval;
                _accumulatedDeltaTime = 0f;
                _accumulatedFrames = 0;

                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            if (hudText == null) return;

            // 1. FPS 格式化与颜色 (>=55 绿色, 30-55 黄色, <30 红色)
            string fpsColor = _currentFps >= 55f ? "#2ECC71" : (_currentFps >= 30f ? "#F1C40F" : "#E74C3C");
            string fpsStr = $"<color={fpsColor}>FPS: {Mathf.RoundToInt(_currentFps)}</color>";

            // 2. 实时监测网络延迟 (联机显示实时 RTT，单机/未连接显示本地 0ms 延迟)
            string pingStr;
            if (AsadoNetworkManager.Instance != null && AsadoNetworkManager.Instance.IsNetworkActive)
            {
                float pingMs = AsadoNetworkManager.Instance.RoundTripTimeMs;
                string pingColor = pingMs < 60f ? "#2ECC71" : (pingMs < 150f ? "#F1C40F" : "#E74C3C");
                pingStr = $" | <color={pingColor}>Ping: {Mathf.RoundToInt(pingMs)}ms</color>";
            }
            else
            {
                string localTag = LanguageManager.T("hud_local");
                pingStr = $" | <color=#2ECC71>Ping: 0ms ({localTag})</color>";
            }

            hudText.text = $"{fpsStr}{pingStr}";
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            PlayerPrefs.SetInt(PrefKey, visible ? 1 : 0);
            PlayerPrefs.Save();

            if (hudCanvasGroup != null)
            {
                hudCanvasGroup.alpha = visible ? 1f : 0f;
                hudCanvasGroup.blocksRaycasts = false;
            }
        }

        public void ToggleVisibility()
        {
            SetVisible(!_isVisible);
        }
    }
}
