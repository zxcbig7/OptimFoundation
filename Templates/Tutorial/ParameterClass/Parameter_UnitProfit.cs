using OptimFoundation.Modeling;
using Tutorial.SetClass;

namespace Tutorial.ParameterClass
{
    /// <summary>單位利潤（元/件）。數學：UnitProfit_p，值在 QTY。1D 參數。</summary>
    [OptParam]
    [OptDim<Set_Product>("Product")]
    public partial class Parameter_UnitProfit { }
}
