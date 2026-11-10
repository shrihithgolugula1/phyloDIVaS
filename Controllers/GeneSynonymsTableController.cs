using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers;

[ApiController]
[Route("api/genes/{geneId:int}/synonyms")]
public class GeneSynonymsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    public GeneSynonymsController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<geneSynonym>>> GetAll(int geneId, CancellationToken ct)
    {
        var list = await _uow.Repository<geneSynonym>().FindAsync(g => g.gene_id == geneId, ct);
        return Ok(list);
    }

    [HttpGet("{synonym}")]
    public async Task<ActionResult<geneSynonym>> GetById(int geneId, string synonym, CancellationToken ct)
    {
        var item = await _uow.Repository<geneSynonym>().GetByIdsAsync(new object[] { geneId, synonym }, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<geneSynonym>> Create(int geneId, [FromBody] geneSynonym body, CancellationToken ct)
    {
        body.gene_id = geneId;
        await _uow.Repository<geneSynonym>().AddAsync(body, ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(body);
    }

    [HttpDelete("{synonym}")]
    public async Task<IActionResult> Delete(int geneId, string synonym, CancellationToken ct)
    {
        var repo = _uow.Repository<geneSynonym>();
        var existing = await repo.GetByIdsAsync(new object[] { geneId, synonym }, ct);
        if (existing is null) return NotFound();

        repo.Remove(existing);
        await _uow.SaveChangesAsync(ct);
        return NoContent();
    }
}
