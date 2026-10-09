using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OptimFoundation.Core
{
    /// <summary>Generator 產生的維度中繼資料；partial class 的其他成員不列入模型維度。</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DimensionNamesAttribute : Attribute
    {
        /// <summary>依 OptDim 宣告順序的維度 property 名稱。</summary>
        public IReadOnlyList<string> Names { get; }

        public DimensionNamesAttribute(params string[] names) { Names = names ?? Array.Empty<string>(); }
    }
}
