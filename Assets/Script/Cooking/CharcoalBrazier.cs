using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

namespace AsadoSimulator.Cooking
{
    /// <summary>
    /// 火盆核心脚本 (CharcoalBrazier)
    /// 功能：
    /// 1. 世界空间 Slider 与 Text 位置绝对固定（Position 绝对不随摄像机位移），仅允许 Rotation Y 轴控制水平转向。
    /// 2. 在点燃之前，同一个 UI 处持续显示当前木炭数量文本（如 "0/4", "1/4", "2/4", "3/4"）。
    /// 3. 当放满 4 个木炭后，进入 2 秒平稳落地倒计时（"4/4 准备燃烧..."），木炭自然落地后，再自动切换为 Slider 燃烧进度条。
    /// 4. 彻底解决只能烧一次的 Bug，燃烧完成后自动重置火盆状态与 "0/4" 计数，支持无限次连续放炭复用。
    /// 5. 自动校准模型几何中心，确保生成的 HotCharcoal 无论 FBX Pivot 偏离多远都精准居中在生成点。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CharcoalBrazier : MonoBehaviour
    {
        [Header("木炭检测配置")]
        [Tooltip("识别为木炭的目标 Tag（项目中通常为 'Carbon' 或 'Charcoal'）")]
        [SerializeField] private string[] charcoalTags = new string[] { "Carbon", "Charcoal" };

        [Tooltip("点燃所需的最少木炭数量（默认 4 个）")]
        [SerializeField] private int requiredCharcoalCount = 4;

        [Tooltip("放入所有木炭后，等待木炭自然掉落、平稳落地的时间（秒），防止木炭在半空中直接定格")]
        [SerializeField] private float settleDelay = 2.0f;

        [Tooltip("集齐所需木炭并平稳落地后，是否自动开始燃烧")]
        [SerializeField] private bool autoIgniteWhenFull = true;

        [Header("燃烧参数")]
        [Tooltip("燃烧/点燃达到高温所需时间（秒）")]
        [SerializeField] private float burnDuration = 5.0f;

        [Header("生成物配置")]
        [Tooltip("高温红色木炭的预制体 (Prefab)，要求带有 Tag: 'HotCharcoal'")]
        [SerializeField] private GameObject hotCharcoalPrefab;

        [Tooltip("生成高温木炭的目标 Transform 锚点（若为空则默认使用火盆自身坐标）")]
        [SerializeField] private Transform hotCharcoalSpawnPoint;

        [Tooltip("自动校准木炭模型几何/碰撞体中心到生成锚点（彻底解决因 Prefab 轴心偏左导致在侧面生成的 Bug）")]
        [SerializeField] private bool autoAlignCenterToSpawnPoint = true;

        [Tooltip("可选：自动为生成的高温木炭替换的发光材质（如 HotCharcoal_Glowing.mat）")]
        [SerializeField] private Material hotCharcoalMaterial;

        [Tooltip("燃烧完毕后，是将收集到的木炭隐藏 (false) 还是彻底销毁 (true)")]
        [SerializeField] private bool destroyOriginalCharcoals = false;

        [Header("UI 计数与进度配置")]
        [Tooltip("承载 UI 的 World Space Canvas（Transform Position 严格锁死，仅旋转 Y 轴）")]
        [SerializeField] private Canvas sliderCanvas;

        [Tooltip("展示燃烧进度的 Slider（燃烧中显示，未燃烧时隐藏）")]
        [SerializeField] private Slider burnProgressSlider;

        [Tooltip("可选：TextMeshPro 文本组件（未燃烧前显示 0/4 等数量提示）")]
        [SerializeField] private TMP_Text countTextTMP;

        [Tooltip("可选：传统 UGUI Text 文本组件（与 countTextTMP 二选一即可）")]
        [SerializeField] private Text countTextUGUI;

        [Tooltip("点燃前是否始终在 Slider 同一位置显示木炭数量（如 0/4）")]
        [SerializeField] private bool showCountTextBeforeBurning = true;

        [Tooltip("UI 是否保持水平朝向主摄像机（严格只旋转 Y 轴，俯仰角固定为 0）")]
        [SerializeField] private bool billboardYOnly = true;

        [Tooltip("可选：强制锁定 UI 在世界空间中的固定 Transform 锚点（若为空，则在启动时以当前 Canvas 世界坐标为准锁死）")]
        [SerializeField] private Transform fixedUiPositionAnchor;

        [Tooltip("当指定了 fixedUiPositionAnchor 时，是否自动将 Slider 和 Text 的内部偏移归零 (0, 0, 0)，让它们精准重合在锚点正中心，彻底消除双重叠加偏移与公转")]
        [SerializeField] private bool autoZeroChildOffsetsOnAnchor = true;

        [Tooltip("在锚点基础上的可选额外微调偏移 (例如 Y 轴微调 +0.1)")]
        [SerializeField] private Vector3 uiPositionOffset = Vector3.zero;

        [Header("事件回调 (可选)")]
        [Tooltip("当收集到新木炭时触发 (传递当前数量)")]
        public UnityEvent<int> onCharcoalAdded;

        [Tooltip("木炭被拿出火盆时触发 (传递剩余数量)")]
        public UnityEvent<int> onCharcoalRemoved;

        [Tooltip("木炭集齐，开始落地平稳倒计时时触发")]
        public UnityEvent onSettlingStarted;

        [Tooltip("开始燃烧时触发（可绑定点火火焰粒子、音效）")]
        public UnityEvent onBurnStarted;

        [Tooltip("燃烧进度更新时触发 (0.0 ~ 1.0)")]
        public UnityEvent<float> onBurnProgressUpdated;

        [Tooltip("燃烧完成并生成高温炭时触发（可绑定高温木炭火焰粒子、音效）")]
        public UnityEvent onBurnCompleted;

        // 内部状态跟踪
        private readonly List<GameObject> _collectedCharcoals = new List<GameObject>();
        private Camera _mainCamera;
        private Vector3 _lockedWorldPosition;
        private bool _hasLockedPosition = false;

        private bool _isSettling = false;
        private float _settleTimer = 0f;
        private bool _isBurning = false;
        private float _burnTimer = 0f;

        // 公开只读属性
        public int CurrentCharcoalCount => _collectedCharcoals.Count;
        public int RequiredCharcoalCount => requiredCharcoalCount;
        public bool IsSettling => _isSettling;
        public bool IsBurning => _isBurning;
        public float BurnProgress => Mathf.Clamp01(_burnTimer / Mathf.Max(0.01f, burnDuration));

        private void Awake()
        {
            // 确保自身 BoxCollider 必须为触发器
            var boxCol = GetComponent<BoxCollider>();
            if (boxCol != null && !boxCol.isTrigger)
            {
                boxCol.isTrigger = true;
            }

            FindMainCamera();
            LockInitialUIPosition();
            InitializeUI();
        }

        private void Start()
        {
            FindMainCamera();
            if (!_hasLockedPosition)
            {
                LockInitialUIPosition();
            }
        }

        /// <summary>
        /// 锁定 UI 的绝对世界坐标，并居中 Canvas Pivot 与子组件，彻底消除叠加位移与公转
        /// </summary>
        private void LockInitialUIPosition()
        {
            if (sliderCanvas != null)
            {
                var rect = sliderCanvas.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.pivot = new Vector2(0.5f, 0.5f);
                }

                if (fixedUiPositionAnchor != null)
                {
                    _lockedWorldPosition = fixedUiPositionAnchor.position + uiPositionOffset;
                    sliderCanvas.transform.position = _lockedWorldPosition;
                }
                else
                {
                    _lockedWorldPosition = sliderCanvas.transform.position;
                }

                // 无论是否指定锚点，只要处于 World Space Canvas 下，都自动将子组件 (Slider 和 Text) 归零到 (0, 0, 0)
                // 彻底消除手动拖动导致的局部坐标双重叠加与摄像机朝向旋转时的公转漂移！
                if (autoZeroChildOffsetsOnAnchor)
                {
                    ZeroChildUI(burnProgressSlider != null ? burnProgressSlider.GetComponent<RectTransform>() : null);
                    ZeroChildUI(countTextTMP != null ? countTextTMP.rectTransform : null);
                    ZeroChildUI(countTextUGUI != null ? countTextUGUI.rectTransform : null);
                }

                _hasLockedPosition = true;
            }
        }

