using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;

namespace UnityBox.AdvancedCostumeController
{
  internal sealed class MenuIconRequest
  {
    public GameObject MenuNode;
    public List<GameObject> Targets = new List<GameObject>();
    public ACCVariantMaterialOverride MaterialVariant;
    public string StableKey;
    public bool UseSharedOutfitFraming;
  }

  /// <summary>
  /// Clone-based menu icon renderer. Each request resets the clone, disables every
  /// renderable object, then enables only its target hierarchy and required bones.
  /// </summary>
  internal static class MenuIconGenerator
  {
    private const int IconSize = 256;
    // Same isolation strategy used by ParameterIconGenerator: preserve the
    // original SkinnedMeshRenderer and let Unity render its real skinning and
    // shader path; only the camera layer is changed for the capture.
    private const int CaptureLayer = 31;

    private sealed class ExternalBoneClone
    {
      public GameObject SourceRoot;
      public GameObject CloneRoot;
    }

    internal static void AddRequest(
      ACCConfig config,
      IList<MenuIconRequest> requests,
      GameObject menuNode,
      IEnumerable<GameObject> targets,
      string stableKey,
      ACCVariantMaterialOverride materialVariant = null,
      bool useSharedOutfitFraming = false)
    {
      if (config == null || !config.AutoGenerateMenuIcons || requests == null || menuNode == null)
        return;

      requests.Add(new MenuIconRequest
      {
        MenuNode = menuNode,
        Targets = targets?.Where(target => target != null).Distinct().ToList()
          ?? new List<GameObject>(),
        StableKey = stableKey,
        MaterialVariant = materialVariant,
        UseSharedOutfitFraming = useSharedOutfitFraming
      });
    }

