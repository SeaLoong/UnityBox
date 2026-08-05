using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>互斥选择参数的布局。压缩模式使用本地 Int 与同步二进制 Bool 位。</summary>
  public sealed class ChoiceParameterLayout
  {
    public int ChoiceCount { get; }
    private readonly bool requestedCompression;
    /// <summary>恰好两个取值时可直接使用一个 Bool，而不需要 Int 或压缩位。</summary>
    public bool UsesBoolean => ChoiceCount == 2;
    public bool UsesCompression => requestedCompression && !UsesBoolean;
    /// <summary>只有一个固定选择值时无需网络同步。</summary>
    public bool RequiresSynchronization => ChoiceCount > 1;
    public int BitCount
    {
      get
      {
        int bits = 0;
        for (int valueCount = ChoiceCount - 1; valueCount > 0; valueCount >>= 1)
          bits++;
        return bits;
      }
    }

    public ChoiceParameterLayout(int choiceCount, bool usesCompression)
    {
      ChoiceCount = choiceCount;
      requestedCompression = usesCompression;
    }

    public string GetBitParameterName(string baseParameterName, int bitIndex)
    {
      return Utils.BuildParamName(baseParameterName, $"Bits/{bitIndex:D2}");
    }
  }

  /// <summary>一个可单独控制的服装部件，或由多个部件组成的命名分组。</summary>
  public class PartControlData
  {
    public string Name { get; set; }
    public List<GameObject> Parts { get; set; } = new List<GameObject>();
    public bool IsGroup { get; set; }
  }

  /// <summary>一个 Outfit Base 或变体自身的部件扫描结果。</summary>
  public class VariantPartData
  {
    public GameObject VariantObject { get; set; }
    public List<GameObject> Parts { get; set; } = new List<GameObject>();
    public List<GameObject> ExcludedParts { get; set; } = new List<GameObject>();
    public List<PartControlData> PartControls { get; set; } = new List<PartControlData>();
  }

  /// <summary>独立参数 Mixer 中同一服装组的一个部件/分组候选参数及其版本对象映射。</summary>
  public class MixerPartSlot
  {
    public string Key { get; set; }
    public string Name { get; set; }
    public List<VariantPartCandidate> Candidates { get; set; } = new List<VariantPartCandidate>();
  }

  /// <summary>一个变体为独立参数 Mixer 部件槽位提供的候选部件。</summary>
  public class VariantPartCandidate
  {
    public GameObject VariantObject { get; set; }
    public PartControlData Control { get; set; }
  }

  /// <summary>
  /// 服装数据结构 — 描述一套服装的所有信息
  /// </summary>
  public class OutfitData
  {
    /// <summary>服装本体 GameObject（拥有服装骨架的最高识别节点）</summary>
    public GameObject BaseObject { get; set; }

    /// <summary>服装对应的 Armature。存在时，生成会确保其具有 MA Merge Armature。</summary>
    public GameObject ArmatureObject { get; set; }

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

    /// <summary>Base 与各变体分别扫描出的部件数据，供混搭使用。</summary>
    public List<VariantPartData> VariantPartData { get; set; } = new List<VariantPartData>();

    /// <summary>当前 Outfit Base 上 Marker 提供的持久菜单显示名称格式规则。</summary>
    public ACCOutfitMarker Marker { get; set; }

    /// <summary>服装显示名称</summary>
    public string Name { get; set; }

    /// <summary>相对于 CostumesRoot 的路径</summary>
    public string RelativePath { get; set; }

    /// <summary>是否为默认服装</summary>
    public bool IsDefaultOutfit { get; set; }

    /// <summary>
    /// 用户显式指定的默认本体或变体对象。为空时由生成器回退到本体或第一个已选对象。
    /// </summary>
    public GameObject DefaultChoiceObject { get; set; }

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

    public VariantPartData GetVariantPartData(GameObject variantObject)
    {
      return VariantPartData.FirstOrDefault(item => item.VariantObject == variantObject);
    }

    /// <summary>
    /// 按相对部件路径或持久分组名构建混搭候选参数。
    /// Candidates 把同一个参数槽位映射到不同版本的实际对象；0 为 Off。
    /// </summary>
    public List<MixerPartSlot> GetMixerPartSlots()
    {
      var orderedSlots = new List<MixerPartSlot>();
      var variantDataList = VariantPartData ?? new List<VariantPartData>();
      var baseData = variantDataList.FirstOrDefault(item => item.VariantObject == BaseObject);
      var baseControls = GetPartControls();
      var definitions = new List<(string Key, string Name, bool IsGroup, string PartName)>();
      var definitionKeys = new HashSet<string>();

      void AddDefinitions(
        GameObject owner,
        IEnumerable<PartControlData> controls,
        bool allowVariantNameFallback)
      {
        foreach (var control in controls ?? Enumerable.Empty<PartControlData>())
        {
          if (control?.Parts == null || control.Parts.Count == 0) continue;
          string key = GetMixerSlotKey(owner, control);
          if (string.IsNullOrEmpty(key)) continue;
          if (!definitionKeys.Add(key)) continue;
          string partName = !control.IsGroup
            ? control.Parts.FirstOrDefault()?.name ?? ""
            : "";
          // Different outfit versions often wrap the same logical part in a
          // different container. Reuse an existing non-group definition with
          // the same control name instead of creating a second disconnected
          // Mixer slot for the path difference.
          if (allowVariantNameFallback && !control.IsGroup && definitions.Any(definition =>
              !definition.IsGroup &&
              (definition.Name == control.Name ||
               (!string.IsNullOrEmpty(partName) && definition.PartName == partName))))
          {
            definitionKeys.Remove(key);
            continue;
          }
          definitions.Add((key, control.Name, control.IsGroup, partName));
        }
      }

      // 先保持本体控制顺序，再追加实体变体独有的控制项；材质变体没有
      // VariantPartData 部件，因此只会复用本体定义。
      AddDefinitions(BaseObject, baseControls, allowVariantNameFallback: false);
      foreach (var variantData in variantDataList)
      {
        if (variantData == null || variantData.VariantObject == BaseObject) continue;
        AddDefinitions(variantData.VariantObject, variantData.PartControls,
          allowVariantNameFallback: true);
      }

      foreach (var definition in definitions)
      {
        string key = definition.Key;
        var slot = new MixerPartSlot { Key = key, Name = definition.Name };
        orderedSlots.Add(slot);

        if (baseData != null && baseData.PartControls != null)
        {
          var baseControl = FindMatchingControl(baseData, key,
            new PartControlData { Name = definition.Name, IsGroup = definition.IsGroup },
            definition.PartName);
          if (baseControl != null)
            slot.Candidates.Add(new VariantPartCandidate
            {
              VariantObject = BaseObject,
              Control = baseControl
            });
        }

        foreach (var variantData in variantDataList)
        {
          if (variantData.VariantObject == BaseObject) continue;
          var variantControl = FindMatchingControl(variantData, key,
            new PartControlData { Name = definition.Name, IsGroup = definition.IsGroup },
            definition.PartName);
          if (variantControl == null)
          {
            var materialVariant = variantData.VariantObject != null
              ? variantData.VariantObject.GetComponent<ACCVariantMaterialOverride>()
              : null;
            if (materialVariant != null && materialVariant.OutfitBase == BaseObject)
            {
              // A material variant reuses the Outfit Base's part hierarchy.
              // Keep it as a candidate; its material curves are scoped to the
              // selected part by the Mixer animation builder.
              variantControl = baseControls.FirstOrDefault(control =>
                GetMixerSlotKey(BaseObject, control) == key ||
                (definition.IsGroup && control.IsGroup &&
                  control.Name == definition.Name) ||
                (!definition.IsGroup && control.Parts != null &&
                  control.Parts.FirstOrDefault()?.name == definition.PartName));
            }
          }
          if (variantControl == null) continue;
          slot.Candidates.Add(new VariantPartCandidate
          {
            VariantObject = variantData.VariantObject,
            Control = variantControl
          });
        }
      }
      // 不按 Parts/Groups 分类排序，也不按字典键排序；严格保持普通模式 PartControls 的顺序。
      return orderedSlots;
    }

    private static PartControlData FindMatchingControl(
      VariantPartData variantData,
      string expectedKey,
      PartControlData normalControl,
      string expectedPartName)
    {
      if (variantData?.PartControls == null) return null;
      return variantData.PartControls.FirstOrDefault(control =>
        GetMixerSlotKey(variantData.VariantObject, control) == expectedKey ||
        (control != null && normalControl != null &&
          control.IsGroup == normalControl.IsGroup &&
          (string.Equals(control.Name, normalControl.Name, System.StringComparison.Ordinal) ||
           (!control.IsGroup &&
            control.Parts != null && normalControl.Parts != null &&
            control.Parts.FirstOrDefault()?.name == expectedPartName))));
    }

    public static string GetMixerSlotKey(GameObject variantObject, PartControlData control)
    {
      if (variantObject == null || control == null || control.Parts == null ||
          control.Parts.Count == 0 || control.Parts[0] == null)
        return "";
      return control.IsGroup
        ? "Groups/" + control.Name
        : "Parts/" + Utils.GetRelativePath(variantObject, control.Parts[0]);
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
    /// <summary>
    /// 为 Mixer 槽位生成独立的 0..N 候选参数。关闭时 Mixer 复用普通部件 Bool 参数，
    /// 以减少同步参数占用；默认关闭。
    /// </summary>
    public bool UseIndependentMixerPartParameters = false;
    public bool EnableParameterCompression = false;
    public bool AutoGenerateMenuIcons = false;
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
    /// 获取实际的生成目录（在 GeneratedFolder 后追加场景、Avatar 和命名空间），
    /// 确保不同 ACC 实例的输出互不覆盖。
    /// </summary>
    public string GetResolvedGeneratedFolder()
    {
      string folder = Utils.NormalizeAssetsFolder(GeneratedFolder);
      string sceneName = CostumesRoot != null && CostumesRoot.scene.IsValid()
        ? (string.IsNullOrWhiteSpace(CostumesRoot.scene.path)
          ? CostumesRoot.scene.name
          : System.IO.Path.GetFileNameWithoutExtension(CostumesRoot.scene.path))
        : "UnsavedScene";
      string sceneIdentity = Utils.SanitizeForFileName(sceneName);
      if (string.IsNullOrWhiteSpace(sceneIdentity)) sceneIdentity = "UnsavedScene";

      var descriptor = CostumesRoot != null
        ? CostumesRoot.GetComponentInParent<VRCAvatarDescriptor>()
        : null;
      var avatarRoot = descriptor != null
        ? descriptor.gameObject
        : CostumesRoot != null ? CostumesRoot.transform.root.gameObject : null;
      string avatarName = avatarRoot != null ? avatarRoot.name : "Avatar";
      string avatarIdentity = Utils.SanitizeForFileName(avatarName);
      if (string.IsNullOrWhiteSpace(avatarIdentity)) avatarIdentity = "Avatar";
      string ns = Utils.SanitizeForFileName(GetGenerationNamespace());
      if (string.IsNullOrWhiteSpace(ns)) ns = DefaultControllerFileName;
      return folder + "/" + sceneIdentity + "/" + avatarIdentity + "/" + ns;
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