        private void ZeroChildUI(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchoredPosition3D = Vector3.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.localPosition = Vector3.zero;
            rt.localRotation = Quaternion.identity;
        }

        private void LateUpdate()
        {
            if (sliderCanvas != null && _hasLockedPosition)
            {
                // 1. 强制锁死 Transform Position，绝对不随摄像机移动发生任何平移！
                sliderCanvas.transform.position = _lockedWorldPosition;

                // 2. 严格仅控制 Y 轴旋转（Yaw），俯仰角 X 和翻滚角 Z 固定为 0
                if (billboardYOnly && _mainCamera != null)
                {
                    float cameraYaw = _mainCamera.transform.eulerAngles.y;
                    sliderCanvas.transform.rotation = Quaternion.Euler(0f, cameraYaw, 0f);

                    // 再次强制锁定坐标，消除任何由于旋转产生的微弱轴心漂移
                    sliderCanvas.transform.position = _lockedWorldPosition;
                }
            }
        }

        private float _networkBrazierSyncTimer = 0f;

        /// <summary>
        /// 网络同步火盆状态 (木炭数、平稳状态、燃烧状态、燃烧进度)
        /// </summary>
        public void NetworkUpdateBrazier(int count, bool isSettling, bool isBurning, float progress)
        {
            if (isBurning)
            {
                _isBurning = true;
                _isSettling = false;
                _burnTimer = progress * burnDuration;
                SetTextVisible(false);
                if (burnProgressSlider != null)
                {
                    burnProgressSlider.gameObject.SetActive(true);
                    burnProgressSlider.value = progress;
                }
            }
            else
            {
                _isBurning = false;
                _isSettling = isSettling;
                if (burnProgressSlider != null) burnProgressSlider.gameObject.SetActive(false);
                SetTextVisible(true);
                if (countTextTMP != null)
                {
                    countTextTMP.text = $"{count}/{requiredCharcoalCount}";
                }
            }
        }

