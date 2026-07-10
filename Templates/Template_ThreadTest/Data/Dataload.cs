using OptimFoundation.Core;

namespace ThreadTest.Data
{
    public class Dataload
    {
        // Sets
        public List<string> Sources = new List<string>();
        public List<string> Dests = new List<string>();

        // Parameters
        public List<Parameter_Cost> parameter_Cost = new List<Parameter_Cost>();
        public Dictionary<string, double> Supply = new Dictionary<string, double>();
        public Dictionary<string, double> Demand = new Dictionary<string, double>();

        public Dataload()
        {
            Sources.AddRange(new[] { "A", "B" });
            Dests.AddRange(new[] { "D1", "D2" });

            Supply["A"] = 10;
            Supply["B"] = 8;

            Demand["D1"] = 7;
            Demand["D2"] = 11;

            parameter_Cost.Add(new Parameter_Cost("A", "D1", 2.0));
            parameter_Cost.Add(new Parameter_Cost("A", "D2", 3.0));
            parameter_Cost.Add(new Parameter_Cost("B", "D1", 1.0));
            parameter_Cost.Add(new Parameter_Cost("B", "D2", 4.0));
        }
    }
}
