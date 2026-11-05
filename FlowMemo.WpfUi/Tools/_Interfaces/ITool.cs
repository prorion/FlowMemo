using System.Windows.Controls;

using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Tools;

public interface ITool
{
    ToolState State { get; }

    MenuItem MenuItem { get; }
}