    public static int Generate(
      GameObject costumesRoot,
      GameObject menuRoot,
      string generatedFolder,
      IReadOnlyList<MenuIconRequest> requests)
    {
      if (costumesRoot == null || menuRoot == null || requests == null) return 0;
      var validRequests = requests
        .Where(request => request?.MenuNode != null &&
          request.Targets?.Any(target => target != null) == true)
        .GroupBy(request => request.MenuNode)
        .Select(group => group.First())
        .ToList();
      if (validRequests.Count == 0) return 0;

      var descriptor = costumesRoot.GetComponentInParent<VRCAvatarDescriptor>();
      var sourceAvatarRoot = descriptor != null
        ? descriptor.gameObject
        : costumesRoot.transform.root.gameObject;
      string iconFolder = Utils.CombineAssetPath(generatedFolder, "MenuIcons");
      Directory.CreateDirectory(iconFolder);
      AssetDatabase.Refresh();
      var requestedNodes = new HashSet<GameObject>(validRequests.Select(request => request.MenuNode));
      ClearExistingIcons(requestedNodes);

      Scene previewScene = default;
      try
      {
        previewScene = EditorSceneManager.NewPreviewScene();
        var cloneRoot = UnityEngine.Object.Instantiate(sourceAvatarRoot);
        cloneRoot.name = sourceAvatarRoot.name;
        SceneManager.MoveGameObjectToScene(cloneRoot, previewScene);
        var externalBoneClones = CloneExternalBoneRoots(
          sourceAvatarRoot, cloneRoot, previewScene);
        RemapSkinnedMeshBones(sourceAvatarRoot, cloneRoot, externalBoneClones);
        DisableStateDrivers(cloneRoot);
        foreach (var externalBoneClone in externalBoneClones)
        {
          DisableAllBehaviours(externalBoneClone.CloneRoot);
          DisableRenderers(externalBoneClone.CloneRoot);
        }
        var cloneRenderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
        var originalSharedMaterials = CaptureSharedMaterials(cloneRenderers);

        var cameraObject = new GameObject("ACC Menu Icon Camera");
        SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.scene = previewScene;
        camera.cameraType = CameraType.Preview;
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.allowHDR = false;
        camera.allowMSAA = true;
        camera.useOcclusionCulling = false;
        camera.renderingPath = RenderingPath.Forward;

        var lightObject = new GameObject("ACC Menu Icon Light");
        SceneManager.MoveGameObjectToScene(lightObject, previewScene);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightObject.transform.rotation = Quaternion.Euler(35f, 145f, 0f);

        Bounds? sharedOutfitBounds = CalculateAvatarFramingBounds(cloneRoot);

        int generated = 0;
        var generatedNodes = new HashSet<GameObject>();
        foreach (var request in validRequests)
        {
          // ApplyMaterialVariant mutates the cloned Renderer materials. Restore the
          // clone before every request, otherwise the next material variant is
          // evaluated against the previous variant and all later icons can become
          // identical to the first captured version.
          RestoreSharedMaterials(originalSharedMaterials);
          var targets = request.Targets
            .Where(target => target != null)
            .Select(target => FindClone(sourceAvatarRoot, cloneRoot, target))
            .Where(target => target != null)
            .Distinct()
            .ToList();
          if (targets.Count == 0) continue;

          var visibleRenderers = ConfigureRequestState(cloneRoot, cloneRenderers, targets);
          if (request.MaterialVariant != null)
            ApplyMaterialVariant(sourceAvatarRoot, cloneRoot, request.MaterialVariant);
          if (visibleRenderers.Count == 0)
          {
            Debug.LogWarning($"[ACC] Menu icon skipped (no visible renderer): {request.StableKey}");
            continue;
          }

          Debug.Log($"[ACC/Icon] {request.StableKey}: targets=[" +
            string.Join(", ", targets.Select(target => target.name)) +
            "], renderers=[" +
            string.Join(", ", visibleRenderers.Select(renderer => renderer.name)) + "]");

          // Force the actual SkinnedMeshRenderer bounds to update before using
          // them for framing. No mesh baking or detached MeshRenderer is involved.
          PrepareOriginalRenderers(visibleRenderers);
          Bounds bounds = request.UseSharedOutfitFraming && sharedOutfitBounds.HasValue
            ? sharedOutfitBounds.Value
            : CalculateBounds(visibleRenderers);
          ConfigureCamera(camera, bounds, cloneRoot.transform.forward, cloneRoot.transform.up);

          string safeKey = Utils.SanitizeForFileName(request.StableKey ?? "MenuIcon");
          if (string.IsNullOrWhiteSpace(safeKey)) safeKey = "MenuIcon";
          safeKey += "_" + StableHash(request.StableKey ?? safeKey);
          string assetPath = Utils.CombineAssetPath(iconFolder, safeKey + ".png");
          Debug.Log($"[ACC/Icon] Direct renderer capture: {request.StableKey}; " +
            $"layer={CaptureLayer}, boundsCenter={bounds.center}, boundsSize={bounds.size}");
          RenderIsolatedOriginalRenderers(camera, visibleRenderers, assetPath,
            request.StableKey);
          ConfigureTextureImporter(assetPath);
          var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
          var menuItem = request.MenuNode.GetComponent<ModularAvatarMenuItem>();
          if (texture == null || menuItem == null) continue;
          Undo.RecordObject(menuItem, "Assign ACC generated menu icon");
          menuItem.PortableControl.Icon = texture;
          EditorUtility.SetDirty(menuItem);
          ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { menuItem });
          generatedNodes.Add(request.MenuNode);
          generated++;
        }

        PropagateIconsToSubmenus(menuRoot, generatedNodes);
        AssetDatabase.SaveAssets();
        return generated;
      }
      finally
      {
        if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
      }
    }

    private static void DisableStateDrivers(GameObject cloneRoot)
    {
      // Traverse the hierarchy once instead of running one full hierarchy query
      // for every component type. These components can otherwise alter the clone
      // or render their own content while a request is being baked.
      foreach (var component in cloneRoot.GetComponentsInChildren<Component>(true))
      {
        if (component is Animator animator) animator.enabled = false;
        else if (component is Animation animation) animation.enabled = false;
        else if (component is LODGroup lodGroup) lodGroup.enabled = false;
        else if (component is Camera camera) camera.enabled = false;
        else if (component is Light light) light.enabled = false;
        else if (component is AudioListener listener) listener.enabled = false;
      }
    }

