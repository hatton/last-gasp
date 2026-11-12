namespace LastGasp;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // These modes run as administrator, started by the taskbar app or by the scheduled task.
        switch (args.FirstOrDefault())
        {
            case "--free":
                return FreeMemory.Run();
            case "--register-task" when args.Length == 2:
                return RunAdminStep(() => FreeTask.Register(args[1]));
            case "--unregister-task":
                return RunAdminStep(FreeTask.Unregister);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
        return 0;
    }

    private static int RunAdminStep(Action step)
    {
        try
        {
            step();
            return 0;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            return ex.HResult;
        }
    }
}
