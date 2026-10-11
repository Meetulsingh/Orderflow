using System.ClientModel;
using FluentValidation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;
using OrderFlow.Assistant.Catalog;
using OrderFlow.Assistant.Chat;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Assistant;

/// <summary>
/// AI shopping assistant, hosted in-process by the API gateway so it adds no service to wake on free
/// hosting. Without an API key the module stays registered but reports itself disabled.
/// </summary>
public static class AssistantModule
{
    public static WebApplicationBuilder AddAssistantModule(this WebApplicationBuilder builder, string catalogAddress)
    {
        var options = AssistantOptions.From(builder.Configuration);
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DailyQuota>();
        builder.Services.AddValidatorsFromAssemblyContaining<ChatRequestValidator>();

        // The catalog may be cold-starting on free hosting, so allow it time to wake.
        builder.Services.AddHttpClient<CatalogClient>(c =>
        {
            c.BaseAddress = new Uri(catalogAddress);
            c.Timeout = TimeSpan.FromSeconds(90);
        });

        if (options.Enabled)
        {
            builder.Services
                .AddChatClient(_ => new OpenAI.Chat.ChatClient(
                        options.Model,
                        new ApiKeyCredential(options.ApiKey!),
                        new OpenAIClientOptions { Endpoint = options.Endpoint })
                    .AsIChatClient())
                .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 4);
            builder.Services.AddScoped<ShoppingAssistant>();
        }

        return builder;
    }

    public static RouteGroupBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assistant").WithTags("Assistant");

        group.MapGet("/status", (AssistantOptions options) => new AssistantStatus(options.Enabled))
            .AllowAnonymous()
            .WithSummary("Whether the AI assistant is configured on this deployment");

        group.MapPost("/chat", (ChatRequest request, HttpContext http, IServiceProvider services, CancellationToken ct) =>
            {
                var assistant = services.GetService<ShoppingAssistant>()
                    ?? throw new AssistantUnavailableException("The AI assistant isn't configured on this deployment.");
                return assistant.ReplyAsync(request, http.User.Identity?.Name, ct);
            })
            .Validate<ChatRequest>()
            .RequireAuthorization()
            .WithSummary("Ask the shopping assistant; it searches the catalog with tools and returns a reply plus product cards");

        return group;
    }
}
