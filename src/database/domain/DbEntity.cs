public class DbEntity<T>
{
    public int Id { get; }
    public T Entity { get; }

    public DbEntity(int id, T entity)
    {
        Id = id;
        Entity = entity;
    }
}