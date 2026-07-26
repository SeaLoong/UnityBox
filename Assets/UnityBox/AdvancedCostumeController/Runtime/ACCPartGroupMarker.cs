using UnityEngine;
using VRC.SDKBase;

namespace UnityBox.AdvancedCostumeController
{
  public enum ACCPartControlMode
  {
    Group,
    Exclude
  }

  /// <summary>
  /// 配置当前对象在 ACC 部件控制中的行为。
  /// 同一服装下拥有相同 Group Name 的分组对象会共用一个菜单开关和参数。
  /// </summary>
  [AddComponentMenu("UnityBox/ACC Part Group Marker")]
  [HelpURL("https://github.com/SeaLoong/UnityBox/blob/master/Docs/AdvancedCostumeController.md#acc-part-group-marker")]
  public class ACCPartGroupMarker : MonoBehaviour, IEditorOnly
  {
    [Tooltip("Group 会将同名对象组合为一个开关；Exclude 会排除当前对象及其所在自动部件。")]
    public ACCPartControlMode Mode = ACCPartControlMode.Group;

    [Tooltip("同名标记对象会一起开关。留空时默认使用当前对象名称。")]
    public string GroupName;

    private void Reset()
    {
      GroupName = gameObject.name;
    }
  }
}