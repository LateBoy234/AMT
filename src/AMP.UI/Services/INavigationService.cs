namespace AMP.UI.Services;

/// <summary>页面级导航请求（由 ShellViewModel 接收并切换当前内容区）。</summary>
public sealed record NavigateToViewRequest(Type ViewModelType);

/// <summary>
/// 页面级导航抽象：通过 Messenger 投递 <see cref="NavigateToViewRequest"/>，
/// 避免子 VM 直接依赖主窗口或 <see cref="IServiceProvider"/>。
/// </summary>
public interface INavigationService
{
    /// <summary>导航到已注册的视图模型（由 Shell 解析并赋给内容区）。</summary>
    void NavigateTo(Type viewModelType);
}
