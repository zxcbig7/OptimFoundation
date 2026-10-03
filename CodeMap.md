# OptimFoundation Code Map

## File Index

索引涵蓋 tracked source、tests、Templates 與 project；排除 generated、外部套件與未追蹤 probe。

| 路徑 | 檔案數 | 責任 |
| --- | --- | --- |
| `Templates/FJSP_BASIC_BRICK` | 28 | 彈性製程排程、soft constraint 與 IIS 示範 |
| `Templates/RosteringProblem` | 46 | 排班限制式與解驗證 |
| `Templates/Sudoku_SHC279` | 19 | 多維集合與 Sudoku 題盤匯入 |
| `Templates/TSP_MultiDimSet` | 18 | 稀疏弧集合與 TSP |
| `Templates/Tutorial` | 22 | B/C/I 變數、參數與標準組裝教學 |
| `src/OptimFoundation.Core` | 25 | solver-neutral 建模、命名、資料載入、IO、logging、實驗紀錄 |
| `src/OptimFoundation.Cplex` | 7 | CPLEX adapter、參數套用、專案與實驗執行 |
| `src/OptimFoundation.Generators` | 3 | Set/Parameter/Variable 與 DataContext 註冊碼生成 |
| `tests/OptimFoundation.Cplex.Tests` | 25 | Core、generator 與 CPLEX 整合測試 |

## Dependency Graph

箭頭表示依賴；Core 不引用 solver SDK。

| 來源 | 依賴 |
| --- | --- |
| Cplex | Core、ILOG.Concert、ILOG.CPLEX |
| Generators | Roslyn；linked source：Core/VariablePrefixNaming.cs |
| Core、Cplex、Templates | Generators（Analyzer，依各 csproj） |
| Templates | Cplex |
| Tests | Core、Cplex、Generators（Analyzer）、xUnit |

## 執行路徑

- `OptData.Load`：載入資料 → 註冊集合與參數 → Freeze。
- `OptModel`：variables → objective → constraints → MIP start；也可由 `ReadModel` 匯入既有模型。
- `OptProject.Solve` 與 `OptExperiment.Run` 共用 `OptEngine.RunModel`：Build → ApplyTo → beforeSolve → Trial.Capture。
- `EngineBase` 管理變數、算式 pool、soft constraints 與模型統計；`OptEngine` 實作 solver primitives。
- `Experiment.Save` 輸出 trial、meta、可選 summary 與 trajectory CSV；同名實驗覆寫，鎖檔時另存。
- `FolderDir` 管理 Input、Output、Log、Model、IIS、Solution、Experiment；保留期清理排除 Input 與 Experiment。

## Symbol Index

以檔案定位型別；public API 契約見 XML comments 與 `specs/developer-guide.md`。

