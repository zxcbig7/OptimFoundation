using System;
using OptimFoundation.Modeling;

namespace SandBox.SetClass
{
    // Set 積木：就是 Set，定義元素型別（[OptSet<T>] 帶型別，無參數 = string）+ 集合本身。
    [OptSet<DateTime>] public partial class Set_Date;
    [OptSet<string>] public partial class Set_Employee;
    [OptSet<string>] public partial class Set_Group;
}
