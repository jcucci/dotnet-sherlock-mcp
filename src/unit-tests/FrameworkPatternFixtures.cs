using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.AspNetCore.Mvc
{
    public abstract class ControllerBase
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ApiControllerAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NonActionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class RouteAttribute(string template) : Attribute
    {
        public string Template { get; } = template;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpGetAttribute : Attribute
    {
        public HttpGetAttribute() { }
        public HttpGetAttribute(string template) => Template = template;
        public string? Template { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HttpPostAttribute : Attribute
    {
        public HttpPostAttribute() { }
        public HttpPostAttribute(string template) => Template = template;
        public string? Template { get; }
    }
}

namespace Microsoft.AspNetCore.Routing
{
    public interface IEndpointRouteBuilder
    {
    }
}

namespace Microsoft.AspNetCore.Builder
{
    using Microsoft.AspNetCore.Routing;

    public static class EndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapGet(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => endpoints;
        public static IEndpointRouteBuilder MapPost(this IEndpointRouteBuilder endpoints, string pattern, Delegate handler) => endpoints;
        public static IEndpointRouteBuilder MapMethods(this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> httpMethods, Delegate handler) => endpoints;
        public static IEndpointRouteBuilder MapGroup(this IEndpointRouteBuilder endpoints, string prefix) => endpoints;
        public static IEndpointRouteBuilder WithName(this IEndpointRouteBuilder endpoints, string name) => endpoints;
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
    }

    public class DbSet<TEntity> where TEntity : class
    {
    }
}

namespace MediatR
{
    public interface IRequest<out TResponse>
    {
    }

    public interface INotification
    {
    }

    public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    public interface INotificationHandler<in TNotification> where TNotification : INotification
    {
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }
}

namespace Sherlock.MCP.Tests.FrameworkPatternFixtures
{
    using MediatR;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.EntityFrameworkCore;

    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        [HttpGet]
        public string[] List() => [];

        [HttpGet("{id}")]
        public string Get(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        [HttpPost]
        public Task CreateAsync() => Task.CompletedTask;

        [HttpGet("/health")]
        public string Health() => "ok";

        [NonAction]
        public void Helper() { }
    }

    public class NotAController
    {
        public void Index() { }
    }

    public static class EndpointMappings
    {
        public static void Map(IEndpointRouteBuilder app)
        {
            app.MapGet("/widgets", () => "all").WithName("ListWidgets");
            app.MapPost("/widgets", WidgetHandlers.Create);
            app.MapMethods("/widgets/{id}", ["PUT", "PATCH"], WidgetHandlers.Update);
            app.MapGroup("/admin");
        }
    }

    public static class WidgetHandlers
    {
        public static string Create() => "created";
        public static string Update(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public interface IWidgetRepository
    {
    }

    public sealed class WidgetRepository : IWidgetRepository
    {
    }

    public interface IClock
    {
    }

    public sealed class SystemClock : IClock
    {
    }

    public interface IAuditSink
    {
    }

    public sealed class AuditSink : IAuditSink
    {
    }

    public sealed class WidgetOptions
    {
    }

    public static class WidgetServiceRegistrations
    {
        public static IServiceCollection AddWidgets(this IServiceCollection services)
        {
            services.AddScoped<IWidgetRepository, WidgetRepository>();
            services.TryAddSingleton<IClock, SystemClock>();
#pragma warning disable CA2263
            services.AddTransient(typeof(IAuditSink), typeof(AuditSink));
#pragma warning restore CA2263
            services.AddSingleton<WidgetOptions>(_ => new WidgetOptions());
            services.AddKeyedScoped<IWidgetRepository, WidgetRepository>("archive");
            return services;
        }
    }

    public class Widget
    {
    }

    public class Gadget
    {
    }

    public class ShopContext : DbContext
    {
        public DbSet<Widget> Widgets { get; set; } = new();
        public DbSet<Gadget> Gadgets { get; set; } = new();
        public string Name { get; set; } = "";
    }

    public class ArchiveContext : ShopContext
    {
    }

    public record GetWidget(int Id) : IRequest<Widget>;

    public record WidgetCreated(int Id) : INotification;

    public class GetWidgetHandler : IRequestHandler<GetWidget, Widget>
    {
        public Task<Widget> Handle(GetWidget request, CancellationToken cancellationToken) => Task.FromResult(new Widget());
    }

    public class WidgetCreatedHandler : INotificationHandler<WidgetCreated>
    {
        public Task Handle(WidgetCreated notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public abstract class AbstractWidgetHandler : INotificationHandler<WidgetCreated>
    {
        public abstract Task Handle(WidgetCreated notification, CancellationToken cancellationToken);
    }
}
