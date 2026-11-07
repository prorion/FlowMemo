using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FlowMemo.WpfUi.Models;

public abstract class BaseModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSaved
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            OnPropertyChanged(nameof(IsSaved));
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// 이 속성이 바뀌면 다시 저장해야 하는지. 기본은 모든 속성이다.
    /// 화면에만 쓰는 속성(포커스, 투명도, 색 브러시 등)이 있는 모델은 저장하는 속성만 true로 돌려준다.
    /// (예전에는 창을 전환할 때마다 포커스가 바뀌어 메모를 DB에 다시 썼다.)
    /// </summary>
    protected virtual bool IsPersistedProperty(string propertyName) => true;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);

        if (propertyName is null || IsPersistedProperty(propertyName))
            IsSaved = false;

        return true;
    }
}
