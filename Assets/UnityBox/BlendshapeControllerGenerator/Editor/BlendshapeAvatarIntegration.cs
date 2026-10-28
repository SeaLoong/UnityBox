using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Optional integration point; animation generation does not require an avatar SDK.</summary>
public interface IBlendshapeAvatarInstaller
{
  void Install(BlendshapeGenerationResult generation);
}

public static class BlendshapeAvatarIntegration
{
  public static IBlendshapeAvatarInstaller Installer { get; set; }
}

public sealed class BlendshapeGeneratedControl
{
  public string Name { get; }
  public string ParameterName { get; }
  public bool IsBoolean { get; }

  public BlendshapeGeneratedControl(string name, string parameterName, bool isBoolean)
  {
    Name = name;
    ParameterName = parameterName;
    IsBoolean = isBoolean;
  }
}

public sealed class BlendshapeGenerationResult
{
  public GameObject Root { get; }
  public AnimatorController Controller { get; }
  public string MenuIdentity { get; }
  public string DisplayName { get; }
  public IReadOnlyList<BlendshapeGeneratedControl> Controls { get; }

  public BlendshapeGenerationResult(GameObject root, AnimatorController controller,
    string menuIdentity, string displayName, IReadOnlyList<BlendshapeGeneratedControl> controls)
  {
    Root = root;
    Controller = controller;
    MenuIdentity = menuIdentity;
    DisplayName = displayName;
    Controls = controls;
  }
}
