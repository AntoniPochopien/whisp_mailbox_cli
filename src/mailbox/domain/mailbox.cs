class Mailbox
{
    int Id { get; }
    string Name { get; }
    string PINhash { get; }

    public Mailbox(int id, string name, string PINhash)
    {
        Id = id;
        Name = name;
        this.PINhash = PINhash;
    }
}