    private static void DisableAllBehaviours(GameObject root)
    {
      foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
        behaviour.enabled = false;
    }

    private static void DisableRenderers(GameObject root)
    {
      foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
      {
        renderer.enabled = false;
        renderer.forceRenderingOff = true;
      }
    }

    private static Dictionary<Renderer, Material[]> CaptureSharedMaterials(
      IReadOnlyList<Renderer> renderers)
    {
      var result = new Dictionary<Renderer, Material[]>();
      foreach (var renderer in renderers)
      {
        if (renderer == null) continue;
        result[renderer] = renderer.sharedMaterials;
      }
      return result;
    }

    private static void RestoreSharedMaterials(
      IReadOnlyDictionary<Renderer, Material[]> originalMaterials)
    {
      foreach (var pair in originalMaterials)
      {
        if (pair.Key == null) continue;
        pair.Key.sharedMaterials = pair.Value != null
          ? (Material[])pair.Value.Clone()
          : Array.Empty<Material>();
      }
    }

    /// <summary>
    /// Costumes can be a separate top-level scene object while their skinned
    /// meshes reference bones from a sibling Avatar. In that layout cloning only
    /// the costumes root leaves every bone reference pointing to the source
    /// Scene. Clone each distinct external root so the renderer can be remapped
    /// without exposing the external Avatar's own renderers.
    /// </summary>
    private static List<ExternalBoneClone> CloneExternalBoneRoots(
      GameObject sourceRoot, GameObject cloneRoot, Scene previewScene)
    {
      var result = new List<ExternalBoneClone>();
      var externalRoots = sourceRoot
        .GetComponentsInChildren<SkinnedMeshRenderer>(true)
        .SelectMany(renderer =>
          (renderer.bones ?? Array.Empty<Transform>())
            .Concat(renderer.rootBone != null
              ? new[] { renderer.rootBone }
              : Array.Empty<Transform>()))
        .Where(bone => bone != null &&
          !bone.IsChildOf(sourceRoot.transform))
        .Select(bone => bone.root.gameObject)
        .Where(root => root != null && root != sourceRoot)
        .Distinct()
        .ToList();

      foreach (var externalRoot in externalRoots)
      {
        var clone = UnityEngine.Object.Instantiate(externalRoot);
        clone.name = externalRoot.name + " (ACC Icon Bones)";
        SceneManager.MoveGameObjectToScene(clone, previewScene);
        result.Add(new ExternalBoneClone
        {
          SourceRoot = externalRoot,
          CloneRoot = clone
        });
      }
      return result;
    }

    /// <summary>
    /// Unity does not always remap a SkinnedMeshRenderer bone reference when the
    /// renderer came from an imported asset but its bones live outside that asset. Such a
    /// reference still points at the source Avatar after Instantiate, so baking
    /// the renderer in a Preview Scene uses the wrong hierarchy (or a hierarchy
    /// in another Scene). Rebuild both arrays from the source hierarchy paths.
    /// </summary>
    private static void RemapSkinnedMeshBones(
      GameObject sourceRoot, GameObject cloneRoot,
      IReadOnlyList<ExternalBoneClone> externalBoneClones)
    {
      int remappedReferences = 0;
      int unresolvedReferences = 0;
      int unmatchedRenderers = 0;
      var sourceRenderers = sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
      var cloneRenderers = cloneRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
      foreach (var sourceRenderer in sourceRenderers)
      {
        var cloneRenderer = FindCloneRenderer(sourceRoot, cloneRoot, sourceRenderer);
        if (cloneRenderer == null)
        {
          unmatchedRenderers++;
          continue;
        }

        var sourceRootBone = sourceRenderer.rootBone;
        if (sourceRootBone != null)
        {
          var cloneRootBone = FindCloneTransform(sourceRoot, cloneRoot,
            sourceRootBone, externalBoneClones);
          if (cloneRootBone != null)
          {
            cloneRenderer.rootBone = cloneRootBone;
            remappedReferences++;
          }
          else
          {
            unresolvedReferences++;
            Debug.LogWarning($"[ACC/Icon] Unresolved root bone for {sourceRenderer.name}: " +
              sourceRootBone.name);
          }
        }

        var sourceBones = sourceRenderer.bones;
        var cloneBones = new Transform[sourceBones.Length];
        for (int i = 0; i < sourceBones.Length; i++)
        {
          var sourceBone = sourceBones[i];
          if (sourceBone == null) continue;
          var cloneBone = FindCloneTransform(sourceRoot, cloneRoot,
            sourceBone, externalBoneClones);
          if (cloneBone == null)
          {
            unresolvedReferences++;
            cloneBones[i] = sourceBone;
            continue;
          }
          cloneBones[i] = cloneBone;
          remappedReferences++;
        }
        cloneRenderer.bones = cloneBones;
      }

      Debug.Log($"[ACC/Icon] Bone remap: sourceRenderers={sourceRenderers.Length}, " +
        $"cloneRenderers={cloneRenderers.Length}, matched={sourceRenderers.Length - unmatchedRenderers}, " +
        $"references={remappedReferences}, unresolved={unresolvedReferences}");
    }

