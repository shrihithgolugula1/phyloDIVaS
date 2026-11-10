using PhyloDivas.Data.New.Entities;
using System.Reflection;
using PhyloDivas.Data.New.Repositories;
namespace CapstoneDesign.Controllers
{
    public class AssembliesController : BaseCrudSingleKeyController<Assembly, long>
    {
        public AssembliesController(IUnitOfWork uow) : base(uow) { }
    }
}
