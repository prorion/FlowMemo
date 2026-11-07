using System.Globalization;

namespace FlowMemo.Core;

/// <summary>
/// 설정 값을 AppSettings 테이블의 문자열로 바꾸고 되돌린다.
/// - bool: true/false (스키마 v4에서 옮긴 1/0도 읽는다)
/// - 정수·실수: 문화권과 무관한 형식
/// - 열거형: 이름 (v4에서 옮긴 숫자도 읽는다). 이름으로 저장하면 열거형 순서를 바꿔도 값이 안 바뀐다.
/// </summary>
public static class SettingValueConverter
{
    public static bool IsSupported(Type type)
        => type == typeof(bool)
        || type == typeof(int)
        || type == typeof(double)
        || type == typeof(string)
        || type.IsEnum;

    public static string Format(object value)
        => value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            string s => s,
            Enum e => e.ToString(),
            _ => throw new ArgumentException($"지원하지 않는 설정 형식: {value.GetType().Name}")
        };

    /// <summary>값을 읽지 못하면(형식이 다르거나 없는 열거형 값) false를 돌려준다. 그러면 기본값을 쓴다.</summary>
    public static bool TryParse(string? text, Type type, out object? value)
    {
        value = null;
        if (text is null)
            return false;

        string trimmed = text.Trim();

        if (type == typeof(bool))
        {
            if (trimmed is "1" || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
                value = true;
            else if (trimmed is "0" || trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
                value = false;
        }
        else if (type == typeof(int))
        {
            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                value = i;
            // 예전 열은 INTEGER/REAL이 섞여 있어 "300.0"처럼 들어 있을 수 있다.
            else if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
                value = (int)d;
        }
        else if (type == typeof(double))
        {
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
                value = d;
        }
        else if (type == typeof(string))
        {
            value = text;
        }
        else if (type.IsEnum)
        {
            // Enum.TryParse는 "7"처럼 정의되지 않은 숫자도 받아들이므로 IsDefined로 한 번 더 거른다.
            if (Enum.TryParse(type, trimmed, ignoreCase: true, out object? parsed) && Enum.IsDefined(type, parsed!))
                value = parsed;
        }

        return value is not null;
    }
}