    private static SkinnedMeshRenderer FindCloneRenderer(
      GameObject sourceRoot, GameObject cloneRoot,
      SkinnedMeshRenderer sourceRenderer)
    {
      var cloneObject = FindClone(sourceRoot, cloneRoot, sourceRenderer.gameObject);
      if (cloneObject == null) return null;

      var sourceComponents = sourceRenderer.gameObject
        .GetComponents<SkinnedMeshRenderer>();
      int componentIndex = Array.IndexOf(sourceComponents, sourceRenderer);
      var cloneComponents = cloneObject.GetComponents<SkinnedMeshRenderer>();
      return componentIndex >= 0 && componentIndex < cloneComponents.Length
        ? cloneComponents[componentIndex]
        : null;
    }

    private static Transform FindCloneTransform(
      GameObject sourceRoot,
      GameObject cloneRoot,
      Transform sourceTransform,
      IReadOnlyList<ExternalBoneClone> externalBoneClones)
    {
      if (sourceTransform == null) return null;
      if (sourceTransform == sourceRoot.transform ||
          sourceTransform.IsChildOf(sourceRoot.transform))
      {
        var clone = FindClone(sourceRoot, cloneRoot, sourceTransform.gameObject);
        return clone != null ? clone.transform : null;
      }

      foreach (var externalBoneClone in externalBoneClones)
      {
        if (sourceTransform != externalBoneClone.SourceRoot.transform &&
            !sourceTransform.IsChildOf(externalBoneClone.SourceRoot.transform))
          continue;
        var clone = FindClone(externalBoneClone.SourceRoot,
          externalBoneClone.CloneRoot, sourceTransform.gameObject);
        return clone != null ? clone.transform : null;
      }
      return null;
    }

    private static List<Renderer> ConfigureRequestState(
      GameObject cloneRoot, IReadOnlyList<Renderer> cloneRenderers,
      IReadOnlyList<GameObject> targets)
    {
      // Reset: no visual object from the clone may be rendered.
      foreach (var renderer in cloneRenderers)
      {
        renderer.enabled = false;
        renderer.gameObject.SetActive(false);
      }

      var visible = new HashSet<Renderer>();
      foreach (var target in targets)
      {
        EnableAncestorChain(target.transform, cloneRoot.transform);
        EnableHierarchy(target.transform);
        foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
        {
          EnableAncestorChain(renderer.transform, cloneRoot.transform);
          renderer.gameObject.SetActive(true);
          renderer.enabled = true;
          renderer.forceRenderingOff = false;
          visible.Add(renderer);
          if (renderer is SkinnedMeshRenderer skinned)
            EnableRequiredBones(skinned, cloneRoot.transform);
        }
      }

      // Bone activation can enable objects carrying unrelated renderers; keep only targets visible.
      foreach (var renderer in cloneRenderers)
        renderer.enabled = visible.Contains(renderer);
      return visible.Where(renderer => renderer != null && renderer.gameObject.activeInHierarchy)
        .ToList();
    }

