using System.Diagnostics;
using System.Text;

if (args is ["--pilot-b-fake-child", var childMarkerPath])
{
    await WriteProcessMarkerAsync(childMarkerPath);
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args is not ["codex", "exec", "--ephemeral", "--json"])
{
    Console.Error.WriteLine("fake cli received an unexpected invocation");
    return 64;
}

using var input = new MemoryStream();
await Console.OpenStandardInput().CopyToAsync(input);
var prompt = Encoding.UTF8.GetString(input.ToArray());
const int ControlledNonzeroExitCode = 23;

switch (prompt)
{
    case "pilot-b.fake.cancel-before-output":
        await WriteProcessMarkerAsync(GetMarkerPath(".pilot-b-fake-parent-ready"));
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;

    case "pilot-b.fake.cancel-during-output":
        Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fake-cancel-thread\"}");
        await Console.Out.FlushAsync();
        await WriteProcessMarkerAsync(GetMarkerPath(".pilot-b-fake-parent-ready"));
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;

    case "pilot-b.fake.cancel-with-child":
        var childMarker = GetMarkerPath(".pilot-b-fake-child-ready");
        await WriteProcessMarkerAsync(GetMarkerPath(".pilot-b-fake-parent-ready"));
        using (var child = new Process
               {
                   StartInfo = new ProcessStartInfo
                   {
                       FileName = Environment.ProcessPath
                           ?? throw new InvalidOperationException("Cannot resolve the fake CLI process path."),
                       UseShellExecute = false,
                       CreateNoWindow = true
                   }
               })
        {
            child.StartInfo.ArgumentList.Add("--pilot-b-fake-child");
            child.StartInfo.ArgumentList.Add(childMarker);
            if (!child.Start())
            {
                return 66;
            }

            await child.WaitForExitAsync();
            return child.ExitCode;
        }

    case "pilot-b.fake.delayed-valid":
        await Task.Delay(TimeSpan.FromMilliseconds(750));
        goto case "pilot-b.fake.valid";

    case "pilot-b.fake.valid":
        Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fake-thread\"}");
        Console.WriteLine("{\"type\":\"turn.started\"}");
        Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"hidden\"}}");
        Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"tool_call\",\"name\":\"noop\"}}");
        Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"phase\":\"commentary\",\"text\":\"Verified the fixture boundary.\"}}");
        Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"phase\":\"final\",\"text\":\"Done.\"}}");
        Console.WriteLine("{\"type\":\"turn.completed\"}");
        return 0;

    case "pilot-b.fake.unsupported":
        Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fake-thread\"}");
        Console.WriteLine("{\"type\":\"turn.started\"}");
        Console.WriteLine("{\"type\":\"future.event\"}");
        Console.WriteLine("{\"type\":\"turn.completed\"}");
        return 0;

    case "pilot-b.fake.out-of-order":
        Console.WriteLine("{\"type\":\"turn.started\"}");
        Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fake-thread\"}");
        Console.WriteLine("{\"type\":\"turn.started\"}");
        Console.WriteLine("{\"type\":\"turn.completed\"}");
        return 0;

    case "pilot-b.fake.terminal-failure":
        Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fake-thread\"}");
        Console.WriteLine("{\"type\":\"turn.started\"}");
        Console.WriteLine("{\"type\":\"turn.failed\",\"message\":\"fake terminal failure\"}");
        return 0;

    case "pilot-b.fake.nonzero":
        Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fake-thread\"}");
        Console.WriteLine("{\"type\":\"turn.started\"}");
        Console.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"phase\":\"final\",\"text\":\"Failed after a complete transcript.\"}}");
        Console.WriteLine("{\"type\":\"turn.completed\"}");
        Console.Error.WriteLine("fake nonzero exit diagnostic");
        return ControlledNonzeroExitCode;

    case "pilot-b.fake.malformed":
        Console.WriteLine("{\"type\":\"thread.started\"}");
        Console.WriteLine("{not-json");
        return 0;

    case "pilot-b.fake.partial":
        Console.WriteLine("{\"type\":\"thread.started\"}");
        return 0;

    case "pilot-b.fake.timeout":
        await Task.Delay(TimeSpan.FromSeconds(5));
        return 0;

    case "pilot-b.fake.failed":
        Console.WriteLine("{\"type\":\"turn.failed\",\"message\":\"fake failure\"}");
        return 1;

    default:
        Console.Error.WriteLine("unknown fake prompt");
        return 65;
}

static string GetMarkerPath(string fileName)
{
    var markerDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "markers"));
    Directory.CreateDirectory(markerDirectory);
    return Path.Combine(markerDirectory, fileName);
}

static async Task WriteProcessMarkerAsync(string markerPath)
{
    using var process = Process.GetCurrentProcess();
    var startedAtTicks = process.StartTime.ToUniversalTime().Ticks;
    await File.WriteAllTextAsync(markerPath, $"{Environment.ProcessId}|{startedAtTicks}");
}
