using XnaFiddle.Api.Slugs;

namespace XnaFiddle.Api.Tests.Infrastructure;

/// <summary>
/// Test fake that returns pre-scripted slugs in order. Used to force deterministic
/// collisions so the controller's retry loop can be exercised.
/// </summary>
public class QueuedSlugGenerator : ISlugGenerator
{
    private readonly Queue<string> _slugs;

    public QueuedSlugGenerator(params string[] slugs) => _slugs = new Queue<string>(slugs);

    public string Generate() => _slugs.Dequeue();
}
