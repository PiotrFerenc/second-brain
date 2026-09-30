namespace SecondBrain.Core;

/// <summary>Wywolanie skilla w czacie: "/nazwa-skilla reszta wiadomosci" na poczatku tekstu.</summary>
public static class SkillCommand
{
    /// <summary>Zamienia "/skill ..." na instrukcje dla agenta; nieznany skill lub brak "/" zwraca tekst bez zmian.</summary>
    public static string Expand(string message, IEnumerable<string> skillNames)
    {
        if (!message.StartsWith('/'))
            return message;

        var parts = message[1..].Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length > 0 ? skillNames.FirstOrDefault(n => n.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) : null;
        if (name is null)
            return message;

        var task = parts.Length > 1 ? parts[1].Trim() : "";
        return $"Uzyj skilla \"{name}\" (najpierw wywolaj use_skill z ta nazwa)." + (task.Length > 0 ? " Zadanie: " + task : "");
    }
}
