using OptimFoundation.Modeling;

namespace FJSP_BASIC.VariableClass
{
    /// <summary>
    /// 變數宣告式範本：展示所有支援的 Set 型別（string(預設) / double / int / DateTime）。
    /// 變數型別由類別名前綴決定（命名天條）：VariableB_=Binary / VariableX_=Continuous / VariableI_=Integer，
    /// 前綴不合法 → OPTF001 compile error。
    /// set 字串順序對應 BuildVars / BuildBVs / BuildCVs 傳入 set 的順序；body 由 AutoSetsGenerator 生成。
    /// 手寫版對照見同資料夾 VariableX_Template.cs。
    /// </summary>
    [OptVar("Set1", "Set2:double", "Set3:int", "Set4:DateTime")]
    public partial class VariableX_Template_Generator { }
}
