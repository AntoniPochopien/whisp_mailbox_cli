class DbEntity<T>
{
    int Id { get; }
    T Entity { get; }

    public DbEntity(int id, T entity)
    {
        Id = id;
        Entity = entity;
    }
}