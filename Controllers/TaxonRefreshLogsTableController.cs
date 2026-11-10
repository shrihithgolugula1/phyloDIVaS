using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers
{
    public class TaxonRefreshLogsController : BaseCrudSingleKeyController<taxonRefreshLog, long>
    {
        public TaxonRefreshLogsController(IUnitOfWork uow) : base(uow) { }
    }
}
