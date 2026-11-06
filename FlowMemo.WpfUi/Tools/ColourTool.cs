using System.Text.RegularExpressions;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Controls;

using FlowMemo.WpfUi.Resources;

namespace FlowMemo.WpfUi.Tools;

public partial class ColourTool : BaseTool, ITool
{
    private enum ToolActions
    {
        RgbToHex,
        HexToRgb
    }

    public ColourTool(NoteTextBoxControl noteTextBox) : base(noteTextBox)
    {
        InitializeMenuItem(
            Strings.Tool_Colour,
            [
                new ToolMenuAction(Strings.Tool_Colour_RgbToHex, new RelayCommand(() => MenuAction(ToolActions.RgbToHex))),
                new ToolMenuAction(Strings.Tool_Colour_HexToRgb, new RelayCommand(() => MenuAction(ToolActions.HexToRgb)))
            ]
        );
    }

    public ToolState State => ToolSettings.ColourToolState;

    private void MenuAction(Enum action)
    {
        ApplyFunctionToNoteText(ModifyTextCallback, action);
    }

    private string ModifyTextCallback(string text, Enum action)
    {
        switch (action)
        {
            case ToolActions.RgbToHex:
                return RgbRegex().Replace(
                    text,
                    match => {
                        int r = int.Parse(match.Groups[1].Value);
                        int g = int.Parse(match.Groups[2].Value);
                        int b = int.Parse(match.Groups[3].Value);

                        if (!IsValidRgb(r, g, b))
                            return match.Value; // Leave as-is if invalid

                        return $"#{r:X2}{g:X2}{b:X2}";
                    }
                );
            case ToolActions.HexToRgb:
                return HexRegex().Replace(
                    text,
                    match =>
                    {
                        string hexValue = match.Groups["hex"].Value;
                        bool isShorthand = (hexValue.Length == 3);

                        int r = (isShorthand) ? Convert.ToInt32(new string(hexValue[0], 2), 16) : Convert.ToInt32(hexValue[..2], 16);
                        int g = (isShorthand) ? Convert.ToInt32(new string(hexValue[1], 2), 16) : Convert.ToInt32(hexValue.Substring(2, 2), 16);
                        int b = (isShorthand) ? Convert.ToInt32(new string(hexValue[2], 2), 16) : Convert.ToInt32(hexValue.Substring(4, 2), 16);

                        return $"{r}, {g}, {b}";
                    }
                );
        }

        return text;
    }

    private static bool IsValidRgb(int r, int g, int b)
    {
        return (
            r >= 0 && r <= 255
            && g >= 0 && g <= 255
            && b >= 0 && b <= 255
        );
    }

    // 숫자 세 개가 단어 경계로 떨어져 있을 때만 (예전에는 "2024, 11, 06" 같은 날짜 일부도 바꿨다).
    [GeneratedRegex(@"(?:rgb\s*)?\(?\[?(?<![\d.])(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})(?![\d.])\)?\]?", RegexOptions.IgnoreCase)]
    private static partial Regex RgbRegex();
    // '#'이 붙은 3/6자리, 또는 숫자가 하나 이상 섞인 6자리 단어만 바꾼다.
    // (예전에는 '#' 없이도 아무 3/6글자나 바꿔서 add, face, decade 같은 영어 단어가 RGB 값이 됐다.)
    [GeneratedRegex(@"(?<![0-9A-Za-z#])(?:#(?<hex>[0-9a-fA-F]{6}|[0-9a-fA-F]{3})|(?<hex>(?=[0-9a-fA-F]*[0-9])[0-9a-fA-F]{6}))(?![0-9A-Za-z])")]
    private static partial Regex HexRegex();
}
