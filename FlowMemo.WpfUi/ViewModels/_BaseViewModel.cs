using FlowMemo.WpfUi.Services;

namespace FlowMemo.WpfUi.ViewModels;

public abstract class BaseViewModel(AppMetadataService appMetadataService, SettingsService settingsService, MessengerService messengerService)
{
    protected AppMetadataService AppMetadataService { get; } = appMetadataService;
    protected SettingsService SettingsService { get; } = settingsService;
    protected MessengerService MessengerService { get; } = messengerService;
}
