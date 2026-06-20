// netstandard2.0 沒有 IsExternalInit；record 的 init 存取子需要它。此為標準 polyfill。
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
