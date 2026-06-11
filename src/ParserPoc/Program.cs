using System;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ParserPreviewWindow window = new();
            window.Run();
            return 0;
        }
        catch (Exception ex)
        {
            NativeMethods.MessageBoxW(IntPtr.Zero, ex.Message, "Parser POC", NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
            return 1;
        }
    }
}
