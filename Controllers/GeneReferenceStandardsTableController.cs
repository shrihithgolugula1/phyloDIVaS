using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Entities;

namespace CapstoneDesign.Controllers
{
    public class GeneReferenceStandardsController : BaseCrudSingleKeyController<geneReferenceStandard, long>
    {
        public GeneReferenceStandardsController(IUnitOfWork uow) : base(uow) { }
    }
}
