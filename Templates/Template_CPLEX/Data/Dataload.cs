using SandBox.SetClass;
using SandBox.VariableClass;
using OptimFoundation.Cplex;

using OptimFoundation.Core;
using OptimFoundation.Core.IO;



namespace SandBox.Data
{
    public partial class Dataload : DataContext
    {
        // 罰分權重參數
        public double Penalty_SixDay = 1;
        public double Penalty_DoubleOffLT2 = 0.1;
        public double Penalty_GroupMismatch = 0.2;
        public double Penalty_NightToDay = 0.2;
        public double Penalty_PreGroup = 0.2;
        public double Penalty_BelowAVG = 0.1;
        public double Penalty_OffOneDay = 0.1;
        public double Penalty_Weekend4Day = 0.1;
        public double Penalty_BackpuGroup = 0;

        //設定Sets
        public List<string> Employee = new List<string>();
        public List<string> Group = new List<string>();
        public List<DateTime> Date = new List<DateTime>();

        // Set 積木：與上面的 List 同一份資料，僅供 DataContext 註冊 / 自動驗證用，既有 List 供 constraint 端沿用
        public Set_Employee EMPLOYEE = new();
        public Set_Group GROUP = new();
        public Set_Date DATE = new();

        // 模型建構使用的參數
        public List<Parameter_NightToDay> parameter_NightToDay = new List<Parameter_NightToDay>(); // 前一天-今天班別對應成本
        public List<Parameter_ShiftDemand> parameter_ShiftDemand = new List<Parameter_ShiftDemand>(); // 每日各班別需求
        public List<Parameter_CrossGroup> parameter_CrossGroup = new List<Parameter_CrossGroup>(); // 跨組別上班成本
        public List<Parameter_PreAssign> parameter_PreAssign = new List<Parameter_PreAssign>(); // 跨組別上班成本
        public List<Parameter_BackupGroup> parameter_BackupGroup = new List<Parameter_BackupGroup>(); // Backup組別

        // 方便銜接 Pattern
        enum GroupE { O, D, E, N, C }

