using System;
using OptimFoundation.Modeling;

namespace SandBox.SetClass
{
    // Set 積木：就是 Set，[OptSet<T>] 顯式宣告元素型別 + 集合本身。
    [OptSet<DateTime>] public partial class Set_Date;
    [OptSet<string>] public partial class Set_Employee;
    [OptSet<string>] public partial class Set_Group;
}
