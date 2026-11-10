using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers
{
    public class UsersController : BaseCrudSingleKeyController<User, Guid>
    {
        public UsersController(IUnitOfWork uow) : base(uow) { }
    }
}
