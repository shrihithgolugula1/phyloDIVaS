using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Entities;


namespace CapstoneDesign.Controllers
{
    public class GeneAssemblyContextsController : BaseCrudSingleKeyController<geneAssemblyContext, long>
    {
        public GeneAssemblyContextsController(IUnitOfWork uow) : base(uow) { }
    }
}
