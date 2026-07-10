using OptimFoundation.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SandBox.VariableClass
{
    public class VariableX_Template : VariableBase
    {
        #region Sets(依照順序新增Set)
        public string Set1 { get; set; }
        public double Set2 { get; set; }
        public int Set3 { get; set; }
        public DateTime Set4 { get; set; }
        #endregion
        
        #region 建構子
        public VariableX_Template(params object[] Sets)
        {
            InitClassBySets(Sets);
        }
        #endregion

    }
}
