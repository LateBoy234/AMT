using CommunityToolkit.Mvvm.Messaging;

namespace AMP.UI.Services;

/// <summary>默认导航实现：仅发送弱引用消息总线请求，实际切换由 Shell 统一处理。</summary>
public sealed class NavigationService : INavigationService
{
    private readonly IMessenger _messenger;

    public NavigationService(IMessenger messenger)
    {
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
    }

    /// <inheritdoc />
    public void NavigateTo(Type viewModelType)
        => _messenger.Send(new NavigateToViewRequest(viewModelType));
}
