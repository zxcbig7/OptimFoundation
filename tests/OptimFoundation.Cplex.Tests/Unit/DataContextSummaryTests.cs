using System.Collections.Generic;
using OptimFoundation.Core;
using Xunit;

namespace OptimFoundation.Cplex.Tests.Unit
{
    // DataContext.GroupSetsByInstance 是純函式：直接餵登記序列（模擬 RegisterSet 的呼叫順序），
    // 驗證「同一顆 set 實例被多個名稱登記」在載入摘要只列一次、N 為真實 set 顆數（見框架資料防護規格追補）。
    public class DataContextSummaryTests
    {
        private sealed class Set_DcsGroup : SetBase<string> { }

        private static Set_DcsGroup BuildSet(params string[] members)
        {
            var s = new Set_DcsGroup();
            s.LoadInline(members);
            return s;
        }

        // (a) 無別名：N 顆各自獨立，各自無附註
        [Fact]
        public void GroupSetsByInstance_NoAliases_EachSetListedOnceWithNoAnnotation()
        {
            var product = BuildSet("Desk", "Chair");
            var region = BuildSet("North", "South", "East");

            var groups = DataContext.GroupSetsByInstance(new (string, ISetBrick)[]
            {
                ("Product", product),
                ("Region", region),
            });

            Assert.Equal(2, groups.Count);
            Assert.Equal("Product", groups[0].PrimaryName);
            Assert.Empty(groups[0].Aliases);
            Assert.Equal("Region", groups[1].PrimaryName);
            Assert.Empty(groups[1].Aliases);
        }

        // (b) 同一顆 set 實例被兩個名稱登記（[OptDim<Set_Group>("PreGroup")] 這類自訂維度別名）→
        // 只列一次，主名 = 最早登記名稱，第二個名稱併為別名附註；N（真實 set 顆數）不因別名多算。
        [Fact]
        public void GroupSetsByInstance_SameInstanceRegisteredTwice_CountsOnceWithAliasAnnotation()
        {
            var groupSet = BuildSet("D", "E", "N", "C");

            var groups = DataContext.GroupSetsByInstance(new (string, ISetBrick)[]
            {
                ("Group", groupSet),     // 主名：型別推導名，先註冊
                ("PreGroup", groupSet),  // 別名：自訂維度名，後註冊，指向同一實例
            });

            var group = Assert.Single(groups);   // Sets（N）的 N＝1，不是 2
            Assert.Equal("Group", group.PrimaryName);
            Assert.Equal(4, group.Set.Count);
            Assert.Equal(new[] { "PreGroup" }, group.Aliases);
        }

        // (c) 三個名稱都指向同一實例（Node/NODE/node 這類多重別名）→ 仍只算一顆，兩個別名都附註列出
        [Fact]
        public void GroupSetsByInstance_ThreeNamesSameInstance_CountsOnceWithBothAliases()
        {
            var nodeSet = BuildSet("N1", "N2");

            var groups = DataContext.GroupSetsByInstance(new (string, ISetBrick)[]
            {
                ("Node", nodeSet),
                ("NODE", nodeSet),
                ("node", nodeSet),
            });

            var group = Assert.Single(groups);
            Assert.Equal("Node", group.PrimaryName);
            Assert.Equal(new[] { "NODE", "node" }, group.Aliases);
        }

        // (d) 不同實例即使成員內容相同，仍各算一顆（分組依 reference equality，非成員內容/名稱）
        [Fact]
        public void GroupSetsByInstance_DifferentInstancesSameContent_CountedSeparately()
        {
            var a = BuildSet("X", "Y");
            var b = BuildSet("X", "Y");

            var groups = DataContext.GroupSetsByInstance(new (string, ISetBrick)[]
            {
                ("A", a),
                ("B", b),
            });

            Assert.Equal(2, groups.Count);
        }
    }
}
