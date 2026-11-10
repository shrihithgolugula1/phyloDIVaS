using Microsoft.AspNetCore.Mvc;
using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers;

[ApiController]
[Route("api/genes/{geneId:int}/transcripts")]
public class GeneTranscriptQuicksController : ControllerBase
{
    private readonly IUnitOfWork _uow;
    public GeneTranscriptQuicksController(IUnitOfWork uow) => _uow = uow;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<geneTranscriptQuick>>> GetAll(int geneId, CancellationToken ct)
    {
        var list = await _uow.Repository<geneTranscriptQuick>().FindAsync(t => t.gene_id == geneId, ct);
        return Ok(list);
    }

    [HttpGet("{transcriptAccession}")]
    public async Task<ActionResult<geneTranscriptQuick>> GetById(int geneId, string transcriptAccession, CancellationToken ct)
    {
        var item = await _uow.Repository<geneTranscriptQuick>()
            .GetByIdsAsync(new object[] { geneId, transcriptAccession }, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<geneTranscriptQuick>> Create(int geneId, [FromBody] geneTranscriptQuick body, CancellationToken ct)
    {
        body.gene_id = geneId;
        await _uow.Repository<geneTranscriptQuick>().AddAsync(body, ct);
        await _uow.SaveChangesAsync(ct);
        return Ok(body);
    }

    [HttpDelete("{transcriptAccession}")]
    public async Task<IActionResult> Delete(int geneId, string transcriptAccession, CancellationToken ct)
    {
        var repo = _uow.Repository<geneTranscriptQuick>();
        var existing = await repo.GetByIdsAsync(new object[] { geneId, transcriptAccession }, ct);
        if (existing is null) return NotFound();

        repo.Remove(existing);
        await _uow.SaveChangesAsync(ct);
        return NoContent();
    }
}
