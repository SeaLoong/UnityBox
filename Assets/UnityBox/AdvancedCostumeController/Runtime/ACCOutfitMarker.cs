using UnityEngine;
using VRC.SDKBase;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>
  /// 显式将当前对象标记为一套 ACC 服装。
  /// 用于原版服装等只包含网格、没有独立骨架分支的层级，
  /// 也可用于将任意容器节点明确指定为服装根。
  /// </summary>
  [AddComponentMenu("UnityBox/ACC Outfit Marker")]
  public class ACCOutfitMarker : MonoBehaviour, IEditorOnly
  {
    [Header("Part Name Formatting")]
    [Tooltip("移除自动部件菜单显示名称中的统一前缀。")]
    public string PartNamePrefixToRemove = "";

    [Tooltip("移除自动部件菜单显示名称中的统一后缀。")]
    public string PartNameSuffixToRemove = "";

    [Tooltip("对自动部件菜单显示名称执行的可选正则表达式替换。")]
    public string PartNameRegexPattern = "";

    [Tooltip("正则表达式替换结果。")]
    public string PartNameRegexReplacement = "";
  }
}