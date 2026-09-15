using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace AsadoSimulator.Cooking
{
    /// <summary>
    /// 烤架核心脚本 (Grill)
    /// 功能：
    /// 1. 触发器检测下方是否放置了 Tag 为 "HotCharcoal" 的高温木炭。
    /// 2. 只有检测到底部存在高温木炭时，烤架才具有“加热能力” (IsHeating == true)。
    /// 3. 当玩家把 Tag 为 "Meat" 或挂载了 Meat 组件的物体放在烤架上时，开始对肉进行加热烹饪。
    /// 4. 采用高容错模块化设计：支持在同一 Trigger 下检测，或通过附带的 GrillHeatZone 子触发器独立检测底部热源。
    /// </summary>
    public class Grill : MonoBehaviour
    {
        [Header("标签与检测配置")]
        [Tooltip("识别为高温木炭的 Tag")]
        [SerializeField] private string hotCharcoalTag = "HotCharcoal";

        [Tooltip("识别为肉类的 Tag（如果肉上挂有 Meat 脚本，即使 Tag 暂时不匹配也可自动识别）")]
        [SerializeField] private string meatTag = "Meat";

        [Tooltip("烤架加热效率倍率 (1.0 为标准速度)")]
        [SerializeField] private float heatEfficiency = 1.0f;

        [Header("多热源叠加 (可选)")]
        [Tooltip("是否根据底部高温炭的数量加速加热（true: 2块炭加热速度翻倍; false: 只要有炭就保持恒定火力）")]
        [SerializeField] private bool scaleHeatWithCharcoalCount = false;

        [Header("事件回调 (可选)")]
        [Tooltip("加热状态切换时触发 (true: 升温开始加热, false: 火熄灭/炭移走)")]
        public UnityEvent<bool> onHeatingStateChanged;

        [Tooltip("烤架上有新肉放入时触发")]
        public UnityEvent<Meat> onMeatPlaced;

        [Tooltip("肉从烤架上拿走时触发")]
        public UnityEvent<Meat> onMeatRemoved;

        // 内部维护的高温木炭列表与正在烹饪的肉类列表
        private readonly HashSet<GameObject> _hotCharcoals = new HashSet<GameObject>();
        private readonly List<Meat> _cookingMeats = new List<Meat>();
        private bool _wasHeating = false;

        // 公开属性
        public bool IsHeating => _hotCharcoals.Count > 0;
        public int HotCharcoalCount => _hotCharcoals.Count;
        public int MeatCount => _cookingMeats.Count;
        public float HeatMultiplier => scaleHeatWithCharcoalCount ? Mathf.Max(1f, _hotCharcoals.Count) * heatEfficiency : heatEfficiency;

        private void Update()
        {
            // 清理列表中可能已被销毁/回收的对象（防空指针）
            CleanInvalidEntries();

            // 监测加热状态变化事件
            bool currentHeating = IsHeating;
            if (currentHeating != _wasHeating)
            {
                _wasHeating = currentHeating;
                onHeatingStateChanged?.Invoke(currentHeating);
            }
        }

        private void CleanInvalidEntries()
        {
            // 清理已销毁的高温炭
            _hotCharcoals.RemoveWhere(charcoal => charcoal == null);

            // 清理已销毁或被拿走的肉
            for (int i = _cookingMeats.Count - 1; i >= 0; i--)
            {
                if (_cookingMeats[i] == null)
                {
                    _cookingMeats.RemoveAt(i);
                }
            }
        }

        #region Trigger 检测逻辑

        private void OnTriggerEnter(Collider other)
        {
            if (other == null) return;

            // 1. 检测高温木炭 (HotCharcoal)
            if (IsMatchingHotCharcoal(other.gameObject, out GameObject charcoalRoot))
            {
                RegisterHotCharcoal(charcoalRoot);
                return;
            }

            // 2. 检测肉类 (Meat)
            if (IsMatchingMeat(other.gameObject, out Meat meatComponent))
            {
                RegisterMeat(meatComponent);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;

            // 1. 移出高温木炭
            if (IsMatchingHotCharcoal(other.gameObject, out GameObject charcoalRoot))
            {
                UnregisterHotCharcoal(charcoalRoot);
                return;
            }

            // 2. 移出肉类
            if (IsMatchingMeat(other.gameObject, out Meat meatComponent))
            {
                UnregisterMeat(meatComponent);
            }
        }

        #endregion

        #region 内部匹配辅助

        private bool IsMatchingHotCharcoal(GameObject obj, out GameObject rootCharcoal)
        {
            rootCharcoal = obj;

            if (obj.CompareTag(hotCharcoalTag))
            {
                rootCharcoal = obj;
                return true;
            }

            // 向上检查父级
            var parent = obj.transform.parent;
            while (parent != null)
            {
                if (parent.CompareTag(hotCharcoalTag))
                {
                    rootCharcoal = parent.gameObject;
                    return true;
                }
                parent = parent.parent;
            }

            return false;
        }

        private bool IsMatchingMeat(GameObject obj, out Meat meatComponent)
        {
            // 优先检查 Meat 组件
            if (obj.TryGetComponent<Meat>(out meatComponent))
            {
                return true;
            }

            // 检查父级或子级是否有 Meat 组件
            meatComponent = obj.GetComponentInParent<Meat>();
            if (meatComponent != null) return true;

            meatComponent = obj.GetComponentInChildren<Meat>();
            if (meatComponent != null) return true;

            // 如果挂载了 Tag: "Meat"
            if (!string.IsNullOrEmpty(meatTag) && (obj.CompareTag(meatTag) || (obj.transform.parent != null && obj.transform.parent.CompareTag(meatTag))))
            {
                // 如果有 Tag 但没有挂脚本，尝试获取或记录
                meatComponent = obj.GetComponentInParent<Meat>();
                return meatComponent != null;
            }

            return false;
        }

        #endregion

        #region 公共注册管理接口 (支持外部或子触发器调用)

        /// <summary>
        /// 注册检测到的高温炭（当放入烤架底部热源区时）
        /// </summary>
        public void RegisterHotCharcoal(GameObject charcoal)
        {
            if (charcoal == null) return;

            if (_hotCharcoals.Add(charcoal))
            {
                bool heating = IsHeating;
                if (heating != _wasHeating)
                {
                    _wasHeating = heating;
                    onHeatingStateChanged?.Invoke(heating);
                }
            }
        }

        /// <summary>
        /// 注销高温炭（当高温炭被移走或熄灭时）
        /// </summary>
        public void UnregisterHotCharcoal(GameObject charcoal)
        {
            if (charcoal == null) return;

            if (_hotCharcoals.Remove(charcoal))
            {
                bool heating = IsHeating;
                if (heating != _wasHeating)
                {
                    _wasHeating = heating;
                    onHeatingStateChanged?.Invoke(heating);
                }
            }
        }

        /// <summary>
        /// 放入烤肉
        /// </summary>
        public void RegisterMeat(Meat meat)
        {
            if (meat == null) return;

            if (!_cookingMeats.Contains(meat))
            {
                _cookingMeats.Add(meat);
                meat.SetCurrentGrill(this);
                onMeatPlaced?.Invoke(meat);
            }
        }

        /// <summary>
        /// 取走烤肉
        /// </summary>
        public void UnregisterMeat(Meat meat)
        {
            if (meat == null) return;

            if (_cookingMeats.Remove(meat))
            {
                meat.SetCurrentGrill(null);
                onMeatRemoved?.Invoke(meat);
            }
        }

        #endregion
    }

    /// <summary>
    /// 辅助组件：烤架独立底部热源检测区 (GrillHeatZone)
    /// 当烤架模型结构较复杂，需要把“底部放炭区”和“上方烤肉区”分开成两个独立的 Trigger 时，
    /// 可将此脚本挂载在底部的热源 Trigger GameObject 上，它会自动将检测到的 HotCharcoal 转发给宿主 Grill。
    /// </summary>
    public class GrillHeatZone : MonoBehaviour
    {
        [Tooltip("所属的主烤架组件。若为空，将自动获取父级中的 Grill")]
        [SerializeField] private Grill parentGrill;

        [Tooltip("目标高温木炭 Tag")]
        [SerializeField] private string hotCharcoalTag = "HotCharcoal";

        private void Awake()
        {
            if (parentGrill == null)
            {
                parentGrill = GetComponentInParent<Grill>();
            }

            var col = GetComponent<Collider>();
            if (col != null && !col.isTrigger)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (parentGrill == null || other == null) return;

            if (other.CompareTag(hotCharcoalTag) || (other.transform.parent != null && other.transform.parent.CompareTag(hotCharcoalTag)))
            {
                GameObject rootCharcoal = other.transform.parent != null && other.transform.parent.CompareTag(hotCharcoalTag) 
                    ? other.transform.parent.gameObject 
                    : other.gameObject;

                parentGrill.RegisterHotCharcoal(rootCharcoal);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (parentGrill == null || other == null) return;

            if (other.CompareTag(hotCharcoalTag) || (other.transform.parent != null && other.transform.parent.CompareTag(hotCharcoalTag)))
            {
                GameObject rootCharcoal = other.transform.parent != null && other.transform.parent.CompareTag(hotCharcoalTag) 
                    ? other.transform.parent.gameObject 
                    : other.gameObject;

                parentGrill.UnregisterHotCharcoal(rootCharcoal);
            }
        }
    }
}
