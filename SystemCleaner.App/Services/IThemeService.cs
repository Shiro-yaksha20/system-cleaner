using AppThemeMode = SystemCleaner.App.Theming.ThemeMode;

namespace SystemCleaner.App.Services;

public interface IThemeService
{
    void ApplyTheme(AppThemeMode mode);
}
