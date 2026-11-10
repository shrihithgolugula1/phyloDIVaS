using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using PhyloDivas.Data.New.Data;

namespace PhyloDivas.Data.New.Repositories;

    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        private readonly PhylodivasContext _ctx;
        private DbSet<T> Set => _ctx.Set<T>();

        public GenericRepository(PhylodivasContext ctx) => _ctx = ctx;

        public async Task<T?> GetByIdAsync(object id, CancellationToken ct = default)
            => await Set.FindAsync(new[] { id }, ct);

        public async Task<T?> GetByIdsAsync(object[] ids, CancellationToken ct = default)
            => await Set.FindAsync(ids, ct);

        public async Task<IReadOnlyList<T>> ListAsync(CancellationToken ct = default)
            => await Set.AsNoTracking().ToListAsync(ct);

        public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
            => await Set.AsNoTracking().Where(predicate).ToListAsync(ct);

        public Task AddAsync(T entity, CancellationToken ct = default) => Set.AddAsync(entity, ct).AsTask();
        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) => Set.AddRangeAsync(entities, ct);

        public void Update(T entity) => Set.Update(entity);
        public void Remove(T entity) => Set.Remove(entity);
        public void RemoveRange(IEnumerable<T> entities) => Set.RemoveRange(entities);
    }

