using DMMPlugin;

try
{
    NotificationUsesHostBoundaryWithoutPluginLifecycleState();
    Console.WriteLine("PASS DMM notification uses Host boundary");
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL DMM notification uses Host boundary");
    Console.Error.WriteLine(ex);
    Environment.Exit(1);
}

static void NotificationUsesHostBoundaryWithoutPluginLifecycleState()
{
    try
    {
        DMMDisplay.Notify("notification");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("TerminalUi", StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException("Expected the uninitialized Host boundary to reject the notification.");
}