        private void Update()
        {
            // 1. 等待木炭平稳落地倒计时
            if (_isSettling && !_isBurning)
            {
                _settleTimer -= Time.deltaTime;
                UpdateCountTextUI();

                if (_settleTimer <= 0f)
                {
                    _isSettling = false;
                    if (autoIgniteWhenFull && _collectedCharcoals.Count >= requiredCharcoalCount)
                    {
                        StartBurning();
                    }
                }
            }

            // 2. 燃烧进度更新
            if (_isBurning)
            {
                UpdateBurningProgress();
            }

            // 3. 联机模式下由房主周期性广播火盆状态
            var net = Multiplayer.AsadoNetworkManager.Instance;
            if (net != null && net.IsNetworkActive && net.IsHost)
            {
                _networkBrazierSyncTimer += Time.deltaTime;
                if (_networkBrazierSyncTimer >= 0.25f)
                {
                    _networkBrazierSyncTimer = 0f;
                    net.BroadcastBrazierState(_collectedCharcoals.Count, _isSettling, _isBurning, BurnProgress);
                }
            }
        }

        private void FindMainCamera()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null)
                {
                    _mainCamera = FindAnyObjectByType<Camera>();
                }
            }
        }

        private static TMP_FontAsset _chineseTmpFontFallback;

        private void InitializeUI()
        {
            // Canvas 整体常开（用于显示 0/4 文本）
            if (sliderCanvas != null)
            {
                sliderCanvas.gameObject.SetActive(true);
            }

            // 改造为【烤肉同款进度条】样式，并优化中文字体与尺寸
            ApplyMeatStyleToSlider();
            SetupChineseFontAndSizeSupport();

            // 未燃烧时：隐藏 Slider，展示文本
            if (burnProgressSlider != null)
            {
                burnProgressSlider.minValue = 0f;
                burnProgressSlider.maxValue = 1f;
                burnProgressSlider.value = 0f;
                burnProgressSlider.gameObject.SetActive(false);
            }

            SetTextVisible(showCountTextBeforeBurning);
            UpdateCountTextUI();
        }

        /// <summary>
        /// 将火盆燃烧进度条改造为【烤肉同款进度条】：
        /// - 相同的暗黑半透明底色 (0.12, 0.12, 0.12, 0.85)
        /// - 相同的温润金黄色填充 (1.0, 0.72, 0.18, 1.0)
        /// - 相同的 -4 像素边距缩进
        /// - 隐藏 Handle 滑块手柄
        /// </summary>
        private void ApplyMeatStyleToSlider()
        {
            if (burnProgressSlider == null) return;

            burnProgressSlider.interactable = false;
            burnProgressSlider.transition = Selectable.Transition.None;

            // 1. 背景底色：与烤肉进度条完全一致的暗黑半透明底色
            var bg = burnProgressSlider.transform.Find("Background");
            if (bg != null && bg.TryGetComponent<Image>(out var bgImg))
            {
                bgImg.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
            }

            // 2. 填充区域：四周 -4 像素边距缩进
            var fillArea = burnProgressSlider.transform.Find("Fill Area");
            if (fillArea != null && fillArea.TryGetComponent<RectTransform>(out var faRt))
            {
                faRt.anchorMin = Vector2.zero;
                faRt.anchorMax = Vector2.one;
                faRt.sizeDelta = new Vector2(-4f, -4f);
                faRt.anchoredPosition = Vector2.zero;
            }

            // 3. 填充物颜色：与烤肉进度条完全一致的温润金黄色
            if (burnProgressSlider.fillRect != null && burnProgressSlider.fillRect.TryGetComponent<Image>(out var fillImg))
            {
                fillImg.color = new Color(1.0f, 0.72f, 0.18f, 1.0f);
                burnProgressSlider.targetGraphic = fillImg;
            }

            // 4. 隐藏不需要的 Handle 滑块手柄
            var handleArea = burnProgressSlider.transform.Find("Handle Slide Area");
            if (handleArea != null)
            {
                handleArea.gameObject.SetActive(false);
            }
        }

        private void SetupChineseFontAndSizeSupport()
        {
            if (countTextTMP == null) return;

            // 1. 将字母/字号设置得稍微更小一点点（从原 0.2f 调整至 0.14f，更精致协调）
            countTextTMP.fontSize = 0.14f;

            // 2. 使用全局经过安全校验的中文字体资产作为 TMP Fallback
            _chineseTmpFontFallback = AsadoSimulator.UI.ModernUIBuilder.GetChineseFontAsset();

            if (_chineseTmpFontFallback != null && AsadoSimulator.UI.ModernUIBuilder.IsFontAssetValid(_chineseTmpFontFallback))
            {
                if (countTextTMP.font != null)
                {
                    if (countTextTMP.font.fallbackFontAssetTable == null)
                    {
                        countTextTMP.font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    }
                    countTextTMP.font.fallbackFontAssetTable.RemoveAll(item => item == null || !AsadoSimulator.UI.ModernUIBuilder.IsFontAssetValid(item));
                    if (!countTextTMP.font.fallbackFontAssetTable.Contains(_chineseTmpFontFallback))
                    {
                        countTextTMP.font.fallbackFontAssetTable.Add(_chineseTmpFontFallback);
                    }
                }
                else
                {
                    countTextTMP.font = _chineseTmpFontFallback;
                }
            }
        }

        private void SetTextVisible(bool visible)
        {
            if (countTextTMP != null) countTextTMP.gameObject.SetActive(visible);
            if (countTextUGUI != null) countTextUGUI.gameObject.SetActive(visible);
        }

        private void UpdateCountTextUI()
        {
            if (!showCountTextBeforeBurning) return;

            int meatCoalCount = 0;
            for (int i = 0; i < _collectedCharcoals.Count; i++)
            {
                if (_collectedCharcoals[i] != null && (_collectedCharcoals[i].GetComponent<Meat>() != null || _collectedCharcoals[i].GetComponentInParent<Meat>() != null))
                {
                    meatCoalCount++;
                }
            }

            string bonusHint = meatCoalCount > 0 ? $" (含{meatCoalCount}块纯肉炭)" : "";
            string content;
            if (_isSettling)
            {
                content = $"{_collectedCharcoals.Count}/{requiredCharcoalCount} (准备点燃...{bonusHint})";
            }
            else
            {
                content = $"{_collectedCharcoals.Count}/{requiredCharcoalCount}{bonusHint}";
            }

            if (countTextTMP != null) countTextTMP.text = content;
            if (countTextUGUI != null) countTextUGUI.text = content;
        }

        #region 触发器检测 (Enter & Exit)

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            if (_isBurning) return; // 燃烧期间不接收新木炭

            if (!IsCharcoal(other.gameObject, out GameObject rootCharcoal)) return;

            // 避免重复收集同一个木炭实例
            if (_collectedCharcoals.Contains(rootCharcoal)) return;

            _collectedCharcoals.Add(rootCharcoal);
            UpdateCountTextUI();
            onCharcoalAdded?.Invoke(_collectedCharcoals.Count);

            // 当数量达到要求时，启动落地等待倒计时（不要立刻冻结，让木炭掉到底部！）
            if (_collectedCharcoals.Count >= requiredCharcoalCount)
            {
                if (!_isSettling)
                {
                    _isSettling = true;
                    _settleTimer = Mathf.Max(0.1f, settleDelay);
                    UpdateCountTextUI();
                    onSettlingStarted?.Invoke();
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;
            if (_isBurning) return;

            if (!IsCharcoal(other.gameObject, out GameObject rootCharcoal)) return;

            // 如果木炭弹跳出去或被玩家拿走
            if (_collectedCharcoals.Remove(rootCharcoal))
            {
                UpdateCountTextUI();
                onCharcoalRemoved?.Invoke(_collectedCharcoals.Count);

                // 如果数量不足，打断落地倒计时
                if (_collectedCharcoals.Count < requiredCharcoalCount)
                {
                    _isSettling = false;
                    _settleTimer = 0f;
                    UpdateCountTextUI();
                }
            }
        }

        private bool IsCharcoal(GameObject obj, out GameObject rootCharcoal)
        {
            rootCharcoal = obj;

            // 1. 戏剧性搞笑功能：检测烤糊炭化的肉块（肉炭也是合格的煤炭！）
            if (obj.TryGetComponent<Meat>(out var meat) || (meat = obj.GetComponentInParent<Meat>()) != null)
            {
                if (meat.IsBurntToCharcoal)
                {
                    rootCharcoal = meat.gameObject;
                    return true;
                }
            }

            // 2. 正常 Tag 匹配 (Carbon, Charcoal)
            if (MatchesCharcoalTag(obj))
            {
                rootCharcoal = obj;
                return true;
            }

            var parent = obj.transform.parent;
            while (parent != null)
            {
                if (MatchesCharcoalTag(parent.gameObject))
                {
                    rootCharcoal = parent.gameObject;
                    return true;
                }
                parent = parent.parent;
            }

            return false;
        }

        private bool MatchesCharcoalTag(GameObject obj)
        {
            if (charcoalTags == null || charcoalTags.Length == 0) return false;

            for (int i = 0; i < charcoalTags.Length; i++)
            {
                if (!string.IsNullOrEmpty(charcoalTags[i]) && obj.CompareTag(charcoalTags[i]))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region 燃烧逻辑 (可循环复用)

        /// <summary>
        /// 开始燃烧
        /// </summary>
        public bool StartBurning()
        {
            if (_isBurning) return false;
            if (_collectedCharcoals.Count < requiredCharcoalCount) return false;

            _isSettling = false;
            _isBurning = true;
            _burnTimer = 0f;

            // 切换 UI：隐藏 4/4 文本，显现燃烧进度条 Slider
            SetTextVisible(false);

            if (burnProgressSlider != null)
            {
                ApplyMeatStyleToSlider();
                burnProgressSlider.gameObject.SetActive(true);
                burnProgressSlider.value = 0f;
            }

            // 木炭已经平稳落地，现在固定刚体（Kinematic），防止燃烧过程中被踢飞
            for (int i = 0; i < _collectedCharcoals.Count; i++)
            {
                var item = _collectedCharcoals[i];
                if (item != null)
                {
                    var rb = item.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = true;
                    }
                }
            }

            onBurnStarted?.Invoke();
            return true;
        }

        private void UpdateBurningProgress()
        {
            _burnTimer += Time.deltaTime;
            float progress = BurnProgress;

            if (burnProgressSlider != null)
            {
                burnProgressSlider.value = progress;
            }

            onBurnProgressUpdated?.Invoke(progress);

            if (_burnTimer >= burnDuration)
            {
                CompleteBurning();
            }
        }

        /// <summary>
        /// 燃烧完成并自动重置状态，支持下一轮无限复用
        /// </summary>
        private void CompleteBurning()
        {
            _isBurning = false;
            _isSettling = false;
            _burnTimer = 0f;

            // 1. 隐藏/销毁原来的普通木炭模型
            for (int i = 0; i < _collectedCharcoals.Count; i++)
            {
                var charcoal = _collectedCharcoals[i];
                if (charcoal != null)
                {
                    if (destroyOriginalCharcoals || charcoal.GetComponent<Meat>() != null || charcoal.GetComponentInParent<Meat>() != null)
                    {
                        Destroy(charcoal);
                    }
                    else
                    {
                        charcoal.SetActive(false);
                    }
                }
            }
            _collectedCharcoals.Clear();

            // 2. 在指定锚点生成红色高温木炭
            SpawnHotCharcoal();

            onBurnCompleted?.Invoke();

            // 3. 彻底重置火盆 UI 与状态，允许下一轮重新投入木炭继续烧！
            InitializeUI();
        }

        private void SpawnHotCharcoal()
        {
            if (hotCharcoalPrefab == null)
            {
                Debug.LogWarning($"[CharcoalBrazier] '{name}' 未指定 'hotCharcoalPrefab'，无法生成高温木炭！", this);
                return;
            }

            Vector3 spawnPos = hotCharcoalSpawnPoint != null ? hotCharcoalSpawnPoint.position : transform.position;
            Quaternion spawnRot = hotCharcoalSpawnPoint != null ? hotCharcoalSpawnPoint.rotation : transform.rotation;

            // 实例化高温木炭
            GameObject hotCharcoal = Instantiate(hotCharcoalPrefab, spawnPos, spawnRot);

            // 自动校正模型中心偏移（彻底解决因 FBX/Prefab 内部 Pivot 偏离导致在左侧生成的 Bug）
            if (autoAlignCenterToSpawnPoint)
            {
                Bounds combinedBounds = CalculateCombinedBounds(hotCharcoal);
                if (combinedBounds.size.sqrMagnitude > 0.0001f)
                {
                    Vector3 centerOffset = combinedBounds.center - hotCharcoal.transform.position;
                    // 将物体根节点反向移动 centerOffset，确保物体的实际几何中心精准落在 spawnPos
                    hotCharcoal.transform.position = spawnPos - centerOffset;
                }
            }

            // 如果指定了发光材质，自动为高温木炭的所有 Renderer 替换发光材质
            if (hotCharcoalMaterial != null)
            {
                var renderers = hotCharcoal.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        renderers[i].material = hotCharcoalMaterial;
                    }
                }
            }

            // 确保生成的高温炭具备正确的 Tag "HotCharcoal"
            if (!hotCharcoal.CompareTag("HotCharcoal"))
            {
                try
                {
                    hotCharcoal.tag = "HotCharcoal";
                }
                catch
                {
                    Debug.LogWarning("[CharcoalBrazier] 警告：请在 Project Settings -> Tags & Layers 中添加 'HotCharcoal' 标签！");
                }
            }
        }

        /// <summary>
        /// 计算物体的实际几何包围盒中心（结合所有 Collider 与 Renderer）
        /// </summary>
        private Bounds CalculateCombinedBounds(GameObject obj)
        {
            Bounds bounds = new Bounds();
            bool hasBounds = false;

            // 优先查询 Collider
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col != null && col.enabled && !col.isTrigger)
                {
                    if (!hasBounds)
                    {
                        bounds = col.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(col.bounds);
                    }
                }
            }

            // 若无非触发器 Collider，则查询 Renderer
            if (!hasBounds)
            {
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r != null && r.enabled)
                    {
                        if (!hasBounds)
                        {
                            bounds = r.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(r.bounds);
                        }
                    }
                }
            }

            return bounds;
        }

        #endregion

        #region 调试与重置接口

        [ContextMenu("Debug: 打印 Slider 坐标")]
        private void DebugPrintSliderPosition()
        {
            if (sliderCanvas != null)
            {
                Debug.Log($"[CharcoalBrazier] Slider 世界坐标: {sliderCanvas.transform.position}, 锁定坐标: {_lockedWorldPosition}");
            }
            else
            {
                Debug.LogWarning("[CharcoalBrazier] sliderCanvas 未赋值！");
            }
        }

        /// <summary>
        /// 外部或调试时重置火盆状态
        /// </summary>
        public void ResetBrazier()
        {
            _isSettling = false;
            _settleTimer = 0f;
            _isBurning = false;
            _burnTimer = 0f;
            _collectedCharcoals.Clear();

            InitializeUI();
        }

        #endregion

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // 在 Scene 视口中高亮 Slider 标记，方便直观查看 Slider 是否在理想位置
            if (sliderCanvas != null)
            {
                Vector3 targetPos = _hasLockedPosition ? _lockedWorldPosition : sliderCanvas.transform.position;
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(targetPos, 0.08f);
                Gizmos.DrawLine(transform.position, targetPos);
            }
        }

        private void OnDrawGizmosSelected()
        {
            // 在 Scene 视口中可视化生成点
            if (hotCharcoalSpawnPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(hotCharcoalSpawnPoint.position, 0.12f);
                Gizmos.DrawRay(hotCharcoalSpawnPoint.position, hotCharcoalSpawnPoint.forward * 0.3f);
            }
        }
        #endif
    }
}
