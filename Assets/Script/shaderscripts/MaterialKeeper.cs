using UnityEngine;

/// <summary>
/// 用于强制打包 URP Renderer Feature 所需但场景内未直接挂载的材质球。请将其挂载到空物体上。
/// </summary>
public class MaterialKeeper : MonoBehaviour
{
    [Tooltip("需要强制打包防剔除的材质球列表（如 Outline_White、Outline_Black）")]
    [SerializeField] private Material[] requiredMaterials;

    public Material[] RequiredMaterials => requiredMaterials;
}
