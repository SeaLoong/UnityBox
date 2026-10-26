using System.Collections;
using UnityEngine;

namespace UnityBox.AvatarSecuritySystem
{
    /// <summary>
    /// Runtime half of the editor-created AnimationClip transform-path probe.
    /// It intentionally does not build an avatar or use VRChat SDK APIs.
    /// </summary>
    public sealed class AnimationPathBindingProbe : MonoBehaviour
    {
        public enum ProbeMode
        {
            RuntimeRename,
            OriginalBundle,
            ModifiedBundle
        }

        [SerializeField] public Transform marker;
        [SerializeField] public Transform plainMarker;
        [SerializeField] public Animator animator;
        [SerializeField] public ProbeMode mode;
        [SerializeField] private float baselineScale;
        [SerializeField] private float renamedScale;

        private IEnumerator Start()
        {
            if (marker == null || plainMarker == null || marker == plainMarker ||
                animator == null || animator.runtimeAnimatorController == null)
            {
                Debug.LogError("[ASS Probe] FAIL: two distinct markers and an Animator controller are required.");
                yield break;
            }

            if (mode != ProbeMode.RuntimeRename && mode != ProbeMode.OriginalBundle &&
                mode != ProbeMode.ModifiedBundle)
            {
                Debug.LogError($"[ASS Probe] FAIL: unknown probe mode {mode}.");
                yield break;
            }

            bool modifiedBundle = mode == ProbeMode.ModifiedBundle;
            string expectedName = modifiedBundle ? "ASS_Marker_NEW" : "ASS_Marker_OLD";
            if (marker.name != expectedName || plainMarker.name != "PlainMarker")
            {
                Debug.LogError($"[ASS Probe] FAIL: marker names do not match mode {mode}.");
                yield break;
            }

            // Reset both scales so a pre-existing value cannot masquerade as animation.
            marker.localScale = Vector3.one;
            plainMarker.localScale = Vector3.one;
            animator.Play("Base Layer.PathProbe", 0, 0f);
            yield return null;
            yield return new WaitForSeconds(0.35f);
            baselineScale = marker.localScale.x;
            float plainBaseline = plainMarker.localScale.x;

            Debug.Log($"[ASS Probe] Initial ({mode}): idMarker='{marker.name}' scale.x={baselineScale:0.###}, " +
                      $"plainMarker='{plainMarker.name}' scale.x={plainBaseline:0.###}");

            float expectedInitialScale = modifiedBundle ? 1f : 2f;
            if (!Approximately(plainBaseline, 2f) || !Approximately(baselineScale, expectedInitialScale))
            {
                Debug.LogError($"[ASS Probe] FAIL ({mode}): expected ID scale {expectedInitialScale} and control scale 2; " +
                               $"observed {baselineScale:0.###} and {plainBaseline:0.###}. This run does not establish the expected binding behavior.");
                yield break;
            }

            if (mode != ProbeMode.RuntimeRename)
            {
                Debug.Log(modifiedBundle
                    ? "[ASS Probe] PASS (ModifiedBundle): the ID marker remained at scale 1 while the control animated to 2. Compare with a passing OriginalBundle run; this result alone does not establish a general protection mechanism."
                    : "[ASS Probe] PASS (OriginalBundle): both markers animated from scale 1 to 2.");
                yield break;
            }

            marker.name = "ASS_Marker_NEW";
            marker.localScale = Vector3.one;
            plainMarker.localScale = Vector3.one;

            animator.Rebind();
            animator.Update(0f);
            animator.Play("Base Layer.PathProbe", 0, 0f);
            yield return null;
            yield return new WaitForSeconds(0.35f);
            renamedScale = marker.localScale.x;
            float plainAfter = plainMarker.localScale.x;

            Debug.Log($"[ASS Probe] After id-marker rename: idMarker='{marker.name}' scale.x={renamedScale:0.###}, " +
                      $"plainMarker='{plainMarker.name}' scale.x={plainAfter:0.###}");

            if (!Approximately(renamedScale, 1f) || !Approximately(plainAfter, 2f))
            {
                Debug.LogError($"[ASS Probe] FAIL (RuntimeRename): expected renamed ID scale 1 and control scale 2; " +
                               $"observed {renamedScale:0.###} and {plainAfter:0.###}. The expected path-binding break was not demonstrated.");
                yield break;
            }

            Debug.Log("[ASS Probe] PASS (RuntimeRename): both markers animated before rename; only the untouched control animated afterward.");
        }

        private static bool Approximately(float actual, float expected)
        {
            return Mathf.Abs(actual - expected) < 0.05f;
        }
    }
}
