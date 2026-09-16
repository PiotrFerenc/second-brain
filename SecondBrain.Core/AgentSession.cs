namespace SecondBrain.Core;

public record AgentSessionMessage(string Role, string Text);

// Jedna zapisana rozmowa z agentem: ConversationState to nieprzezroczysty blob providera
// (patrz IAgent), potrzebny zeby kontynuowac ta sama rozmowe; Messages to rownolegla,
// czytelna kopia do wyswietlenia w UI bez parsowania blobu.
public record AgentSession(
    Guid Id,
    string Title,
    string ConversationState,
    IReadOnlyList<AgentSessionMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
