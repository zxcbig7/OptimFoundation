using System;

namespace OptimFoundation.Core
{
    /// <summary>
    /// blessed 建構路徑：new + generator 註冊 + ValidateData 一次到位（見框架資料防護規格）。
    /// 專案端改用 OptData.Load(() => new Dataload(source)) 取代直接 new Dataload(source)。
    /// </summary>
    public static class OptData
    {
        public static T Load<T>(Func<T> factory) where T : DataContext
        {
            var instance = factory();
            instance.Initialize();
            return instance;
        }
    }
}
