namespace BitCode.Framework.Shared.Kernel;

public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : IEquatable<TId>
{
    public TId Id { get; protected set; } = default!;

    protected Entity()
    {
    }

    protected Entity(TId id)
    {
        Id = id;
    }

    public bool Equals(Entity<TId>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id.Equals(other.Id);
    }

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
