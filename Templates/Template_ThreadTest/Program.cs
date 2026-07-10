using System.Text;
using OptimFoundation.Core;
using ThreadTest;

namespace MyApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            // ── Benders Decomposition（主要驗證）──
            using (BendersDecomposition benders = new BendersDecomposition())
            {
                benders.Execute();
                Logging.Info($"Benders 總時間:", benders.totalTimer);
            }

            // ── 功能驗證（Phase 1~7）──
            // using (ThreadTestProblem project = new ThreadTestProblem())
            // {
            //     project.Execute();
            //     Logging.Info($"整體運作時間:", project.totalTimer);
            // }
        }
    }
}
