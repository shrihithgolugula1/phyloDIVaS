using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers
{
    public class TaxaController : BaseCrudSingleKeyController<taxon, int>
    {
        public TaxaController(IUnitOfWork uow) : base(uow) { }
    }
}
