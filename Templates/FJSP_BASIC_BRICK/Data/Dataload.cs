using System.Globalization;
using OptimFoundation.Core;
using OptimFoundation.Core.IO;

namespace FJSP_BASIC_BRICK
{
    /// <summary>資料唯一入口：把 Data/*.csv 讀成積木，再由 DataContext 驗證。</summary>
    public sealed partial class Dataload : DataContext
    {
        public List<Set_Lot> set_Lot = new();
        public Set_Operation set_Operation = new();   // 行序＝加工順序（RoutePrecedence／MakespanDef 依此索引）
        public List<Set_Eqp> set_Eqp = new();

        public List<Parameter_ProcessTime> parameter_ProcessTime = new();
        public List<Parameter_ExactlyOne> parameter_ExactlyOne = new();
        public List<Parameter_MakespanFloor> parameter_MakespanFloor = new();
        public List<Parameter_SoftMakespanTarget> parameter_SoftMakespanTarget = new();
        public List<Parameter_MakespanPenalty> parameter_MakespanPenalty = new();
        public List<Parameter_NoOverlapForwardOffset> parameter_NoOverlapForwardOffset = new();
        public List<Parameter_NoOverlapBackwardOffset> parameter_NoOverlapBackwardOffset = new();

        /// <summary>BigM＝Σ_{lot,op} max_eqp ProcessTime（最壞情況全序列排程長度上界）；純粹 max/min 彙總，由數據推導、NEVER 寫死。</summary>
        public double BigM => parameter_ProcessTime
            .GroupBy(p => new { p.Lot, p.Operation })
            .Sum(g => g.Max(p => p.QTY));

        /// <summary>Range 規劃窗上界＝最壞情況上界，保證放大實例仍可行；非綁定 demo 值。</summary>
        public double MakespanDeadline => BigM;

        /// <summary>
        /// Phase 3 demo variant 專用：保證 infeasible 的 Makespan 上限（＝理論下界 − 1；"− 1" 為 Model.md 定義的結構偏移，非資料）。
        /// 任一 lot 的 makespan 下界＝Σ_op min_eqp ProcessTime，取所有 lot 下界的最大值再 − 1。
        /// </summary>
        public double InfeasibleMakespanCap => parameter_ProcessTime
            .GroupBy(p => new { p.Lot, p.Operation })
            .Select(g => new { g.Key.Lot, MinTime = g.Min(p => p.QTY) })
            .GroupBy(x => x.Lot)
            .Max(g => g.Sum(x => x.MinTime)) - 1;

        //public Dataload() : this(new DbDataSource()) { }
        public Dataload() : this(new CsvDataSource()) { }

        /// <summary>讀取已就位的 canonical CSV；此建構子只做資料載入。</summary>
        public Dataload(IDataSource source)
        {
            set_Lot = source.Load<Set_Lot>("Set_Lot");
            set_Operation = source.Load<Set_Operation>("Set_Operation");
            set_Eqp = source.Load<Set_Eqp>("Set_Eqp");
            parameter_ProcessTime = source.Load<Parameter_ProcessTime>("Parameter_ProcessTime");
            parameter_ExactlyOne = source.Load<Parameter_ExactlyOne>("Parameter_ExactlyOne");
            parameter_MakespanFloor = source.Load<Parameter_MakespanFloor>("Parameter_MakespanFloor");
            parameter_SoftMakespanTarget = source.Load<Parameter_SoftMakespanTarget>("Parameter_SoftMakespanTarget");
            parameter_MakespanPenalty = source.Load<Parameter_MakespanPenalty>("Parameter_MakespanPenalty");
            parameter_NoOverlapForwardOffset = source.Load<Parameter_NoOverlapForwardOffset>("Parameter_NoOverlapForwardOffset");
            parameter_NoOverlapBackwardOffset = source.Load<Parameter_NoOverlapBackwardOffset>("Parameter_NoOverlapBackwardOffset");
        }

