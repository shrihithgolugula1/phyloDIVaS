using APIInterface.SupportingClasses;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers
{
    public class AssemblyStatsController : BaseCrudSingleKeyController<AssemblyStats, long>
    {
        public AssemblyStatsController(IUnitOfWork uow) : base(uow) { }
    }
}
