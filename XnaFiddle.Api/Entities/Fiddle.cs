namespace XnaFiddle.Api.Entities;

public class Fiddle
{
    public int Id { get; set; }
    public required string Slug { get; set; }
    public required string Content { get; set; }
    public List<string> FileReferences { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}
