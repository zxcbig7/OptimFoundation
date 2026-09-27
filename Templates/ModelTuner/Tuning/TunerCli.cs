using System.Globalization;

namespace ModelTuner
{
    /// <summary>命令列參數解析與用法。錯誤一律印用法後回傳 2。</summary>
    public static class TunerCli
    {
        public const double DefaultTuneBudgetSeconds = 600;
        public const int DefaultTuneRepeat = 3;

        public static bool TryParseRound(string[] args, out int round)
        {
            round = -1;
            if (args.Length > 1 && int.TryParse(args[1], out round) && round >= 0) return true;

            Console.Error.WriteLine($"參數錯誤：{args[0]} 需要 round 編號（非負整數）。");
            PrintUsage();
            return false;
        }

        public static bool TryParseTune(string[] args, out double budgetSeconds, out int repeat)
        {
            budgetSeconds = DefaultTuneBudgetSeconds;
            repeat = DefaultTuneRepeat;
            for (int i = 1; i < args.Length; i++)
            {
                bool ok = args[i] == "--repeat"
                    ? i + 1 < args.Length && int.TryParse(args[++i], out repeat) && repeat >= 1
                    : double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out budgetSeconds) && budgetSeconds > 0;
                if (ok) continue;

                Console.Error.WriteLine($"參數錯誤：cplex-tune 無法解析 '{args[i]}'。");
                PrintUsage();
                return false;
            }
            return true;
        }

        public static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("用法：dotnet run -- <mode> [args]");
            Console.WriteLine();
            Console.WriteLine("  (無參數) production：productionBaseline 解 Instances/tune 每個模型檔");
            Console.WriteLine("  lock S0 契約凍結：模型檔指紋寫進 instances.lock");
            Console.WriteLine("  exp <N> 跑 Program.cs 的 R<N> 區塊 → archive 到 Experiments/ → 產 facts");
            Console.WriteLine("  holdout <N> <label> R<N> 的 baseline 與 champion 在 holdout seeds / instances 重跑");
            Console.WriteLine("  facts <N|rN|rN-holdout|experiment> 從 archive 重產 TUNING-FACTS 與彙總");
            Console.WriteLine($"  cplex-tune [總秒數] [--repeat n] CPLEX 內建 tuning tool（預設 {DefaultTuneBudgetSeconds}s、單檔 repeat {DefaultTuneRepeat}）");
            Console.WriteLine();
        }
    }
}
