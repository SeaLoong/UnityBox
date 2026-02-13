using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 动画构建器 — 负责创建所有 AnimationClip 和 AnimatorController 结构
  /// </summary>
  public class AnimationBuilder
  {
    private readonly ACCConfig config;
    private readonly GameObject costumesRoot;

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

      // 添加服装参数
      controller.AddParameter(config.CostumeParamName, AnimatorControllerParameterType.Int);

      // 添加部件参数
      if (config.EnableParts)
      {
        foreach (var outfit in outfits)
        {
          foreach (var part in outfit.Parts)
          {
            string partParamName = GetPartParamName(outfit, part);
            controller.AddParameter(partParamName, AnimatorControllerParameterType.Float);
          }
        }
      }

      // 添加 CustomMixer 参数
      if (config.EnableCustomMixer)
      {
        AddCustomMixerParameters(controller, outfits);
      }

      // 创建服装切换层
      CreateOutfitSwitchingLayer(controller, outfits, outfitIndexMap, defaultOutfit);

      // 创建部件相关层
      if (config.EnableParts)
      {
        CreatePartsInitLayer(controller, outfits);
        CreatePartsControlLayer(controller, outfits);
      }

      // 创建 CustomMixer 动画层
      if (config.EnableCustomMixer)
      {
        CreateCustomMixerLayers(controller, outfits, outfitIndexMap);
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

      AnimatorState defaultState = null;
      foreach (var obj in allObjects)
      {
        int index = outfitIndexMap[obj];
        var state = layer.stateMachine.AddState(obj.name, new Vector3(300, 50 + index * 60, 0));

        var clip = CreateOutfitSwitchClip(outfits, allObjects, obj, index);
        state.motion = clip;
        state.writeDefaultValues = true;

        if (defaultOutfit != null && obj == defaultOutfit.BaseObject)
          defaultState = state;

        var transition = layer.stateMachine.AddAnyStateTransition(state);
        transition.AddCondition(AnimatorConditionMode.Equals, index, config.CostumeParamName);
        transition.duration = 0;
        transition.hasExitTime = false;
      }

      if (defaultState != null)
        layer.stateMachine.defaultState = defaultState;

      controller.AddLayer(layer);
    }

    private AnimationClip CreateOutfitSwitchClip(
      List<OutfitData> outfits,
      List<GameObject> allObjects,
      GameObject activeObject,
      int index)
    {
      string animFolder = EnsureAnimFolder();
      string sanitizedName = Utils.SanitizeForFileName(activeObject.name);
      string animPath = Path.Combine(animFolder, $"Outfit_{index:D3}_{sanitizedName}.anim").Replace("\\", "/");

      var clip = CreateBaseClip();
      var activeOutfit = outfits.FirstOrDefault(o => o.GetAllObjects().Contains(activeObject));

      foreach (var obj in allObjects)
      {
        bool active = obj == activeObject;

        // 如果 activeObject 是变体，也要启用其本体
        if (!active && activeOutfit != null &&
            obj == activeOutfit.BaseObject &&
            activeOutfit.Variants.Contains(activeObject))
        {
          active = true;
        }

        var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    #endregion

    #region 部件层

    private void CreatePartsInitLayer(AnimatorController controller, List<OutfitData> outfits)
    {
      var layer = CreateLayer("Parts Init", controller);

      string animFolder = EnsureAnimFolder();
      string animPath = Path.Combine(animFolder, "PartsInit_OFF.anim").Replace("\\", "/");
      var clip = CreateBaseClip();

      // 为所有部件设置初始 OFF 状态
      foreach (var outfit in outfits)
      {
        foreach (var part in outfit.Parts)
        {
          var curve = AnimationCurve.Constant(0, 1f / 60f, 0f);
          string path = Utils.GetRelativePath(costumesRoot, part);
          clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
        }
      }

      AssetDatabase.CreateAsset(clip, animPath);

      var state = layer.stateMachine.AddState("Init", new Vector3(300, 50, 0));
      state.motion = clip;
      state.writeDefaultValues = true;
      layer.stateMachine.defaultState = state;

      controller.AddLayer(layer);
    }

    private void CreatePartsControlLayer(AnimatorController controller, List<OutfitData> outfits)
    {
      if (!outfits.Any(o => o.Parts.Count > 0)) return;

      var layer = CreateLayer("Parts Control", controller);

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
        foreach (var part in outfit.Parts)
        {
          string partParamName = GetPartParamName(outfit, part);
          var onClip = CreatePartToggleClip(part, true, partParamName);

          children.Add(new ChildMotion
          {
            motion = onClip,
            directBlendParameter = partParamName,
            timeScale = 1f,
            mirror = false,
            cycleOffset = 0f
          });
        }
      }
      blendTree.children = children.ToArray();

      var state = layer.stateMachine.AddState("Parts", new Vector3(300, 50, 0));
      state.motion = blendTree;
      state.writeDefaultValues = true;
      layer.stateMachine.defaultState = state;

      controller.AddLayer(layer);
    }

    private AnimationClip CreatePartToggleClip(GameObject part, bool active, string paramName)
    {
      string animFolder = EnsureAnimFolder("Parts");
      string sanitized = Utils.SanitizeForFileName(paramName);
      string animPath = Path.Combine(animFolder, $"{sanitized}_{(active ? "ON" : "OFF")}.anim").Replace("\\", "/");

      var clip = CreateBaseClip();
      var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
      string path = Utils.GetRelativePath(costumesRoot, part);
      clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    #endregion

    #region CustomMixer 动画层

    /// <summary>
    /// 为 CustomMixer 添加参数到 AnimatorController
    /// </summary>
    private void AddCustomMixerParameters(AnimatorController controller, List<OutfitData> outfits)
    {
      // CustomMixer 各部件的独立参数
      foreach (var outfit in outfits)
      {
        foreach (var part in outfit.Parts)
        {
          string partParamName = GetMixerPartParamName(outfit, part);
          // 避免重复添加
          if (!controller.parameters.Any(p => p.name == partParamName))
            controller.AddParameter(partParamName, AnimatorControllerParameterType.Float);
        }

        // 如果有变体，添加变体组参数
        if (outfit.HasVariants())
        {
          string variantParamName = GetMixerVariantGroupParamName(outfit);
          if (!controller.parameters.Any(p => p.name == variantParamName))
            controller.AddParameter(variantParamName, AnimatorControllerParameterType.Int);
        }
      }
    }

    /// <summary>
    /// 创建 CustomMixer 的动画层：
    /// 1. 变体组切换层（通过 Int 参数）
    /// 2. 部件控制层（Direct BlendTree）
    /// 这些层只有在 costume == customMixerIndex 时才激活
    /// </summary>
    private void CreateCustomMixerLayers(
      AnimatorController controller,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      int customMixerIndex = outfitIndexMap.Count; // CustomMixer 使用最后一个索引

      // 变体组切换层：每个有变体的 outfit 一个层（用完整路径去重，避免同名冲突）
      var processedGroups = new HashSet<string>();
      foreach (var outfit in outfits)
      {
        if (!outfit.HasVariants()) continue;

        string groupKey = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
        if (processedGroups.Contains(groupKey)) continue;
        processedGroups.Add(groupKey);

        CreateMixerVariantLayer(controller, outfit, customMixerIndex);
      }

      // 部件控制层
      if (outfits.Any(o => o.Parts.Count > 0))
      {
        CreateMixerPartsControlLayer(controller, outfits, customMixerIndex);
      }
    }

    /// <summary>
    /// 创建混搭模式的变体切换层
    /// 当 costume == customMixerIndex 时，用变体组参数控制显示哪个变体
    /// </summary>
    private void CreateMixerVariantLayer(
      AnimatorController controller,
      OutfitData outfit,
      int customMixerIndex)
    {
      string layerName = $"Mixer_{outfit.OutfitObject.name}";
      var layer = CreateLayer(layerName, controller);

      string variantParam = GetMixerVariantGroupParamName(outfit);
      var allVariants = outfit.GetAllObjects();

      // 默认关闭状态
      var offState = layer.stateMachine.AddState("Off", new Vector3(300, 50, 0));
      var offClip = CreateMixerVariantClip(outfit, null, "Off");
      offState.motion = offClip;
      offState.writeDefaultValues = true;
      layer.stateMachine.defaultState = offState;

      // 为每个变体创建状态
      for (int i = 0; i < allVariants.Count; i++)
      {
        var variant = allVariants[i];
        var state = layer.stateMachine.AddState(variant.name, new Vector3(300, 120 + i * 60, 0));

        var clip = CreateMixerVariantClip(outfit, variant, variant.name);
        state.motion = clip;
        state.writeDefaultValues = true;

        // AnyState → 变体：当 costume == customMixerIndex 且 variantParam == i
        var anyTrans = layer.stateMachine.AddAnyStateTransition(state);
        anyTrans.AddCondition(AnimatorConditionMode.Equals, customMixerIndex, config.CostumeParamName);
        anyTrans.AddCondition(AnimatorConditionMode.Equals, i, variantParam);
        anyTrans.duration = 0;
        anyTrans.hasExitTime = false;
      }

      // 当 costume != customMixerIndex 时回到 Off
      var exitTrans = layer.stateMachine.AddAnyStateTransition(offState);
      exitTrans.AddCondition(AnimatorConditionMode.NotEqual, customMixerIndex, config.CostumeParamName);
      exitTrans.duration = 0;
      exitTrans.hasExitTime = false;

      controller.AddLayer(layer);
    }

    private AnimationClip CreateMixerVariantClip(OutfitData outfit, GameObject activeVariant, string label)
    {
      string animFolder = EnsureAnimFolder("Mixer");
      string safeName = Utils.SanitizeForFileName($"{outfit.OutfitObject.name}_{label}");
      string animPath = Path.Combine(animFolder, $"MixerVariant_{safeName}.anim").Replace("\\", "/");

      var clip = CreateBaseClip();

      foreach (var obj in outfit.GetAllObjects())
      {
        bool active = (activeVariant != null && obj == activeVariant);
        // 如果激活的是变体，本体也要激活
        if (!active && activeVariant != null &&
            obj == outfit.BaseObject &&
            outfit.Variants.Contains(activeVariant))
        {
          active = true;
        }

        var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    /// <summary>
    /// 创建混搭模式的部件控制层（Direct BlendTree）
    /// 使用独立于普通部件的参数名
    /// </summary>
    private void CreateMixerPartsControlLayer(
      AnimatorController controller,
      List<OutfitData> outfits,
      int customMixerIndex)
    {
      var layer = CreateLayer("Mixer Parts", controller);

      var blendTree = new BlendTree
      {
        name = "MixerParts",
        blendType = BlendTreeType.Direct,
        hideFlags = HideFlags.HideInHierarchy
      };
      AssetDatabase.AddObjectToAsset(blendTree, controller);

      var children = new List<ChildMotion>();
      foreach (var outfit in outfits)
      {
        foreach (var part in outfit.Parts)
        {
          string partParamName = GetMixerPartParamName(outfit, part);
          var onClip = CreatePartToggleClip(part, true, $"Mixer_{partParamName}");

          children.Add(new ChildMotion
          {
            motion = onClip,
            directBlendParameter = partParamName,
            timeScale = 1f,
            mirror = false,
            cycleOffset = 0f
          });
        }
      }

      if (children.Count == 0) return;

      blendTree.children = children.ToArray();

      // 默认状态：Off（非混搭模式时停留在此空状态）
      var offState = layer.stateMachine.AddState("Off", new Vector3(300, 50, 0));
      offState.writeDefaultValues = true;
      layer.stateMachine.defaultState = offState;

      // 激活状态：仅在 costume == customMixerIndex 时进入 BlendTree
      var activeState = layer.stateMachine.AddState("MixerParts", new Vector3(300, 150, 0));
      activeState.motion = blendTree;
      activeState.writeDefaultValues = true;

      // Off → Active: costume == customMixerIndex
      var transIn = offState.AddTransition(activeState);
      transIn.AddCondition(AnimatorConditionMode.Equals, customMixerIndex, config.CostumeParamName);
      transIn.duration = 0;
      transIn.hasExitTime = false;

      // Active → Off: costume != customMixerIndex
      var transOut = activeState.AddTransition(offState);
      transOut.AddCondition(AnimatorConditionMode.NotEqual, customMixerIndex, config.CostumeParamName);
      transOut.duration = 0;
      transOut.hasExitTime = false;

      controller.AddLayer(layer);
    }

    #endregion

    #region 辅助方法

    /// <summary>获取部件参数名（普通模式）</summary>
    public string GetPartParamName(OutfitData outfit, GameObject part)
    {
      string partRelPath = Utils.GetRelativePath(outfit.BaseObject, part);
      return Utils.BuildParamName(config.ParamPrefix, outfit.RelativePath + "/" + partRelPath);
    }

    /// <summary>获取混搭模式下的部件参数名</summary>
    public string GetMixerPartParamName(OutfitData outfit, GameObject part)
    {
      string partRelPath = Utils.GetRelativePath(outfit.BaseObject, part);
      string outfitRelPath = outfit.RelativePath;
      return Utils.BuildParamName(config.ParamPrefix,
        config.CustomMixerName + "/" + outfitRelPath + "/" + partRelPath);
    }

    /// <summary>获取混搭模式下的变体组参数名</summary>
    public string GetMixerVariantGroupParamName(OutfitData outfit)
    {
      string groupRelPath = Utils.GetRelativePath(config.CostumesRoot, outfit.OutfitObject);
      return Utils.BuildParamName(config.ParamPrefix,
        config.CustomMixerName + "/" + groupRelPath);
    }

    private AnimatorControllerLayer CreateLayer(string name, AnimatorController controller)
    {
      var layer = new AnimatorControllerLayer
      {
        name = name,
        defaultWeight = 1f,
        stateMachine = new AnimatorStateMachine()
      };
      layer.stateMachine.name = name;
      layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
      AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
      return layer;
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
      string folder = Path.Combine(config.GeneratedFolder, "Animations");
      if (!string.IsNullOrEmpty(subfolder))
        folder = Path.Combine(folder, subfolder);
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
