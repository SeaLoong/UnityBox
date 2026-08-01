using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 标记一个服装层级对象为材质变体。
  /// ACC 在生成时读取此组件，为 OutfitBase 中匹配的材质槽生成替换动画。
  /// </summary>
  [AddComponentMenu("UnityBox/ACC Variant Material Override")]
  [HelpURL("https://github.com/SeaLoong/UnityBox/blob/master/Assets/UnityBox/AdvancedCostumeController/README.md#accvariantmaterialoverride")]
  public class ACCVariantMaterialOverride : MonoBehaviour, IEditorOnly
  {
    [Serializable]
    public class MaterialReplacement
    {
      public Material Source;
      public Material Replacement;
    }

    [Serializable]
    public class RendererMaterialReplacement
    {
      [Tooltip("服装本体上需要精准替换的 Renderer。\nRenderer on the Outfit Base to override.")]
      public Renderer TargetRenderer;

      [Min(0)]
      [Tooltip("需要替换的材质槽位索引。\nMaterial slot index to override.")]
      public int MaterialSlot;

      [Tooltip("比对时记录的原材质，仅用于校验和展示。\nOriginal material recorded during comparison.")]
      public Material Source;

      [Tooltip("此 Renderer 槽位使用的替换材质。\nReplacement for this renderer slot.")]
      public Material Replacement;
    }

    [Tooltip("需要进行材质替换的服装本体。创建组件时会尝试选择同级的另一个对象。\nOutfit base whose materials should be replaced; Reset tries to select a sibling.")]
    public GameObject OutfitBase;

    [Tooltip("留空 Replacement 表示保持 Source 材质不变。\nAn empty Replacement keeps the Source material unchanged.")]
    public List<MaterialReplacement> Replacements = new List<MaterialReplacement>();

    [Tooltip("仅覆盖指定 Renderer 的指定槽位；可通过 Inspector 自动对照当前变体与服装本体生成。\nOverrides only specific renderer slots; the Inspector can generate these entries by comparing this variant with the Outfit Base.")]
    public List<RendererMaterialReplacement> RendererOverrides =
      new List<RendererMaterialReplacement>();

    private void Reset()
    {
      if (transform.parent == null) return;

      for (int i = 0; i < transform.parent.childCount; i++)
      {
        var sibling = transform.parent.GetChild(i).gameObject;
        if (sibling != gameObject)
        {
          OutfitBase = sibling;
          break;
        }
      }
    }
  }
}