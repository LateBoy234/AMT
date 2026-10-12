# AMP 架构设计文档

> 配套 `README.md`。本文档回答三件事：**为什么这样分层**、**关键接口长什么样**、
> **换厂商/换仪器/重排流程时改哪里**，以及**已知限制与演进路线**。

---

## 1. 需求回顾与判据

| 需求 | 性质 |
| --- | --- |
| 不同厂家的三轴控制器，各提供 C++ DLL；也可数据库模拟 | **变化点** |
| 被测量仪器多种类型；也可数据库模拟 | **变化点** |
| 三大流程：SVD修正 → 计量判断 → 重复性测试；可单独/任意组合；每流程独立界面；组合时一步完成自动跳下一步 | **变化点**（组合方式可变） |
| 运动控制 9 态固定序列 | **稳定点**（状态机） |

**结论：分层是刚需，分模块要克制。**
判据：在变化点后面放接口（适配器/策略），把稳定点做成领域模型（状态机、规则、流程契约）。
分得太细会引入与业务无关的抽象成本；本项目的模块粒度 = 变化点数量 + 一个编排层。

---

## 2. 分层与依赖方向

```
┌─────────────────────────────────────────────────────────────┐
│ AMP.UI（表现层）                                              │
│  WPF 页面/VM、导航、日志面板、组合根 AddAmpServices           │
│  依赖：Application + Infrastructure.* + Domain（仅类型）      │
└──────────────────────────┬──────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ AMP.Application（应用层）                                    │
│  流程编排 FlowOrchestrator / 9 态状态机 / 单点执行器 / 三个流程 │
│  只依赖 Domain 接口，不知道任何厂商/数据库/UI                 │
└──────────────────────────┬──────────────────────────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────┐
│ AMP.Domain（领域层，零依赖）                                  │
│  IMotionController / IInstrument / IFlowStep / 存储接口 / 规则 │
│  MotionState 9 态枚举 / MeasureData / FlowContext / FlowResult │
└─────────────────────────────────────────────────────────────┘
        ▲                                    ▲
        │ 实现接口（DIP）                    │ 实现接口（DIP）
┌───────┴──────────────┐        ┌───────────┴──────────────┐
│ AMP.Infrastructure.Motion│        │ AMP.Infrastructure.Instrument│
│  VendorA / VendorB 适配器 │        │  Spectrophotometer / Colorimeter│
│  DatabaseSimulatedMotion  │        │  DatabaseSimulatedInstrument    │
└───────────────────────┘        └──────────────────────────┘
        ▲                                    ▲
        └──────────── AMP.Infrastructure.Data（配置/SQLite/日志，被上两者引用）┘
```

- **单向依赖**：上层只能依赖下层；Domain 与 Infrastructure 靠接口解耦（DIP）。
- **组合根在 UI**：`AddAmpServices(options)` 一处完成全部装配，把配置、日志、
  导航、页面串起来。
- **模拟/真实切换 = 换 DI 注册**（实际就是改配置一行），业务代码零改动。

---

## 3. 关键接口设计

### 3.1 IMotionController（运动，变化点①）

```csharp
public interface IMotionController
{
    string VendorName { get; }
    bool IsConnected { get; }
    AxisPosition Position { get; }
    event EventHandler<AxisPositionChangedEventArgs>? PositionChanged;

    Task<OperationResult> ConnectAsync(CancellationToken ct = default);
    Task<OperationResult> DisconnectAsync(CancellationToken ct = default);
    Task<OperationResult> HomeAsync(CancellationToken ct = default);
    Task<OperationResult> MoveXYAsync(double x, double y, CancellationToken ct = default);
    Task<OperationResult> PressZAsync(double z, CancellationToken ct = default);
    Task<OperationResult> ReleaseGripperAsync(CancellationToken ct = default);
    Task<OperationResult> ClampGripperAsync(CancellationToken ct = default);
    Task<OperationResult> RetractZAsync(double distance, CancellationToken ct = default);
    Task<OperationResult> StopAsync(CancellationToken ct = default);
}
```

- 契约化：所有动作返回 `OperationResult`（IsSuccess + Message），事件通知位置变化。
- **实现一（真实）**：`VendorAMotionController` / `VendorBMotionController`，
  每个厂商一个适配器（适配器模式），内部 `DllImport` 调厂商 C++ DLL，
  把各自不同的错误码/调用约定翻译成统一 `OperationResult`。
- **实现二（模拟）**：`DatabaseSimulatedMotionController`，轴位置持久化 SQLite，
  分步延时模拟运动动画，可并发取消。

### 3.2 IInstrument（仪器，变化点②）

