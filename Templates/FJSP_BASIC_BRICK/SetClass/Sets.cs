using OptimFoundation.Modeling;

namespace FJSP_BASIC_BRICK.SetClass
{
    // Set 積木：就是 Set，定義元素型別（[OptSet] 預設 string）+ 集合本身。generator 補 : SetBase<string>。
    [OptSet<string>] 
    public partial class Set_Lot { }

    [OptSet<string>] 
    public partial class Set_Operation { }

    [OptSet<string>]
    public partial class Set_Eqp { }
}
