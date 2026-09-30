using Microsoft.EntityFrameworkCore;
using Mrp.Masters.Persistence;
using Mrp.SharedKernel.Domain;
using Mrp.SharedKernel.Web;

namespace Mrp.Masters.Application;

/// <summary>Describes how one master entity is searched, mapped and saved.</summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TDto">Response type.</typeparam>
/// <typeparam name="TRequest">Create/update request type.</typeparam>
public interface IMasterDefinition<TEntity, TDto, TRequest>
    where TEntity : Entity
{
    /// <summary>Display name used in error messages.</summary>
    string Name { get; }

    /// <summary>Applies the free-text search and active filter.</summary>
    IQueryable<TEntity> Filter(IQueryable<TEntity> query, string? search, bool activeOnly);

    /// <summary>Default list order.</summary>
    IQueryable<TEntity> Order(IQueryable<TEntity> query);

    /// <summary>Creates an empty entity.</summary>
    TEntity Create(TRequest request);

    /// <summary>Validates references and copies the request onto the entity.</summary>
    Task ApplyAsync(TEntity entity, TRequest request, MastersDbContext db, CancellationToken cancellationToken);

    /// <summary>Maps entities to responses (may load related data).</summary>
    Task<IReadOnlyList<TDto>> ToDtoAsync(IReadOnlyList<TEntity> entities, MastersDbContext db, CancellationToken cancellationToken);
}

/// <summary>List, get, create and update for a master entity. Masters are deactivated, never deleted.</summary>
public sealed class MasterCrud<TEntity, TDto, TRequest>(MastersDbContext db, IMasterDefinition<TEntity, TDto, TRequest> definition)
    where TEntity : Entity
{
    /// <summary>One page of records.</summary>
    public async Task<PagedResult<TDto>> ListAsync(string? search, bool? activeOnly, int? page, int? pageSize, CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (text is { Length: > 100 })
        {
            text = text[..100];
        }

        var query = definition.Order(definition.Filter(db.Set<TEntity>().AsNoTracking(), text, activeOnly ?? false));
        var result = await query.ToPagedAsync(page, pageSize, cancellationToken);
        var items = await definition.ToDtoAsync(result.Items, db, cancellationToken);
        return new PagedResult<TDto>(items, result.Total, result.Page, result.PageSize);
    }

    /// <summary>One record.</summary>
    public async Task<TDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw DomainException.NotFound(definition.Name, id);
        return (await definition.ToDtoAsync([entity], db, cancellationToken))[0];
    }

    /// <summary>Creates a record.</summary>
    public async Task<TDto> CreateAsync(TRequest request, CancellationToken cancellationToken)
    {
        var entity = definition.Create(request);
        await definition.ApplyAsync(entity, request, db, cancellationToken);
        db.Set<TEntity>().Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (await definition.ToDtoAsync([entity], db, cancellationToken))[0];
    }

    /// <summary>Updates a record.</summary>
    public async Task<TDto> UpdateAsync(Guid id, TRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw DomainException.NotFound(definition.Name, id);
        await definition.ApplyAsync(entity, request, db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await definition.ToDtoAsync([entity], db, cancellationToken))[0];
    }
}

/// <summary>Helpers shared by master definitions.</summary>
internal static class MasterQueries
{
    public static IQueryable<T> FilterCoded<T>(IQueryable<T> query, string? search, bool activeOnly)
        where T : Domain.CodedMaster
    {
        if (activeOnly)
        {
            query = query.Where(e => e.IsActive);
        }

        if (search is not null)
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(e => EF.Functions.ILike(e.Code, pattern, "\\") || EF.Functions.ILike(e.Name, pattern, "\\")
                || (e.NameEn != null && EF.Functions.ILike(e.NameEn, pattern, "\\")));
        }

        return query;
    }

    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    public static async Task RequireAsync<T>(MastersDbContext db, Guid id, string name, CancellationToken cancellationToken)
        where T : Entity
    {
        if (!await db.Set<T>().AnyAsync(e => e.Id == id, cancellationToken))
        {
            throw new DomainException("masters.reference_not_found", $"{name} '{id}' does not exist.", 400);
        }
    }
}
