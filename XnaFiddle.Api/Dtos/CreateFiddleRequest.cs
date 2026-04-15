namespace XnaFiddle.Api.Dtos;

public record CreateFiddleRequest(string Content, List<string> FileReferences);