    private static void PrepareOriginalRenderers(IReadOnlyList<Renderer> renderers)
    {
      foreach (var renderer in renderers)
      {
        if (renderer == null) continue;
        renderer.enabled = true;
        renderer.forceRenderingOff = false;
        if (renderer is SkinnedMeshRenderer skinned)
          skinned.updateWhenOffscreen = true;
      }
      Physics.SyncTransforms();
    }

    private static void RenderIsolatedOriginalRenderers(
      Camera camera, IReadOnlyList<Renderer> renderers, string assetPath,
      string stableKey)
    {
      var previousLayers = new Dictionary<Renderer, int>();
      foreach (var renderer in renderers)
      {
        if (renderer == null) continue;
        previousLayers[renderer] = renderer.gameObject.layer;
        renderer.gameObject.layer = CaptureLayer;
      }

      int previousMask = camera.cullingMask;
      camera.cullingMask = 1 << CaptureLayer;
      try
      {
        // First use the renderer's real bounds. If its imported AABB is stale,
        // retry with a large local bound without changing the camera framing.
        bool hasPixels;
        RenderPng(camera, assetPath, out hasPixels);
        if (hasPixels) return;

        foreach (var renderer in renderers)
        {
          if (renderer is SkinnedMeshRenderer skinned)
            skinned.localBounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        }
        Physics.SyncTransforms();
        RenderPng(camera, assetPath, out hasPixels);
        if (hasPixels)
          Debug.Log($"[ACC/Icon] Original skinned renderer retry succeeded: {stableKey}");
      }
      finally
      {
        camera.cullingMask = previousMask;
        foreach (var pair in previousLayers)
          if (pair.Key != null) pair.Key.gameObject.layer = pair.Value;
      }
    }

    private static void EnableHierarchy(Transform root)
    {
      root.gameObject.SetActive(true);
      for (int i = 0; i < root.childCount; i++)
        EnableHierarchy(root.GetChild(i));
    }

    private static void EnableAncestorChain(Transform node, Transform stopAt)
    {
      for (var current = node; current != null; current = current.parent)
      {
        current.gameObject.SetActive(true);
        if (current == stopAt) break;
      }
    }

    private static void EnableRequiredBones(SkinnedMeshRenderer renderer, Transform avatarRoot)
    {
      if (renderer.rootBone != null)
        EnableAncestorChain(renderer.rootBone, avatarRoot);
      foreach (var bone in renderer.bones)
      {
        if (bone != null) EnableAncestorChain(bone, avatarRoot);
      }
    }

    private static GameObject FindClone(
      GameObject sourceRoot, GameObject cloneRoot, GameObject target)
    {
      if (target == sourceRoot) return cloneRoot;
      var indices = new Stack<int>();
      var current = target.transform;
      while (current != null && current != sourceRoot.transform)
      {
        indices.Push(current.GetSiblingIndex());
        current = current.parent;
      }
      if (current != sourceRoot.transform) return null;
      var clone = cloneRoot.transform;
      while (indices.Count > 0)
      {
        int index = indices.Pop();
        if (index < 0 || index >= clone.childCount) return null;
        clone = clone.GetChild(index);
      }
      return clone.gameObject;
    }

    private static Bounds CalculateBounds(IReadOnlyList<Renderer> renderers)
    {
      Bounds? result = null;
      foreach (var renderer in renderers)
      {
        if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
        Bounds current = renderer.bounds;
        if (!result.HasValue) result = current;
        else
        {
          var combined = result.Value;
          combined.Encapsulate(current);
          result = combined;
        }
      }
      return result ?? new Bounds(Vector3.zero, Vector3.one * 0.1f);
    }

