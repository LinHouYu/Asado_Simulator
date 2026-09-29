using System;
using System.Collections.Generic;
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
        [Tooltip("是否为 Carne_Asado 专属模式（仅变色红色肉质部分，保持白色骨头和脂肪不被染黑）")]
        [SerializeField] private bool isCarneAsado = false;

        [Tooltip("肉块的主渲染器 (用于 Carne_Asado 针对性变色。若为空将自动检测)")]
        [SerializeField] private Renderer meatRenderer;

        [Tooltip("生肉阶段基础颜色 (Raw - 0.0)")]
        [SerializeField] private Color rawColor = new Color(0.85f, 0.22f, 0.22f, 1.0f);

        [Tooltip("完美烤熟基础颜色 (Cooked - 0.5)")]
        [SerializeField] private Color cookedColor = new Color(0.55f, 0.30f, 0.10f, 1.0f);

        [Tooltip("完全烧焦基础颜色 (Burnt - 1.0)")]
        [SerializeField] private Color burntColor = new Color(0.12f, 0.12f, 0.12f, 1.0f);

        [Tooltip("是否使用 MaterialPropertyBlock（推荐开启，性能高且不会在内存中实例化重复材质）")]
        [SerializeField] private bool usePropertyBlock = true;

        [Header("戏剧性功能：焦炭煤炭化")]
        [Tooltip("当肉块完全烤焦 (cookProgress >= 1.0) 时，是否将其转变为普通煤炭 (Tag: Carbon)，可直接投入火盆作为燃料使用")]
        [SerializeField] private bool turnIntoCharcoalWhenBurnt = true;

        [Tooltip("转变为煤炭后的搞笑提示标签")]
        [SerializeField] private string charcoalHumorLabel = "💀 炭化完成！已获得优质肉炭 (可投入火盆燃烧)";

        [Header("UI 显示配置")]
        [Tooltip("如果未手动指定 Canvas 和 Slider，是否在肉块头顶自动生成小巧的熟度进度条 UI")]
        [SerializeField] private bool autoCreateUIIfMissing = true;

        [Tooltip("显示熟度进度的 Slider")]
        [SerializeField] private Slider cookSlider;

        [Tooltip("挂载 Slider 的 World Space Canvas（用于面向摄像机与显示控制）")]
        [SerializeField] private Canvas sliderCanvas;

        [Tooltip("是否仅在受热烹饪或有进度时显示 UI（未烤制时隐藏，视觉更清爽）")]
        [SerializeField] private bool autoHideUI = true;

        [Header("事件回调 (可选)")]
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
        private readonly List<Renderer> _cachedRenderers = new List<Renderer>();

        // Shader 属性 ID 缓存（同时兼容 URP 和标准管线）
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit / Unlit
        private static readonly int ColorId = Shader.PropertyToID("_Color");         // Standard / Legacy
        private static readonly int CookProgressId = Shader.PropertyToID("_CookProgress");
        private static readonly int BurnAmountId = Shader.PropertyToID("_BurnAmount");

        private bool _isBurntToCharcoal = false;
        private Text _uiHumorText;

        // 公开属性
        public float CookProgress => cookProgress;
        public bool IsOnHeatingGrill => _currentGrill != null && _currentGrill.IsHeating;
        public bool IsBurntToCharcoal => _isBurntToCharcoal;

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
            if (usePropertyBlock)
            {
                _propBlock = new MaterialPropertyBlock();
            }

            FindMainCamera();
            InitializeRenderers();
            EnsureUI();
            InitializeUI();
            UpdateMeatAppearance(cookProgress);
        }

        private void InitializeRenderers()
        {
            _cachedRenderers.Clear();

            bool isCarne = isCarneAsado || gameObject.name.Contains("Carne_Asado") || (transform.parent != null && transform.parent.name.Contains("Carne_Asado"));

            if (isCarne)
            {
                // Carne_Asado 专属：仅变色肉质部分，绝对不污染白色的骨头和脂肪
                if (meatRenderer != null)
                {
                    _cachedRenderers.Add(meatRenderer);
                }
                else
                {
                    var all = GetComponentsInChildren<Renderer>(true);
                    Renderer foundMeat = null;
                    for (int i = 0; i < all.Length; i++)
                    {
                        var r = all[i];
                        if (r == null) continue;
                        if (r.gameObject.name.ToLower().Contains("meat") || (r.sharedMaterial != null && r.sharedMaterial.name.ToLower().Contains("meat")))
                        {
                            foundMeat = r;
                            break;
                        }
                    }
                    if (foundMeat != null)
                    {
                        meatRenderer = foundMeat;
                        _cachedRenderers.Add(foundMeat);
                    }
                    else if (all.Length > 0)
                    {
                        meatRenderer = all[0];
                        _cachedRenderers.Add(all[0]);
                    }
                }
            }
            else
            {
                // 其他所有肉类 (Chorizo、Matambre、Morcilla、Pollo 等)：
                // 收集模型下的全部子渲染器，确保整个模型一整块一起变色，不再是一小块！
                var all = GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    var r = all[i];
                    if (r == null) continue;
                    if (r.GetComponentInParent<Canvas>() != null) continue;
                    _cachedRenderers.Add(r);
                }

                if (_cachedRenderers.Count > 0 && meatRenderer == null)
                {
                    meatRenderer = _cachedRenderers[0];
                }
            }
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

        private void EnsureUI()
        {
            if (sliderCanvas == null)
            {
                sliderCanvas = GetComponentInChildren<Canvas>(true);
            }
            if (cookSlider == null)
            {
                cookSlider = GetComponentInChildren<Slider>(true);
            }

            // 如果预制体上未手动绑定 Canvas/Slider，自动在肉块上方动态构建精巧的 World Space Slider
            if ((sliderCanvas == null || cookSlider == null) && autoCreateUIIfMissing)
            {
                CreateDefaultWorldSpaceUI();
            }
        }

        private void CreateDefaultWorldSpaceUI()
        {
            // 1. 创建 Canvas 物体
            GameObject canvasObj = new GameObject("[Auto] MeatCanvas");
            canvasObj.transform.SetParent(transform, false);

            float topOffset = 0.2f;
            if (meatRenderer != null)
            {
                topOffset = Mathf.Max(0.12f, meatRenderer.bounds.extents.y + 0.08f);
            }
            canvasObj.transform.localPosition = new Vector3(0f, topOffset, 0f);
            canvasObj.transform.localRotation = Quaternion.identity;
            canvasObj.transform.localScale = new Vector3(0.003f, 0.003f, 0.003f);

            sliderCanvas = canvasObj.AddComponent<Canvas>();
            sliderCanvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(100f, 16f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            // 2. 创建 Slider 物体
            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(canvasObj.transform, false);
            var sliderRt = sliderObj.AddComponent<RectTransform>();
            sliderRt.anchorMin = Vector2.zero;
            sliderRt.anchorMax = Vector2.one;
            sliderRt.sizeDelta = Vector2.zero;
            sliderRt.anchoredPosition = Vector2.zero;

            cookSlider = sliderObj.AddComponent<Slider>();
            cookSlider.interactable = false;
            cookSlider.transition = Selectable.Transition.None;
            cookSlider.minValue = 0f;
            cookSlider.maxValue = 1f;
            cookSlider.value = cookProgress;

            // 3. 背景底色 (暗黑透明)
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(sliderObj.transform, false);
            var bgRt = bgObj.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            var bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);

            // 4. Fill Area & Fill (金黄色进度条)
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            var faRt = fillArea.AddComponent<RectTransform>();
            faRt.anchorMin = Vector2.zero;
            faRt.anchorMax = Vector2.one;
            faRt.sizeDelta = new Vector2(-4f, -4f);

            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillArea.transform, false);
            var fillRt = fillObj.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.sizeDelta = Vector2.zero;
            var fillImage = fillObj.AddComponent<Image>();
            fillImage.color = new Color(1.0f, 0.72f, 0.18f, 1.0f);

            cookSlider.fillRect = fillRt;
            cookSlider.targetGraphic = fillImage;
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
                // 当在烤架上受热，或者已经有熟度进度，或者已烧焦碳化为煤炭时显示
                bool shouldShow = IsOnHeatingGrill || (cookProgress > 0.01f && cookProgress < 1.0f) || _isBurntToCharcoal;
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
                if (turnIntoCharcoalWhenBurnt)
                {
                    TransformIntoCharcoal();
                }
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
            if (_cachedRenderers.Count == 0)
            {
                InitializeRenderers();
                if (_cachedRenderers.Count == 0) return;
            }

            bool isCarne = isCarneAsado || gameObject.name.Contains("Carne_Asado") || (transform.parent != null && transform.parent.name.Contains("Carne_Asado"));

            Color targetColor;
            float burnShaderAmount = 0f;

            if (isCarne)
            {
                // Carne_Asado: 从设定的 rawColor (鲜红) 过渡到 cookedColor (熟棕) 到 burntColor (炭黑)
                if (progress <= 0.5f)
                {
                    float t = progress / 0.5f;
                    targetColor = Color.Lerp(rawColor, cookedColor, t);
                    burnShaderAmount = 0f;
                }
                else
                {
                    float t = (progress - 0.5f) / 0.5f;
                    targetColor = Color.Lerp(cookedColor, burntColor, t);
                    burnShaderAmount = t;
                }
            }
            else
            {
                // 其他肉类 (自带真实纹理贴图)：
                // 0.0 全生时使用纯白 (1, 1, 1) 完美呈现其自身材质自带的逼真生肉贴图！
                // 0.0 ~ 0.5: 均匀渗入金黄烤熟棕色 (cookedColor)
                // 0.5 ~ 1.0: 均匀渗入焦黑炭化色 (burntColor)
                if (progress <= 0.5f)
                {
                    float t = progress / 0.5f;
                    targetColor = Color.Lerp(Color.white, cookedColor, t);
                    burnShaderAmount = 0f;
                }
                else
                {
                    float t = (progress - 0.5f) / 0.5f;
                    targetColor = Color.Lerp(cookedColor, burntColor, t);
                    burnShaderAmount = t;
                }
            }

            // 对整块模型的所有子渲染器统一设置，实现整块模型同步均匀变色！
            for (int i = 0; i < _cachedRenderers.Count; i++)
            {
                var rend = _cachedRenderers[i];
                if (rend == null) continue;

                if (usePropertyBlock)
                {
                    rend.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(BaseColorId, targetColor);
                    _propBlock.SetColor(ColorId, targetColor);
                    _propBlock.SetFloat(CookProgressId, progress);
                    _propBlock.SetFloat(BurnAmountId, burnShaderAmount);
                    rend.SetPropertyBlock(_propBlock);
                }
                else
                {
                    Material mat = rend.material;
                    if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, targetColor);
                    else if (mat.HasProperty(ColorId)) mat.SetColor(ColorId, targetColor);
                    if (mat.HasProperty(CookProgressId)) mat.SetFloat(CookProgressId, progress);
                    if (mat.HasProperty(BurnAmountId)) mat.SetFloat(BurnAmountId, burnShaderAmount);
                }
            }

            if (turnIntoCharcoalWhenBurnt && progress >= 1.0f && !_isBurntToCharcoal)
            {
                TransformIntoCharcoal();
            }
        }

        /// <summary>
        /// 戏剧性搞笑功能：肉块烧满完全焦糊后，彻底物理碳化为普通煤炭 (Tag: Carbon)，可直接放入火盆作为燃料使用！
        /// </summary>
        public void TransformIntoCharcoal()
        {
            if (_isBurntToCharcoal) return;
            _isBurntToCharcoal = true;

            // 1. 修改标签为 Carbon，让火盆和各类检测将其直接认作普通煤炭
            gameObject.tag = "Carbon";
            var allColliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < allColliders.Length; i++)
            {
                allColliders[i].gameObject.tag = "Carbon";
            }

            // 2. 物体命名增加幽默后缀
            if (!gameObject.name.Contains("(焦炭)"))
            {
                gameObject.name = $"{gameObject.name} (焦炭/可当煤炭)";
            }

            // 3. 喷出一阵搞笑的焦黑烟雾特效 (POOF!)
            SpawnBurntSmokePuff();

            // 4. 更新 UI 显示幽默提示与炭黑进度条
            UpdateCharcoalUI();
        }

        private void SpawnBurntSmokePuff()
        {
            GameObject puff = new GameObject("BurntSmokePuff");
            puff.transform.position = transform.position + Vector3.up * 0.1f;
            var ps = puff.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.8f;
            main.startLifetime = 1.2f;
            main.startSpeed = 0.8f;
            main.startSize = 0.18f;
            main.startColor = new Color(0.08f, 0.08f, 0.08f, 0.9f);
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.loop = false;

            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;

            var pRenderer = puff.GetComponent<ParticleSystemRenderer>();
            Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") 
                             ?? Shader.Find("Particles/Standard Unlit") 
                             ?? Shader.Find("Sprites/Default");
            pRenderer.material = new Material(pShader);
            pRenderer.material.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

            ps.Play();
            Destroy(puff, 1.8f);
        }

        private void UpdateCharcoalUI()
        {
            if (sliderCanvas == null) return;

            sliderCanvas.gameObject.SetActive(true);

            // 修改 Slider 为炭黑色
            if (cookSlider != null)
            {
                cookSlider.value = 1.0f;
                if (cookSlider.fillRect != null && cookSlider.fillRect.TryGetComponent<Image>(out var fillImg))
                {
                    fillImg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
                }
            }

            // 动态挂载/更新幽默文本提示
            if (_uiHumorText == null)
            {
                GameObject textObj = new GameObject("CharcoalHumorText");
                textObj.transform.SetParent(sliderCanvas.transform, false);
                var rt = textObj.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 6f);
                rt.sizeDelta = new Vector2(260f, 30f);

                _uiHumorText = textObj.AddComponent<Text>();
                _uiHumorText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_uiHumorText.font == null) _uiHumorText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _uiHumorText.fontSize = 11;
                _uiHumorText.alignment = TextAnchor.MiddleCenter;
                _uiHumorText.color = new Color(1f, 0.85f, 0.2f, 1f); // 亮眼金黄提示色
                _uiHumorText.horizontalOverflow = HorizontalWrapMode.Overflow;
                _uiHumorText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (_uiHumorText != null)
            {
                _uiHumorText.text = charcoalHumorLabel;
                _uiHumorText.gameObject.SetActive(true);
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
