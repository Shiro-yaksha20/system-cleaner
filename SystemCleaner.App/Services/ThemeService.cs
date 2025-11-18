using System;
using System.Linq;
using System.Windows;
using AppThemeMode = SystemCleaner.App.Theming.ThemeMode;

namespace SystemCleaner.App.Services;

public sealed class ThemeService
{
    private const string LightResourceUri = "Themes/LightTheme.xaml";
    private const string DarkResourceUri = "Themes/DarkTheme.xaml";

    public void ApplyTheme(AppThemeMode mode)
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        var themeDictionary = dictionaries.FirstOrDefault(dict => dict.Source is not null &&
            (dict.Source.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
             dict.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)));

        var targetUri = mode switch
        {
            AppThemeMode.Dark => new Uri(DarkResourceUri, UriKind.Relative),
            AppThemeMode.Light => new Uri(LightResourceUri, UriKind.Relative),
            AppThemeMode.System => GetSystemThemeUri(),
            _ => GetSystemThemeUri()
        };

        if (themeDictionary is null)
        {
            dictionaries.Add(new ResourceDictionary { Source = targetUri });
        }
        else if (!Equals(themeDictionary.Source, targetUri))
        {
            dictionaries.Remove(themeDictionary);
            dictionaries.Add(new ResourceDictionary { Source = targetUri });
        }
    }

    private static Uri GetSystemThemeUri()
    {
        return IsSystemInDarkMode() ? new Uri(DarkResourceUri, UriKind.Relative) : new Uri(LightResourceUri, UriKind.Relative);
    }

    private static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }
}
