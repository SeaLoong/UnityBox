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
        [SerializeField] public Transform marker;
        [SerializeField] public Transform plainMarker;
        [SerializeField] public Animator animator;
        [SerializeField] private float baselineScale;
        [SerializeField] private float renamedScale;

        private IEnumerator Start()
        {
            if (marker == null || animator == null)
            {
                Debug.LogError("[ASS Probe] Probe scene is missing marker or Animator reference.");
                yield break;
            }

            animator.Play("Base Layer.PathProbe", 0, 0f);
            yield return null;
            yield return new WaitForSeconds(0.35f);
            baselineScale = marker.localScale.x;
            float plainBaseline = plainMarker != null ? plainMarker.localScale.x : 0f;

            Debug.Log($"[ASS Probe] Initial: idMarker='{marker.name}' scale.x={baselineScale:0.###}" +
                      (plainMarker != null ? $", plainMarker='{plainMarker.name}' scale.x={plainBaseline:0.###}" : ""));

            marker.name = "ASS_Marker_NEW";
            marker.localScale = Vector3.one;
            if (plainMarker != null) plainMarker.localScale = Vector3.one;

            animator.Rebind();
            animator.Update(0f);
            animator.Play("Base Layer.PathProbe", 0, 0f);
            yield return null;
            yield return new WaitForSeconds(0.35f);
            renamedScale = marker.localScale.x;
            float plainAfter = plainMarker != null ? plainMarker.localScale.x : 0f;

            Debug.Log($"[ASS Probe] After id-marker rename: idMarker='{marker.name}' scale.x={renamedScale:0.###}" +
                      (plainMarker != null ? $", plainMarker='{plainMarker.name}' scale.x={plainAfter:0.###}" : ""));

            bool idAnimatedInitially = baselineScale > 1.5f;
            bool idAnimatedAfterRename = renamedScale > 1.5f;
            bool plainAnimated = plainMarker != null && plainAfter > 1.5f;

            if (plainMarker != null && !plainAnimated)
            {
                Debug.Log("[ASS Probe] Result: even the untouched control marker was not driven, so this run cannot judge the replacement effect. Check the test harness.");
            }
            else if (!idAnimatedInitially)
            {
                Debug.Log("[ASS Probe] Result: the ID marker was never driven while the untouched control marker was. " +
                          "This is the expected byte-modified AssetBundle result: the replaced path no longer matches its stored binding data, " +
                          "so animation driving through the replaced path stops working.");
            }
            else if (!idAnimatedAfterRename)
            {
                Debug.Log("[ASS Probe] Result: the ID marker was driven initially but stopped after it was renamed at runtime. " +
                          "Unity does not migrate path bindings when an object is renamed.");
            }
            else
            {
                Debug.Log("[ASS Probe] Result: both markers were still driven. The path-binding protection idea is NOT reliable in this configuration.");
            }
        }
    }
}