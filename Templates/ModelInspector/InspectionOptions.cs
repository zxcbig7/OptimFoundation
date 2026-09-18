using System.Text.RegularExpressions;

namespace ModelInspector
{
    /// <summary>命令列參數。模型檔路徑為必填，其餘旋鈕都有預設值。</summary>
    public sealed class InspectionOptions
    {
        /// <summary>模型檔絕對路徑（.lp / .mps / .sav，含各自的 .gz / .bz2）。</summary>
        public string ModelFile { get; private set; } = "";

        /// <summary>報告與框架輸出檔的名稱前綴；預設取模型檔名（不含副檔名）。</summary>
        public string Label { get; private set; } = "";

        /// <summary>求解時間上限（秒）。</summary>
        public double TimeLimit { get; private set; } = 60;

        /// <summary>相對 MIP gap 停止門檻。0 = 求到最佳。</summary>
        public double MipGap { get; private set; } = 0;

        /// <summary>執行緒數；null = 用 CPLEX 預設。</summary>
        public int? Threads { get; private set; }

        /// <summary>亂數種子；null = 用 CPLEX 預設。</summary>
        public int? Seed { get; private set; }

        /// <summary>是否記錄收斂軌跡。掛 callback 會關閉 CPLEX dynamic search，量效能時應關掉。</summary>
        public bool CaptureTrajectory { get; private set; } = true;

        /// <summary>solver log 是否即時印到 Console；關掉仍完整寫進框架 log 檔。</summary>
        public bool SolverLog { get; private set; } = true;

        /// <summary>Console 每個區塊最多印幾筆明細；完整內容一律進報告檔。</summary>
        public int MaxPrint { get; private set; } = 20;

        /// <summary>解析參數。回傳 false 時 error 帶原因，呼叫端應印用法後結束。</summary>
        public static bool TryParse(string[] args, out InspectionOptions options, out string error)
        {
            options = new InspectionOptions();
            error = "";

            if (args.Length == 0)
            {
                error = "缺少模型檔路徑。";
                return false;
            }

            var positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--no-trajectory":
                        options.CaptureTrajectory = false;
                        continue;
                    case "--quiet":
                        options.SolverLog = false;
                        continue;
                }

                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    positional.Add(arg);
                    continue;
                }

                if (i + 1 >= args.Length)
                {
                    error = $"參數 {arg} 缺少值。";
                    return false;
                }

                string value = args[++i];
                switch (arg)
                {
                    case "--name":
                        options.Label = value;
                        break;
                    case "--timelimit":
                        if (!TryPositiveDouble(arg, value, out double timeLimit, out error)) return false;
                        options.TimeLimit = timeLimit;
                        break;
                    case "--mipgap":
                        if (!double.TryParse(value, out double mipGap) || mipGap < 0 || mipGap >= 1)
                        {
                            error = $"參數 {arg} 需要 [0, 1) 的數值，收到 '{value}'。";
                            return false;
                        }
                        options.MipGap = mipGap;
                        break;
                    case "--threads":
                        if (!TryPositiveInt(arg, value, out int threads, out error)) return false;
                        options.Threads = threads;
                        break;
                    case "--seed":
                        if (!int.TryParse(value, out int seed))
                        {
                            error = $"參數 {arg} 需要整數，收到 '{value}'。";
                            return false;
                        }
                        options.Seed = seed;
                        break;
                    case "--max-print":
                        if (!TryPositiveInt(arg, value, out int maxPrint, out error)) return false;
                        options.MaxPrint = maxPrint;
                        break;
                    default:
                        error = $"未知參數 {arg}。";
                        return false;
                }
            }

            if (positional.Count == 0)
            {
                error = "缺少模型檔路徑。";
                return false;
            }
            if (positional.Count > 1)
            {
                error = $"只接受一個模型檔，收到 {positional.Count} 個：{string.Join(", ", positional)}。";
                return false;
            }

            // ImportModel 對相對路徑的基準是執行檔的 Models/ 資料夾，跟使用者打指令時的位置不同；
            // 這裡先相對 CWD 展開成絕對路徑，讓「跨專案指向另一個 bin 目錄」這個主要用法能直接work。
            options.ModelFile = Path.GetFullPath(positional[0]);

            if (!File.Exists(options.ModelFile))
            {
                error = $"找不到模型檔：{options.ModelFile}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.Label))
                options.Label = DefaultLabel(options.ModelFile);

            return true;
        }

        // 框架匯出的檔名已經帶了用途與時間戳（RosteringProblem_SAV_2026-08-30_17-36-04.sav），
        // 直接拿來當前綴會讓本次的輸出檔疊上第二組時間戳。剝掉那一段，只留專案名。
        private static readonly Regex FrameworkStamp =
            new Regex(@"_(LP|MPS|SAV|Solution|IIS)_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$", RegexOptions.IgnoreCase);

        private static string DefaultLabel(string modelFile)
        {
            string name = Path.GetFileNameWithoutExtension(modelFile);
            // .lp.gz 這類雙副檔名會留下一層，再剝一次
            if (Path.HasExtension(name)) name = Path.GetFileNameWithoutExtension(name);
            return FrameworkStamp.Replace(name, "");
        }

        private static bool TryPositiveInt(string arg, string value, out int result, out string error)
        {
            error = "";
            if (int.TryParse(value, out result) && result > 0) return true;
            error = $"參數 {arg} 需要正整數，收到 '{value}'。";
            return false;
        }

        private static bool TryPositiveDouble(string arg, string value, out double result, out string error)
        {
            error = "";
            if (double.TryParse(value, out result) && result > 0) return true;
            error = $"參數 {arg} 需要正數，收到 '{value}'。";
            return false;
        }

        /// <summary>印出用法。</summary>
        public static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("用法：ModelInspector <model-file> [options]");
            Console.WriteLine();
            Console.WriteLine("  <model-file>        .lp / .mps / .sav（含 .gz / .bz2）；相對路徑以目前工作目錄為基準");
            Console.WriteLine();
            Console.WriteLine("  --name <label>      報告與輸出檔的名稱前綴（預設：模型檔名）");
            Console.WriteLine("  --timelimit <sec>   求解時間上限，預設 60");
            Console.WriteLine("  --mipgap <v>        相對 gap 門檻 [0,1)，預設 0（求到最佳）");
            Console.WriteLine("  --threads <n>       執行緒數（預設用 CPLEX 自己的）");
            Console.WriteLine("  --seed <n>          亂數種子（預設用 CPLEX 自己的）");
            Console.WriteLine("  --max-print <n>     Console 每區塊最多印幾筆，預設 20（報告檔一律完整）");
            Console.WriteLine("  --no-trajectory     不記錄收斂軌跡（量效能時建議關掉，callback 會關閉 dynamic search）");
            Console.WriteLine("  --quiet             solver log 不印到 Console（仍完整寫進 log 檔）");
            Console.WriteLine();
        }
    }
}
