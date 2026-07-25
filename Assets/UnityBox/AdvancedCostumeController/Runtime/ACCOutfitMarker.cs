using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 显式将当前对象标记为一套 ACC 服装。
  /// 用于原版服装等只包含网格、没有独立骨架分支的层级，
  /// 也可用于将任意容器节点明确指定为服装根。
  /// </summary>
  [AddComponentMenu("UnityBox/ACC Outfit Marker")]
  public class ACCOutfitMarker : MonoBehaviour
  {
  }
}