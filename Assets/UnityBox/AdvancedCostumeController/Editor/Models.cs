using System.Collections.Generic;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 服装数据结构 — 描述一套服装的所有信息
  /// </summary>
  public class OutfitData
  {
    /// <summary>服装本体 GameObject（包含 Mesh 的直接父节点）</summary>
    public GameObject BaseObject { get; set; }

    /// <summary>服装根对象（有变体时为更上层的组节点，无变体时等于 BaseObject）</summary>
    public GameObject OutfitObject { get; set; }

    /// <summary>变体列表（同级的其他服装对象）</summary>
    public List<GameObject> Variants { get; set; } = new List<GameObject>();

    /// <summary>部件列表（BaseObject 下的子节点）</summary>
    public List<GameObject> Parts { get; set; } = new List<GameObject>();

    /// <summary>服装显示名称</summary>
    public string Name { get; set; }

    /// <summary>相对于 CostumesRoot 的路径</summary>
    public string RelativePath { get; set; }

    /// <summary>是否为默认服装</summary>
    public bool IsDefaultOutfit { get; set; }

    /// <summary>本体是否被选中（默认 true，用于生成时过滤未勾选的本体）</summary>
    public bool IsBaseSelected { get; set; } = true;

    /// <summary>是否有变体</summary>
    public bool HasVariants() => Variants.Count > 0;

    /// <summary>获取所有选中的对象（根据 IsBaseSelected 决定是否包含本体）</summary>
    public List<GameObject> GetAllObjects()
    {
      var result = new List<GameObject>();
      if (IsBaseSelected) result.Add(BaseObject);
      result.AddRange(Variants);
      return result;
    }
  }

  /// <summary>
  /// ACC 运行时配置 — 存储编辑器中的所有设置项
  /// </summary>
  public class ACCConfig
  {
    public GameObject CostumesRoot;
    public string ParamPrefix = "CST";
    public string CostumeParamName = "costume";
public string GeneratedFolder = "Assets/UnityBox/Generated/AdvancedCostumeController";
    public string IgnoreNamesCsv = "Armature,Bone,Skeleton";
    public GameObject DefaultOutfitOverride;
    public bool EnableParts = false;
    public bool EnableCustomMixer = false;
    public string CustomMixerName = "CustomMix";
  }
}
