using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers;

[ApiController]
[Route("api/genes/{geneId:int}/identifiers")]
public class GeneIdentifierXrefsController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    public GeneIdentifierXrefsController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<geneIdentifierXref>>> GetAll(int geneId, CancellationToken ct)
    {
        var list = await _uow.Repository<geneIdentifierXref>().FindAsync(x => x.gene_id == geneId, ct);
        return Ok(list);
    }

    [HttpGet("{source}/{identifier}")]
    public async Task<ActionResult<geneIdentifierXref>> GetById(int geneId, string source, string identifier, CancellationToken ct)
    {
        var item = await _uow.Repository<geneIdentifierXref>()
            .GetByIdsAsync(new object[] { geneId, source, identifier }, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<geneIdentifierXref>> Create(int geneId, [FromBody] geneIdentifierXref body, CancellationToken ct)
    {
        body.gene_id = geneId;
        await _uow.Repository<geneIdentifierXref>().AddAsync(body, ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(body);
    }

    [HttpDelete("{source}/{identifier}")]
    public async Task<IActionResult> Delete(int geneId, string source, string identifier, CancellationToken ct)
    {
        var repo = _uow.Repository<geneIdentifierXref>();
        var existing = await repo.GetByIdsAsync(new object[] { geneId, source, identifier }, ct);
        if (existing is null) return NotFound();

        repo.Remove(existing);
        await _uow.SaveChangesAsync(ct);
        return NoContent();
    }
}
