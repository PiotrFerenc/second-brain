namespace SecondBrain.Desktop.ViewModels;

public class AgentChatItem(string role, string text)
{
    public string Role { get; } = role;
    public string Text { get; } = text;
    public bool IsUser => Role == "user";
}
