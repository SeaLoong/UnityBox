using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Unity Animator construction shared by ACC's avatar integration.</summary>
  public static class AnimatorChoiceBuilder
  {
    public const float ValueTolerance = 0.25f;

    public static void AddChoiceParameters(AnimatorController controller, string baseParameterName,
      ChoiceParameterLayout layout, int defaultChoiceIndex)
    {
      if (!controller.parameters.Any(parameter => parameter.name == baseParameterName))
      {
        controller.AddParameter(new AnimatorControllerParameter
        {
          name = baseParameterName,
          // Keep the Animator-side parameter Float because BlendTree inputs
          // consume numeric values. The Modular Avatar expression parameter
          // uses Bool for two-value domains; its 0/1 value maps directly here.
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

    public static AnimatorStateTransition CreateAnyStateTransition(
      AnimatorStateMachine stateMachine, AnimatorState state)
    {
      var transition = stateMachine.AddAnyStateTransition(state);
      transition.duration = 0;
      transition.hasExitTime = false;
      transition.canTransitionToSelf = false;
      return transition;
    }

    public static AnimatorStateTransition CreateStateTransition(
      AnimatorState sourceState, AnimatorState targetState)
    {
      var transition = sourceState.AddTransition(targetState);
      transition.duration = 0;
      transition.hasExitTime = false;
      transition.canTransitionToSelf = false;
      return transition;
    }

    /// <summary>为 Animator Float 选择值添加稳定的离散值匹配区间。</summary>
    public static void AddChoiceValueConditions(AnimatorStateTransition transition,
      string parameterName, int choiceIndex)
    {
      transition.AddCondition(AnimatorConditionMode.Greater,
        choiceIndex - ValueTolerance, parameterName);
      transition.AddCondition(AnimatorConditionMode.Less,
        choiceIndex + ValueTolerance, parameterName);
    }

  }
}
