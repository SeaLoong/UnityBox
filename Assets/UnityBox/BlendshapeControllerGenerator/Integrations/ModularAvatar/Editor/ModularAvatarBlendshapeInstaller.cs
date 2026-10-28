using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public sealed class ModularAvatarBlendshapeInstaller : IBlendshapeAvatarInstaller, IBlendshapeAvatarUninstaller
{
  static ModularAvatarBlendshapeInstaller()
  {
    BlendshapeAvatarIntegration.Installer = new ModularAvatarBlendshapeInstaller();
  }

  public void Install(BlendshapeGenerationResult generation)
  {
    Undo.IncrementCurrentGroup();
    int undoGroup = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Install blendshape controls");
    var avatarRoot = generation.Root;
    var controller = generation.Controller;
    var menuIdentity = generation.MenuIdentity;
    var displayName = generation.DisplayName;
    var top = FindDirectChild(avatarRoot.transform, "Blendshapes Menu");
    if (top == null) top = CreateChild(avatarRoot.transform, "Blendshapes Menu").transform;
    EnsureComponent<ModularAvatarMenuInstaller>(top.gameObject);
    ConfigureSubmenu(top.gameObject, "Blendshapes");

    var existing = FindDirectChild(top, menuIdentity);
    if (existing == null)
    {
      // Recognize a menu from the old name-only generator by its animation target,
      // so the first regeneration does not leave two active controllers behind.
      var legacy = FindDirectChild(top, displayName);
      var oldMerge = legacy != null ? legacy.GetComponent<ModularAvatarMergeAnimator>() : null;
      var targetPaths = GetBlendshapeBindingPaths(controller);
      if (oldMerge != null && targetPaths.Count > 0 &&
          targetPaths.SetEquals(GetBlendshapeBindingPaths(oldMerge.animator)))
        existing = legacy;
    }

    var menu = CreateChild(top, menuIdentity + " (building)");
    try
    {
      ConfigureSubmenu(menu, displayName);
      var parameters = Undo.AddComponent<ModularAvatarParameters>(menu);
      // Reserve directories first: shapes "Face" and "Face/Smile" must coexist.
      foreach (var control in generation.Controls)
        EnsureMenuDirectories(menu.transform, MenuSegments(control.Name).SkipLast(1));
      foreach (var control in generation.Controls)
      {
        string paramName = control.ParameterName;
        var parts = MenuSegments(control.Name);
        var parent = EnsureMenuDirectories(menu.transform, parts.SkipLast(1));
        var node = CreateUniqueChild(parent, parts.LastOrDefault() ?? control.ParameterName);
        var item = Undo.AddComponent<ModularAvatarMenuItem>(node);
        bool isBool = control.IsBoolean;
        item.label = parts.LastOrDefault() ?? control.Name;
        item.PortableControl.Type = isBool ? PortableControlType.Toggle : PortableControlType.RadialPuppet;
        item.PortableControl.Parameter = isBool ? paramName : "";
        item.PortableControl.SubParameters = isBool
          ? ImmutableList<string>.Empty : ImmutableList.Create(paramName);
        item.PortableControl.Value = isBool ? 1 : 0;
        item.automaticValue = false;
        item.isSaved = true;
        item.isSynced = true;
        item.isDefault = false;
        EditorUtility.SetDirty(item);
        parameters.parameters.Add(new ParameterConfig
        {
          nameOrPrefix = paramName,
          remapTo = "",
          internalParameter = false,
          isPrefix = false,
          syncType = isBool ? ParameterSyncType.Bool : ParameterSyncType.Float,
          localOnly = false,
          defaultValue = 0,
          saved = true,
          hasExplicitDefaultValue = true
        });
      }
      var merge = Undo.AddComponent<ModularAvatarMergeAnimator>(menu);
      merge.animator = controller;
      merge.pathMode = MergeAnimatorPathMode.Relative;
      merge.relativePathRoot = new AvatarObjectReference();
      merge.relativePathRoot.Set(avatarRoot);
      merge.mergeAnimatorMode = MergeAnimatorMode.Append;
      merge.layerType = VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX;
      merge.matchAvatarWriteDefaults = true;
      EditorUtility.SetDirty(merge);
      EditorUtility.SetDirty(parameters);

      if (existing != null)
      {
        RemoveLegacyParameters(top.gameObject, existing, menu.transform);
        Undo.DestroyObjectImmediate(existing.gameObject);
      }
      menu.name = menuIdentity;
      var parameterConflicts = new List<string>();
      var provided = nadena.dev.ndmf.ParameterInfo.ForUI.GetParametersForObject(avatarRoot,
        (type, first, second) => parameterConflicts.Add(first.EffectiveName)).ToArray();
      var ownNames = new HashSet<string>(generation.Controls.Select(control => control.ParameterName), StringComparer.Ordinal);
      var ownConflicts = parameterConflicts.Where(ownNames.Contains).Distinct().ToArray();
      if (ownConflicts.Length > 0)
        throw new InvalidOperationException("Blendshape parameter types conflict with existing controls: " + string.Join(", ", ownConflicts));
      int parameterCost = provided.Sum(parameter => parameter.BitUsage);
      int maximum = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.MAX_PARAMETER_COST;
      if (parameterCost > maximum)
        throw new InvalidOperationException($"Expression parameter budget exceeded: {parameterCost}/{maximum} bits. Change selected Float controls to Bool or remove unused controls.");
      Debug.Log($"[BlendshapeControllerGenerator] Expression parameter budget: {parameterCost}/{maximum} bits.");
      Undo.CollapseUndoOperations(undoGroup);
      PrefabUtility.RecordPrefabInstancePropertyModifications(top.gameObject);
      UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(avatarRoot.scene);
    }
    catch
    {
      Undo.RevertAllDownToGroup(undoGroup);
      throw;
    }
  }

  public void Uninstall(GameObject root, IReadOnlyList<string> menuIdentities)
  {
    var top = FindDirectChild(root.transform, "Blendshapes Menu");
    if (top == null) return;
    Undo.IncrementCurrentGroup();
    int group = Undo.GetCurrentGroup();
    Undo.SetCurrentGroupName("Remove selected blendshape controls");
    foreach (string identity in menuIdentities.Distinct())
    {
      var menu = FindDirectChild(top, identity);
      if (menu == null || menu.GetComponent<ModularAvatarMergeAnimator>() == null) continue;
      RemoveLegacyParameters(top.gameObject, menu, null);
      Undo.DestroyObjectImmediate(menu.gameObject);
    }
    if (top.childCount == 0) Undo.DestroyObjectImmediate(top.gameObject);
    Undo.CollapseUndoOperations(group);
    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
  }

  private static HashSet<string> GetBlendshapeBindingPaths(RuntimeAnimatorController controller)
  {
    return controller == null ? new HashSet<string>() : new HashSet<string>(controller.animationClips
      .SelectMany(AnimationUtility.GetCurveBindings)
      .Where(binding => binding.type == typeof(SkinnedMeshRenderer) &&
        binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
      .Select(binding => binding.path));
  }

  private static void RemoveLegacyParameters(GameObject top, Transform oldMenu, Transform replacement)
  {
    var legacyParameters = top.GetComponent<ModularAvatarParameters>();
    var merge = oldMenu.GetComponent<ModularAvatarMergeAnimator>();
    var controller = merge != null ? merge.animator as AnimatorController : null;
    if (legacyParameters == null || controller == null) return;
    var names = new HashSet<string>(controller.parameters.Select(parameter => parameter.name));
    // Keep parameters still consumed by other generated controllers.
    foreach (Transform sibling in top.transform)
    {
      if (sibling == oldMenu || sibling == replacement) continue;
      var other = sibling.GetComponent<ModularAvatarMergeAnimator>();
      if (other != null && other.animator is AnimatorController otherController)
        names.ExceptWith(otherController.parameters.Select(parameter => parameter.name));
    }
    Undo.RecordObject(legacyParameters, "Migrate blendshape parameter declarations");
    legacyParameters.parameters.RemoveAll(parameter => !parameter.isPrefix && names.Contains(parameter.nameOrPrefix));
    EditorUtility.SetDirty(legacyParameters);
    PrefabUtility.RecordPrefabInstancePropertyModifications(legacyParameters);
  }

  private static T EnsureComponent<T>(GameObject target) where T : Component
  {
    return target.GetComponent<T>() ?? Undo.AddComponent<T>(target);
  }

  private static void ConfigureSubmenu(GameObject target, string label)
  {
    var item = EnsureComponent<ModularAvatarMenuItem>(target);
    Undo.RecordObject(item, "Configure blendshape submenu");
    item.label = label;
    item.PortableControl.Type = PortableControlType.SubMenu;
    item.PortableControl.Parameter = "";
    item.PortableControl.SubParameters = ImmutableList<string>.Empty;
    item.automaticValue = false;
    item.MenuSource = SubmenuSource.Children;
    EditorUtility.SetDirty(item);
    PrefabUtility.RecordPrefabInstancePropertyModifications(item);
  }

  private static Transform EnsureMenuDirectories(Transform parent, IEnumerable<string> parts)
  {
    foreach (var part in parts)
    {
      var child = FindDirectChild(parent, part);
      if (child == null) child = CreateChild(parent, part).transform;
      ConfigureSubmenu(child.gameObject, part);
      parent = child;
    }
    return parent;
  }

  private static string[] MenuSegments(string name)
  {
    return name.Split('/').Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
  }

  private static Transform FindDirectChild(Transform parent, string name)
  {
    foreach (Transform child in parent)
      if (child.name == name) return child;
    return null;
  }

  private static GameObject CreateChild(Transform parent, string name)
  {
    var result = new GameObject(name);
    Undo.RegisterCreatedObjectUndo(result, "Create blendshape menu");
    result.transform.SetParent(parent, false);
    return result;
  }

  private static GameObject CreateUniqueChild(Transform parent, string name)
  {
    string candidate = name;
    for (int i = 2; FindDirectChild(parent, candidate) != null; i++)
      candidate = name + " (" + i + ")";
    return CreateChild(parent, candidate);
  }

}
