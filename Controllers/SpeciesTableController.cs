using PhyloDivas.Data.New.Entities;
using PhyloDivas.Data.New.Repositories;

namespace CapstoneDesign.Controllers;


public class SpeciesController : BaseCrudSingleKeyController<genomeSpecies, int>
    {
        public SpeciesController(IUnitOfWork uow) : base(uow) { }
    }
