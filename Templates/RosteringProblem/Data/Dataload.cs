using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace RosteringProblem
{
    /// <summary>資料唯一入口：把 Data/*.csv 讀成 Set/Parameter rows，再由 DataContext 驗證 Parameter。</summary>
    public sealed partial class Dataload : DataContext
    {
        /// <summary>班別群組列舉，只用於 import 模式產生 Set_Group 的固定成員順序（O,D,E,N,C）。</summary>
        private enum GroupE { O, D, E, N, C }

        public List<Set_Employee> set_Employee = new();
        public List<Set_Group> set_Group = new();
        public List<Set_Date> set_Date = new();

        public List<Parameter_ShiftDemand> parameter_ShiftDemand = new();
        public List<Parameter_CrossGroup> parameter_CrossGroup = new();
        public List<Parameter_NightToDay> parameter_NightToDay = new();
        public List<Parameter_PreAssign> parameter_PreAssign = new();
        public List<Parameter_BackupGroup> parameter_BackupGroup = new();

        public List<Parameter_One> parameter_One = new();
        public List<Parameter_SixDayWindow> parameter_SixDayWindow = new();
        public List<Parameter_NightToDayWindow> parameter_NightToDayWindow = new();
        public List<Parameter_OffOneDayWindow> parameter_OffOneDayWindow = new();
        public List<Parameter_DoubleOffWindow> parameter_DoubleOffWindow = new();
        public List<Parameter_DoubleOffThreshold> parameter_DoubleOffThreshold = new();
        public List<Parameter_WeekendOffThreshold> parameter_WeekendOffThreshold = new();

        public List<Parameter_OffOneDayPenalty> parameter_OffOneDayPenalty = new();
        public List<Parameter_SixDayPenalty> parameter_SixDayPenalty = new();
        public List<Parameter_GroupMismatchPenalty> parameter_GroupMismatchPenalty = new();
        public List<Parameter_NightToDayPenalty> parameter_NightToDayPenalty = new();
        public List<Parameter_DoubleOffLT2Penalty> parameter_DoubleOffLT2Penalty = new();
        public List<Parameter_BelowAVGPenalty> parameter_BelowAVGPenalty = new();
        public List<Parameter_Weekend4DayPenalty> parameter_Weekend4DayPenalty = new();

        /// <summary>無參數 ctor 只做一件事：把預設來源餵給下面真正讀檔的 ctor。</summary>
        public Dataload() : this(new CsvDataSource()) { }

        /// <summary>標準接口：一行載一顆，只讀不算。換 CSV / Oracle / 記憶體只換 source。</summary>
        public Dataload(IDataSource source)
        {
            set_Employee = source.Load<Set_Employee>("Set_Employee");
            set_Group = source.Load<Set_Group>("Set_Group");
            set_Date = source.Load<Set_Date>("Set_Date");

            parameter_ShiftDemand = source.Load<Parameter_ShiftDemand>("Parameter_ShiftDemand");
            parameter_CrossGroup = source.Load<Parameter_CrossGroup>("Parameter_CrossGroup");
            parameter_NightToDay = source.Load<Parameter_NightToDay>("Parameter_NightToDay");
            parameter_PreAssign = source.Load<Parameter_PreAssign>("Parameter_PreAssign");
            parameter_BackupGroup = source.Load<Parameter_BackupGroup>("Parameter_BackupGroup");

            parameter_One = source.Load<Parameter_One>("Parameter_One");
            parameter_SixDayWindow = source.Load<Parameter_SixDayWindow>("Parameter_SixDayWindow");
            parameter_NightToDayWindow = source.Load<Parameter_NightToDayWindow>("Parameter_NightToDayWindow");
            parameter_OffOneDayWindow = source.Load<Parameter_OffOneDayWindow>("Parameter_OffOneDayWindow");
            parameter_DoubleOffWindow = source.Load<Parameter_DoubleOffWindow>("Parameter_DoubleOffWindow");
            parameter_DoubleOffThreshold = source.Load<Parameter_DoubleOffThreshold>("Parameter_DoubleOffThreshold");
            parameter_WeekendOffThreshold = source.Load<Parameter_WeekendOffThreshold>("Parameter_WeekendOffThreshold");

            parameter_OffOneDayPenalty = source.Load<Parameter_OffOneDayPenalty>("Parameter_OffOneDayPenalty");
            parameter_SixDayPenalty = source.Load<Parameter_SixDayPenalty>("Parameter_SixDayPenalty");
            parameter_GroupMismatchPenalty = source.Load<Parameter_GroupMismatchPenalty>("Parameter_GroupMismatchPenalty");
            parameter_NightToDayPenalty = source.Load<Parameter_NightToDayPenalty>("Parameter_NightToDayPenalty");
            parameter_DoubleOffLT2Penalty = source.Load<Parameter_DoubleOffLT2Penalty>("Parameter_DoubleOffLT2Penalty");
            parameter_BelowAVGPenalty = source.Load<Parameter_BelowAVGPenalty>("Parameter_BelowAVGPenalty");
            parameter_Weekend4DayPenalty = source.Load<Parameter_Weekend4DayPenalty>("Parameter_Weekend4DayPenalty");
        }

        /// <summary>
        /// import 模式：本專案沒有不規則外部來源（原始版本是用固定種子 Random(42) 程式生成範例排班資料，
        /// 不是攤平外部矩陣／報表），因此白名單外的生成邏輯（迴圈、Random、enum 掃描、日期運算）集中在這裡，
        /// 而不是 Dataload(IDataSource)。<paramref name="rawFile"/> 僅為配合三態 CLI 簽名一致，不讀取任何檔案。
        /// 所有數值與原始 Dataload() 完全一致，只是把「怎麼組織」改成 import → Export() → 求解只讀 CSV 的標準流程。
        /// </summary>
        public Dataload(string rawFile)
        {
            Logging.Info($"[Dataload] import 模式：以固定種子重新生成範例排班資料（來源引數 '{rawFile}' 僅供 CLI 介面一致，本專案資料為程式生成，不讀取外部檔案）。");

            // 罰分權重（與 Objective 的係數一一對應，數值與原始 Dataload 完全相同）
            double offOneDayPenalty = 0.1;
            double sixDayPenalty = 1;
            double groupMismatchPenalty = 0.2;
            double nightToDayPenalty = 0.2;
            double preGroupPenalty = 0.2;      // NightToDayRule 表 QTY 的快照值；目前未被任何 Constraint 讀取（見 Model.md）
            double belowAvgPenalty = 0.1;
            double doubleOffLT2Penalty = 0.1;
            double weekend4DayPenalty = 0.1;
            double backupGroupPenalty = 0;     // CrossGroup 表中 Backup 班別的權重快照值；同上未被讀取

            // 結構常數（原本是 Constraint 內的字面數字，現在資料化）
            double one = 1;
            double sixDayWindow = 6;
            double nightToDayWindow = 2;
            double offOneDayWindow = 3;
            double doubleOffWindow = 3;
            double doubleOffThreshold = 2;
            double weekendOffThreshold = 4;

            // 班別群組（O,D,E,N,C，宣告順序）
            var groupNames = Enum.GetNames(typeof(GroupE));

            // 人員群組
            (int Count, string Group)[] headcountByGroup = [(7, "D"), (5, "E"), (3, "N"), (1, "C")];
            int totalEmployeeCount = headcountByGroup.Sum(g => g.Count);
            var employeeNames = new List<string>();
            for (int e = 1; e <= totalEmployeeCount; e++)
                employeeNames.Add($"E{e}");

            // 跨組別上班成本（需特別設定）：休、主要、Backup 班別之外的班別，跨組別上班都要扣分
            var crossGroup = new List<Parameter_CrossGroup>();
            int employeeIndex = 1;
            foreach (var group in headcountByGroup)
            {
                var crossGroupOptions = groupNames.Where(name => name != group.Group && name != "O").ToList();
                for (int j = 0; j < group.Count; j++)
                {
                    foreach (var option in crossGroupOptions)
                        crossGroup.Add(new Parameter_CrossGroup { Employee = $"E{employeeIndex}", Group = option, QTY = groupMismatchPenalty });
                    employeeIndex++;
                }
            }

            // Backup 班別（需特別設定）
            var backupGroup = new List<Parameter_BackupGroup>
            {
                new Parameter_BackupGroup { Employee = "E1", Group = "C" },
            };

            // 找到該員工該班別的跨組別上班成本，調整為 Backup 成本
            foreach (var backup in backupGroup)
            {
                var matched = crossGroup.FirstOrDefault(w => w.Employee == backup.Employee && w.Group == backup.Group);
                if (matched != null)
                    matched.QTY = backupGroupPenalty;
            }

            // 前一天→今天班別違規組合表
            var nightToDayRules = new List<Parameter_NightToDay>
            {
                new Parameter_NightToDay { PreGroup = "N", Group = "D", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "N", Group = "E", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "N", Group = "C", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "E", Group = "D", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "E", Group = "C", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "D", Group = "N", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "E", Group = "N", QTY = preGroupPenalty },
                new Parameter_NightToDay { PreGroup = "C", Group = "N", QTY = preGroupPenalty },
            };

            // 每日各班別需求：既有種子（2026-01-01、2026-01-02）優先，其餘日期以固定種子隨機生成
            var shiftDemand = new List<Parameter_ShiftDemand>
            {
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 1), Group = "D", QTY = 4 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 1), Group = "E", QTY = 3 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 1), Group = "N", QTY = 2 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 1), Group = "C", QTY = 1 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 2), Group = "D", QTY = 5 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 2), Group = "E", QTY = 3 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 2), Group = "N", QTY = 2 },
                new Parameter_ShiftDemand { Date = new DateTime(2026, 1, 2), Group = "C", QTY = 1 },
            };
            var seededDates = new HashSet<DateTime>(shiftDemand.Select(s => s.Date));

            int year = 2026;
            int month = 1;
            int daysInMonth = DateTime.DaysInMonth(year, month);
            var random = new Random(42); // 固定種子：範本教學需要每次跑出同一份需求資料，才能對照解可重現

            var dateList = new List<DateTime>();
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                dateList.Add(date);

                if (seededDates.Contains(date)) continue; // 這天的需求由既有種子提供

                shiftDemand.Add(new Parameter_ShiftDemand { Date = date, Group = "D", QTY = random.Next(4, 6) });
                shiftDemand.Add(new Parameter_ShiftDemand { Date = date, Group = "E", QTY = 3 });
                shiftDemand.Add(new Parameter_ShiftDemand { Date = date, Group = "N", QTY = 2 });
                shiftDemand.Add(new Parameter_ShiftDemand { Date = date, Group = "C", QTY = 1 });
            }

            // 預排班（需特別設定）
            var preAssign = new List<Parameter_PreAssign>
            {
                new Parameter_PreAssign { Date = new DateTime(2026, 1, 1), Employee = "E1", Group = "E" },
                new Parameter_PreAssign { Date = new DateTime(2026, 1, 1), Employee = "E3", Group = "O" },
                new Parameter_PreAssign { Date = new DateTime(2026, 1, 2), Employee = "E2", Group = "D" },
                new Parameter_PreAssign { Date = new DateTime(2026, 1, 2), Employee = "E3", Group = "E" },
            };

            set_Employee = employeeNames.Select(Employee => new Set_Employee { Employee = Employee }).ToList();
            set_Group = groupNames.Select(Group => new Set_Group { Group = Group }).ToList();
            set_Date = dateList.Select(Date => new Set_Date { Date = Date }).ToList();

            parameter_ShiftDemand = shiftDemand;
            parameter_CrossGroup = crossGroup;
            parameter_NightToDay = nightToDayRules;
            parameter_PreAssign = preAssign;
            parameter_BackupGroup = backupGroup;

            parameter_One.Add(new Parameter_One { QTY = one });
            parameter_SixDayWindow.Add(new Parameter_SixDayWindow { QTY = sixDayWindow });
            parameter_NightToDayWindow.Add(new Parameter_NightToDayWindow { QTY = nightToDayWindow });
            parameter_OffOneDayWindow.Add(new Parameter_OffOneDayWindow { QTY = offOneDayWindow });
            parameter_DoubleOffWindow.Add(new Parameter_DoubleOffWindow { QTY = doubleOffWindow });
            parameter_DoubleOffThreshold.Add(new Parameter_DoubleOffThreshold { QTY = doubleOffThreshold });
            parameter_WeekendOffThreshold.Add(new Parameter_WeekendOffThreshold { QTY = weekendOffThreshold });

            parameter_OffOneDayPenalty.Add(new Parameter_OffOneDayPenalty { QTY = offOneDayPenalty });
            parameter_SixDayPenalty.Add(new Parameter_SixDayPenalty { QTY = sixDayPenalty });
            parameter_GroupMismatchPenalty.Add(new Parameter_GroupMismatchPenalty { QTY = groupMismatchPenalty });
            parameter_NightToDayPenalty.Add(new Parameter_NightToDayPenalty { QTY = nightToDayPenalty });
            parameter_DoubleOffLT2Penalty.Add(new Parameter_DoubleOffLT2Penalty { QTY = doubleOffLT2Penalty });
            parameter_BelowAVGPenalty.Add(new Parameter_BelowAVGPenalty { QTY = belowAvgPenalty });
            parameter_Weekend4DayPenalty.Add(new Parameter_Weekend4DayPenalty { QTY = weekend4DayPenalty });
        }

        /// <summary>把 import ctor 產生的資料輸出成求解流程使用的 Template CSV。</summary>
        public void Export()
        {
            CsvCtrl.WriteRows(set_Employee, "Set_Employee");
            CsvCtrl.WriteRows(set_Group, "Set_Group");
            CsvCtrl.WriteRows(set_Date, "Set_Date");

            CsvCtrl.WriteRows(parameter_ShiftDemand, "Parameter_ShiftDemand");
            CsvCtrl.WriteRows(parameter_CrossGroup, "Parameter_CrossGroup");
            CsvCtrl.WriteRows(parameter_NightToDay, "Parameter_NightToDay");
            CsvCtrl.WriteRows(parameter_PreAssign, "Parameter_PreAssign");
            CsvCtrl.WriteRows(parameter_BackupGroup, "Parameter_BackupGroup");

            CsvCtrl.WriteRows(parameter_One, "Parameter_One");
            CsvCtrl.WriteRows(parameter_SixDayWindow, "Parameter_SixDayWindow");
            CsvCtrl.WriteRows(parameter_NightToDayWindow, "Parameter_NightToDayWindow");
            CsvCtrl.WriteRows(parameter_OffOneDayWindow, "Parameter_OffOneDayWindow");
            CsvCtrl.WriteRows(parameter_DoubleOffWindow, "Parameter_DoubleOffWindow");
            CsvCtrl.WriteRows(parameter_DoubleOffThreshold, "Parameter_DoubleOffThreshold");
            CsvCtrl.WriteRows(parameter_WeekendOffThreshold, "Parameter_WeekendOffThreshold");

            CsvCtrl.WriteRows(parameter_OffOneDayPenalty, "Parameter_OffOneDayPenalty");
            CsvCtrl.WriteRows(parameter_SixDayPenalty, "Parameter_SixDayPenalty");
            CsvCtrl.WriteRows(parameter_GroupMismatchPenalty, "Parameter_GroupMismatchPenalty");
            CsvCtrl.WriteRows(parameter_NightToDayPenalty, "Parameter_NightToDayPenalty");
            CsvCtrl.WriteRows(parameter_DoubleOffLT2Penalty, "Parameter_DoubleOffLT2Penalty");
            CsvCtrl.WriteRows(parameter_BelowAVGPenalty, "Parameter_BelowAVGPenalty");
            CsvCtrl.WriteRows(parameter_Weekend4DayPenalty, "Parameter_Weekend4DayPenalty");
        }
    }
}
