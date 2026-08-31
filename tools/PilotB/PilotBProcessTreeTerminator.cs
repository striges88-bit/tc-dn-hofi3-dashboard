using System.Diagnostics;

namespace CryptoIndicatorApp.PilotB;

internal interface IPilotBProcessTreeTerminator
{
    void Terminate(Process process);
}

internal sealed class PilotBProcessTreeTerminator : IPilotBProcessTreeTerminator
{
    public void Terminate(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }
    }
}
