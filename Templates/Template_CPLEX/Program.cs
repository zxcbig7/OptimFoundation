
using OptimFoundation.Core;
using SandBox;

namespace MyApp
{
    internal class Program
    {

        static void Main(string[] args)
        {
            // tuning 實驗環境示範：dotnet run -- experiment
            if (args.Length > 0 && args[0] == "experiment")
            {
                ExperimentDemo.Run();
                return;
            }

            using (RosteringProblem project = new RosteringProblem())
            {
                project.Execute();
                Logging.Info($"整體運作時間:", project.totalTimer);
            }
        }
    }
}