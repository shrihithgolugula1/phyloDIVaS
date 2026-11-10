using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Entities;

namespace CapstoneDesign.Controllers
{
    public class GenesController : BaseCrudSingleKeyController<gene, int>
    {
        public GenesController(IUnitOfWork uow) : base(uow) { }
    }
}
