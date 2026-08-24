using System.Text;

if (args is not ["codex", "exec", "--ephemeral", "--json"])
{
    Console.Error.WriteLine("fake cli received an unexpected invocation");
    return 64;
}

using var input = new MemoryStream();
await Console.OpenStandardInput().CopyToAsync(input);
var prompt = Encoding.UTF8.GetString(input.ToArray());

switch (prompt)
{
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
