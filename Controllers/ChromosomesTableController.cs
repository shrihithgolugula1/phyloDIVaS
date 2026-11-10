using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers
{
    public class ChromosomesController : BaseCrudSingleKeyController<genomeChromosome, long>
    {
        public ChromosomesController(IUnitOfWork uow) : base(uow) { }
    }
}
