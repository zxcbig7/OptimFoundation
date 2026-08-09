using OptimFoundation.Modeling;

namespace Tutorial
{
    /// <summary>是否開線 0/1。數學：Setup_{p,d,s}。前綴 B_ = binary。3D 變數（Product × Date × Shift）。</summary>
    [OptVar]
    [OptDim<string>("Product")]
    [OptDim<DateTime>("Date")]
    [OptDim<int>("Shift")]
    public sealed partial class VariableB_Setup { }
}
