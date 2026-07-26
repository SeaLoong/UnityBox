using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>一个可单独控制的服装部件，或由多个部件组成的命名分组。</summary>
  public class PartControlData
  {
    public string Name { get; set; }
    public List<GameObject> Parts { get; set; } = new List<GameObject>();
    public bool IsGroup { get; set; }
  }

  /// <summary>
  /// 服装数据结构 — 描述一套服装的所有信息
  /// </summary>
  public class OutfitData
  {
    /// <summary>服装本体 GameObject（拥有服装骨架的最高识别节点）</summary>
    public GameObject BaseObject { get; set; }

    /// <summary>服装根对象（有变体时为更上层的组节点，无变体时等于 BaseObject）</summary>
    public GameObject OutfitObject { get; set; }

    /// <summary>变体列表（同级的其他服装对象）</summary>
    public List<GameObject> Variants { get; set; } = new List<GameObject>();

    /// <summary>部件列表（BaseObject 下的子节点）</summary>
    public List<GameObject> Parts { get; set; } = new List<GameObject>();

    /// <summary>被 Exclude Marker 显式排除、仅用于预览说明的对象。</summary>
    public List<GameObject> ExcludedParts { get; set; } = new List<GameObject>();

    /// <summary>生成时使用的部件控制项；为空时每个 Parts 项各自生成开关。</summary>
    public List<PartControlData> PartControls { get; set; } = new List<PartControlData>();

    /// <summary>当前 Outfit Base 上 Marker 提供的持久菜单显示名称格式规则。</summary>
    public ACCOutfitMarker Marker { get; set; }

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

    public List<PartControlData> GetPartControls()
    {
      if (PartControls != null && PartControls.Count > 0)
        return PartControls;

      return Parts.Select(part => new PartControlData
      {
        Name = part.name,
        Parts = new List<GameObject> { part },
        IsGroup = false
      }).ToList();
    }
  }

  /// <summary>
  /// ACC 运行时配置 — 存储编辑器中的所有设置项
  /// </summary>
  public class ACCConfig
  {
    public const string DefaultControllerFileName = "CostumeController";
    public const string MenuObjectName = "ACC_Menu";
    /// <summary>混搭模式参数路径中使用的固定前缀。</summary>
    public const string MixerParamPrefix = "Mixer";

    public GameObject CostumesRoot;
    public string ParamPrefix = "";
    public ACCLanguage Language = ACCLanguage.Auto;
    public string GeneratedFolder = "Assets/UnityBox/Generated/AdvancedCostumeController";
    public GameObject DefaultOutfitOverride;
    public bool EnableParts = false;
    public bool EnableCustomMixer = false;
    public string CustomMixerName = "";
    public string RootMenuName = "";

    /// <summary>当 CostumesRoot 变更时自动从根对象名称刷新 ParamPrefix 和 RootMenuName。</summary>
    public void ApplyAutoDefaultsFromRoot()
    {
      string rootName = GetRootBasedDefaultName();
      if (string.IsNullOrWhiteSpace(rootName)) return;

      ParamPrefix = rootName;
      RootMenuName = rootName;
    }

    /// <summary>服装切换主 Int 参数始终使用有效的命名空间。</summary>
    public string MainParameterName => EffectiveParamPrefix;

    /// <summary>ParamPrefix 为空时回退到服装根对象名称。</summary>
    public string EffectiveParamPrefix
    {
      get
      {
        if (!string.IsNullOrWhiteSpace(ParamPrefix))
          return ParamPrefix;
        return GetRootBasedDefaultName();
      }
    }

    /// <summary>菜单显示名称。为空时默认与 Parameter Prefix 一致。</summary>
    public string EffectiveRootMenuName
    {
      get
      {
        if (!string.IsNullOrWhiteSpace(RootMenuName))
          return RootMenuName;
        return EffectiveParamPrefix;
      }
    }

    public string GetControllerFileName()
    {
      string sourceName = GetGenerationNamespace();
      if (string.IsNullOrWhiteSpace(sourceName))
        sourceName = DefaultControllerFileName;

      string safeFileName = Utils.SanitizeForFileName(sourceName);
      if (string.IsNullOrWhiteSpace(safeFileName))
        safeFileName = DefaultControllerFileName;

      return safeFileName + ".controller";
    }

    /// <summary>
    /// 获取实际的生成目录（在 GeneratedFolder 后追加菜单名称和命名空间），
    /// 确保不同 ACC 实例的输出互不覆盖。
    /// </summary>
    public string GetResolvedGeneratedFolder()
    {
      string folder = Utils.NormalizeAssetsFolder(GeneratedFolder);
      string menuName = Utils.SanitizeForFileName(EffectiveRootMenuName);
      string ns = Utils.SanitizeForFileName(GetGenerationNamespace());
      return folder + "/" + menuName + "/" + ns;
    }

    /// <summary>生成资产和 Animator Layer 使用的稳定命名空间。</summary>
    public string GetGenerationNamespace()
    {
      return EffectiveParamPrefix;
    }

    private string GetRootBasedDefaultName()
    {
      return CostumesRoot != null ? CostumesRoot.name : "";
    }
  }
}
