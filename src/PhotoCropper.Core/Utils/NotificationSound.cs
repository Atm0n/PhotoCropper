namespace PhotoCropper.Core.Utils;

public static class NotificationSound
{
    public static void PlayCompletionSound()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Console.Beep(880, 200);
            }
            else
            {
                Console.Write("\a");
            }
        }
        catch
        {
            // Non-critical: Ignore audio driver, headless, or container environment failures
        }
    }
}
