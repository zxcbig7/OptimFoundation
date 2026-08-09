using System;
using System.Collections.Generic;
using System.Linq;

namespace OptimFoundation.Core
{
    /// <summary>Creates a data context through the project's chosen constructor.</summary>
    public static class OptData
    {
        public static T Load<T>(System.Func<T> factory)
        {
            var value = factory();
            if (value is DataContext data)
            {
                data.Initialize();
                data.Freeze();
            }
            return value;
        }
    }

    /// <summary>Flattened generated Parameter row used by validation and diagnostics.</summary>
    public sealed class ParamRow
    {
        public object[] Index { get; }
        public (string Name, double Value)[] Numbers { get; }
        public ParamRow(object[] index, (string Name, double Value)[] numbers)
        {
            Index = index;
            Numbers = numbers;
        }
    }

    public sealed class ParamRegistration
    {
        public string Name { get; }
        public string[] IndexFields { get; }
        public IReadOnlyList<ParamRow> Rows { get; }
        public int RowCount => Rows.Count;
        public ParamRegistration(string name, string[] indexFields, IReadOnlyList<ParamRow> rows)
        {
            Name = name;
            IndexFields = indexFields;
            Rows = rows;
        }
    }

    /// <summary>Base class for Dataload. Set and Parameter rows are owned by the project as List&lt;T&gt;.</summary>
    public abstract class DataContext
    {
        private bool _isFrozen;
        private readonly List<ParamRegistration> _params = new();

        protected void RegisterParam<T>(
            IReadOnlyList<T> rows,
            string[] indexFields,
            Func<T, object[]> indexOf,
            Func<T, (string Name, double Value)[]> numbersOf)
            where T : ModelElementBase
        {
            GuardMutation(typeof(T).Name);
            _params.Add(new ParamRegistration(
                typeof(T).Name,
                indexFields,
                rows.Select(row => new ParamRow(indexOf(row), numbersOf(row))).ToArray()));
        }

        protected virtual void RegisterAll() { }

        internal void Initialize()
        {
            RegisterAll();
            ValidateData();
        }

        internal void Freeze() => _isFrozen = true;

        protected void GuardMutation(string member)
        {
            if (_isFrozen)
                throw new InvalidOperationException($"DataContext member '{member}' is frozen; 模型建構階段不得修改資料。");
        }

        protected void ValidateData()
        {
            var issues = DataValidator.Validate(_params);
            if (issues.Count > 0) throw new DataValidationException(issues);

            Logging.Info("===== Data load summary =====");
            Logging.Info($"Parameters ({_params.Count}):");
            foreach (var parameter in _params)
                Logging.Info($"  {parameter.Name}: index=[{string.Join(",", parameter.IndexFields)}], rows={parameter.RowCount}");
        }
    }
}
