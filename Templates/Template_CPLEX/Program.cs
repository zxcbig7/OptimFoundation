
using OptimFoundation.Core;
using SandBox;

namespace MyApp
{
    internal class Program
    {

        static void Main(string[] args)
        {

            using (RosteringProblem project = new RosteringProblem())
            {
                project.Execute();
                Logging.Info($"整體運作時間:", project.totalTimer);
            }
        }
    }
}