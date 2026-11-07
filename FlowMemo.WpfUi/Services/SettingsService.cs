using System.ComponentModel;
using System.Reflection;
using System.Windows.Threading;

using FlowMemo.Core;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Models;

namespace FlowMemo.WpfUi.Services;

/// <summary>
/// 설정 모델 네 개를 AppSettings 키/값 테이블과 주고받는다.
/// 키는 "구역.속성이름"이다. 설정 모델에 속성(bool·int·double·string·열거형)을 추가하면
/// 저장과 불러오기는 따로 손대지 않아도 된다. 기본값은 모델의 초기값이다.
/// </summary>
public class SettingsService(SettingsRepository settingsRepository)
{
    public const string ApplicationSection = "Application";
    public const string NotesSection = "Notes";
    public const string EditorSection = "Editor";
    public const string ToolsSection = "Tools";

    private readonly SettingsRepository _settingsRepository = settingsRepository;

    // 예전에는 앱을 정상 종료할 때만 설정을 저장해서, 강제 종료·오류·Windows 종료 때 바꾼 설정이 사라졌다.
    // 이제 설정이 바뀌면 잠시 뒤(연속 변경은 묶어서) 바로 저장한다.
    private DispatcherTimer? _autoSaveTimer;

    public ApplicationSettingsModel ApplicationSettings { get; private set; } = new();
    public NoteSettingsModel NoteSettings { get; private set; } = new();
    public EditorSettingsModel EditorSettings { get; private set; } = new();
    public ToolSettingsModel ToolSettings { get; private set; } = new();

    public async Task Load()
    {
        IReadOnlyDictionary<string, string> stored = await _settingsRepository.GetAll();

        ApplicationSettings = new();
        NoteSettings = new();
        EditorSettings = new();
        ToolSettings = new();

        foreach ((string key, BaseModel model, PropertyInfo property) in GetSettingProperties())
        {
            if (!stored.TryGetValue(key, out string? text))
                continue; // 저장된 적 없는 설정은 기본값

            if (SettingValueConverter.TryParse(text, property.PropertyType, out object? value))
                property.SetValue(model, value);
            else
                ErrorReporter.Log(new FormatException($"설정 '{key}'의 값 '{text}'을(를) 읽을 수 없어 기본값을 씁니다."), "Settings load");
        }

        foreach (BaseModel model in GetModels().Select(m => m.Model))
            model.IsSaved = true;

        EnableAutoSave();
    }

    /// <summary>설정 하나하나의 DB 키, 모델, 속성.</summary>
    public IEnumerable<(string Key, BaseModel Model, PropertyInfo Property)> GetSettingProperties()
    {
        foreach ((string section, BaseModel model) in GetModels())
        {
            foreach (PropertyInfo property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.Name == nameof(BaseModel.IsSaved))
                    continue;
                if (!SettingValueConverter.IsSupported(property.PropertyType))
                    continue;

                yield return ($"{section}.{property.Name}", model, property);
            }
        }
    }

    private IEnumerable<(string Section, BaseModel Model)> GetModels()
    {
        yield return (ApplicationSection, ApplicationSettings);
        yield return (NotesSection, NoteSettings);
        yield return (EditorSection, EditorSettings);
        yield return (ToolsSection, ToolSettings);
    }

    private void EnableAutoSave()
    {
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        }

        foreach (BaseModel model in GetModels().Select(m => m.Model))
            model.PropertyChanged += OnAnySettingChanged;
    }

    private void OnAnySettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BaseModel.IsSaved) || _autoSaveTimer is null)
            return;

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async void OnAutoSaveTimerTick(object? sender, EventArgs e)
    {
        _autoSaveTimer?.Stop();
        try
        {
            await Save();
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Settings auto-save");
        }
    }

    public async Task Save()
    {
        await _settingsRepository.SaveAll(
            GetSettingProperties().Select(setting => new KeyValuePair<string, string>(
                setting.Key,
                SettingValueConverter.Format(setting.Property.GetValue(setting.Model)!)
            ))
        );

        foreach (BaseModel model in GetModels().Select(m => m.Model))
            model.IsSaved = true;
    }
}
