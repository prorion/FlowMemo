using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace FlowMemo.WpfUi.Helpers;

public static class MenuHelper
{
    // Fluent 테마의 SubmenuItem(일반 항목)과 SubmenuHeader(하위 메뉴가 있는 항목) 템플릿은
    // 글자 시작 위치가 약 27 DIP 다르다. (125% 화면에서 34px 차이로 확인)
    private const double SubmenuHeaderTextOffset = 27;

    /// <summary>
    /// 같은 메뉴 안에서 '개수 세기'·'도구' 같은 하위 메뉴 항목만 왼쪽으로 쏠려 보이던 문제를 맞춘다.
    /// 머리글을 여백 있는 TextBlock으로 바꾸고, 화면 읽기 프로그램용 이름은 그대로 남긴다.
    /// </summary>
    public static void AlignSubmenuHeader(MenuItem item, double minWidth = 0)
    {
        if (item.Header is string text)
        {
            item.Header = new TextBlock
            {
                Text = text,
                Margin = new Thickness(SubmenuHeaderTextOffset, 0, 0, 0)
            };
            AutomationProperties.SetName(item, text);
        }

        if (minWidth > 0)
            item.MinWidth = minWidth;
    }
}
