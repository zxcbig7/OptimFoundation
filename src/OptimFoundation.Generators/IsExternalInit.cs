// 補上 netstandard2.0 缺少的 IsExternalInit 型別，讓編譯器能產生 record 的 init 存取子。
namespace System.Runtime.CompilerServices
{
    /// <summary>編譯器辨識用的空類別，讓 netstandard2.0 也能用 record 的 init 存取子；沒有執行期行為。</summary>
    internal static class IsExternalInit { }
}
