using CivilAI.Workspace;
using System.Text.Json;

internal static class AgentChecks
{
    public static void Run()
    {
        var provider = new ScriptedProvider();
        var session = new AgentSession();
        var calls = new List<string>();
        Task<string> Execute(string tool, JsonElement args, CancellationToken token)
        {
            calls.Add(tool);
            return Task.FromResult(tool == "context" ? """{"commands":["MOVE"]}""" : """{"status":"command ended","modified":["AB"]}""");
        }
        provider.Replies.Enqueue("""{"tool":"context","message":"","arguments":{}}""");
        provider.Replies.Enqueue("""{"tool":"command","message":"Moving the selection.","arguments":{"name":"MOVE","tokens":[]}}""");
        provider.Replies.Enqueue("""{"tool":"ask","message":"How far should the next copy go?","arguments":{"options":["2 m","6 m"]}}""");
        var response = session.RunAsync("Move and copy this.", provider, new("Local","test",""), "",
            Execute, _ => {}, CancellationToken.None).GetAwaiter().GetResult();
        if (response.GetProperty("tool").GetString() != "ask" || calls.Count != 2 ||
            !provider.LastMessages.Any(m => m.Content.Contains("modified")))
            throw new InvalidOperationException("Agent failed to feed operation results into the next step.");
        provider.Replies.Enqueue("""{"tool":"done","message":"Finished.","arguments":{}}""");
        session.RunAsync("6 m", provider, new("Local","test",""), "", Execute, _ => {},
            CancellationToken.None).GetAwaiter().GetResult();
        if (!provider.LastMessages.Any(m => m.Content.Contains("Move and copy this.")))
            throw new InvalidOperationException("Follow-up lost the original task.");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        try
        {
            session.RunAsync("erase", provider, new("Local","test",""), "", Execute, _ => {}, stop.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("Cancellation was ignored.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS: Agent executes multiple steps, observes results, resumes questions and honors cancellation");
    }

    private sealed class ScriptedProvider : IAiProvider
    {
        public Queue<string> Replies = new();
        public IReadOnlyList<ChatMessage> LastMessages = Array.Empty<ChatMessage>();
        public PreparedRequest Prepare(ProviderConfiguration config, IReadOnlyList<ChatMessage> messages)
        {
            LastMessages = messages.ToArray();
            return new(new Uri("http://localhost/"), "{}", "Local");
        }
        public Task<string> SendAsync(PreparedRequest request, string key, CancellationToken cancellation) =>
            Task.FromResult(Replies.Dequeue());
    }
}
