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
        int customMixerIndex = ACCConfig.CustomMixerIndex;
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
      var mixerObjects = new HashSet<GameObject>(
        outfits.SelectMany(outfit => outfit.GetAllObjects()));
      var objectsToAnimate = allObjects.Concat(mixerObjects).Distinct();
      var mixerControlledParts = new HashSet<GameObject>(
        outfits.SelectMany(outfit => outfit.GetMixerPartSlots())
          .SelectMany(slot => slot.Candidates)
          .SelectMany(candidate => candidate.Control.Parts));

      // 混搭时所有 Base/Variant 容器保持激活，但普通模式分组对应的部件先全部关闭；
      // 槽位层随后只打开用户选中的候选部件。
      foreach (var obj in objectsToAnimate)
      {
        bool active = mixerObjects.Contains(obj) && !mixerControlledParts.Contains(obj);
        var curve = AnimationCurve.Constant(0, 1f / 60f, active ? 1f : 0f);
        string path = Utils.GetRelativePath(costumesRoot, obj);
        clip.SetCurve(path, typeof(GameObject), "m_IsActive", curve);
      }

      foreach (var part in mixerControlledParts)
      {
        var curve = AnimationCurve.Constant(0, 1f / 60f, 0f);
        string path = Utils.GetRelativePath(costumesRoot, part);
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
          foreach (var slot in outfit.GetMixerPartSlots())
          {
            string slotParamName = Mixer.BuildMixerSlotParamName(config, outfit, slot);
            if (!controller.parameters.Any(p => p.name == slotParamName))
              controller.AddParameter(slotParamName, AnimatorControllerParameterType.Int);
          }
        }
    }

    /// <summary>
    /// 创建 CustomMixer 的动画层：每个服装组的每个部件槽位一个 Int 状态层。
    /// 这些层只有在主 Parameter Prefix == customMixerIndex 时才写入部件曲线。
    /// </summary>
    private void CreateCustomMixerLayers(
      AnimatorController controller,
      List<OutfitData> outfits,
      Dictionary<GameObject, int> outfitIndexMap)
    {
      int customMixerIndex = GetCustomMixerIndex(outfits);

      foreach (var outfit in outfits)
      {
        foreach (var slot in outfit.GetMixerPartSlots())
          CreateMixerSlotLayer(controller, outfit, slot, customMixerIndex);
      }
    }

    /// <summary>
    /// 创建混搭模式的部件槽位层。
    /// 同一槽位的候选部件由一个 Int 参数互斥选择。
    /// </summary>
    private void CreateMixerSlotLayer(
      AnimatorController controller,
      OutfitData outfit,
      MixerPartSlot slot,
      int customMixerIndex)
    {
      string groupPath = Utils.GetRelativePath(costumesRoot, outfit.OutfitObject);
      string layerName = "Mixer_" + Utils.SanitizeForFileName(groupPath + "_" + slot.Key);
      var layer = CreateLayer(layerName, controller);

      string slotParam = Mixer.BuildMixerSlotParamName(config, outfit, slot);
      var allCandidateParts = slot.Candidates
        .SelectMany(candidate => candidate.Control.Parts)
        .Distinct()
        .ToList();

      var inactiveState = layer.stateMachine.AddState("Inactive", new Vector3(80, 50, 0));
      inactiveState.writeDefaultValues = false;
      layer.stateMachine.defaultState = inactiveState;

      var offState = layer.stateMachine.AddState("MixerOff", new Vector3(300, 50, 0));
      offState.motion = CreateMixerSlotClip(allCandidateParts, null,
        groupPath + "_" + slot.Key + "_Off");
      offState.writeDefaultValues = true;

      var enterOff = inactiveState.AddTransition(offState);
      enterOff.AddCondition(AnimatorConditionMode.Equals,
        customMixerIndex, config.MainParameterName);
      enterOff.AddCondition(AnimatorConditionMode.Equals, 0, slotParam);
      enterOff.duration = 0;
      enterOff.hasExitTime = false;

      for (int i = 0; i < slot.Candidates.Count; i++)
      {
        var candidate = slot.Candidates[i];
        var state = layer.stateMachine.AddState(
          candidate.VariantObject.name + "_" + candidate.Control.Name,
          new Vector3(300, 120 + i * 60, 0));
        state.motion = CreateMixerSlotClip(allCandidateParts, candidate.Control.Parts,
          groupPath + "_" + slot.Key + "_" + i);
        state.writeDefaultValues = true;

        var anyTrans = layer.stateMachine.AddAnyStateTransition(state);
        anyTrans.AddCondition(AnimatorConditionMode.Equals,
          customMixerIndex, config.MainParameterName);
        anyTrans.AddCondition(AnimatorConditionMode.Equals, i + 1, slotParam);
        anyTrans.duration = 0;
        anyTrans.hasExitTime = false;
      }

      var exitTrans = layer.stateMachine.AddAnyStateTransition(inactiveState);
      exitTrans.AddCondition(AnimatorConditionMode.NotEqual,
        customMixerIndex, config.MainParameterName);
      exitTrans.duration = 0;
      exitTrans.hasExitTime = false;

      var mixerOffTrans = layer.stateMachine.AddAnyStateTransition(offState);
      mixerOffTrans.AddCondition(AnimatorConditionMode.Equals,
        customMixerIndex, config.MainParameterName);
      mixerOffTrans.AddCondition(AnimatorConditionMode.Equals, 0, slotParam);
      mixerOffTrans.duration = 0;
      mixerOffTrans.hasExitTime = false;

      controller.AddLayer(layer);
    }

    private AnimationClip CreateMixerSlotClip(
      List<GameObject> allParts,
      List<GameObject> activeParts,
      string label)
    {
      string animFolder = EnsureAnimFolder("Mixer");
      string safeName = Utils.SanitizeForFileName(label);
      string animPath = Path.Combine(animFolder, $"MixerVariant_{safeName}.anim").Replace("\\", "/");

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
      return ACCConfig.CustomMixerIndex;
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
