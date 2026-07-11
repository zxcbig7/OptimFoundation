using OptimFoundation.Modeling;

namespace FJSP_BASIC.ParameterClass
{
    /// <summary>
    /// 參數宣告式範本：展示所有支援的 Set 型別（string / double / int / DateTime）+ QTY。
    /// body 由 AutoSetsGenerator 生成；純 key 參數（無 QTY）設 [OptParam(..., HasValue = false)]。
    /// 手寫版對照見同資料夾 Parameter_Template.cs。
    /// </summary>
    [OptParam("Set1", "Set2:double", "Set3:int", "Set4:DateTime")]
    public partial class Parameter_Template_Generator { }
}
