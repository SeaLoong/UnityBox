using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 动画构建器 — 负责创建所有 AnimationClip 和 AnimatorController 结构
  /// </summary>
  public class AnimationBuilder
  {
    private const string IsLocalParameter = "IsLocal";
    private const string AlwaysOneParameterSuffix = "Internal/AlwaysOne";
    private const float ChoiceValueTolerance = 0.25f;
    private readonly ACCConfig config;
    private readonly GameObject costumesRoot;

    /// <summary>
    /// 一个可压缩的离散选择域。每个域仍有独立的参数、状态与 AnyState 条件；
    /// 仅 Animator Layer 由所有域共享。
    /// </summary>
    private sealed class ChoiceCompressionDomain
    {
      public string Label;
      public string LocalParameter;
      public ChoiceParameterLayout Layout;
      public List<int> Values;
    }

    public AnimationBuilder(ACCConfig config)
    {
      this.config = config;
      this.costumesRoot = config.CostumesRoot;
    }

    /// <summary>
    /// 创建完整的 AnimatorController
    /// </summary>
    public AnimatorController CreateController(
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData defaultOutfit,
      string path)
    {
      var controller = AnimatorController.CreateAnimatorControllerAtPath(path);

      if (config.EnableParts)
        EnsureAlwaysOneParameter(controller);

      var mainLayout = Generator.GetMainChoiceLayout(outfitIndexMap, config.EnableCustomMixer,
        config.EnableParameterCompression);
      int defaultIndex = Generator.ResolveDefaultChoiceIndex(defaultOutfit, outfitIndexMap);
      AddChoiceParameters(controller, config.MainParameterName, mainLayout, defaultIndex);
      var compressionDomains = new List<ChoiceCompressionDomain>();
      AddChoiceCompressionDomain(compressionDomains, "Outfit Selection",
        config.MainParameterName, mainLayout, GetMainChoiceValues(outfitIndexMap));

      // 添加部件参数
      if (config.EnableParts)
      {
        foreach (var outfit in outfits)
        {
          foreach (var control in outfit.GetPartControls())
          {
            string partParamName = GetPartParamName(outfit, control);
            controller.AddParameter(partParamName, AnimatorControllerParameterType.Float);
          }
        }
      }

      // 添加 CustomMixer 参数
      if (config.EnableCustomMixer)
      {
        AddCustomMixerParameters(controller, outfits, outfitIndexMap, defaultOutfit,
          compressionDomains);
      }

      CreateSharedChoiceCompressionLayer(controller, compressionDomains);

      // 创建服装切换层
      CreateOutfitSwitchingLayer(controller, outfits, outfitIndexMap, defaultOutfit);

      // 创建部件相关层
      if (config.EnableParts)
      {
          CreatePartsControlLayer(controller, outfits);
      }

      AssetDatabase.SaveAssets();
      return controller;
    }

    #region 服装切换层

    private void CreateOutfitSwitchingLayer(
      AnimatorController controller,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData defaultOutfit)
    {
      var layer = CreateLayer("Outfit Switching", controller);
      var allObjects = outfitIndexMap.OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
      var blendTree = new BlendTree
      {
        name = "Outfit Switching",
        blendType = BlendTreeType.Simple1D,
        blendParameter = config.MainParameterName,
        useAutomaticThresholds = false,
        hideFlags = HideFlags.HideInHierarchy
      };
      AssetDatabase.AddObjectToAsset(blendTree, controller);
      var children = new List<ChildMotion>();
      foreach (var obj in allObjects)
      {
        int index = outfitIndexMap[obj];
        var clip = CreateOutfitSwitchClip(outfits, allObjects, obj, index);
        children.Add(new ChildMotion
        {
          motion = clip,
          threshold = index,
          timeScale = 1f,
          mirror = false,
          cycleOffset = 0f
        });
      }

      if (config.EnableCustomMixer)
      {
        int customMixerValue = Generator.GetCustomMixerValue(outfitIndexMap.Count);
        children.Add(new ChildMotion
        {
          motion = CreateCustomMixerEntryClip(outfits, allObjects, customMixerValue,
            defaultOutfit, outfitIndexMap),
          threshold = customMixerValue,
          timeScale = 1f,
          mirror = false,
          cycleOffset = 0f
        });
      }

      blendTree.children = children.ToArray();
      var state = layer.stateMachine.AddState("Outfit Switching", new Vector3(300, 50, 0));
      state.motion = blendTree;
      state.writeDefaultValues = true;
      layer.stateMachine.defaultState = state;

      AddLayer(controller, layer);
    }

    private AnimationClip CreateOutfitSwitchClip(
      List<OutfitData> outfits,
      List<GameObject> allObjects,
      GameObject activeObject,
      int index)
    {
      string animFolder = EnsureAnimFolder();
      string sanitizedName = activeObject != null
        ? Utils.SanitizeForFileName(activeObject.name)
        : "None";
      string animPath = Utils.CombineAssetPath(animFolder, $"Outfit_{index:D3}_{sanitizedName}.anim");

      var clip = CreateBaseClip();
      var activeOutfit = outfits.FirstOrDefault(o => o.GetAllObjects().Contains(activeObject));
      var activeMaterialVariant = activeObject != null
        ? activeObject.GetComponent<ACCVariantMaterialOverride>()
        : null;
      var objectsToAnimate = allObjects
        .Concat(outfits.Select(outfit => outfit.BaseObject))
        .Distinct();

      foreach (var obj in objectsToAnimate)
      {
        // 完整预制件转换成材质变体后只作为材质对照来源，运行时不激活其网格。
        bool active = obj == activeObject && activeMaterialVariant == null;

        // 变体可能依赖本体下的共享部件，因此选择任意变体时保持本体活动。
        if (!active && activeOutfit != null &&
            obj == activeOutfit.BaseObject &&
            (activeOutfit.Variants.Contains(activeObject) ||
             (activeMaterialVariant != null && activeMaterialVariant.OutfitBase == obj)))
        {
          active = true;
        }

        var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      WriteMaterialVariantCurves(clip, outfits, activeObject);

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    private AnimationClip CreateCustomMixerEntryClip(
      List<OutfitData> outfits,
      List<GameObject> allObjects,
      int index,
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      string animFolder = EnsureAnimFolder();
      string animPath = Utils.CombineAssetPath(animFolder, $"Outfit_{index:D3}_CustomMixer.anim");
      var clip = CreateBaseClip();
      var mixerObjects = new HashSet<GameObject>(
        outfits.SelectMany(outfit => outfit.GetAllObjects()));
      var objectsToAnimate = allObjects
        .Concat(mixerObjects)
        .Concat(outfits.Select(outfit => outfit.OutfitObject))
        .Where(obj => obj != null)
        .Distinct();

      var defaultChoice = Generator.ResolveDefaultChoiceObject(defaultOutfit, outfitIndexMap);
      bool defaultChoiceIsVariant = defaultOutfit != null && defaultChoice != null &&
        defaultOutfit.Variants.Contains(defaultChoice);
      var defaultMaterialMarker = defaultChoice != null
        ? defaultChoice.GetComponent<ACCVariantMaterialOverride>()
        : null;

      // 混搭入口先关闭非默认服装组，同时保留默认服装组与其默认选择对象。
      // 槽位子树随后依据与普通 Parts 相同的默认参数值，决定受控部件的 On/Off。
      foreach (var obj in objectsToAnimate)
      {
        bool active = defaultOutfit != null &&
          (obj == defaultOutfit.OutfitObject ||
           (obj == defaultChoice && defaultMaterialMarker == null) ||
           (defaultChoiceIsVariant && obj == defaultOutfit.BaseObject));
        var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      if (defaultOutfit != null)
      {
        var controlledParts = new HashSet<GameObject>(defaultOutfit.GetMixerPartSlots()
          .SelectMany(slot => slot.Candidates)
          .SelectMany(candidate => candidate.Control.Parts));
        var allParts = defaultOutfit.Parts
          .Concat((defaultOutfit.VariantPartData ?? new List<VariantPartData>())
            .SelectMany(data => data.Parts))
          .Where(part => part != null)
          .Distinct();
        var activeCurve = AnimationCurve.Constant(0, 1f / 60f, 1f);
        foreach (var part in allParts)
        {
          if (controlledParts.Contains(part)) continue;
          string path = Utils.GetRelativePath(costumesRoot, part);
          clip.SetCurve(path, typeof(GameObject), "m_IsActive", activeCurve);
        }
      }

      WriteMaterialVariantCurves(clip, outfits, defaultChoice);
      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    #endregion

    #region 部件层

    private void CreatePartsControlLayer(
      AnimatorController controller,
      List<OutfitData> outfits)
    {
      bool hasNormalParts = outfits.Any(o => o.GetPartControls().Count > 0);
      bool hasMixerParts = config.EnableCustomMixer && outfits
        .GroupBy(outfit => outfit.OutfitObject)
        .Any(group => group.First().GetMixerPartSlots().Count > 0);
      if (!hasNormalParts && !hasMixerParts) return;

      var layer = CreateLayer("Parts Control", controller);

      var normalTree = hasNormalParts ? CreateNormalPartsBlendTree(controller, outfits) : null;
      var mixerTree = hasMixerParts ? CreateMixerPartsBlendTree(controller, outfits) : null;

      AnimatorState normalState = null;
      if (normalTree != null)
      {
        normalState = layer.stateMachine.AddState("Normal", new Vector3(300, 50, 0));
        normalState.motion = normalTree;
        normalState.writeDefaultValues = true;
        layer.stateMachine.defaultState = normalState;
      }

      if (mixerTree != null)
      {
        var mixerState = layer.stateMachine.AddState("Mixer", new Vector3(300, 150, 0));
        mixerState.motion = mixerTree;
        mixerState.writeDefaultValues = true;

        int mixerValue = Generator.GetCustomMixerValue(
          outfits.SelectMany(outfit => outfit.GetAllObjects()).Distinct().Count());
        var toMixer = CreateAnyStateTransition(layer.stateMachine, mixerState);
        AddChoiceValueConditions(toMixer, config.MainParameterName, mixerValue);

        if (normalState != null)
        {
          AddNotChoiceTransitions(layer.stateMachine, normalState,
            config.MainParameterName, mixerValue);
        }
        else
        {
          layer.stateMachine.defaultState = mixerState;
        }
      }

      AddLayer(controller, layer);
    }

    private BlendTree CreateNormalPartsBlendTree(
      AnimatorController controller,
      IEnumerable<OutfitData> outfits)
    {
      var blendTree = new BlendTree
      {
        name = "Parts",
        blendType = BlendTreeType.Direct,
        hideFlags = HideFlags.HideInHierarchy
      };
      AssetDatabase.AddObjectToAsset(blendTree, controller);

      var children = new List<ChildMotion>();
      foreach (var outfit in outfits)
      {
        foreach (var control in outfit.GetPartControls())
        {
          string partParamName = GetPartParamName(outfit, control);
          var partTree = new BlendTree
          {
            name = "Part " + control.Name,
            blendType = BlendTreeType.Simple1D,
            blendParameter = partParamName,
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
          };
          AssetDatabase.AddObjectToAsset(partTree, controller);
          partTree.children = new[]
          {
            new ChildMotion
            {
              motion = CreatePartToggleClip(control.Parts, false, partParamName),
              threshold = 0f,
              timeScale = 1f,
              mirror = false,
              cycleOffset = 0f
            },
            new ChildMotion
            {
              motion = CreatePartToggleClip(control.Parts, true, partParamName),
              threshold = 1f,
              timeScale = 1f,
              mirror = false,
              cycleOffset = 0f
            }
          };
          children.Add(new ChildMotion
          {
            motion = partTree,
            directBlendParameter = GetAlwaysOneParameterName(),
            timeScale = 1f,
            mirror = false,
            cycleOffset = 0f
          });
        }
      }
      blendTree.children = children.ToArray();
      return blendTree;
    }

    private BlendTree CreateMixerPartsBlendTree(
      AnimatorController controller,
      IEnumerable<OutfitData> outfits)
    {
      var blendTree = new BlendTree
      {
        name = "Mixer Parts",
        blendType = BlendTreeType.Direct,
        hideFlags = HideFlags.HideInHierarchy
      };
      AssetDatabase.AddObjectToAsset(blendTree, controller);

      var children = new List<ChildMotion>();
      var processedOutfitObjects = new HashSet<GameObject>();
      foreach (var outfit in outfits)
      {
        if (!processedOutfitObjects.Add(outfit.OutfitObject)) continue;
        var slots = outfit.GetMixerPartSlots();
        if (slots.Count == 0) continue;
        var activationClip = CreateMixerOutfitActivationClip(outfit,
          Utils.GetRelativePath(costumesRoot, outfit.OutfitObject));
        foreach (var slot in slots)
        {
          var slotTree = CreateMixerSlotBlendTree(controller, outfit, slot);
          string slotParam = Mixer.BuildMixerSlotParamName(config, outfit, slot);
          children.Add(new ChildMotion
          {
            motion = CreateMixerOutfitActivationBlendTree(controller, activationClip,
              slotParam, Utils.GetRelativePath(costumesRoot, outfit.OutfitObject) + "_" + slot.Key),
            directBlendParameter = GetAlwaysOneParameterName(),
            timeScale = 1f,
            mirror = false,
            cycleOffset = 0f
          });
          children.Add(new ChildMotion
          {
            motion = slotTree,
            directBlendParameter = GetAlwaysOneParameterName(),
            timeScale = 1f,
            mirror = false,
            cycleOffset = 0f
          });
        }
      }
      blendTree.children = children.ToArray();
      return blendTree;
    }

    /// <summary>
    /// 槽位值是离散选择序号，不能直接用作 Direct BlendTree 权重：值 2、3… 会把
    /// 根对象激活 Clip 加权到大于 1。改用 Simple1D 门控后，0 不输出激活曲线，任意
    /// 正值都会在阈值 1 钳制为同一份激活 Clip。
    /// </summary>
    private BlendTree CreateMixerOutfitActivationBlendTree(
      AnimatorController controller,
      AnimationClip activationClip,
      string slotParameter,
      string label)
    {
      var blendTree = new BlendTree
      {
        name = "Mixer Activate " + label,
        blendType = BlendTreeType.Simple1D,
        blendParameter = slotParameter,
        useAutomaticThresholds = false,
        hideFlags = HideFlags.HideInHierarchy
      };
      AssetDatabase.AddObjectToAsset(blendTree, controller);
      blendTree.children = new[]
      {
        new ChildMotion
        {
          motion = CreateEmptyMixerActivationClip(label),
          threshold = 0f,
          timeScale = 1f,
          mirror = false,
          cycleOffset = 0f
        },
        new ChildMotion
        {
          motion = activationClip,
          threshold = 1f,
          timeScale = 1f,
          mirror = false,
          cycleOffset = 0f
        }
      };
      return blendTree;
    }

    private AnimationClip CreateEmptyMixerActivationClip(string label)
    {
      string animFolder = EnsureAnimFolder("Mixer");
      string safeName = Utils.SanitizeForFileName(label);
      string animPath = Utils.CombineAssetPath(animFolder, $"MixerOutfit_{safeName}_Inactive.anim");
      var clip = CreateBaseClip();
      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    private AnimationClip CreatePartToggleClip(IEnumerable<GameObject> parts, bool active, string paramName)
    {
      string animFolder = EnsureAnimFolder("Parts");
      string sanitized = Utils.SanitizeForFileName(paramName);
      string animPath = Utils.CombineAssetPath(animFolder, $"{sanitized}_{(active ? "ON" : "OFF")}.anim");

      var clip = CreateBaseClip();
      var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
      foreach (var part in parts)
      {
        string path = Utils.GetRelativePath(costumesRoot, part);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    #endregion

    #region 材质变体曲线

    /// <summary>
    /// 将所有带标记服装先还原为原材质，再对当前变体应用替换。
    /// 曲线被写入服装/变体切换片段，避免额外生成一个材质动画层。
    /// </summary>
    private void WriteMaterialVariantCurves(
      AnimationClip clip,
      IEnumerable<OutfitData> outfits,
      GameObject activeObject)
    {
      foreach (var marker in outfits
        .SelectMany(outfit => outfit.GetAllObjects())
        .Select(obj => obj.GetComponent<ACCVariantMaterialOverride>())
        .Where(marker => marker != null && marker.OutfitBase != null))
      {
        WriteRendererMaterials(clip, marker.OutfitBase, null);
      }

      var activeMarker = activeObject != null
        ? activeObject.GetComponent<ACCVariantMaterialOverride>()
        : null;
      if (activeMarker != null && activeMarker.OutfitBase != null)
        WriteRendererMaterials(clip, activeMarker.OutfitBase, activeMarker);
    }

    private void WriteRendererMaterials(
      AnimationClip clip,
      GameObject outfitBase,
      ACCVariantMaterialOverride marker)
    {
      foreach (var renderer in outfitBase.GetComponentsInChildren<Renderer>(true))
      {
        var materials = renderer.sharedMaterials;
        for (int slot = 0; slot < materials.Length; slot++)
        {
          string path = Utils.GetRelativePath(costumesRoot, renderer.gameObject);
          var material = materials[slot];
          if (marker != null)
          {
            var rendererOverride = marker.RendererOverrides?.FirstOrDefault(entry =>
              entry != null && entry.TargetRenderer == renderer &&
              entry.MaterialSlot == slot && entry.Replacement != null);
            if (rendererOverride != null)
            {
              material = rendererOverride.Replacement;
            }
            else
            {
              var replacement = marker.Replacements?.FirstOrDefault(entry =>
                entry != null && entry.Source == material);
              if (replacement != null && replacement.Replacement != null)
                material = replacement.Replacement;
            }
          }

          var binding = EditorCurveBinding.PPtrCurve(path, typeof(Renderer),
            $"m_Materials.Array.data[{slot}]");
          AnimationUtility.SetObjectReferenceCurve(clip, binding,
            new[] { new ObjectReferenceKeyframe { time = 0f, value = material } });
        }
      }
    }

    #endregion

    #region CustomMixer 动画层

    /// <summary>
    /// 为 CustomMixer 添加参数到 AnimatorController
    /// </summary>
    private void AddCustomMixerParameters(
      AnimatorController controller,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap,
      OutfitData defaultOutfit,
      List<ChoiceCompressionDomain> compressionDomains)
    {
      // CustomMixer 各部件的独立参数
      var processedOutfitObjects = new HashSet<GameObject>();
      foreach (var outfit in outfits)
      {
        if (!processedOutfitObjects.Add(outfit.OutfitObject)) continue;
        foreach (var slot in outfit.GetMixerPartSlots())
        {
          string slotParamName = Mixer.BuildMixerSlotParamName(config, outfit, slot);
          var slotLayout = new ChoiceParameterLayout(slot.Candidates.Count + 1,
            config.EnableParameterCompression);
          int slotDefaultValue = Generator.GetMixerSlotDefaultValue(defaultOutfit,
            outfitIndexMap, outfit, slot);
          AddChoiceParameters(controller, slotParamName, slotLayout, slotDefaultValue);
          AddChoiceCompressionDomain(compressionDomains, "Mixer " + slotParamName,
            slotParamName, slotLayout, Enumerable.Range(0, slot.Candidates.Count + 1));
        }
      }
    }

    private AnimationClip CreateMixerOutfitActivationClip(OutfitData outfit, string label)
    {
      string animFolder = EnsureAnimFolder("Mixer");
      string safeName = Utils.SanitizeForFileName(label);
      string animPath = Utils.CombineAssetPath(animFolder, $"MixerOutfit_{safeName}.anim");
      var clip = CreateBaseClip();
      var objects = new[] { outfit.OutfitObject }
        .Concat(outfit.GetAllObjects())
        .Where(obj => obj != null &&
          obj.GetComponent<ACCVariantMaterialOverride>() == null)
        .Distinct();
      var controlledParts = new HashSet<GameObject>(outfit.GetMixerPartSlots()
        .SelectMany(slot => slot.Candidates)
        .SelectMany(candidate => candidate.Control.Parts));
      var allParts = outfit.Parts
        .Concat((outfit.VariantPartData ?? new List<VariantPartData>())
          .SelectMany(data => data.Parts))
        .Where(part => part != null)
        .Distinct();
      var activeCurve = AnimationCurve.Constant(0, 1f / 60f, 1f);
      foreach (var obj in objects)
      {
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", activeCurve);
      }
      foreach (var part in allParts)
      {
        // Mixer 槽位候选完全由对应的 Simple1D 子树负责；这里不再写入 0，
        // 避免同一 DirectBlendTree 中激活 Clip 与槽位 Clip 重复绑定并被 AAO 重新加权。
        if (controlledParts.Contains(part)) continue;
        string path = Utils.GetRelativePath(costumesRoot, part);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", activeCurve);
      }
      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    private BlendTree CreateMixerSlotBlendTree(
      AnimatorController controller,
      OutfitData outfit,
      MixerPartSlot slot)
    {
      string groupPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
      string slotParam = Mixer.BuildMixerSlotParamName(config, outfit, slot);
      var allCandidateParts = slot.Candidates
        .SelectMany(candidate => candidate.Control.Parts)
        .Distinct()
        .ToList();

      var blendTree = new BlendTree
      {
        name = "Mixer " + slot.Key,
        blendType = BlendTreeType.Simple1D,
        blendParameter = slotParam,
        useAutomaticThresholds = false,
        hideFlags = HideFlags.HideInHierarchy
      };
      AssetDatabase.AddObjectToAsset(blendTree, controller);
      var children = new List<ChildMotion>
      {
        new ChildMotion
        {
          motion = CreateMixerSlotClip(allCandidateParts, null,
            groupPath + "_" + slot.Key + "_Off"),
          threshold = 0f,
          timeScale = 1f,
          mirror = false,
          cycleOffset = 0f
        }
      };
      for (int i = 0; i < slot.Candidates.Count; i++)
      {
        var candidate = slot.Candidates[i];
        children.Add(new ChildMotion
        {
          motion = CreateMixerSlotClip(allCandidateParts, candidate.Control.Parts,
            groupPath + "_" + slot.Key + "_" + i),
          threshold = i + 1,
          timeScale = 1f,
          mirror = false,
          cycleOffset = 0f
        });
      }
      blendTree.children = children.ToArray();
      return blendTree;
    }

    private AnimationClip CreateMixerSlotClip(
      List<GameObject> allParts,
      List<GameObject> activeParts,
      string label)
    {
      string animFolder = EnsureAnimFolder("Mixer");
      string safeName = Utils.SanitizeForFileName(label);
      string animPath = Utils.CombineAssetPath(animFolder, $"MixerVariant_{safeName}.anim");

      var clip = CreateBaseClip();
      var activeSet = activeParts != null
        ? new HashSet<GameObject>(activeParts)
        : new HashSet<GameObject>();
      foreach (var part in allParts)
      {
        var curve = AnimationCurve.Constant(0, 1f / 60f,
          activeSet.Contains(part) ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, part);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    #endregion

    #region 辅助方法

    /// <summary>获取部件参数名（普通模式）</summary>
    public string GetPartParamName(OutfitData outfit, PartControlData control)
    {
      return Utils.BuildParamName(config.MainParameterName,
        outfit.RelativePath + "/" + GetPartControlKey(outfit, control));
    }

    private static string GetPartControlKey(OutfitData outfit, PartControlData control)
    {
      if (control.IsGroup)
        return "Groups/" + control.Name;
      return "Parts/" + Utils.GetRelativePath(outfit.BaseObject, control.Parts[0]);
    }

    private static int ResolveDefaultIndex(OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      if (defaultOutfit == null) return 0;
      return defaultOutfit.GetAllObjects()
        .Where(outfitIndexMap.ContainsKey)
        .Select(obj => outfitIndexMap[obj])
        .DefaultIfEmpty(0)
        .First();
    }

    private static void AddChoiceParameters(AnimatorController controller, string baseParameterName,
      ChoiceParameterLayout layout, int defaultChoiceIndex)
    {
      if (!controller.parameters.Any(parameter => parameter.name == baseParameterName))
      {
        controller.AddParameter(new AnimatorControllerParameter
        {
          name = baseParameterName,
          // VRCFury 会将非 Float 的 BlendTree 输入替换为 Controller 中的第一个 Float；
          // MA MMD Relay 注入时该 Float 可能正是 __MA/Internal/MMDNotActive。表达式菜单
          // 参数仍保持 Int，只有 Animator 内部读取选择值时使用 Float。
          type = AnimatorControllerParameterType.Float,
          defaultFloat = defaultChoiceIndex
        });
      }
      if (!layout.UsesCompression || layout.BitCount == 0) return;

      for (int i = 0; i < layout.BitCount; i++)
      {
        string parameterName = layout.GetBitParameterName(baseParameterName, i);
        if (!controller.parameters.Any(parameter => parameter.name == parameterName))
        {
          controller.AddParameter(new AnimatorControllerParameter
          {
            name = parameterName,
            type = AnimatorControllerParameterType.Bool,
            defaultBool = (defaultChoiceIndex & (1 << i)) != 0
          });
        }
      }
    }

    private static AnimatorStateTransition CreateAnyStateTransition(AnimatorStateMachine stateMachine,
      AnimatorState state, bool canTransitionToSelf = false)
    {
      var transition = stateMachine.AddAnyStateTransition(state);
      transition.duration = 0;
      transition.hasExitTime = false;
      transition.canTransitionToSelf = canTransitionToSelf;
      return transition;
    }

    /// <summary>为 Animator Float 选择值添加稳定的离散值匹配区间。</summary>
    private static void AddChoiceValueConditions(AnimatorStateTransition transition,
      string parameterName, int choiceIndex)
    {
      transition.AddCondition(AnimatorConditionMode.Greater,
        choiceIndex - ChoiceValueTolerance, parameterName);
      transition.AddCondition(AnimatorConditionMode.Less,
        choiceIndex + ChoiceValueTolerance, parameterName);
    }

    /// <summary>
    /// Animator Float 没有精确 NotEqual 条件，使用低于/高于目标选择区间的两条
    /// AnyState Transition 表达“不等于”。
    /// </summary>
    private static void AddNotChoiceTransitions(AnimatorStateMachine stateMachine,
      AnimatorState targetState, string parameterName, int choiceIndex)
    {
      var below = CreateAnyStateTransition(stateMachine, targetState);
      below.AddCondition(AnimatorConditionMode.Less,
        choiceIndex - ChoiceValueTolerance, parameterName);

      var above = CreateAnyStateTransition(stateMachine, targetState);
      above.AddCondition(AnimatorConditionMode.Greater,
        choiceIndex + ChoiceValueTolerance, parameterName);
    }

    /// <summary>
    /// 注册一个需要压缩的选择域。状态会由共享压缩 Layer 统一承载，但每个域仍保留
    /// 自己的参数、Driver 与 AnyState 条件，因此多个域可在相邻帧独立触发。
    /// </summary>
    private static void AddChoiceCompressionDomain(
      List<ChoiceCompressionDomain> domains,
      string label,
      string localParameter,
      ChoiceParameterLayout layout,
      IEnumerable<int> choiceValues)
    {
      if (!layout.UsesCompression || layout.BitCount == 0) return;

      var values = choiceValues.Distinct().ToList();
      if (values.Count != layout.ChoiceCount)
        throw new System.ArgumentException("Choice values must match the parameter layout.");

      domains.Add(new ChoiceCompressionDomain
      {
        Label = label,
        LocalParameter = localParameter,
        Layout = layout,
        Values = values
      });
    }

    /// <summary>
    /// 创建一个共享的压缩事件分发 Layer。每个状态只在进入时读写自己所属域的参数；
    /// 所有转换均从 AnyState 出发并带有域专属条件，当前状态不需要表示所有域的
    /// 笛卡尔积组合。
    /// </summary>
    private void CreateSharedChoiceCompressionLayer(
      AnimatorController controller,
      List<ChoiceCompressionDomain> domains)
    {
      if (domains.Count == 0) return;

      EnsureIsLocalParameter(controller);
      var compressionLayer = CreateLayer("Parameter Compression", controller);
      // localOnly=false means the Driver may run on both local and remote
      // avatars, not remote-only. Keep the default state free of Drivers so
      // loading the controller cannot overwrite saved/local parameter values.
      var idleState = compressionLayer.stateMachine.AddState("Idle",
        new Vector3(300, 0, 0));
      idleState.writeDefaultValues = false;
      compressionLayer.stateMachine.defaultState = idleState;

      foreach (var domain in domains)
      {
        for (int encodedValue = 0; encodedValue < domain.Values.Count; encodedValue++)
        {
          int localValue = domain.Values[encodedValue];

          var encodeState = compressionLayer.stateMachine.AddState(
            domain.Label + " = " + localValue,
            new Vector3(300, compressionLayer.stateMachine.states.Length * 50, 0));
          // 该层只运行 Parameter Driver，不应借由 WD 重置任意动画绑定。
          encodeState.writeDefaultValues = false;

          var compressDriver = encodeState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
          compressDriver.localOnly = true;
          compressDriver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
          for (int bit = 0; bit < domain.Layout.BitCount; bit++)
          {
            compressDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
              name = domain.Layout.GetBitParameterName(domain.LocalParameter, bit),
              value = (encodedValue & (1 << bit)) != 0 ? 1 : 0,
              type = VRC_AvatarParameterDriver.ChangeType.Set
            });
          }

          // Keep the global decoder in a separate state. On a local avatar a
          // global Driver is also eligible to run, so sharing this state would
          // make the local/remote split implicit and fragile.
          var decodeState = compressionLayer.stateMachine.AddState(
            domain.Label + " = " + localValue + " (Remote)",
            new Vector3(550, compressionLayer.stateMachine.states.Length * 50, 0));
          decodeState.writeDefaultValues = false;

          var decompressDriver = decodeState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
          decompressDriver.localOnly = false;
          decompressDriver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
          {
            new VRC_AvatarParameterDriver.Parameter
            {
              name = domain.LocalParameter,
              value = localValue,
              type = VRC_AvatarParameterDriver.ChangeType.Set
            }
          };

          for (int bit = 0; bit < domain.Layout.BitCount; bit++)
          {
            var encodeTransition = CreateAnyStateTransition(compressionLayer.stateMachine, encodeState,
              canTransitionToSelf: true);
            AddChoiceValueConditions(encodeTransition, domain.LocalParameter, localValue);
            encodeTransition.AddCondition(AnimatorConditionMode.If, 0, IsLocalParameter);
            encodeTransition.AddCondition(
              (encodedValue & (1 << bit)) != 0
                ? AnimatorConditionMode.IfNot
                : AnimatorConditionMode.If,
              0, domain.Layout.GetBitParameterName(domain.LocalParameter, bit));
          }

          AddDecodeTransition(compressionLayer.stateMachine, decodeState, domain.LocalParameter,
            domain.Layout, encodedValue, AnimatorConditionMode.Less,
            localValue - ChoiceValueTolerance);
          AddDecodeTransition(compressionLayer.stateMachine, decodeState, domain.LocalParameter,
            domain.Layout, encodedValue, AnimatorConditionMode.Greater,
            localValue + ChoiceValueTolerance);
        }
      }

      AddLayer(controller, compressionLayer);
    }

    private static void AddDecodeTransition(
      AnimatorStateMachine stateMachine,
      AnimatorState targetState,
      string localParameter,
      ChoiceParameterLayout layout,
      int encodedValue,
      AnimatorConditionMode localMismatchMode,
      float localMismatchThreshold)
    {
      var decodeTransition = CreateAnyStateTransition(stateMachine, targetState,
        canTransitionToSelf: true);
      decodeTransition.AddCondition(AnimatorConditionMode.IfNot, 0, IsLocalParameter);
      decodeTransition.AddCondition(localMismatchMode, localMismatchThreshold, localParameter);
      for (int bit = 0; bit < layout.BitCount; bit++)
      {
        decodeTransition.AddCondition(
          (encodedValue & (1 << bit)) != 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
          0, layout.GetBitParameterName(localParameter, bit));
      }
    }

    private static void EnsureIsLocalParameter(AnimatorController controller)
    {
      if (!controller.parameters.Any(parameter => parameter.name == IsLocalParameter))
        controller.AddParameter(IsLocalParameter, AnimatorControllerParameterType.Bool);
    }

    private string GetAlwaysOneParameterName()
    {
      return Utils.BuildParamName(config.MainParameterName, AlwaysOneParameterSuffix);
    }

    private void EnsureAlwaysOneParameter(AnimatorController controller)
    {
      string parameterName = GetAlwaysOneParameterName();
      if (controller.parameters.Any(parameter => parameter.name == parameterName)) return;

      controller.AddParameter(new AnimatorControllerParameter
      {
        name = parameterName,
        type = AnimatorControllerParameterType.Float,
        defaultFloat = 1f
      });
    }

    private IEnumerable<int> GetMainChoiceValues(Dictionary<GameObject, int> outfitIndexMap)
    {
      var values = outfitIndexMap.Values.OrderBy(value => value).ToList();
      if (config.EnableCustomMixer)
        values.Add(Generator.GetCustomMixerValue(outfitIndexMap.Count));
      return values;
    }

    private AnimatorControllerLayer CreateLayer(string name, AnimatorController controller)
    {
      string namespaceLabel = Utils.SanitizeForFileName(config.GetGenerationNamespace());
      string layerName = string.IsNullOrEmpty(namespaceLabel)
        ? name : namespaceLabel + "/" + name;

      // Unity 的 Controller 首层具有特殊权重语义，MA 的 MMD Relay 也会依据 FX
      // 的原始前几层调整控制层。不要把 ACC 的选择树复用到默认 Base Layer，避免
      // 后续构建链把服装参数与 MMD Relay 的内部参数混入同一个首层对象。
      var layer = new AnimatorControllerLayer
      {
        name = layerName,
        defaultWeight = 1f,
        stateMachine = new AnimatorStateMachine()
      };
      layer.stateMachine.name = layer.name;
      layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
      AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
      return layer;
    }

    private static void AddLayer(AnimatorController controller, AnimatorControllerLayer layer)
    {
      var layers = controller.layers;
      for (int i = 0; i < layers.Length; i++)
      {
        if (layers[i].stateMachine != layer.stateMachine) continue;
        layers[i] = layer;
        controller.layers = layers;
        return;
      }
      controller.AddLayer(layer);
    }

    private AnimationClip CreateBaseClip()
    {
      var clip = new AnimationClip { legacy = false, wrapMode = WrapMode.Once };
      var settings = AnimationUtility.GetAnimationClipSettings(clip);
      settings.loopTime = false;
      AnimationUtility.SetAnimationClipSettings(clip, settings);
      return clip;
    }

    private string EnsureAnimFolder(string subfolder = null)
    {
      string folder = Utils.CombineAssetPath(config.GetResolvedGeneratedFolder(), "Animations");
      if (!string.IsNullOrEmpty(subfolder))
        folder = Utils.CombineAssetPath(folder, subfolder);
      if (!Directory.Exists(folder))
      {
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
      }
      return folder;
    }

    #endregion
  }
}
