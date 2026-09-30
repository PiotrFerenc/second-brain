using SecondBrain.Core;
using SecondBrain.Infrastructure;
using SecondBrain.Tests.Support;

namespace SecondBrain.Tests.Infrastructure;

public class AgentSessionStoreTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly FileAgentSessionStore _store;

    public AgentSessionStoreTests() => _store = new FileAgentSessionStore(_root.Notes);

    public void Dispose() => _root.Dispose();

    private static AgentSession Session(string title, DateTimeOffset updated, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title, "{\"state\":1}", [new AgentSessionMessage("user", "czesc"), new AgentSessionMessage("assistant", "hej")],
            updated.AddDays(-1), updated);

    [Fact]
    public async Task List_NoDir_Empty() =>
        Assert.Empty(await _store.ListAsync());

    [Fact]
    public async Task SaveThenList_RoundTrips()
    {
        var s = Session("rozmowa", DateTimeOffset.UtcNow);
        await _store.SaveAsync(s);

        var loaded = Assert.Single(await _store.ListAsync());
        Assert.Equal(s.Id, loaded.Id);
        Assert.Equal("rozmowa", loaded.Title);
        Assert.Equal(s.ConversationState, loaded.ConversationState);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("hej", loaded.Messages[1].Text);
    }

    [Fact]
    public async Task List_NewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.SaveAsync(Session("stara", now.AddHours(-5)));
        await _store.SaveAsync(Session("nowa", now));
        await _store.SaveAsync(Session("srednia", now.AddHours(-1)));

        Assert.Equal(["nowa", "srednia", "stara"], (await _store.ListAsync()).Select(s => s.Title));
    }

    [Fact]
    public async Task Save_SameId_Overwrites()
    {
        var id = Guid.NewGuid();
        await _store.SaveAsync(Session("v1", DateTimeOffset.UtcNow, id));
        await _store.SaveAsync(Session("v2", DateTimeOffset.UtcNow, id));

        Assert.Equal("v2", Assert.Single(await _store.ListAsync()).Title);
    }

    [Fact]
    public async Task Delete_RemovesSession()
    {
        var s = Session("x", DateTimeOffset.UtcNow);
        await _store.SaveAsync(s);
        await _store.DeleteAsync(s.Id);

        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public async Task Delete_Missing_DoesNotThrow() =>
        await _store.DeleteAsync(Guid.NewGuid());
}
