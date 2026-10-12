# AMP — 自动化测量平台（架构骨架）

一个面向**多厂商运动控制器 + 多类型测量仪器**的 C# WPF 上位机架构骨架。
本仓库是「拿到项目如何定架构」的落地示例：先识别变化点与稳定点，在变化点后面放接口，
把稳定点做成领域模型，用分层 + DI 让**换厂商、换仪器、重排流程**都不改业务代码。

> 状态：可编译、可运行（数据库模拟全链路已通过冒烟测试）。无硬件即可演示完整测量流程。

---

## 1. 目录结构（7 个工程）

```
E:\AMP
├── AMP.slnx                  # 解决方案（.NET SDK 10 生成）
├── docs\
│   └── ARCHITECTURE.md       # 架构设计文档（分层依据、关键接口、状态机、演进路线）
└── src\
    ├── AMP.Domain                      # 领域层：纯 C# 类库，零外部依赖
    │   ├── Motion\                     #   MotionState 9 态枚举、IMotionController、轴位置
    │   ├── Instrument\                 #   IInstrument、MeasureData、LabValue
    │   ├── Flows\                      #   IFlowStep、FlowContext、FlowResult、PointSpec
    │   ├── Data\                       #   存储抽象：IMotionStateStore / IStandardDataStore / IMeasureResultStore
    │   ├── Rules\                      #   ISvdCorrectionRule / IMetrologyJudgeRule / IRepeatabilityRule + 骨架实现
    │   └── Logging\                    #   IAppLogger
    ├── AMP.Application                 # 应用层：流程编排 + 状态机（不依赖任何基础设施）
    │   ├── StateMachines\              #   MotionStateMachine（9 态状态模式）
    │   ├── Measurement\                #   PointMeasurementRunner（单点：运动+仪器接线）
    │   ├── Flows\                      #   三个流程 + FlowOrchestrator + FlowChainFactory
    │   └── DependencyInjection\        #   AddApplicationServices
    ├── AMP.Infrastructure.Data         # 数据基础设施：配置、SQLite、文件日志
    ├── AMP.Infrastructure.Motion       # 运动基础设施：VendorA/B 适配器(P/Invoke 骨架) + 数据库模拟
    ├── AMP.Infrastructure.Instrument   # 仪器基础设施：分光光度计/色差计适配器骨架 + 数据库模拟
    ├── AMP.UI                          # 表现层：WPF + CommunityToolkit.Mvvm（组合根在这里）
    └── AMP.SmokeTest                   # 无界面冒烟测试：装配 DI → 跑全流程链 → 校验状态机/落库
```

依赖方向：`UI → Application → Domain`，`UI → Infrastructure.*`；
Infrastructure 实现 Domain 接口并经 **DI 注入**（依赖倒置），Domain 不知道任何基础设施。

---

## 2. 四个变化点 → 四个接口（一句话）

| 需求里的变化点 | 接口 | 实现 | 切换方式 |
| --- | --- | --- | --- |
| 不同厂家三轴控制器（C++ DLL） | `IMotionController` | `VendorAMotionController` / `VendorBMotionController`（P/Invoke）/ `DatabaseSimulatedMotionController` | `appsettings.json` 的 `Motion.Vendor` |
| 多种被测量仪器 | `IInstrument` | `SpectrophotometerInstrument` / `ColorimeterInstrument` / `DatabaseSimulatedInstrument` | `appsettings.json` 的 `Instrument.Vendor` |
| 测量流程可单独/任意组合 | `IFlowStep` + `FlowOrchestrator` | `SvdCorrectionFlow` / `MetrologyJudgeFlow` / `RepeatabilityTestFlow` | `appsettings.json` 的 `FlowChain` 数组 |
| 数据/行为模拟 | 三个存储接口 + 配置 | `SqliteDataStore`（种子标准数据、持久化轴位置、落库测量结果） | `Database.SeedStandardDataOnFirstRun` 等 |

**默认全部走 Simulation**：`Motion.Vendor=Simulation`、`Instrument.Vendor=Simulation`，
打开程序即可演示；接真实 DLL/仪器时改配置即可，业务与界面代码零改动。

---

## 3. 测量流程

- 三个流程各自独立界面（SVD修正 / 计量判断 / 重复性测试），侧栏切换。
- `FlowChain` 配置决定组合：可单独（一项）、任意顺序（多项）。
- 组合流程运行时，每完成一步，`FlowNavigationService` 监听 `FlowOrchestrator.StepCompleted`
  事件自动跳转到下一个流程界面；链结束回到空闲。
- 任一流程页上的「开始（从本流程）」按钮从该流程在链中的位置开始执行。

默认链：`SVD修正 → 计量判断 → 重复性测试`。

---

## 4. 运动状态机（9 态，秒级）与流程编排（分钟级）分离

硬件级状态机 `MotionStateMachine`（状态模式，每个状态一个 handler，`CanTransitionTo` 校验迁移合法性）：

```
空闲 → XY移动 → XY到位 → Z轴下压 → 夹爪释放 → 测量 → 夹紧 → Z轴回退 → 完成 →（回）空闲
```

`PointMeasurementRunner` 把每个点位串成上述 9 态动作序列；`FlowOrchestrator` 在更高一层
把「点位」编排成流程（秒级 vs 分钟级两个状态机各管各的，互不干扰）。

---

## 5. 运行

```powershell
# 构建（.NET SDK 8 及以上）
dotnet build "E:\AMP\AMP.slnx" -c Debug

# 启动界面（默认数据库模拟，无硬件）
dotnet run --project "E:\AMP\src\AMP.UI\AMP.UI.csproj"

# 无界面冒烟测试（跑完整流程链，约 50 秒）
dotnet run --project "E:\AMP\src\AMP.SmokeTest\AMP.SmokeTest.csproj"
```

首次运行自动创建 `amp.db`（输出目录）并写入种子标准数据：SVD 砖块 8 行 × 31 通道、
计量标准 5 行 Lab、白板 1 行；运动轴位置持久化、测量结果落库。

---

## 6. 接入真实设备（路线）

1. **厂商 A DLL**：把 `Motion.Vendor` 改为 `VendorA`，将 `vendor_a_motion.dll`
   放到输出目录（当前为 P/Invoke 骨架：加载失败会给出明确日志，引导切回 Simulation）。
2. **厂商 B DLL**：改为 `VendorB`（句柄式 + StdCall 约定骨架）。
3. **真实仪器**：把 `Instrument.Vendor` 改为 `Spectrophotometer` / `Colorimeter`，
   在对应适配器里实现协议（当前连接显式失败，避免静默误连）。
4. 每个厂商 = **新增一个适配器类 + 改一行配置**，Domain/Application/UI 不动。

详见 `docs\ARCHITECTURE.md`。
