namespace SecondBrain.Core;

public record Note(
    Guid Id,
    string Title,
    string RawContent,
    string CompressedContent,
    string[] Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string FilePath = "",
    Guid? ParentId = null,
    bool Pinned = false);

public record ScoredNote(Note Note, float Score);
