using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem
{
    /// <summary>Native Unity backend for ASS state-entry operations.</summary>
    public sealed class AnimatorStateEntryActions : StateMachineBehaviour
    {
        [Serializable]
        public struct Assignment
        {
            public string parameter;
            public float value;
        }

        public Assignment[] assignments = Array.Empty<Assignment>();
        public bool localOnly;
        public int controlledLayer = -1;
        public float layerWeight;
        public string audioSourcePath;
        public AudioClip audioClip;

        private RuntimeAnimatorController cachedController;
        private Dictionary<string, AnimatorControllerParameterType> parameterTypes;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (parameterTypes == null || cachedController != animator.runtimeAnimatorController)
            {
                cachedController = animator.runtimeAnimatorController;
                parameterTypes = new Dictionary<string, AnimatorControllerParameterType>();
                foreach (var parameter in animator.parameters)
                    parameterTypes[parameter.name] = parameter.type;
            }

            if (localOnly && !IsLocal(animator)) return;
            foreach (var assignment in assignments)
            {
                switch (parameterTypes[assignment.parameter])
                {
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(assignment.parameter, assignment.value != 0f);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(assignment.parameter, Mathf.RoundToInt(assignment.value));
                        break;
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(assignment.parameter, assignment.value);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (assignment.value != 0f) animator.SetTrigger(assignment.parameter);
                        else animator.ResetTrigger(assignment.parameter);
                        break;
                }
            }
            if (controlledLayer >= 0)
                animator.SetLayerWeight(controlledLayer, layerWeight);
            if (audioClip != null)
            {
                var source = animator.transform.Find(audioSourcePath).GetComponent<AudioSource>();
                source.gameObject.SetActive(true);
                source.clip = audioClip;
                source.loop = false;
                source.Play();
            }
        }

        private bool IsLocal(Animator animator)
        {
            switch (parameterTypes["IsLocal"])
            {
                case AnimatorControllerParameterType.Bool: return animator.GetBool("IsLocal");
                case AnimatorControllerParameterType.Int: return animator.GetInteger("IsLocal") > 0;
                default: return animator.GetFloat("IsLocal") > 0.5f;
            }
        }
    }
}
