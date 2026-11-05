using System.Windows;

namespace FlowMemo.WpfUi.Messages;

public record OpenSettingsWindowMessage(
    Window? Owner = null
);
