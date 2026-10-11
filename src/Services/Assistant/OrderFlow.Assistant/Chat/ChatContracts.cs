using FluentValidation;
using OrderFlow.Assistant.Catalog;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Assistant.Chat;

/// <summary>One message of the conversation, as kept by the browser. Tool calls stay server-side.</summary>
public sealed record ChatTurn(string Role, string Content);

public sealed record ChatRequest(IReadOnlyList<ChatTurn> Messages);

/// <summary>The assistant's answer plus the products it chose to show as cards.</summary>
public sealed record ChatReply(string Reply, IReadOnlyList<CatalogProduct> Products);

public sealed record AssistantStatus(bool Enabled);

public sealed class AssistantUnavailableException(string message) : AppException(message)
{
    public override int StatusCode => StatusCodes.Status503ServiceUnavailable;
    public override string Title => "Assistant unavailable";
}

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public const int MaxMessages = 20;
    public const int MaxLength = 1000;

    public ChatRequestValidator()
    {
        RuleFor(x => x.Messages).NotEmpty().Must(m => m.Count <= MaxMessages)
            .WithMessage($"A conversation can have at most {MaxMessages} messages.");
        RuleFor(x => x.Messages).Must(m => m.Count > 0 && m[^1].Role == "user")
            .WithMessage("The last message must come from the user.");
        RuleForEach(x => x.Messages).ChildRules(turn =>
        {
            turn.RuleFor(t => t.Role).Must(r => r is "user" or "assistant");
            turn.RuleFor(t => t.Content).NotEmpty().MaximumLength(MaxLength);
        });
    }
}
