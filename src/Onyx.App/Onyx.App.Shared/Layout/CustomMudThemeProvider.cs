using MudBlazor;

namespace Onyx.App.Shared.Layout;

public class CustomMudThemeProvider : MudThemeProvider
{
    private bool _isDarkMode = false;
    private MudTheme _currentTheme = new MudTheme();

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            _isDarkMode = value;
            UpdateTheme();
        }
    }

    private void UpdateTheme()
    {
        _currentTheme = _isDarkMode ? GenerateDarkTheme() : GenerateLightTheme();
        StateHasChanged();
    }

    private MudTheme GenerateDarkTheme()
    {
        return new MudTheme
        {
            PaletteDark = new PaletteDark
            {
                Black = "#E0E0E0",
                Background = "3A3A3A",
                Surface = "#838383", 
                TextPrimary = "#A1A1A1",
                Primary =  "#B34704",
                Secondary = "#E85B09",
                Tertiary =  "#FF8F32",
                White = "#F5F1EB"
                
            }
        };
    }

    private MudTheme GenerateLightTheme()
    {
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Black = "#E0E0E0",
                Background = "3A3A3A",
                Surface = "#838383", 
                TextPrimary = "#A1A1A1",
                Primary =  "#B34704",
                Secondary = "#E85B09",
                Tertiary =  "#FF8F32",
                White = "#F5F1EB"
                
            }
        };
    }
}