    private static Bounds? CalculateAvatarFramingBounds(GameObject cloneRoot)
    {
      var animator = cloneRoot.GetComponentInChildren<Animator>(true);
      if (animator != null && animator.isHuman)
      {
        var points = new List<Vector3>();
        for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
        {
          var transform = animator.GetBoneTransform(bone);
          if (transform != null) points.Add(transform.position);
        }
        if (points.Count > 1)
        {
          var bounds = new Bounds(points[0], Vector3.zero);
          foreach (var point in points.Skip(1)) bounds.Encapsulate(point);
          float height = Mathf.Max(bounds.size.y, 0.5f);
          // Bones do not include garment thickness; provide stable full-avatar margins.
          bounds.Expand(new Vector3(height * 0.18f, height * 0.08f, height * 0.18f));
          return bounds;
        }
      }
      return null;
    }

    private static void ApplyMaterialVariant(
      GameObject sourceRoot, GameObject cloneRoot, ACCVariantMaterialOverride marker)
    {
      if (marker.OutfitBase == null) return;

      // Preserve the original list-order semantics while avoiding a linear scan
      // of both override lists for every Renderer/material slot.
      var preciseOverrides = new Dictionary<Renderer, Dictionary<int, Material>>();
      foreach (var entry in marker.RendererOverrides ?? Enumerable.Empty<ACCVariantMaterialOverride.RendererMaterialReplacement>())
      {
        if (entry == null || entry.TargetRenderer == null || entry.Replacement == null) continue;
        if (!preciseOverrides.TryGetValue(entry.TargetRenderer, out var slots))
        {
          slots = new Dictionary<int, Material>();
          preciseOverrides.Add(entry.TargetRenderer, slots);
        }
        if (!slots.ContainsKey(entry.MaterialSlot))
          slots.Add(entry.MaterialSlot, entry.Replacement);
      }

      var globalReplacements = new Dictionary<Material, Material>();
      foreach (var entry in marker.Replacements ?? Enumerable.Empty<ACCVariantMaterialOverride.MaterialReplacement>())
      {
        if (entry == null || entry.Source == null || entry.Replacement == null) continue;
        if (!globalReplacements.ContainsKey(entry.Source))
          globalReplacements.Add(entry.Source, entry.Replacement);
      }

      foreach (var sourceRenderer in marker.OutfitBase.GetComponentsInChildren<Renderer>(true))
      {
        var cloneObject = FindClone(sourceRoot, cloneRoot, sourceRenderer.gameObject);
        if (cloneObject == null) continue;
        var sourceComponents = sourceRenderer.GetComponents(sourceRenderer.GetType());
        int componentIndex = Array.IndexOf(sourceComponents, sourceRenderer);
        var cloneComponents = cloneObject.GetComponents(sourceRenderer.GetType());
        if (componentIndex < 0 || componentIndex >= cloneComponents.Length) continue;
        var cloneRenderer = cloneComponents[componentIndex] as Renderer;
        if (cloneRenderer == null) continue;
        var materials = cloneRenderer.sharedMaterials;
        for (int slot = 0; slot < materials.Length; slot++)
        {
          if (preciseOverrides.TryGetValue(sourceRenderer, out var preciseSlots) &&
              preciseSlots.TryGetValue(slot, out var preciseReplacement))
          {
            materials[slot] = preciseReplacement;
            continue;
          }
          if (materials[slot] != null &&
              globalReplacements.TryGetValue(materials[slot], out var globalReplacement))
            materials[slot] = globalReplacement;
        }
        cloneRenderer.sharedMaterials = materials;
      }
    }

