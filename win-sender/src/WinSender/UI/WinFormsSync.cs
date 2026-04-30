using System.Threading;

namespace WinSender.UI;

internal static class WinFormsSync
{
    public static SynchronizationContext? UiContext { get; private set; }

    public static void CaptureUiContext()
    {
        UiContext = SynchronizationContext.Current;
    }
}
