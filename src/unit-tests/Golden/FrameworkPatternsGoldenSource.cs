using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sherlock.MCP.Tests.Golden;

internal static class FrameworkPatternsGoldenSource
{
    public const string AssemblyName = "Sherlock.Golden.Framework";

    public const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        namespace Microsoft.Extensions.DependencyInjection
        {
            public interface IServiceCollection { }

            public static class ServiceCollectionServiceExtensions
            {
                public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services) where TImplementation : TService => services;
                public static IServiceCollection AddSingleton<TService>(this IServiceCollection services, Func<object, TService> factory) => services;
                public static IServiceCollection AddTransient(this IServiceCollection services, Type serviceType, Type implementationType) => services;
            }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public abstract class ControllerBase { }

            [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
            public sealed class RouteAttribute(string template) : Attribute
            {
                public string Template { get; } = template;
            }

            [AttributeUsage(AttributeTargets.Method)]
            public sealed class HttpGetAttribute : Attribute
            {
                public HttpGetAttribute() { }
                public HttpGetAttribute(string template) { }
            }
        }

        namespace Microsoft.AspNetCore.Builder
        {
            public interface IEndpointRouteBuilder { }

            public static class EndpointRouteBuilderExtensions
            {
                public static IEndpointRouteBuilder MapGet(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => endpoints;
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }

            public class DbSet<TEntity> where TEntity : class { }
        }

        namespace MediatR
        {
            public interface IRequest<out TResponse> { }

            public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
            {
                Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
            }
        }

        namespace Golden.Framework
        {
            using MediatR;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.Extensions.DependencyInjection;

            public class Order { }

            public interface IOrderStore { }

            public sealed class OrderStore : IOrderStore { }

            public interface IClock { }

            public sealed class SystemClock : IClock { }

            public sealed class OrderOptions { }

            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpGet]
                public Order[] List() => [];

                [HttpGet("{id}")]
                public Order? Get(int id) => null;
            }

            public static class Startup
            {
                public static void AddOrders(IServiceCollection services)
                {
                    services.AddScoped<IOrderStore, OrderStore>();
                    services.AddSingleton<OrderOptions>(_ => new OrderOptions());
                    services.AddTransient(typeof(IClock), typeof(SystemClock));
                }

                public static void MapOrders(IEndpointRouteBuilder app) => app.MapGet("/orders/count", () => 0);
            }

            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; } = new();
            }

            public record GetOrder(int Id) : IRequest<Order>;

            public class GetOrderHandler : IRequestHandler<GetOrder, Order>
            {
                public Task<Order> Handle(GetOrder request, CancellationToken cancellationToken) => Task.FromResult(new Order());
            }
        }
        """;

    public static string Emit(string directory)
    {
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, $"{AssemblyName}.dll");
        var compilation = CSharpCompilation.Create(
            AssemblyName,
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest))],
            PdbFixtures.PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, deterministic: true));

        using var peStream = File.Create(assemblyPath);
        var result = compilation.Emit(peStream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return assemblyPath;
    }
}
