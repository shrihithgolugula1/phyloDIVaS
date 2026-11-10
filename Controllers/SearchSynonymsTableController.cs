using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers
{
    public class SearchHistoriesController : BaseCrudSingleKeyController<searchHistory, long>
    {
        public SearchHistoriesController(IUnitOfWork uow) : base(uow) { }
    }
}
