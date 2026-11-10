using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New.Repositories;

namespace FeedbackAPI;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseCrudSingleKeyController<TEntity, TKey> : ControllerBase
    where TEntity : class
{
    protected readonly IUnitOfWork Uow;
    protected BaseCrudSingleKeyController(IUnitOfWork uow) => Uow = uow;

    [HttpGet]
    public virtual async Task<ActionResult<IReadOnlyList<TEntity>>> GetAll(CancellationToken ct)
        => Ok(await Uow.Repository<TEntity>().ListAsync(ct));

    [HttpGet("{id}")]
    public virtual async Task<ActionResult<TEntity>> GetById([FromRoute] TKey id, CancellationToken ct)
    {
        var item = await Uow.Repository<TEntity>().GetByIdAsync(id!, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public virtual async Task<ActionResult<TEntity>> Create([FromBody] TEntity body, CancellationToken ct)
    {
        await Uow.Repository<TEntity>().AddAsync(body, ct);
        await Uow.SaveChangesAsync(ct);
        return Ok(body);
    }

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Update([FromRoute] TKey id, [FromBody] TEntity body, CancellationToken ct)
    {
        var repo = Uow.Repository<TEntity>();
        var existing = await repo.GetByIdAsync(id!, ct);
        if (existing is null) return NotFound();

        // simplest: overwrite
        repo.Update(body);
        await Uow.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete([FromRoute] TKey id, CancellationToken ct)
    {
        var repo = Uow.Repository<TEntity>();
        var existing = await repo.GetByIdAsync(id!, ct);
        if (existing is null) return NotFound();

        repo.Remove(existing);
        await Uow.SaveChangesAsync(ct);
        return NoContent();
    }
}
