using System.Threading.Tasks;

namespace HornetStudio.Core.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);
}
