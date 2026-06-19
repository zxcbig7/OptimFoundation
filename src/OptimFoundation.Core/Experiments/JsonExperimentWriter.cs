using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptimFoundation.Core
{
    /// <summary>
    /// 巢狀 JSON（含 config 快照與 convergence 軌跡），LLM 友善、機器可讀。
    /// 使用 System.Text.Json（net48 相容）。同名實驗 Read → 合併 → Write 整檔。
    /// </summary>
    public sealed class JsonExperimentWriter : IExperimentWriter
    {
        private static readonly JsonSerializerOptions _opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,        // 中文/符號直出，利人與 LLM 閱讀
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,  // 非 Optimal 的 NaN/Infinity
            Converters = { new JsonStringEnumConverter() }                // SolveStatus 以字串輸出
        };

        public ExpWriterType Extension => ExpWriterType.JSON;

        public void Write(Experiment experiment, string path)
        {
            string json = JsonSerializer.Serialize(experiment, _opts);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        public Experiment Read(string path)
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<Experiment>(json, _opts);
        }
    }
}