| 檔案 | 型別 |
| --- | --- |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_AssignOneEqp.cs` | `Constraint_AssignOneEqp` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_CompleteDef.cs` | `Constraint_CompleteDef` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanDef.cs` | `Constraint_MakespanDef` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanInfeasibleCap.cs` | `Constraint_MakespanInfeasibleCap` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanTargetSoft.cs` | `Constraint_MakespanTargetSoft` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_MakespanWindow.cs` | `Constraint_MakespanWindow` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_NoOverlap.cs` | `Constraint_NoOverlap` |
| `Templates/FJSP_BASIC_BRICK/Constraint/Constraint_RoutePrecedence.cs` | `Constraint_RoutePrecedence` |
| `Templates/FJSP_BASIC_BRICK/Data/Dataload.cs` | `Dataload` |
| `Templates/FJSP_BASIC_BRICK/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_ExactlyOne.cs` | `Parameter_ExactlyOne` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_MakespanFloor.cs` | `Parameter_MakespanFloor` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_MakespanPenalty.cs` | `Parameter_MakespanPenalty` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_NoOverlapBackwardOffset.cs` | `Parameter_NoOverlapBackwardOffset` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_NoOverlapForwardOffset.cs` | `Parameter_NoOverlapForwardOffset` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_ProcessTime.cs` | `Parameter_ProcessTime` |
| `Templates/FJSP_BASIC_BRICK/Parameter/Parameter_SoftMakespanTarget.cs` | `Parameter_SoftMakespanTarget` |
| `Templates/FJSP_BASIC_BRICK/Program.cs` | `Program` |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Eqp.cs` | `Set_Eqp` |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Lot.cs` | `Set_Lot` |
| `Templates/FJSP_BASIC_BRICK/Set/Set_Operation.cs` | `Set_Operation` |
| `Templates/FJSP_BASIC_BRICK/Solution/FJSP_BASIC_BRICKSolution.cs` | `FJSP_BASIC_BRICKSolution` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableB_Assign.cs` | `VariableB_Assign` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableB_Precede.cs` | `VariableB_Precede` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Complete.cs` | `VariableC_Complete` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Makespan.cs` | `VariableC_Makespan` |
| `Templates/FJSP_BASIC_BRICK/Variable/VariableC_Start.cs` | `VariableC_Start` |
| `Templates/RosteringProblem/Constraint/Constraint_BelowAVG.cs` | `Constraint_BelowAVG` |
| `Templates/RosteringProblem/Constraint/Constraint_CrossGroup.cs` | `Constraint_CrossGroup` |
| `Templates/RosteringProblem/Constraint/Constraint_DoubleOffLT2.cs` | `Constraint_DoubleOffLT2` |
| `Templates/RosteringProblem/Constraint/Constraint_FullfillDemand.cs` | `Constraint_FullfillDemand` |
| `Templates/RosteringProblem/Constraint/Constraint_NightToDay.cs` | `Constraint_NightToDay` |
| `Templates/RosteringProblem/Constraint/Constraint_OffOneDay.cs` | `Constraint_OffOneDay` |
| `Templates/RosteringProblem/Constraint/Constraint_OneGroup.cs` | `Constraint_OneGroup` |
| `Templates/RosteringProblem/Constraint/Constraint_PreAssign.cs` | `Constraint_PreAssign` |
| `Templates/RosteringProblem/Constraint/Constraint_SixDayWork.cs` | `Constraint_SixDayWork` |
| `Templates/RosteringProblem/Constraint/Constraint_WeekendLT4.cs` | `Constraint_WeekendLT4` |
| `Templates/RosteringProblem/Data/Dataload.cs` | `Dataload` |
| `Templates/RosteringProblem/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` |
| `Templates/RosteringProblem/Parameter/Parameter_BackupGroup.cs` | `Parameter_BackupGroup` |
| `Templates/RosteringProblem/Parameter/Parameter_BelowAVGPenalty.cs` | `Parameter_BelowAVGPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_CrossGroup.cs` | `Parameter_CrossGroup` |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffLT2Penalty.cs` | `Parameter_DoubleOffLT2Penalty` |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffThreshold.cs` | `Parameter_DoubleOffThreshold` |
| `Templates/RosteringProblem/Parameter/Parameter_DoubleOffWindow.cs` | `Parameter_DoubleOffWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_GroupMismatchPenalty.cs` | `Parameter_GroupMismatchPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDay.cs` | `Parameter_NightToDay` |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDayPenalty.cs` | `Parameter_NightToDayPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_NightToDayWindow.cs` | `Parameter_NightToDayWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_OffOneDayPenalty.cs` | `Parameter_OffOneDayPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_OffOneDayWindow.cs` | `Parameter_OffOneDayWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_One.cs` | `Parameter_One` |
| `Templates/RosteringProblem/Parameter/Parameter_PreAssign.cs` | `Parameter_PreAssign` |
| `Templates/RosteringProblem/Parameter/Parameter_ShiftDemand.cs` | `Parameter_ShiftDemand` |
| `Templates/RosteringProblem/Parameter/Parameter_SixDayPenalty.cs` | `Parameter_SixDayPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_SixDayWindow.cs` | `Parameter_SixDayWindow` |
| `Templates/RosteringProblem/Parameter/Parameter_Weekend4DayPenalty.cs` | `Parameter_Weekend4DayPenalty` |
| `Templates/RosteringProblem/Parameter/Parameter_WeekendOffThreshold.cs` | `Parameter_WeekendOffThreshold` |
| `Templates/RosteringProblem/Program.cs` | `Program` |
| `Templates/RosteringProblem/Set/Set_Date.cs` | `Set_Date` |
| `Templates/RosteringProblem/Set/Set_Employee.cs` | `Set_Employee` |
| `Templates/RosteringProblem/Set/Set_Group.cs` | `Set_Group` |
| `Templates/RosteringProblem/Solution/RosteringProblemSolution.cs` | `RosteringProblemSolution` |
| `Templates/RosteringProblem/Variable/VariableB_DoubleOffFlag.cs` | `VariableB_DoubleOffFlag` |
| `Templates/RosteringProblem/Variable/VariableB_DoubleOffLT2.cs` | `VariableB_DoubleOffLT2` |
| `Templates/RosteringProblem/Variable/VariableB_GroupMismatch.cs` | `VariableB_GroupMismatch` |
| `Templates/RosteringProblem/Variable/VariableB_NightToDay.cs` | `VariableB_NightToDay` |
| `Templates/RosteringProblem/Variable/VariableB_Off1Day.cs` | `VariableB_Off1Day` |
| `Templates/RosteringProblem/Variable/VariableB_ShiftAssign.cs` | `VariableB_ShiftAssign` |
| `Templates/RosteringProblem/Variable/VariableB_SixDayWork.cs` | `VariableB_SixDayWork` |
| `Templates/RosteringProblem/Variable/VariableC_BelowAVG.cs` | `VariableC_BelowAVG` |
| `Templates/RosteringProblem/Variable/VariableC_WeekendLT4.cs` | `VariableC_WeekendLT4` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_BlockDigit.cs` | `Constraint_BlockDigit` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_CellValue.cs` | `Constraint_CellValue` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_ColumnDigit.cs` | `Constraint_ColumnDigit` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_Given.cs` | `Constraint_Given` |
| `Templates/Sudoku_SHC279/Constraint/Constraint_RowDigit.cs` | `Constraint_RowDigit` |
| `Templates/Sudoku_SHC279/Data/Dataload.cs` | `Dataload` |
| `Templates/Sudoku_SHC279/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` |
| `Templates/Sudoku_SHC279/Parameter/Parameter_ExactlyOne.cs` | `Parameter_ExactlyOne` |
| `Templates/Sudoku_SHC279/Parameter/Parameter_ObjCoef.cs` | `Parameter_ObjCoef` |
| `Templates/Sudoku_SHC279/Program.cs` | `Program` |
| `Templates/Sudoku_SHC279/Set/Set_Block.cs` | `Set_Block` |
| `Templates/Sudoku_SHC279/Set/Set_BlockCell.cs` | `Set_BlockCell` |
| `Templates/Sudoku_SHC279/Set/Set_Column.cs` | `Set_Column` |
| `Templates/Sudoku_SHC279/Set/Set_Digit.cs` | `Set_Digit` |
| `Templates/Sudoku_SHC279/Set/Set_Given.cs` | `Set_Given` |
| `Templates/Sudoku_SHC279/Set/Set_Row.cs` | `Set_Row` |
| `Templates/Sudoku_SHC279/Solution/Sudoku_SHC279Solution.cs` | `Sudoku_SHC279Solution` |
| `Templates/Sudoku_SHC279/Variable/VariableB_CellDigit.cs` | `VariableB_CellDigit` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_CustomerInDegree.cs` | `Constraint_CustomerInDegree` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_CustomerOutDegree.cs` | `Constraint_CustomerOutDegree` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_DepotInDegree.cs` | `Constraint_DepotInDegree` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_DepotOutDegree.cs` | `Constraint_DepotOutDegree` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_SubtourMTZ.cs` | `Constraint_SubtourMTZ` |
| `Templates/TSP_MultiDimSet/Constraint/Constraint_VisitOrderRange.cs` | `Constraint_VisitOrderRange` |
| `Templates/TSP_MultiDimSet/Data/Dataload.cs` | `Dataload` |
| `Templates/TSP_MultiDimSet/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` |
| `Templates/TSP_MultiDimSet/Parameter/Parameter_ArcCost.cs` | `Parameter_ArcCost` |
| `Templates/TSP_MultiDimSet/Program.cs` | `Program` |
| `Templates/TSP_MultiDimSet/Set/Set_Arc.cs` | `Set_Arc` |
| `Templates/TSP_MultiDimSet/Set/Set_Customer.cs` | `Set_Customer` |
| `Templates/TSP_MultiDimSet/Set/Set_Depot.cs` | `Set_Depot` |
| `Templates/TSP_MultiDimSet/Set/Set_Node.cs` | `Set_Node` |
| `Templates/TSP_MultiDimSet/Solution/TSP_MultiDimSetSolution.cs` | `TSP_MultiDimSetSolution` |
| `Templates/TSP_MultiDimSet/Variable/VariableB_UseArc.cs` | `VariableB_UseArc` |
| `Templates/TSP_MultiDimSet/Variable/VariableC_VisitOrder.cs` | `VariableC_VisitOrder` |
| `Templates/Tutorial/Constraint/Constraint_BatchDef.cs` | `Constraint_BatchDef` |
| `Templates/Tutorial/Constraint/Constraint_Capacity.cs` | `Constraint_Capacity` |
| `Templates/Tutorial/Constraint/Constraint_Demand.cs` | `Constraint_Demand` |
| `Templates/Tutorial/Constraint/Constraint_SetupLink.cs` | `Constraint_SetupLink` |
| `Templates/Tutorial/Data/Dataload.cs` | `Dataload` |
| `Templates/Tutorial/Objective/ObjectiveFunction.cs` | `ObjectiveFunction` |
| `Templates/Tutorial/Parameter/Parameter_BatchSize.cs` | `Parameter_BatchSize` |
| `Templates/Tutorial/Parameter/Parameter_Capacity.cs` | `Parameter_Capacity` |
| `Templates/Tutorial/Parameter/Parameter_Demand.cs` | `Parameter_Demand` |
| `Templates/Tutorial/Parameter/Parameter_MachineHours.cs` | `Parameter_MachineHours` |
| `Templates/Tutorial/Parameter/Parameter_SetupCost.cs` | `Parameter_SetupCost` |
| `Templates/Tutorial/Parameter/Parameter_UnitProfit.cs` | `Parameter_UnitProfit` |
| `Templates/Tutorial/Program.cs` | `Program` |
| `Templates/Tutorial/Set/Set_Date.cs` | `Set_Date` |
| `Templates/Tutorial/Set/Set_Machine.cs` | `Set_Machine` |
| `Templates/Tutorial/Set/Set_Product.cs` | `Set_Product` |
| `Templates/Tutorial/Set/Set_Shift.cs` | `Set_Shift` |
| `Templates/Tutorial/Solution/TutorialSolution.cs` | `TutorialSolution` |
| `Templates/Tutorial/Variable/VariableB_Setup.cs` | `VariableB_Setup` |
| `Templates/Tutorial/Variable/VariableC_Produce.cs` | `VariableC_Produce` |
| `Templates/Tutorial/Variable/VariableI_Batch.cs` | `VariableI_Batch` |
| `src/OptimFoundation.Core/DataContext.cs` | `DataIssueKind`, `DataIssue`, `DataValidator`, `OptData`, `ParamRow`, `SetRegistration`, `ParamRegistration`, `DataContext`, `ParameterLookupExtensions` |
| `src/OptimFoundation.Core/EngineBase.cs` | `ISolverConfig`, `ISolverEngine`, `ISpecialConstraints`, `SolveStatus`, `VarType`, `ConstraintSense`, `ObjectiveSense`, `ModelType`, `OptBounds`, `EngineBase` |
| `src/OptimFoundation.Core/Experiments/ConfigSnapshot.cs` | `ConfigSnapshot` |
| `src/OptimFoundation.Core/Experiments/ExpCsvWriter.cs` | `CsvExperimentWriter`, `MetaCsvWriter`, `SummaryCsvWriter`, `TrajectoryCsvWriter`, `ExperimentCsv` |
| `src/OptimFoundation.Core/Experiments/Experiment.cs` | `Experiment`, `ITrajectorySource`, `Trial`, `ConfigSummary`, `BaselineComparison`, `BaselineComparer` |
| `src/OptimFoundation.Core/Experiments/SolveMetrics.cs` | `SolveMetrics`, `ConvergencePoint` |
| `src/OptimFoundation.Core/IO/IDataSource.cs` | `IDataSource`, `ISolutionSink`, `ISolutionBatch` |
| `src/OptimFoundation.Core/IO/InMemoryDataSource.cs` | `InMemoryDataSource` |
| `src/OptimFoundation.Core/IO/ModelRowMapper.cs` | `ModelRowMapper` |
| `src/OptimFoundation.Core/IO/TabularData.cs` | `TabularData` |
| `src/OptimFoundation.Core/IO/csv/CsvCtrl.cs` | `CsvCtrl` |
| `src/OptimFoundation.Core/IO/csv/CsvDataSource.cs` | `CsvDataSource`, `CsvSolutionSink` |
| `src/OptimFoundation.Core/IO/db/DbCtrlBase.cs` | `DbCtrlBase` |
| `src/OptimFoundation.Core/IO/db/DbDataSource.cs` | `DbDataSource` |
| `src/OptimFoundation.Core/IO/db/IDbCtrl.cs` | `IDbCtrl` |
| `src/OptimFoundation.Core/IO/db/OracleDbCtrl.cs` | `OracleDbCtrl`, `OracleSolutionSink` |
| `src/OptimFoundation.Core/Infrastructure/ClassInfo.cs` | `ReflectionHelper`, `ClassInfo` |
| `src/OptimFoundation.Core/Infrastructure/FolderDir.cs` | `FolderDir`, `ProjFolder` |
| `src/OptimFoundation.Core/Infrastructure/Logging.cs` | `Logging` |
| `src/OptimFoundation.Core/Infrastructure/ProjectConfig.cs` | `ProjectConfig` |
| `src/OptimFoundation.Core/ModelElementBase.cs` | `ModelElementBase`, `SetRowBase`, `ParameterBase`, `VariableBase`, `ConstraintBase` |
| `src/OptimFoundation.Core/ModelNaming.cs` | `ModelNaming` |
| `src/OptimFoundation.Core/VariableManager.cs` | `VariableManager` |
| `src/OptimFoundation.Core/VariablePrefixNaming.cs` | `VariablePrefixNaming` |
| `src/OptimFoundation.Cplex/CplexConfig.cs` | `CplexConfig` |
| `src/OptimFoundation.Cplex/OptEngine.Configuration.cs` | `OptEngine` |
| `src/OptimFoundation.Cplex/OptEngine.cs` | `OptEngine` |
| `src/OptimFoundation.Cplex/OptExperiment.cs` | `OptExperiment` |
| `src/OptimFoundation.Cplex/OptModel.cs` | `OptModel` |
| `src/OptimFoundation.Cplex/OptProject.cs` | `OptProject` |
| `src/OptimFoundation.Generators/AutoSetsGenerator.cs` | `AutoSetsGenerator`, `OptSetAttribute`, `OptParamAttribute`, `OptVarAttribute`, `OptDimAttribute` |
| `src/OptimFoundation.Generators/IsExternalInit.cs` | `IsExternalInit` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ExperimentIntegrationTests.cs` | `ExperimentIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ModelImportIntegrationTests.cs` | `ModelImportIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/OptEngineIntegrationTests.cs` | `OptEngineIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/ProjectScopeIntegrationTests.cs` | `ProjectScopeIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolutionPipelineIntegrationTests.cs` | `SolutionPipelineIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamCoverageTests.cs` | `SolverParamCoverageTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/SolverParamValueMatrixTests.cs` | `SolverParamValueMatrixTests` |
| `tests/OptimFoundation.Cplex.Tests/Integration/UnreferencedVariableIntegrationTests.cs` | `UnreferencedVariableIntegrationTests` |
| `tests/OptimFoundation.Cplex.Tests/Mocks/MockEngine.cs` | `MockEngine`, `MockConfig` |
| `tests/OptimFoundation.Cplex.Tests/Mocks/TestModels.cs` | `VarS`, `VarDG`, `VarInt`, `VariableB_Pick`, `VariableC_Amt`, `VariableI_Cnt`, `VariableX_LegacyAmt`, `VariableY_LegacyCnt`, `VariableC_ArcFlow`, `VariableC_ArcFlowByDate`, `VariableC_ArcFlowWrongArity`, `Set_Arc`, `Constraint_Test`, `ParamX` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ConfigSummaryTests.cs` | `ConfigSummaryTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/DbCtrlBaseTransactionTests.cs` | `FakeDbTransaction`, `FakeDbConnection`, `TestableDbCtrl`, `DbCtrlBaseTransactionTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/EngineBaseTests.cs` | `EngineBaseTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ExperimentSaveTests.cs` | `ExperimentSaveTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/FakeDbCtrl.cs` | `FakeDbCtrl` |
| `tests/OptimFoundation.Cplex.Tests/Unit/GeneratorNumericCoverageTests.cs` | `Set_GncItem`, `Parameter_GncProfit`, `Parameter_GncScalar`, `VariableC_GncAmount`, `VariableI_GncCount`, `GncDataload`, `GeneratorNumericCoverageTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ModelElementBaseTests.cs` | `ModelElementBaseTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ObservabilityTests.cs` | `ObservabilityTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/OracleSolutionSinkTests.cs` | `OracleSolutionSinkTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/PoolSemanticsAndMipStartTests.cs` | `PoolSemanticsAndMipStartTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/RunnerSymmetryTests.cs` | `RunnerSymmetryTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/ScaleGuardTests.cs` | `ScaleGuardTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/StringOverloadParityTests.cs` | `StringOverloadParityTests` |
| `tests/OptimFoundation.Cplex.Tests/Unit/VariableManagerTests.cs` | `VariableManagerTests` |

## Build

```powershell
dotnet build OptimFoundation.sln
dotnet test tests/OptimFoundation.Cplex.Tests/OptimFoundation.Cplex.Tests.csproj
```

CPLEX 由 `CplexDir` 指定；預設為 `C:\IBM\ILOG\CPLEX_Studio2211`。Templates 可個別使用 `dotnet build <csproj>`。

## Review Scope Notes

- 註解調整須保留 API 契約、數學式、單位、參數範圍、非直觀原因及平台限制。
- Generator 字串只有輸出註解可調整；不得改變 executable tokens 或 diagnostics。
- 不修改 Markdown 規格正文、TuningHistory、generated、外部套件或相鄰 AI-Modeling。
