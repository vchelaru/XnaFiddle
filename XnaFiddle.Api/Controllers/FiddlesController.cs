using EntityFramework.Exceptions.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using XnaFiddle.Api.Data;
using XnaFiddle.Api.Dtos;
using XnaFiddle.Api.Entities;
using XnaFiddle.Api.Slugs;

namespace XnaFiddle.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FiddlesController(FiddleDbContext db, ISlugGenerator slugGenerator) : ControllerBase
{
    private const int MaxSlugAttempts = 5;

    [HttpGet("{slug}")]
    public async Task<ActionResult<FiddleResponse>> Get(string slug)
    {
        var fiddle = await db.Fiddles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Slug == slug);

        if (fiddle is null)
            return NotFound();

        return ToResponse(fiddle);
    }

    [HttpPost]
    public async Task<ActionResult<FiddleResponse>> Create(CreateFiddleRequest request)
    {
        var fiddle = new Fiddle
        {
            Slug = slugGenerator.Generate(),
            Content = request.Content,
            FileReferences = request.FileReferences,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Fiddles.Add(fiddle);

        for (var attempt = 0; attempt < MaxSlugAttempts; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                return CreatedAtAction(nameof(Get), new { slug = fiddle.Slug }, ToResponse(fiddle));
            }
            catch (UniqueConstraintException)
            {
                fiddle.Slug = slugGenerator.Generate();
            }
        }

        return Problem(
            "Could not generate a unique slug after several attempts.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static FiddleResponse ToResponse(Fiddle f) =>
        new(f.Slug, f.Content, f.FileReferences, f.CreatedAt);
}
