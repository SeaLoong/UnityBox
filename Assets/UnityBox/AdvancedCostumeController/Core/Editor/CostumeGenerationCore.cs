using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UnityBox.AdvancedCostumeController
{
  /// <summary>Shared naming and path rules, independent of avatar integration packages.</summary>
  public static class CostumeGenerationCore
  {
  /// <summary>
  /// 清理字符串，只保留字母数字和斜杠
  /// </summary>
  public static string Sanitize(string s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    var arr = s.Select(c => char.IsLetterOrDigit(c) || c == '/' ? c : '_').ToArray();
    return new string(arr);
  }

  /// <summary>
  /// 清理字符串用于文件名，将所有非法字符（包括斜杠）替换为下划线
  /// </summary>
  public static string SanitizeForFileName(string s)
  {
    if (string.IsNullOrEmpty(s)) return "";
    var arr = s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
    return new string(arr);
  }

  /// <summary>保留可读前缀，并区分被文件名清理折叠的空格、标点和路径分隔符。</summary>
  public static string GetStableAssetName(string value)
  {
    value = value ?? "";
    string sanitized = SanitizeForFileName(value);
    const int maximumPrefixLength = 80;
    if (sanitized == value && sanitized.Length <= maximumPrefixLength)
      return sanitized;
    if (sanitized.Length > maximumPrefixLength)
      sanitized = sanitized.Substring(0, maximumPrefixLength);
    return sanitized + "_" + StableIdentifierHash(value);
  }

  private static string StableIdentifierHash(string value)
  {
    using (var algorithm = System.Security.Cryptography.SHA256.Create())
    {
      var bytes = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
      return string.Concat(bytes.Take(8).Select(valueByte => valueByte.ToString("x2")));
    }
  }

  /// <summary>检查命名空间能否产生至少包含一个字母或数字的稳定资产名。</summary>
  public static bool HasUsableGenerationNamespace(string value)
  {
    return !string.IsNullOrWhiteSpace(value) &&
      SanitizeForFileName(value).Any(char.IsLetterOrDigit);
  }

  /// <summary>
  /// 验证用户输入的输出目录是 Assets 内的安全相对路径。
  /// 生成时会删除该目录下当前 ACC 的专属子目录，不能接受绝对路径或父级跳转。
  /// </summary>
  public static bool IsSafeAssetsFolder(string path)
  {
    if (string.IsNullOrWhiteSpace(path)) return false;

    string normalized = path.Replace('\\', '/').TrimEnd('/');
    if (normalized != "Assets" && !normalized.StartsWith("Assets/")) return false;

    return normalized.Split('/').All(segment =>
      !string.IsNullOrWhiteSpace(segment) && segment != "." && segment != "..");
  }

  /// <summary>将合法的 Assets 路径规范化为 Unity AssetDatabase 使用的斜杠形式。</summary>
  public static string NormalizeAssetsFolder(string path)
  {
    return path.Replace('\\', '/').TrimEnd('/');
  }

  /// <summary>合并并规范化 Unity AssetDatabase 使用的路径。</summary>
  public static string CombineAssetPath(params string[] parts)
  {
    return Path.Combine(parts).Replace('\\', '/');
  }

  /// <summary>
  /// 构建参数名称
  /// </summary>
  public static string BuildParamName(string paramPrefix, string relPath)
  {
    relPath = relPath ?? "";
    string path = Sanitize(relPath);
    if (path != relPath)
      path += "_" + StableIdentifierHash(relPath);
    if (string.IsNullOrEmpty(paramPrefix))
      return path;
    return paramPrefix + "/" + path;
  }

  /// <summary>
  /// 格式化菜单显示名称。格式化仅用于标签，不会改变动画绑定或参数路径。
  /// 依次执行精确前缀/后缀移除和可选正则替换；无效正则会保留前两步结果。
  /// </summary>
  public static string FormatPartDisplayName(
    string source,
    string prefixToRemove,
    string suffixToRemove,
    string regexPattern,
    string regexReplacement)
  {
    string result = source ?? "";
    if (!string.IsNullOrEmpty(prefixToRemove) &&
        result.StartsWith(prefixToRemove, StringComparison.Ordinal))
      result = result.Substring(prefixToRemove.Length);
    if (!string.IsNullOrEmpty(suffixToRemove) &&
        result.EndsWith(suffixToRemove, StringComparison.Ordinal))
      result = result.Substring(0, result.Length - suffixToRemove.Length);

    if (!string.IsNullOrWhiteSpace(regexPattern))
    {
      try { result = Regex.Replace(result, regexPattern, regexReplacement ?? ""); }
      catch (ArgumentException) { }
    }

    return string.IsNullOrWhiteSpace(result) ? source : result;
  }

  public static bool IsValidRegex(string pattern)
  {
    if (string.IsNullOrWhiteSpace(pattern)) return true;
    try
    {
      _ = new Regex(pattern);
      return true;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  }
}
