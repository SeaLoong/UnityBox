namespace UnityBox.AdvancedCostumeController
{
  /// <summary>互斥选择参数的布局。压缩模式使用本地 Animator Float 选择值与同步二进制 Bool 位。</summary>
  public sealed class ChoiceParameterLayout
  {
    public int ChoiceCount { get; }
    private readonly bool requestedCompression;
    /// <summary>恰好两个取值时可直接使用一个 Bool，而不需要 Int 或压缩位。</summary>
    public bool UsesBoolean => ChoiceCount == 2;
    public bool UsesCompression => requestedCompression && !UsesBoolean;
    /// <summary>只有一个固定选择值时无需网络同步。</summary>
    public bool RequiresSynchronization => ChoiceCount > 1;
    public int BitCount
    {
      get
      {
        int bits = 0;
        for (int valueCount = ChoiceCount - 1; valueCount > 0; valueCount >>= 1)
          bits++;
        return bits;
      }
    }

    public ChoiceParameterLayout(int choiceCount, bool usesCompression)
    {
      ChoiceCount = choiceCount;
      requestedCompression = usesCompression;
    }

    public string GetBitParameterName(string baseParameterName, int bitIndex)
    {
      return CostumeGenerationCore.BuildParamName(baseParameterName, $"Bits/{bitIndex:D2}");
    }
  }

}
