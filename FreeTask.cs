namespace LastGasp;

/// <summary>
/// The scheduled task that runs "LastGasp.exe --free" with highest privileges, so that freeing
/// memory needs no UAC prompt. It exists only while "Ask for administrator permission each
/// time" is off. Creating and deleting it need admin rights; running it does not.
/// </summary>
internal static class FreeTask
{
    public const string Name = "Last Gasp Free Memory";

    private const int TASK_ACTION_EXEC = 0;
    private const int TASK_LOGON_INTERACTIVE_TOKEN = 3;
    private const int TASK_RUNLEVEL_HIGHEST = 1;
    private const int TASK_INSTANCES_IGNORE_NEW = 2;
    private const int TASK_CREATE_OR_UPDATE = 6;
    private const int TASK_STATE_QUEUED = 2;
    private const int TASK_STATE_RUNNING = 4;

    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service;
    }

    /// <summary>Needs admin rights. <paramref name="user"/> is the account the task runs as.</summary>
    public static void Register(string user)
    {
        dynamic service = Connect();
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "Frees memory when Last Gasp asks it to.";
        definition.Principal.UserId = user;
        definition.Principal.LogonType = TASK_LOGON_INTERACTIVE_TOKEN;
        definition.Principal.RunLevel = TASK_RUNLEVEL_HIGHEST;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT5M";
        definition.Settings.MultipleInstances = TASK_INSTANCES_IGNORE_NEW;
        dynamic action = definition.Actions.Create(TASK_ACTION_EXEC);
        action.Path = Environment.ProcessPath!;
        action.Arguments = "--free";
        service.GetFolder(@"\").RegisterTaskDefinition(Name, definition, TASK_CREATE_OR_UPDATE,
            null, null, TASK_LOGON_INTERACTIVE_TOKEN, null);
    }

    /// <summary>Needs admin rights. Does nothing if the task does not exist.</summary>
    public static void Unregister()
    {
        if (Exists())
            Connect().GetFolder(@"\").DeleteTask(Name, 0);
    }

    public static bool Exists()
    {
        try
        {
            Connect().GetFolder(@"\").GetTask(Name);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>Runs the task and waits for it to finish.</summary>
    /// <returns>The exit code of "LastGasp.exe --free".</returns>
    public static async Task<int> RunAsync()
    {
        dynamic task = Connect().GetFolder(@"\").GetTask(Name);
        task.Run(null);
        var deadline = DateTime.UtcNow.AddMinutes(1);
        do
        {
            await Task.Delay(250);
        } while (((int)task.State is TASK_STATE_RUNNING or TASK_STATE_QUEUED) && DateTime.UtcNow < deadline);
        return (int)task.LastTaskResult;
    }
}
