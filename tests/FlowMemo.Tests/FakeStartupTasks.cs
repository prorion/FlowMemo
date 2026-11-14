using FlowMemo.WpfUi.Helpers;

/// <summary>시작 프로그램 등록 시험용 작업 스케줄러. 실제 작업을 만들지 않는다.</summary>
internal sealed class FakeStartupTasks : IStartupTaskClient
{
    private readonly Dictionary<string, string> _tasks = [];

    /// <summary>true면 정책으로 작업을 만들 수 없는 PC처럼 행동한다.</summary>
    public bool Fail { get; set; }

    public int Count => _tasks.Count;

    public string? Xml(string taskName) => _tasks.GetValueOrDefault(taskName);

    public bool Exists(string taskName) => _tasks.ContainsKey(taskName);

    public bool Register(string taskName, string taskXml, out string error)
    {
        if (Fail)
        {
            error = "정책으로 막힘 (시험)";
            return false;
        }

        _tasks[taskName] = taskXml;
        error = "";
        return true;
    }

    public void Delete(string taskName) => _tasks.Remove(taskName);
}
