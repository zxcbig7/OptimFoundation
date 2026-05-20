using OptimFoundation.Core;

namespace SandBox.Data
{
    public class Parameter_Template : ParameterBase
    {
        #region Sets(依照順序新增Set)
        public string Set1 { get; set; }
        public double Set2 { get; set; }
        public int Set3 { get; set; }
        public DateTime Set4 { get; set; }
        public double QTY { get; set; }
        #endregion

        #region 建構子
        public Parameter_Template(params object[] Sets)
        {
            InitClassBySets(Sets); // 不要動到這裡
        }
        #endregion
    }
}
