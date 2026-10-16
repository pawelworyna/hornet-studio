using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using HornetStudio.Core.Services;

namespace HornetStudio.Desktop.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard != null)
        {
            var dt = new DataTransfer();
            dt.Add(DataTransferItem.CreateText(text));
            await desktop.MainWindow.Clipboard.SetDataAsync(dt);
        }
    }
}
