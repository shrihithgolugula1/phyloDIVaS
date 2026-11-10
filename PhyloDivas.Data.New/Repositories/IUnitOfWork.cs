using PhyloDivas.Data.New.Data;

namespace PhyloDivas.Data.New.Repositories;
    public interface IUnitOfWork : IAsyncDisposable
    {
        IGenericRepository<T> Repository<T>() where T : class;
        Task<int> SaveChangesAsync(CancellationToken ct = default);
    }

