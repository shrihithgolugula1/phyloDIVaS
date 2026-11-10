using System.Collections.Concurrent;
using PhyloDivas.Data.New.Data;

namespace PhyloDivas.Data.New.Repositories;

public class UnitOfWork : IUnitOfWork
    {
        private readonly PhylodivasContext _ctx;
        private readonly ConcurrentDictionary<Type, object> _repos = new();

        public UnitOfWork(PhylodivasContext ctx) => _ctx = ctx;

        public IGenericRepository<T> Repository<T>() where T : class =>
            (IGenericRepository<T>)_repos.GetOrAdd(typeof(T), _ => new GenericRepository<T>(_ctx));

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => _ctx.SaveChangesAsync(ct);
        public ValueTask DisposeAsync() => _ctx.DisposeAsync();
    }


