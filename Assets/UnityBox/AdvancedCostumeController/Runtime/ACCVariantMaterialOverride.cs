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
  public class ACCVariantMaterialOverride : MonoBehaviour, IEditorOnly
  {
    [Serializable]
    public class MaterialReplacement
    {
      public Material Source;
      public Material Replacement;
    }

    [Tooltip("需要进行材质替换的服装本体。创建组件时会尝试选择同级的另一个对象。")]
    public GameObject OutfitBase;

    [Tooltip("留空 Replacement 表示保持 Source 材质不变。")]
    public List<MaterialReplacement> Replacements = new List<MaterialReplacement>();

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