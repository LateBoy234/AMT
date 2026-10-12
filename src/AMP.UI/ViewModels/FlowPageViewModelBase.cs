using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using AMP.Application.Flows;
using AMP.Domain.Flows;
using AMP.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AMP.UI.ViewModels;

/// <summary>
/// 流程页视图模型基类：提供「从本流程开始运行流程链 / 停止」、
/// 本流程摘要回填与共享运行日志（三个流程页共用同一份日志与同一编排器）。
/// </summary>
public abstract partial class FlowPageViewModelBase : ObservableObject
{
    private readonly FlowOrchestrator _orchestrator;
    private readonly FlowContextFactory _contextFactory;
    private readonly Dispatcher _dispatcher;

    protected FlowPageViewModelBase(
        FlowOrchestrator orchestrator,
        FlowContextFactory contextFactory,
        RunLogStore logStore)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        ArgumentNullException.ThrowIfNull(logStore);

        Logs = logStore.Lines;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _orchestrator.StepCompleted += OnStepCompleted;
        _orchestrator.RunFinished += OnRunFinished;
    }

    /// <summary>共享运行日志行。</summary>
    public ObservableCollection<RunLogLine> Logs { get; }

    /// <summary>本流程最近一次运行的摘要行。</summary>
    public ObservableCollection<string> Summary { get; } = [];

    /// <summary>流程步骤 Key（与配置一致）。</summary>
    public abstract string Key { get; }

    /// <summary>界面标题。</summary>
    public abstract string Title { get; }

    /// <summary>界面说明。</summary>
    public abstract string Description { get; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>从本流程在流程链中的位置开始运行（组合流程完成后由导航服务自动跳转）。</summary>
    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var index = FindIndex();
        if (index < 0)
        {
            StatusText = $"「{Title}」不在流程链配置中，无法从本页开始运行。";
            return;
        }

        IsRunning = true;
        StatusText = "运行中…";
        Summary.Clear();
        await _orchestrator.RunAsync(_contextFactory.Create(), index);
    }

    [RelayCommand]
    private void Stop() => _orchestrator.Stop();

    /// <summary>本流程在流程链中的下标；不在链中返回 -1。</summary>
    private int FindIndex()
    {
        for (var i = 0; i < _orchestrator.Chain.Count; i++)
        {
            if (_orchestrator.Chain[i].Key == Key)
            {
                return i;
            }
        }

        return -1;
    }

    private void OnStepCompleted(object? sender, FlowStepCompletedEventArgs e)
    {
        if (e.Step.Key != Key)
        {
            return;
        }

        _dispatcher.Invoke(() =>
        {
            Summary.Clear();
            foreach (var line in e.Result.Summary)
            {
                Summary.Add(line);
            }

            StatusText = e.Result.IsSuccess ? e.Result.Message : $"失败：{e.Result.Message}";
        });
    }

    private void OnRunFinished(object? sender, FlowRunFinishedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            IsRunning = false;
            StatusText = e.Result.Message;
        });
    }
}
