using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace AsadoSimulator.Cooking
{
    /// <summary>
    /// 烤肉熟度状态枚举
    /// </summary>
    public enum MeatCookState
    {
        Raw,        // 生肉 (0.0 ~ 0.4)
        Cooked,     // 完美烤熟 (0.4 ~ 0.6，其中 0.5 为黄金熟度)
        Overcooked, // 偏老/轻微焦化 (0.6 ~ 0.85)
        Burnt       // 完全烧焦 (0.85 ~ 1.0)
    }

    /// <summary>
    /// 烤肉核心脚本 (Meat)
    /// 功能：
    /// 1. 拥有自己的世界空间 Canvas Slider UI，显示熟度进度 (0.0 到 1.0)，始终面向摄像机。
    /// 2. 只有当所在的 Grill 处于加热状态 (isHeating == true) 时，cookProgress 才会持续增加。
    /// 3. 熟度逻辑：0.0~0.5 为生肉到熟肉（0.5 视为完美烤熟），0.5~1.0 为逐渐烧焦（1.0 为完全烧焦）。
    /// 4. 动态外观变色 (UpdateMeatAppearance)：通过 Color.Lerp 实现 生肉红 -> 熟肉棕褐 -> 烧焦黑 的平滑渐变，并支持 Shader Float 参数联动。
    /// </summary>
    [DisallowMultipleComponent]
    public class Meat : MonoBehaviour
    {
        [Header("烤制参数")]
        [Tooltip("当前熟度进度 (0.0: 全生, 0.5: 完美烤熟, 1.0: 焦炭)")]
        [Range(0f, 1f)]
        [SerializeField] private float cookProgress = 0f;

        [Tooltip("基础烤制速度（每秒增加的熟度，0.05 约等于 10 秒烤熟，20 秒焦糊）")]
        [SerializeField] private float cookSpeed = 0.05f;

        [Header("外观渲染与颜色配置")]
        [Tooltip("肉块的主渲染器 (MeshRenderer)。若为空，将在 Awake 中自动获取自身或子级的 Renderer")]
        [SerializeField] private Renderer meatRenderer;

        [Tooltip("生肉阶段基础颜色 (Raw - 0.0)")]
        [SerializeField] private Color rawColor = new Color(0.85f, 0.22f, 0.22f, 1.0f);

        [Tooltip("完美烤熟基础颜色 (Cooked - 0.5)")]
        [SerializeField] private Color cookedColor = new Color(0.55f, 0.30f, 0.10f, 1.0f);

        [Tooltip("完全烧焦基础颜色 (Burnt - 1.0)")]
        [SerializeField] private Color burntColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);

        [Tooltip("是否使用 MaterialPropertyBlock（推荐开启，性能高且不会在内存中实例化重复材质）")]
        [SerializeField] private bool usePropertyBlock = true;

        [Header("UI 显示配置")]
        [Tooltip("显示熟度进度的 Slider")]
        [SerializeField] private Slider cookSlider;

        [Tooltip("挂载 Slider 的 World Space Canvas（用于面向摄像机与显示控制）")]
        [SerializeField] private Canvas sliderCanvas;

        [Tooltip("是否仅在受热烹饪或有进度时显示 UI（未烤制时隐藏，视觉更清爽）")]
        [SerializeField] private bool autoHideUI = true;

        [Header("事件回调 (可选)")]
        [Tooltip("熟度进度更新时触发 (传递 0.0 ~ 1.0)")]
        public UnityEvent<float> onCookProgressChanged;

        [Tooltip("达到完美烤熟时触发 (约 0.5 时)")]
        public UnityEvent onPerfectCooked;

        [Tooltip("完全烧焦时触发 (1.0 时)")]
        public UnityEvent onBurnt;

        // 当前烤肉所在的烤架引用（由 Grill 脚本在肉放入时注入）
        private Grill _currentGrill;
        private Camera _mainCamera;
        private MaterialPropertyBlock _propBlock;
        private bool _perfectCookedFired = false;
        private bool _burntFired = false;

        // Shader 属性 ID 缓存（同时兼容 URP 和标准管线）
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit / Unlit
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // Standard / Legacy
        private static readonly int CookProgressId = Shader.PropertyToID("_CookProgress");
        private static readonly int BurnAmountId = Shader.PropertyToID("_BurnAmount");

        // 公开属性
        public float CookProgress => cookProgress;
        public bool IsOnHeatingGrill => _currentGrill != null && _currentGrill.IsHeating;

        public MeatCookState CurrentCookState
        {
            get
            {
                if (cookProgress < 0.40f) return MeatCookState.Raw;
                if (cookProgress <= 0.65f) return MeatCookState.Cooked;
                if (cookProgress < 0.90f) return MeatCookState.Overcooked;
                return MeatCookState.Burnt;
            }
        }

        public bool IsPerfectCooked => Mathf.Abs(cookProgress - 0.5f) <= 0.1f;

        private void Awake()
        {
            if (meatRenderer == null)
            {
                meatRenderer = GetComponentInChildren<Renderer>();
            }

            if (usePropertyBlock)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            FindMainCamera();
            InitializeUI();
            UpdateMeatAppearance(cookProgress);
        }

        private void Start()
        {
            FindMainCamera();
        }

        private void LateUpdate()
        {
            // UI 始终面向摄像机 (Billboard)
            if (sliderCanvas != null && sliderCanvas.gameObject.activeSelf)
            {
                UpdateUIBillboard();
            }
        }

        private void Update()
        {
            // 只有当所在的 Grill 处于加热状态，并且还未达到完全烧焦时，才继续烤制
            if (IsOnHeatingGrill && cookProgress < 1.0f)
            {
                ApplyHeat(cookSpeed * Time.deltaTime);
            }

            // 更新 UI 显隐逻辑
            UpdateUIVisibility();
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

        private void InitializeUI()
        {
            if (cookSlider != null)
            {
                cookSlider.minValue = 0f;
                cookSlider.maxValue = 1f;
                cookSlider.value = cookProgress;
            }

            if (autoHideUI && sliderCanvas != null)
            {
                sliderCanvas.gameObject.SetActive(cookProgress > 0.01f);
            }
        }

        private void UpdateUIBillboard()
        {
            if (_mainCamera == null)
            {
                FindMainCamera();
                if (_mainCamera == null) return;
            }

            Vector3 localPos = sliderCanvas.transform.localPosition;

            // 严格仅对齐摄像机的水平视线航向角 (Yaw)，X=0, Z=0，杜绝俯仰旋转导致的 UI 翻转、畸变与飞离
            float cameraYaw = _mainCamera.transform.eulerAngles.y;
            sliderCanvas.transform.rotation = Quaternion.Euler(0f, cameraYaw, 0f);

            // 锁定局部位置，防止任何轴心偏移导致位移
            sliderCanvas.transform.localPosition = localPos;
        }

        private void UpdateUIVisibility()
        {
            if (sliderCanvas == null) return;

            if (autoHideUI)
            {
                // 当在烤架上受热，或者已经有熟度进度时显示
                bool shouldShow = IsOnHeatingGrill || (cookProgress > 0.01f && cookProgress < 1.0f);
                if (sliderCanvas.gameObject.activeSelf != shouldShow)
                {
                    sliderCanvas.gameObject.SetActive(shouldShow);
                }
            }
        }

        /// <summary>
        /// 施加热量，增加熟度进度
        /// </summary>
        /// <param name="delta">本次增加的进度量</param>
        public void ApplyHeat(float delta)
        {
            if (cookProgress >= 1.0f) return;

            cookProgress = Mathf.Clamp01(cookProgress + delta);

            // 更新 UI Slider
            if (cookSlider != null)
            {
                cookSlider.value = cookProgress;
            }

            // 动态更新肉的外观颜色与着色器参数
            UpdateMeatAppearance(cookProgress);

            onCookProgressChanged?.Invoke(cookProgress);

            // 触发关键节点事件
            if (!_perfectCookedFired && cookProgress >= 0.5f)
            {
                _perfectCookedFired = true;
                onPerfectCooked?.Invoke();
            }

            if (!_burntFired && cookProgress >= 1.0f)
            {
                _burntFired = true;
                onBurnt?.Invoke();
            }
        }

        /// <summary>
        /// 核心变色逻辑：根据当前熟度平滑过渡肉的外观颜色与着色器参数
        /// 逻辑：
        /// - 0.0 ~ 0.5：生肉红 (Raw) 平滑过渡到 烤肉棕褐 (Cooked)
        /// - 0.5 ~ 1.0：烤肉棕褐 (Cooked) 平滑过渡到 烧焦炭黑 (Burnt)
        /// </summary>
        /// <param name="progress">当前熟度进度 (0.0 ~ 1.0)</param>
        public void UpdateMeatAppearance(float progress)
        {
            if (meatRenderer == null) return;

            Color targetColor;
            float burnShaderAmount = 0f;

            if (progress <= 0.5f)
            {
                // 第一阶段：生肉红 -> 熟肉棕
                float t = progress / 0.5f;
                targetColor = Color.Lerp(rawColor, cookedColor, t);
                burnShaderAmount = 0f;
            }
            else
            {
                // 第二阶段：熟肉棕 -> 烧焦黑
                float t = (progress - 0.5f) / 0.5f;
                targetColor = Color.Lerp(cookedColor, burntColor, t);
                burnShaderAmount = t;
            }

            // 方案 A：使用 MaterialPropertyBlock (推荐，高效且不破环合批与材质文件)
            if (usePropertyBlock)
            {
                meatRenderer.GetPropertyBlock(_propBlock);

                // 兼容 URP (_BaseColor) 与 Built-in (_Color)
                _propBlock.SetColor(BaseColorId, targetColor);
                _propBlock.SetColor(ColorId, targetColor);

                // 额外提供 Float 参数方便自定义 Shader 做烧焦噪波/溶解/炭化边缘
                _propBlock.SetFloat(CookProgressId, progress);
                _propBlock.SetFloat(BurnAmountId, burnShaderAmount);

                meatRenderer.SetPropertyBlock(_propBlock);
            }
            // 方案 B：直接修改实例化材质 (fallback 方式)
            else
            {
                Material mat = meatRenderer.material;
                if (mat.HasProperty(BaseColorId))
                {
                    mat.SetColor(BaseColorId, targetColor);
                }
                else if (mat.HasProperty(ColorId))
                {
                    mat.SetColor(ColorId, targetColor);
                }

                if (mat.HasProperty(CookProgressId)) mat.SetFloat(CookProgressId, progress);
                if (mat.HasProperty(BurnAmountId)) mat.SetFloat(BurnAmountId, burnShaderAmount);
            }
        }

        /// <summary>
        /// 设置当前所在的烤架引用（由 Grill 的 OnTriggerEnter / Exit 调用）
        /// </summary>
        public void SetCurrentGrill(Grill grill)
        {
            _currentGrill = grill;
        }

        #region 调试与 Gizmos

        [ContextMenu("Debug: 打印 Slider 坐标")]
        private void DebugPrintSliderPosition()
        {
            if (sliderCanvas != null)
            {
                Debug.Log($"[Meat] Slider 世界坐标: {sliderCanvas.transform.position}, 局部坐标: {sliderCanvas.transform.localPosition}");
            }
            else
            {
                Debug.LogWarning("[Meat] sliderCanvas 未赋值！");
            }
        }

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (sliderCanvas != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(sliderCanvas.transform.position, 0.05f);
                Gizmos.DrawLine(transform.position, sliderCanvas.transform.position);
            }
        }
        #endif

        #endregion
    }
}
