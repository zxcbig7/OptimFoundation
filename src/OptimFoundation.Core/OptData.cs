using System;

namespace OptimFoundation.Core
{
    /// <summary>
    /// blessed 建構路徑：new + generator 註冊 + ValidateData 一次到位（見框架資料防護規格）。
    /// 專案端改用 OptData.Load(() => new Dataload(source)) 取代直接 new Dataload(source)。
    /// </summary>
    public static class OptData
    {
        /// <summary>
        /// 建立 Dataload 並完成初始化：跑 factory → 登記 sets / params → 驗資料。
        /// ALWAYS 走這條路徑取得 Dataload，直接 new 會跳過驗證，錯誤資料要到求解階段才炸。
        /// </summary>
        /// <param name="factory">建立 Dataload 的委派，例：() =&gt; new Dataload(source)。</param>
        /// <exception cref="DataValidationException">資料有問題（dangling index / 重複鍵 / 型別不符 / FullGrid 缺列…），一次列出全部。</exception>
        public static T Load<T>(Func<T> factory) where T : DataContext
        {
            var instance = factory();
            instance.Initialize();
            return instance;
        }
    }
}