```csharp
public interface IInstrument
{
    string VendorName { get; }
    string ModelName { get; }
    bool IsConnected { get; }
    Task<OperationResult> ConnectAsync(CancellationToken ct = default);
    Task<OperationResult> DisconnectAsync(CancellationToken ct = default);
    Task<OperationResult> CalibrateAsync(CalibrationKind kind, CancellationToken ct = default);
    Task<MeasureData> MeasureAsync(string? standardSet = null, CancellationToken ct = default);
}
```

`MeasureData` 承载多通道测量值 + 可选 Lab，真实项目为光谱 31/36 通道。

### 3.3 IFlowStep + FlowOrchestrator（流程，变化点③）

```csharp
public interface IFlowStep
{
    string Key { get; }
    string Name { get; }
    string Description { get; }
    Task<FlowResult> ExecuteAsync(FlowContext context, CancellationToken ct);
}
```

- 每个流程是一个 `IFlowStep`（策略），`FlowChainFactory` 按配置的 Key 列表组装成链。
- `FlowOrchestrator.RunAsync(context, startStepIndex)`：
  - `startStepIndex = 0` → 组合全流程；`= n` → 从第 n 步开始（单独/部分组合）；
  - 顺序执行，步骤间可被 `Stop()` 取消；
  - 每步完成触发 `StepCompleted` 事件 —— **UI 的 FlowNavigationService 订阅它自动跳转下一界面**；
  - 全部完成触发 `RunFinished`。

### 3.4 存储抽象（模拟数据底座）

```csharp
public interface IMotionStateStore    // 轴位置持久化（重启恢复）
public interface IStandardDataStore   // 标准数据（SVD砖块/计量标准/白板）
public interface IMeasureResultStore  // 测量结果落库（追溯）
```

`SqliteDataStore` 一个类实现三个接口；首次运行自动建表 + 种子数据。

### 3.5 规则（计量领域模型）

`ISvdCorrectionRule` / `IMetrologyJudgeRule` / `IRepeatabilityRule` 放在 Domain，
骨架实现（正规方程+岭正则 / ΔE 阈值判级 / 样本标准差）供上层使用；
真实项目可换 MathNet.Numerics 的 SVD 分解等，接口与结果契约不变。

---

## 4. 两个状态机为什么必须分开

| | 运动状态机 `MotionStateMachine` | 流程编排 `FlowOrchestrator` |
| --- | --- | --- |
| 粒度 | 单点动作：XY移动→到位→Z下压→夹爪释放→测量→夹紧→Z回退→完成 | 多个点位组成的业务步骤：SVD修正→计量判断→重复性测试 |
| 时间尺度 | 秒级（每个点位几秒） | 分钟级（整个链约 50 秒） |
| 生命周期 | 一个点位执行一次完整迁移 | 一次运行执行多个步骤 |
| 谁驱动 | `PointMeasurementRunner` 按点位触发 | UI/冒烟测试触发，订阅事件推进 |

9 态用**状态模式**实现（每个状态一个 handler，`CanTransitionTo` 校验合法迁移），
比 switch 分支更易扩展（如增加"人工确认"态）且状态迁移规则一目了然。

---

## 5. 配置（appsettings.json）—— 所有变化点在这里切换

```jsonc
{
  "Motion":  { "Vendor": "Simulation",  ... },   // VendorA / VendorB / Simulation
  "Instrument": { "Vendor": "Simulation", ... }, // Spectrophotometer / Colorimeter / Simulation
  "FlowChain": ["SvdCorrection", "MetrologyJudge", "RepeatabilityTest"], // 可单独/任意组合
  "Flow": { "SvdBrickCount": 8, "MetrologySampleCount": 5, "RepeatCount": 10, ... },
  "Database": { "FileName": "amp.db", "SeedStandardDataOnFirstRun": true },
  "Logging":  { "Directory": "Logs" }
}
```

`AmtOptions` → `OptionsStore.Load()` 读取；`FlowOptions.ToDomain()` 映射为领域层 `FlowRunOptions`。

---

## 6. 已知限制与演进路线

1. **SVD 残差为 0（8 砖过拟合）**：8 块砖 × 31 通道远少于未知量（31×31），
   最小二乘可精确拟合每个目标通道 → RMSE≈0 属过拟合而非 bug。
   **演进**：增加砖块数（如 24 色卡），或把修正矩阵约束为对角/带状，或换 MathNet SVD 伪逆。
2. **厂商 DLL 为 P/Invoke 骨架**：只定义了调用约定与错误码翻译，未接真实驱动。
   **演进**：按厂商 SDK 文档填充 `DllImport` 签名与初始化序列。
3. **仪器适配器连接显式失败**：避免静默误连真实设备。
   **演进**：实现真实协议（串口/网络）。
4. **配置即代码**：`Vendor` 是字符串开关。**演进**：可改为数据库配置表/启动参数。
5. **单点串行**：流程链串行执行。**演进**：多点位并发（注意夹爪/Z 轴互斥），
   需要把 `PointMeasurementRunner` 的并发控制与状态机迁移规则一起设计。
