using UnityEngine;

/// <summary>
/// 双态轮廓高亮控制系统 (完美配合 URP Renderer Feature)
/// 修复：抓取时保持白色高亮，平时为黑色边缘
/// </summary>
public class ItemHighlight : MonoBehaviour
{
    [Header("Layer 配置 (请在 Inspector 填入准确的数字)")]
    [Tooltip("没抓/没看时的正常 Layer (黑色边框，填 8: ItemNormal)")]
    public int normalLayer = 8;

    [Tooltip("准星看着时的悬停 Layer (白色边框，填 9: ItemHover)")]
    public int hoverLayer = 9;

    [Tooltip("抓在手里时的锁定 Layer (白色边框，填 9: ItemHover)")]
    public int grabbedLayer = 9;

    [Tooltip("是否递归应用到所有子物体网格")]
    public bool includeChildren = true;

    // 内部状态记录
    private bool isHovered = false;
    private bool isGrabbed = false;
    
    // 防掉帧核心
    private int currentAppliedLayer = -1; 

    private void Start()
    {
        // 游戏开始时，强制应用正常状态（黑边）
        ApplyLayer(normalLayer);
    }

    /// <summary>
    /// 设置注视/悬停状态：为 true 时切到白色，false 时切回黑色
    /// </summary>
    public void SetHover(bool isHovering)
    {
        isHovered = isHovering;

        // 如果物品正被抓在手里，绝对不允许悬停逻辑干扰它！
        if (isGrabbed) return;

        ApplyLayer(isHovered ? hoverLayer : normalLayer);
    }

    /// <summary>
    /// 设置抓取状态：抓起时变成白色锁定；放下时根据是否看着它决定黑白
    /// </summary>
    public void SetGrabbed(bool grabbed)
    {
        isGrabbed = grabbed;

        if (isGrabbed)
        {
            // 抓起来，直接锁定为白色边框 (Layer 9)
            ApplyLayer(grabbedLayer);
        }
        else
        {
            // 放下手，如果眼睛还在看着它就是白色，没看就是黑色
            ApplyLayer(isHovered ? hoverLayer : normalLayer);
        }
    }

    /// <summary>
    /// 统一设置 Layer 并递归应用到所有子网格
    /// </summary>
    private void ApplyLayer(int targetLayer)
    {
        // 【终极性能拦截】：如果当前已经在这个图层了，直接跳过！防止掉帧！
        if (currentAppliedLayer == targetLayer) return;

        currentAppliedLayer = targetLayer;
        gameObject.layer = targetLayer;

        if (includeChildren)
        {
            SetLayerRecursively(transform, targetLayer);
        }
    }

    private void SetLayerRecursively(Transform parent, int targetLayer)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            child.gameObject.layer = targetLayer;
            if (child.childCount > 0)
            {
                SetLayerRecursively(child, targetLayer);
            }
        }
    }

    public bool IsGrabbed => isGrabbed;
    public bool IsHovered => isHovered;
}