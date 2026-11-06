using System.Text.Json;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using System.Text.Encodings.Web;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public class JsonTool : BaseTool, ITool
{
    private enum ToolActions
    {
        JsonPrettify
    }

    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        // 기본 인코더는 한글 등 ASCII 밖의 문자를 한처럼 이스케이프한다.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public JsonTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Json,
            [
                new ToolMenuAction(Strings.Tool_Json_Prettify, new RelayCommand(() => MenuAction(ToolActions.JsonPrettify)))
            ]
        );
    }

    public ToolState State => ToolSettings.JsonToolState;

    private void MenuAction(ToolActions action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        try
        {
            switch (action)
            {
                case ToolActions.JsonPrettify:
                    object? jsonObject = JsonSerializer.Deserialize<object>(text);
                    if (jsonObject != null)
                        return JsonSerializer.Serialize<object>(jsonObject, _jsonSerializerOptions);
                    break;
            }
        }
        catch
        {
        }

        return text;
    }
}
