using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Entities;

namespace CapstoneDesign.Controllers;

[ApiController]
[Route("api/species/{taxId:int}/synonyms")]
public class SpeciesSynonymsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    public SpeciesSynonymsController(IUnitOfWork uow) => _uow = uow;

    // GET /api/species/{taxId}/synonyms
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<genomeSpeciesSynonym>>> List([FromRoute] int taxId, CancellationToken ct)
    {
        var list = await _uow.Repository<genomeSpeciesSynonym>().FindAsync(s => s.tax_id == taxId, ct);
        return Ok(list);
    }

    // GET /api/species/{taxId}/synonyms/{synonym}
    [HttpGet("{synonym}")]
    public async Task<ActionResult<genomeSpeciesSynonym>> Get([FromRoute] int taxId, [FromRoute] string synonym, CancellationToken ct)
    {
        var item = await _uow.Repository<genomeSpeciesSynonym>().GetByIdsAsync(new object[] { taxId, synonym }, ct);
        return item is null ? NotFound() : Ok(item);
    }

    // POST /api/species/{taxId}/synonyms
    [HttpPost]
    public async Task<ActionResult<genomeSpeciesSynonym>> Create([FromRoute] int taxId, [FromBody] genomeSpeciesSynonym body, CancellationToken ct)
    {
        body.tax_id = taxId; // ensure route and body match
        await _uow.Repository<genomeSpeciesSynonym>().AddAsync(body, ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(body);
    }

    // DELETE /api/species/{taxId}/synonyms/{synonym}
    [HttpDelete("{synonym}")]
    public async Task<IActionResult> Delete([FromRoute] int taxId, [FromRoute] string synonym, CancellationToken ct)
    {
        var repo = _uow.Repository<genomeSpeciesSynonym>();
        var existing = await repo.GetByIdsAsync(new object[] { taxId, synonym }, ct);
        if (existing is null) return NotFound();

        repo.Remove(existing);
        await _uow.SaveChangesAsync(ct);
        return NoContent();
    }
}
