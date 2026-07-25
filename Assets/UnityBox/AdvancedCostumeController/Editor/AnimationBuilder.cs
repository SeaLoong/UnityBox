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
        controller.AddParameter(config.MainParameterName, AnimatorControllerParameterType.Int);

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
      var defaultObject = ResolveDefaultObject(defaultOutfit, outfitIndexMap);

      AnimatorState defaultState = null;
      AnimatorState firstState = null;
      foreach (var obj in allObjects)
      {
        int index = outfitIndexMap[obj];
        var state = layer.stateMachine.AddState(obj.name, new Vector3(300, 50 + index * 60, 0));

        var clip = CreateOutfitSwitchClip(outfits, allObjects, obj, index);
        state.motion = clip;
        state.writeDefaultValues = true;
        firstState ??= state;

        if (obj == defaultObject)
          defaultState = state;

        var transition = layer.stateMachine.AddAnyStateTransition(state);
          transition.AddCondition(AnimatorConditionMode.Equals, index, config.MainParameterName);
        transition.duration = 0;
        transition.hasExitTime = false;
      }

      layer.stateMachine.defaultState = defaultState ?? firstState;

      if (config.EnableCustomMixer)
      {
        int customMixerIndex = outfitIndexMap.Count;
        var mixerState = layer.stateMachine.AddState("Custom Mixer",
          new Vector3(300, 50 + customMixerIndex * 60, 0));
        mixerState.motion = CreateCustomMixerEntryClip(outfits, allObjects, customMixerIndex);
        mixerState.writeDefaultValues = true;

        var mixerTransition = layer.stateMachine.AddAnyStateTransition(mixerState);
        mixerTransition.AddCondition(AnimatorConditionMode.Equals,
           customMixerIndex, config.MainParameterName);
        mixerTransition.duration = 0;
        mixerTransition.hasExitTime = false;
      }

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
      var objectsToAnimate = allObjects
        .Concat(outfits.Select(outfit => outfit.BaseObject))
        .Distinct();

      foreach (var obj in objectsToAnimate)
      {
        bool active = obj == activeObject;

        // 变体可能依赖本体下的共享部件，因此选择任意变体时保持本体活动。
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

      WriteMaterialVariantCurves(clip, outfits, activeObject);

      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    private AnimationClip CreateCustomMixerEntryClip(
      List<OutfitData> outfits,
      List<GameObject> allObjects,
      int index)
    {
      string animFolder = EnsureAnimFolder();
      string animPath = Path.Combine(animFolder, $"Outfit_{index:D3}_CustomMixer.anim")
        .Replace("\\", "/");
      var clip = CreateBaseClip();
      var baseObjects = new HashSet<GameObject>(outfits.Select(outfit => outfit.BaseObject));
      var objectsToAnimate = allObjects.Concat(baseObjects).Distinct();

      // 混搭时所有服装本体必须保持激活，Mixer Parts 才能控制其下部件。
      foreach (var obj in objectsToAnimate)
      {
        bool active = baseObjects.Contains(obj);
        var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      WriteMaterialVariantCurves(clip, outfits, null);
      AssetDatabase.CreateAsset(clip, animPath);
      return clip;
    }

    private static GameObject ResolveDefaultObject(
      OutfitData defaultOutfit,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      if (defaultOutfit == null) return null;
      if (outfitIndexMap.ContainsKey(defaultOutfit.BaseObject))
        return defaultOutfit.BaseObject;

      return defaultOutfit.GetAllObjects()
        .FirstOrDefault(outfitIndexMap.ContainsKey);
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
      if (!outfits.Any(o => o.GetPartControls().Count > 0)) return;

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
        foreach (var control in outfit.GetPartControls())
        {
          string partParamName = GetPartParamName(outfit, control);
          var onClip = CreatePartToggleClip(control.Parts, true, partParamName);

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

      if (config.EnableCustomMixer)
      {
        int customMixerIndex = GetCustomMixerIndex(outfits);
        var offState = layer.stateMachine.AddState("Off", new Vector3(300, 150, 0));
        offState.writeDefaultValues = false;

        var toOff = layer.stateMachine.AddAnyStateTransition(offState);
        toOff.AddCondition(AnimatorConditionMode.Equals, customMixerIndex,
           config.MainParameterName);
        toOff.duration = 0;
        toOff.hasExitTime = false;

        var toParts = offState.AddTransition(state);
        toParts.AddCondition(AnimatorConditionMode.NotEqual, customMixerIndex,
           config.MainParameterName);
        toParts.duration = 0;
        toParts.hasExitTime = false;
      }

      controller.AddLayer(layer);
    }

    private AnimationClip CreatePartToggleClip(IEnumerable<GameObject> parts, bool active, string paramName)
    {
      string animFolder = EnsureAnimFolder("Parts");
      string sanitized = Utils.SanitizeForFileName(paramName);
      string animPath = Path.Combine(animFolder, $"{sanitized}_{(active ? "ON" : "OFF")}.anim").Replace("\\", "/");

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
            var replacement = marker.Replacements.FirstOrDefault(entry =>
              entry != null && entry.Source == material);
            if (replacement != null && replacement.Replacement != null)
              material = replacement.Replacement;
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
    private void AddCustomMixerParameters(AnimatorController controller, List<OutfitData> outfits)
    {
      // CustomMixer 各部件的独立参数
      foreach (var outfit in outfits)
      {
        foreach (var control in outfit.GetPartControls())
        {
          string partParamName = GetMixerPartParamName(outfit, control);
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
    /// 这些层只有在主 Parameter Prefix == customMixerIndex 时才激活
    /// </summary>
    private void CreateCustomMixerLayers(
      AnimatorController controller,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      int customMixerIndex = GetCustomMixerIndex(outfits);

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
      if (outfits.Any(o => o.GetPartControls().Count > 0))
      {
        CreateMixerPartsControlLayer(controller, outfits, customMixerIndex);
      }
    }

    /// <summary>
    /// 创建混搭模式的变体切换层
    /// 当主 Parameter Prefix == customMixerIndex 时，用变体组参数控制显示哪个变体
    /// </summary>
    private void CreateMixerVariantLayer(
      AnimatorController controller,
      OutfitData outfit,
      int customMixerIndex)
    {
      string groupPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
      string layerName = "Mixer_" + Utils.SanitizeForFileName(groupPath);
      var layer = CreateLayer(layerName, controller);

      string variantParam = GetMixerVariantGroupParamName(outfit);
      var allVariants = outfit.GetAllObjects();

      // 默认关闭状态
      var offState = layer.stateMachine.AddState("Off", new Vector3(300, 50, 0));
      // 非混搭模式不写任何对象曲线，避免覆盖 Outfit Switching 的正常结果。
      offState.writeDefaultValues = false;
      layer.stateMachine.defaultState = offState;

      // 为每个变体创建状态
      for (int i = 0; i < allVariants.Count; i++)
      {
        var variant = allVariants[i];
        var state = layer.stateMachine.AddState(variant.name, new Vector3(300, 120 + i * 60, 0));

        var clip = CreateMixerVariantClip(outfit, variant, variant.name);
        state.motion = clip;
        state.writeDefaultValues = true;

        // AnyState → 变体：当主 Parameter Prefix == customMixerIndex 且 variantParam == i
        var anyTrans = layer.stateMachine.AddAnyStateTransition(state);
          anyTrans.AddCondition(AnimatorConditionMode.Equals, customMixerIndex, config.MainParameterName);
        anyTrans.AddCondition(AnimatorConditionMode.Equals, i, variantParam);
        anyTrans.duration = 0;
        anyTrans.hasExitTime = false;
      }

      // 当 costume != customMixerIndex 时回到 Off
      var exitTrans = layer.stateMachine.AddAnyStateTransition(offState);
        exitTrans.AddCondition(AnimatorConditionMode.NotEqual, customMixerIndex, config.MainParameterName);
      exitTrans.duration = 0;
      exitTrans.hasExitTime = false;

      controller.AddLayer(layer);
    }

    private AnimationClip CreateMixerVariantClip(OutfitData outfit, GameObject activeVariant, string label)
    {
      string animFolder = EnsureAnimFolder("Mixer");
      string stablePath = activeVariant != null
        ? Utils.GetHierarchyPath(costumesRoot, activeVariant)
        : "Off";
      string safeName = Utils.SanitizeForFileName(
        $"{outfit.OutfitObject.name}_{label}_{stablePath}");
      string animPath = Path.Combine(animFolder, $"MixerVariant_{safeName}.anim").Replace("\\", "/");

      var clip = CreateBaseClip();

      var objectsToAnimate = outfit.GetAllObjects()
        .Append(outfit.BaseObject)
        .Distinct();
      foreach (var obj in objectsToAnimate)
      {
        bool active = (activeVariant != null && obj == activeVariant);
        // 变体可能依赖本体下的共享部件，因此选择任意变体时保持本体活动。
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

      WriteMaterialVariantCurves(clip, new[] { outfit }, activeVariant);

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
        foreach (var control in outfit.GetPartControls())
        {
          string partParamName = GetMixerPartParamName(outfit, control);
          var onClip = CreatePartToggleClip(control.Parts, true, $"Mixer_{partParamName}");

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
      offState.writeDefaultValues = false;
      layer.stateMachine.defaultState = offState;

      // 激活状态：仅在主 Parameter Prefix == customMixerIndex 时进入 BlendTree
      var activeState = layer.stateMachine.AddState("MixerParts", new Vector3(300, 150, 0));
      activeState.motion = blendTree;
      activeState.writeDefaultValues = true;

      // Off → Active: 主 Parameter Prefix == customMixerIndex
      var transIn = offState.AddTransition(activeState);
        transIn.AddCondition(AnimatorConditionMode.Equals, customMixerIndex, config.MainParameterName);
      transIn.duration = 0;
      transIn.hasExitTime = false;

      // Active → Off: costume != customMixerIndex
      var transOut = activeState.AddTransition(offState);
        transOut.AddCondition(AnimatorConditionMode.NotEqual, customMixerIndex, config.MainParameterName);
      transOut.duration = 0;
      transOut.hasExitTime = false;

      controller.AddLayer(layer);
    }

    #endregion

    #region 辅助方法

    /// <summary>获取部件参数名（普通模式）</summary>
    public string GetPartParamName(OutfitData outfit, PartControlData control)
    {
      return Utils.BuildParamName(config.ParamPrefix,
        outfit.RelativePath + "/" + GetPartControlKey(outfit, control));
    }

    /// <summary>获取混搭模式下的部件参数名</summary>
    public string GetMixerPartParamName(OutfitData outfit, PartControlData control)
    {
      string outfitRelPath = outfit.RelativePath;
      return Utils.BuildParamName(config.ParamPrefix,
        config.CustomMixerName + "/" + outfitRelPath + "/" + GetPartControlKey(outfit, control));
    }

    private static string GetPartControlKey(OutfitData outfit, PartControlData control)
    {
      if (control.IsGroup)
        return "Groups/" + control.Name;
      return Utils.GetRelativePath(outfit.BaseObject, control.Parts[0]);
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
      string namespaceLabel = Utils.SanitizeForFileName(config.GetGenerationNamespace());
      var layer = new AnimatorControllerLayer
      {
        name = string.IsNullOrEmpty(namespaceLabel) ? name : namespaceLabel + "/" + name,
        defaultWeight = 1f,
        stateMachine = new AnimatorStateMachine()
      };
      layer.stateMachine.name = layer.name;
      layer.stateMachine.hideFlags = HideFlags.HideInHierarchy;
      AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
      return layer;
    }

    private int GetCustomMixerIndex(IEnumerable<OutfitData> outfits)
    {
      return outfits.SelectMany(outfit => outfit.GetAllObjects()).Distinct().Count();
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
      string folder = Path.Combine(config.GetResolvedGeneratedFolder(), "Animations");
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