        public Dataload()
        {
            // 班別群組
            this.Group.AddRange(Enum.GetNames(typeof(GroupE)));


            // 人員群組
            (int, string)[] EMYNum_list = [(7, "D"), (5, "E"), (3, "N"), (1, "C")]; // D、E、N、C班人數
            int totalENum = EMYNum_list.Sum(s => s.Item1); // 總人數
            for (int e = 1; e <= totalENum; e++) { this.Employee.Add($"E{e}"); }

            // 跨組別上班成本(需特別設定)
            int i = 1;
            foreach (var group in EMYNum_list)
            {
                // 休、主要、Backup班別之外的班別，跨組別上班都要扣分
                var inhig = this.Group.Where(w => w != group.Item2 && w != "O").ToList();
                for (int j = 0; j < group.Item1; j++)
                {
                    foreach (var inh in inhig)
                    {
                        parameter_CrossGroup.Add(new Parameter_CrossGroup { Employee = $"E{i}", Group = inh, QTY = Penalty_GroupMismatch });
                    }
                    i++;
                }
            }

            //填資料
            parameter_BackupGroup.Add(new Parameter_BackupGroup { Employee = "E1", Group = "C" }); // 員工E1的Backup班別為C，跨組別上班成本調整為 Backup 成本

            // 找到該員工該班別的跨組別上班成本，調整為 Backup 成本
            foreach (var backup in this.parameter_BackupGroup)
            {
                var CrossGroup = parameter_CrossGroup.FirstOrDefault(w => w.Employee == backup.Employee && w.Group == backup.Group);

                if (CrossGroup != null)
                {
                    CrossGroup.QTY = Penalty_BackpuGroup;
                }
            }



            // 昨天->今天班別對應成本
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "N", Group = "D", QTY = Penalty_PreGroup }); // 晚->早
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "N", Group = "E", QTY = Penalty_PreGroup }); // 晚->午
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "N", Group = "C", QTY = Penalty_PreGroup }); // 晚->行
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "E", Group = "D", QTY = Penalty_PreGroup }); // 午->早
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "E", Group = "C", QTY = Penalty_PreGroup }); // 午->行
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "D", Group = "N", QTY = Penalty_PreGroup }); // 早->晚
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "E", Group = "N", QTY = Penalty_PreGroup }); // 午->晚
            parameter_NightToDay.Add(new Parameter_NightToDay { PreGroup = "C", Group = "N", QTY = Penalty_PreGroup }); // 行->晚


            parameter_ShiftDemand = CsvCtrl.BuildParameter<Parameter_ShiftDemand>("Parameter_ShiftDemand");

            // CSV 種子資料已覆蓋的日子不再自動產生——同 (Date,Group) 重複會被框架驗證器擋下（DuplicateKey）
            var seededDates = new HashSet<DateTime>();
            foreach (var seeded in parameter_ShiftDemand)
                seededDates.Add(seeded.Date);

            //  排程月份
            int year = 2026;
            int month = 1;   // 1~12
            int daysInMonth = DateTime.DaysInMonth(year, month);
            Random random = new Random(42);   // 固定種子：範本教學需要每次跑出同一份需求資料，才能對照解可重現（同 FJSP_BASIC_BRICK 的 seed:42 慣例）

            for (int day = 1; day <= daysInMonth; day++)
            {
                DateTime d = new DateTime(year, month, day);
                Date.Add(d);

                if (seededDates.Contains(d)) continue;   // 這天的需求由 CSV 種子提供

                //每日各班別需求
                parameter_ShiftDemand.Add(new Parameter_ShiftDemand { Date = d, Group = "D", QTY = random.Next(4, 6) }); // 隨機4~5人需求
                parameter_ShiftDemand.Add(new Parameter_ShiftDemand { Date = d, Group = "E", QTY = 3 });
                parameter_ShiftDemand.Add(new Parameter_ShiftDemand { Date = d, Group = "N", QTY = 2 });
                parameter_ShiftDemand.Add(new Parameter_ShiftDemand { Date = d, Group = "C", QTY = 1 });
            }



            // 預排班 (需特別設定)
            parameter_PreAssign.Add(new Parameter_PreAssign { Date = new DateTime(2026, 1, 1), Employee = "E1", Group = "E" });
            parameter_PreAssign.Add(new Parameter_PreAssign { Date = new DateTime(2026, 1, 1), Employee = "E3", Group = "O" });
            parameter_PreAssign.Add(new Parameter_PreAssign { Date = new DateTime(2026, 1, 2), Employee = "E2", Group = "D" });
            parameter_PreAssign.Add(new Parameter_PreAssign { Date = new DateTime(2026, 1, 2), Employee = "E3", Group = "E" });


            #region 資料讀取 - CSV
            //this.Set1 = CSVCtrl.ReadStrSet("Set_Set1.csv");
            //this.Set2 = CSVCtrl.ReadDoubleSet("Set_Set2.csv");
            //this.Set3 = CSVCtrl.ReadIntSet("Set_Set3.csv");
            //this.Set4 = CSVCtrl.ReadDateSet("Set_Set4.csv");
            //this.parameters_Template = CSVCtrl.BuildParameter<Parameter_Template>("Prarm");
            #endregion

            EMPLOYEE.LoadFrom(Employee);
            GROUP.LoadFrom(Group);
            DATE.LoadFrom(Date);
        }

        public void WriteToCSV(OptEngine engine)
        {
            #region CSV 操作
            //CSVCtrl.SaveToCSV<VariableB_ShiftAssign>(engine.GetSetVarSol<VariableB_ShiftAssign>(), DATA_ID: "V1", USER_ID: "VIC");
            //CSVCtrl.SaveToCSV<VariableX_WeekendLT4>(engine.GetSetVarSol<VariableX_WeekendLT4>(), DATA_ID: "V1", USER_ID: "VIC");
            //CSVCtrl.SaveToCSV<VariableX_BelowAVG>(engine.GetSetVarSol<VariableX_BelowAVG>(), DATA_ID: "V1", USER_ID: "VIC");
            //CSVCtrl.SaveToCSV<VariableB_Off1Day>(engine.GetSetVarSol<VariableB_Off1Day>(), DATA_ID: "V1", USER_ID: "VIC");
            #endregion
        }
    }
}
