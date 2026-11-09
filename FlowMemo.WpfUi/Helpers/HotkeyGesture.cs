using System.Windows.Input;

namespace FlowMemo.WpfUi.Helpers;

/// <summary>전역 단축키. 설정에는 "Ctrl+Alt+H" 형식의 문자열로 저장한다.</summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    private static readonly (ModifierKeys Modifier, string Name)[] ModifierNames =
    [
        (ModifierKeys.Control, "Ctrl"),
        (ModifierKeys.Alt, "Alt"),
        (ModifierKeys.Shift, "Shift"),
        (ModifierKeys.Windows, "Win")
    ];

    /// <summary>수정 키 없이 등록하면 그 키를 어디서도 입력할 수 없게 되므로 수정 키를 요구한다.</summary>
    public bool IsValid => Modifiers != ModifierKeys.None && Key != Key.None && !IsModifierKey(Key);

    public static bool IsModifierKey(Key key)
        => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public override string ToString()
    {
        ModifierKeys pressed = Modifiers; // 구조체의 람다는 this를 쓸 수 없다.
        IEnumerable<string> names = ModifierNames.Where(m => pressed.HasFlag(m.Modifier)).Select(m => m.Name);
        return string.Join("+", names.Append(KeyName(Key)));
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);

        ModifierKeys modifiers = ModifierKeys.None;
        foreach (string part in parts[..^1])
        {
            (ModifierKeys Modifier, string Name) match = ModifierNames.FirstOrDefault(m => m.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
                return false;
            modifiers |= match.Modifier;
        }

        if (!TryParseKey(parts[^1], out Key key))
            return false;

        HotkeyGesture parsed = new(modifiers, key);
        if (!parsed.IsValid)
            return false;

        gesture = parsed;
        return true;
    }

    private static string KeyName(Key key)
        => key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString();

    private static bool TryParseKey(string name, out Key key)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            key = Key.D0 + (name[0] - '0');
            return true;
        }

        // Enum.TryParse는 "12" 같은 숫자도 받아들이므로 이름만 허용한다.
        if (name.Length > 0 && !char.IsAsciiDigit(name[0]) && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key))
            return true;

        key = Key.None;
        return false;
    }
}