    private static void ConfigureCamera(
      Camera camera, Bounds bounds, Vector3 avatarForward, Vector3 avatarUp)
    {
      if (avatarForward.sqrMagnitude < 0.001f) avatarForward = Vector3.forward;
      if (avatarUp.sqrMagnitude < 0.001f) avatarUp = Vector3.up;
      avatarForward.Normalize();
      avatarUp.Normalize();
      Vector3 right = Vector3.Cross(avatarUp, avatarForward).normalized;
      avatarUp = Vector3.Cross(avatarForward, right).normalized;
      float halfWidth = 0f;
      float halfHeight = 0f;
      float halfDepth = 0f;
      foreach (var corner in GetBoundsCorners(bounds))
      {
        Vector3 offset = corner - bounds.center;
        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, right)));
        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, avatarUp)));
        halfDepth = Mathf.Max(halfDepth, Mathf.Abs(Vector3.Dot(offset, avatarForward)));
      }
      camera.aspect = 1f;
      camera.orthographicSize = Mathf.Max(halfHeight, halfWidth, 0.05f) * 1.12f;
      float distance = Mathf.Max(halfDepth * 2f + 1f, 1f);
      camera.transform.position = bounds.center + avatarForward * distance;
      camera.transform.rotation = Quaternion.LookRotation(-avatarForward, avatarUp);
      camera.nearClipPlane = 0.01f;
      camera.farClipPlane = distance + halfDepth * 2f + 10f;
    }

    private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds)
    {
      Vector3 min = bounds.min;
      Vector3 max = bounds.max;
      yield return new Vector3(min.x, min.y, min.z);
      yield return new Vector3(min.x, min.y, max.z);
      yield return new Vector3(min.x, max.y, min.z);
      yield return new Vector3(min.x, max.y, max.z);
      yield return new Vector3(max.x, min.y, min.z);
      yield return new Vector3(max.x, min.y, max.z);
      yield return new Vector3(max.x, max.y, min.z);
      yield return new Vector3(max.x, max.y, max.z);
    }

    private static void RenderPng(
      Camera camera, string assetPath, out bool hasVisiblePixels)
    {
      var rt = RenderTexture.GetTemporary(IconSize, IconSize, 24,
        RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
      var previousActive = RenderTexture.active;
      Texture2D texture = null;
      hasVisiblePixels = false;
      try
      {
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
        texture.Apply();
        int visiblePixels = texture.GetPixels32().Count(pixel => pixel.a > 8);
        hasVisiblePixels = visiblePixels > 0;
        // A transparent/blank capture is still a valid icon. It must be saved
        // so the menu item receives the generated result instead of inheriting
        // an unrelated descendant icon.
        File.WriteAllBytes(assetPath, texture.EncodeToPNG());
        if (!hasVisiblePixels)
          Debug.LogWarning($"[ACC/Icon] Blank capture saved as icon: {assetPath}");
      }
      finally
      {
        if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        camera.targetTexture = null;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(rt);
      }
      AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ConfigureTextureImporter(string assetPath)
    {
      var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
      if (importer == null) return;
      importer.alphaIsTransparency = true;
      importer.mipmapEnabled = false;
      importer.sRGBTexture = true;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.maxTextureSize = IconSize;
      importer.SaveAndReimport();
    }

    private static Texture2D PropagateIconsToSubmenus(
      GameObject node, HashSet<GameObject> generatedNodes)
    {
      Texture2D descendant = null;
      for (int i = 0; i < node.transform.childCount; i++)
      {
        var childIcon = PropagateIconsToSubmenus(
          node.transform.GetChild(i).gameObject, generatedNodes);
        if (descendant == null && childIcon != null) descendant = childIcon;
      }
      var item = node.GetComponent<ModularAvatarMenuItem>();
      if (item == null) return descendant;
      if (!generatedNodes.Contains(node) &&
        !Utils.HasUsableMenuIcon(item) && descendant != null)
      {
        Undo.RecordObject(item, "Assign inherited ACC menu icon");
        item.PortableControl.Icon = descendant;
        EditorUtility.SetDirty(item);
        ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { item });
      }
      return item.PortableControl.Icon ?? descendant;
    }

    private static void ClearExistingIcons(ISet<GameObject> requestedNodes)
    {
      foreach (var node in requestedNodes)
      {
        var item = node != null ? node.GetComponent<ModularAvatarMenuItem>() : null;
        if (item == null || Utils.HasUsableMenuIcon(item)) continue;
        Undo.RecordObject(item, "Replace ACC menu icon");
        item.PortableControl.Icon = null;
        EditorUtility.SetDirty(item);
        ACCEditorUndo.RecordPrefabInstanceModifications(new UnityEngine.Object[] { item });
      }
    }

    private static string StableHash(string value)
    {
      unchecked
      {
        uint hash = 2166136261;
        foreach (char character in value ?? "")
        {
          hash ^= character;
          hash *= 16777619;
        }
        return hash.ToString("x8");
      }
    }
  }
}
