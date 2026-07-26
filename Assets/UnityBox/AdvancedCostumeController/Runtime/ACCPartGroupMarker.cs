using UnityEngine;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 将当前对象作为 ACC 部件分组的一员。
  /// 同一服装下拥有相同 Group Name 的标记对象会共用一个菜单开关和参数。
  /// </summary>
  [AddComponentMenu("UnityBox/ACC Part Group Marker")]
  public class ACCPartGroupMarker : MonoBehaviour
  {
    [Tooltip("同名标记对象会一起开关。留空时默认使用当前对象名称。")]
    public string GroupName;

    private void Reset()
    {
      GroupName = gameObject.name;
    }
  }
}