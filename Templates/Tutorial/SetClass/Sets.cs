using OptimFoundation.Modeling;

namespace Tutorial.SetClass
{
    // Set 積木：一顆 = 一個索引集合，[OptSet<T>] 宣告元素型別。展示三種元素型別：string / DateTime / int。
    // 成員來源：Data/Set_{Name}.csv（一行一成員、無表頭），由 Dataload ctor 的 XXX.Load(source) 載入；
    //   DateTime / int 由 SetBase.ParseElement 自動從字串轉型。
    [OptSet<string>]
    public partial class Set_Product { }

    [OptSet<string>]
    public partial class Set_Machine { }

    [OptSet<DateTime>]
    public partial class Set_Date { }

    [OptSet<int>]
    public partial class Set_Shift { }
}
