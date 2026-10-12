using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using AMP.Application.Flows;
using AMP.Application.StateMachines;
using AMP.Domain.Instrument;
using AMP.Domain.Logging;
using AMP.Domain.Motion;
using AMP.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace AMP.UI.ViewModels;

/// <summary>侧栏导航项。</summary>
public sealed record ShellNavigationItem(string Title, string Description, Type ViewModelType);

/// <summary>
/// 主壳视图模型：承载当前流程界面（ContentControl 内容区）、侧栏导航、
 /// 底部状态条（运动状态 + 轴坐标 + 连接状态）与全局「开始全流程/停止」。
/// </summary>
public partial class ShellViewModel : ObservableRecipient
{
    private readonly IServiceProvider _services;
    private readonly INavigationService _navigation;
    private readonly IMotionController _motion;
    private readonly IInstrument _instrument;
    private readonly MotionStateMachine _stateMachine;
    private readonly FlowOrchestrator _orchestrator;
    private readonly FlowContextFactory _contextFactory;
    private readonly IAppLogger _logger;
    private readonly Dispatcher _dispatcher;

    public ShellViewModel(
        IServiceProvider services,
        IMessenger messenger,
        INavigationService navigation,
        IMotionController motion,
        IInstrument instrument,
        MotionStateMachine stateMachine,
        FlowOrchestrator orchestrator,
        FlowContextFactory contextFactory,
        IAppLogger logger,
        RunLogStore logStore)
        : base(messenger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
        _instrument = instrument ?? throw new ArgumentNullException(nameof(instrument));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(logStore);

        Logs = logStore.Lines;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        MotionBadge = $"运动控制：{motion.VendorName}";
        InstrumentBadge = $"仪器：{instrument.VendorName}";

        NavigationItems =
        [
            new("SVD修正", "流程一：色砖测量与修正矩阵", typeof(SvdCorrectionViewModel)),
            new("计量判断", "流程二：样品计量判级", typeof(MetrologyJudgeViewModel)),
            new("重复性测试", "流程三：重复性评估", typeof(RepeatabilityTestViewModel)),
        ];

        Messenger.Register<ShellViewModel, NavigateToViewRequest>(this, static (r, m) => r.ApplyNavigate(m));
        _stateMachine.StateChanged += OnMotionStateChanged;
        _motion.PositionChanged += OnPositionChanged;

        // 启动后自动连接运动平台与仪器（模拟模式必定成功，真机未接入时给出日志）。
        _ = ConnectDevicesAsync();

        NavigateTo(typeof(SvdCorrectionViewModel));
    }

    /// <summary>侧栏导航项。</summary>
    public ObservableCollection<ShellNavigationItem> NavigationItems { get; }

    /// <summary>共享运行日志。</summary>
    public ObservableCollection<RunLogLine> Logs { get; }

    [ObservableProperty]
    private ObservableObject? _currentViewModel;

    [ObservableProperty]
    private ShellNavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private string _motionStateText = "空闲";

    [ObservableProperty]
    private string _axisText = "X=0.0  Y=0.0  Z=0.0";

    [ObservableProperty]
    private string _motionStatusText = "连接中…";

    [ObservableProperty]
    private string _instrumentStatusText = "连接中…";

    [ObservableProperty]
    private string _motionBadge = string.Empty;

    [ObservableProperty]
    private string _instrumentBadge = string.Empty;

    /// <summary>侧栏点击 → 切换界面。</summary>
    partial void OnSelectedNavigationItemChanged(ShellNavigationItem? value)
    {
        if (value is not null)
        {
            NavigateTo(value.ViewModelType);
        }
    }

    /// <summary>从流程链第一个步骤开始运行全部流程。</summary>
    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (_orchestrator.IsRunning)
        {
            return;
        }

        await _orchestrator.RunAsync(_contextFactory.Create(), 0);
    }

    [RelayCommand]
    private void StopAll() => _orchestrator.Stop();

    private void ApplyNavigate(NavigateToViewRequest request)
    {
        _dispatcher.Invoke(() =>
        {
            var viewModel = _services.GetService(request.ViewModelType) as ObservableObject;
            if (viewModel is null)
            {
                return;
            }

            CurrentViewModel = viewModel;
            SelectedNavigationItem = NavigationItems.FirstOrDefault(item => item.ViewModelType == request.ViewModelType);
        });
    }

    private void NavigateTo(Type viewModelType) => _navigation.NavigateTo(viewModelType);

    private async Task ConnectDevicesAsync()
    {
        var motionResult = await _motion.ConnectAsync().ConfigureAwait(false);
        _logger.Info(motionResult.Message);
        _dispatcher.Invoke(() => MotionStatusText = motionResult.IsSuccess ? "运动：已连接" : "运动：未连接");

        var instrumentResult = await _instrument.ConnectAsync().ConfigureAwait(false);
        _logger.Info(instrumentResult.Message);
        _dispatcher.Invoke(() => InstrumentStatusText = instrumentResult.IsSuccess ? "仪器：已连接" : "仪器：未连接");
    }

    private void OnMotionStateChanged(object? sender, MotionStateChangedEventArgs e)
        => _dispatcher.Invoke(() => MotionStateText = e.State.ToDisplayName());

    private void OnPositionChanged(object? sender, AxisPositionChangedEventArgs e)
        => _dispatcher.Invoke(() => AxisText = $"X={e.Position.X:F1}  Y={e.Position.Y:F1}  Z={e.Position.Z:F1}");
}
