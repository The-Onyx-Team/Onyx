namespace Onyx.App.Services;

public static class AlertService
{
    public static async Task ShowErrorAsync(string title, string message)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var window = Application.Current?.Windows[0];
            var page = window?.Page;

            if (page != null)
            {
                await page.DisplayAlert(title, message, "Close");
            }
        });
    }
}
