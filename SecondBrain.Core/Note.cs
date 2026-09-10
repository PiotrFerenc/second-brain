namespace SecondBrain.Core;

public record Note(
    Guid Id,
    string Title,
    string RawContent,
    string CompressedContent,
    string[] Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string FilePath = "");

public record ScoredNote(Note Note, float Score);
