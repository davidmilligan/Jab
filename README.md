# Jab
Jab is a source generator for C# utilizing the new [source generator](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.md) features of the Roslyn Compiler. 
It takes care of automatically generating some of the most common and tedious boilerplate code needed for doing dependency injection in C#/.NET (particularly Aspnet Core).
Namely:

* Contructors that simply take a bunch of dependencies as parameters and store them in class fields or properties
* Service registrations (e.g. `services.AddTransient<IMyService, MyService>()`)
* TODO: Interfaces that mirror concrete implementation types (i.e. automatic creation of interfaces for given classes based on their public members)
* *More to come...*

## Constructor Generation
Given the following code:
```
public partial class MyService
{
  [Jab] public IDep1 Dep1 { get; }
  [Jab] public ILogger Logger { get; }
}
```
Jab will automatically generate the following constructor:
```
public partial class MyService
{
  public MyService(IDep1 Dep1, ILogger<MyService> Logger)
  {
    this.Dep1 = Dep1;
    this.Logger = Logger;
  }
}
```

## Service Registration
Simply apply the appropriate attribute (`[Transient]`,`[Scoped]`, or `[Singleton]`) to your service and Jab will automatically add it to a generated extension method that can be used to register all services in the assembly.
For example, given the following code:
```
[Transient] 
public class Service1 : IService1 { }

[Singleton]
public class Service2 : IService2, IService3 { }
```
Jab will generate the following:
```
public static IServiceCollection Jab(this IServiceCollection services) => services
  .AddTransient<Service1, Service1>()
  .AddTransient<IService1, Service1>()
  .AddSingleton<Service2, Service2>()
  .AddSingleton<IService2, Service2>()
  .AddSingleton<IService3, Service2>();
```
which you can use in your `Startup.cs` or whatever:
```
public void ConfigureServices(IServiceCollection services)
{
  services.Jab();
  //...
}
```

## Why "Jab"?
The idea for the name comes from a conversation I overhead where a friend was asking another friend "have you been jabbed yet?" meaning "have you recieved a covid-19 vaccine yet?".
That made me think of "jab" as a fun, short, and apt synonym for "inject". That made it a good candidate for the name for `[Jab]` attribute since `[Inject]` was already taken (Blazor), and it was nice an short (nobody wants to type `[AutomaticallyInjectInContructor]`).