        /// <summary>
        /// import 模式：把不規則的「實例生成規格」（規模＋seed＋範圍）攤平成 seeded、決定論的標準 CSV。
        /// rawFile 相對於 Data/、不帶副檔名；內容見 Data/raw/FJSP_Instance.csv（表頭 Lots,Operations,Eqps,Seed,MinHours,MaxHours）。
        /// 這是全專案唯一允許出現迴圈與 Random 的地方；求解路徑（上面的 IDataSource ctor）只讀已就位的 CSV。
        /// </summary>
        public Dataload(string rawFile)
        {
            var data = new CsvDataSource().LoadData(rawFile);
            if (data.Rows.Count < 1)
                throw new InvalidDataException("FJSP import requires a header and one data row.");
            string Read(string column) => data.Rows[0][column]?.ToString()?.Trim() ?? string.Empty;
            int lots = int.Parse(Read("Lots"), CultureInfo.InvariantCulture);
            int operations = int.Parse(Read("Operations"), CultureInfo.InvariantCulture);
            int eqps = int.Parse(Read("Eqps"), CultureInfo.InvariantCulture);
            int seed = int.Parse(Read("Seed"), CultureInfo.InvariantCulture);
            int minHours = int.Parse(Read("MinHours"), CultureInfo.InvariantCulture);
            int maxHours = int.Parse(Read("MaxHours"), CultureInfo.InvariantCulture);

            var lotNames = Enumerable.Range(1, lots).Select(l => $"LOT{l}").ToList();
            var operationNames = Enumerable.Range(1, operations).Select(o => $"OP{o}").ToList();
            var eqpNames = Enumerable.Range(1, eqps).Select(e => $"EQP{e}").ToList();

            set_Lot = lotNames.Select(Lot => new Set_Lot { Lot = Lot }).ToList();
            set_Operation = operationNames.Select(Operation => new Set_Operation { Operation = Operation }).ToList();
            set_Eqp = eqpNames.Select(Eqp => new Set_Eqp { Eqp = Eqp }).ToList();

            var rng = new Random(seed);
            foreach (var lot in lotNames)
                foreach (var operation in operationNames)
                    foreach (var eqp in eqpNames)
                        parameter_ProcessTime.Add(new Parameter_ProcessTime
                        {
                            Lot = lot,
                            Operation = operation,
                            Eqp = eqp,
                            QTY = rng.Next(minHours, maxHours + 1),
                        });

            parameter_ExactlyOne.Add(new Parameter_ExactlyOne { QTY = 1.0 });
            parameter_MakespanFloor.Add(new Parameter_MakespanFloor { QTY = 0.0 });
            parameter_SoftMakespanTarget.Add(new Parameter_SoftMakespanTarget { QTY = 10.0 });
            parameter_MakespanPenalty.Add(new Parameter_MakespanPenalty { QTY = 2.0 });
            parameter_NoOverlapForwardOffset.Add(new Parameter_NoOverlapForwardOffset { QTY = 3.0 });
            parameter_NoOverlapBackwardOffset.Add(new Parameter_NoOverlapBackwardOffset { QTY = 2.0 });
        }

        /// <summary>把 import ctor 產生的資料輸出成求解流程使用的 canonical CSV。</summary>
        public void Export()
        {
            CsvCtrl.WriteRows(set_Lot, "Set_Lot");
            CsvCtrl.WriteRows(set_Operation, "Set_Operation");
            CsvCtrl.WriteRows(set_Eqp, "Set_Eqp");
            CsvCtrl.WriteRows(parameter_ProcessTime, "Parameter_ProcessTime");
            CsvCtrl.WriteRows(parameter_ExactlyOne, "Parameter_ExactlyOne");
            CsvCtrl.WriteRows(parameter_MakespanFloor, "Parameter_MakespanFloor");
            CsvCtrl.WriteRows(parameter_SoftMakespanTarget, "Parameter_SoftMakespanTarget");
            CsvCtrl.WriteRows(parameter_MakespanPenalty, "Parameter_MakespanPenalty");
            CsvCtrl.WriteRows(parameter_NoOverlapForwardOffset, "Parameter_NoOverlapForwardOffset");
            CsvCtrl.WriteRows(parameter_NoOverlapBackwardOffset, "Parameter_NoOverlapBackwardOffset");
        }
    }
}
