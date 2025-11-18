namespace SystemCleaner.App.ViewModels;

public sealed class VirusTotalEngineResultViewModel : ObservableObject
{
    private string _engine = string.Empty;
    private string? _category;
    private string? _result;

    public string Engine
    {
        get => _engine;
        set => SetProperty(ref _engine, value);
    }

    public string? Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public string? Result
    {
        get => _result;
        set => SetProperty(ref _result, value);
    }
}
