namespace XnaFiddle.Api.Dtos;

public record FiddleResponse(string Slug, string Content, List<string> FileReferences, DateTimeOffset CreatedAt);
