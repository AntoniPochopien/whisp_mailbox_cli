using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _builder;

    public TypeRegistrar(IServiceCollection builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public ITypeResolver Build()
    {
        // budujemy ServiceProvider i zwracamy resolver
        return new TypeResolver(_builder.BuildServiceProvider());
    }

    public void Register(Type service, Type implementation)
    {
        _builder.AddSingleton(service, implementation);
    }

    public void RegisterInstance(Type service, object implementation)
    {
        _builder.AddSingleton(service, implementation);
    }

    public void RegisterLazy(Type service, Func<object> factory)
    {
        _builder.AddSingleton(service, _ => factory());
    }
}

public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider _provider;

    public TypeResolver(ServiceProvider provider)
    {
        _provider = provider;
    }

    public object Resolve(Type? type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        return _provider.GetService(type)
            ?? throw new InvalidOperationException($"Could not resolve type {type.Name}");
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}
