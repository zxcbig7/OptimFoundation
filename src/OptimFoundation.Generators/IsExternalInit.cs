// netstandard2.0 沒有 IsExternalInit；record 的 init 存取子需要它。此為標準 polyfill。
namespace System.Runtime.CompilerServices
{
    /// <summary>編譯器辨識用的空類別，讓 netstandard2.0 也能用 record 的 init 存取子；沒有執行期行為。</summary>
    internal static class IsExternalInit { }
}
