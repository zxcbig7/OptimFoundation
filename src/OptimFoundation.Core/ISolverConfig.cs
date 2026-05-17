namespace OptimFoundation.Core
{
    public interface ISolverConfig
    {
        double? TimeLimit { get; set; }
        double? MipGap { get; set; }
        int? Threads { get; set; }
        bool LogToConsole { get; set; }
        string LogFilePath { get; set; }
    }
}
