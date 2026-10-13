using System.ClientModel;
using Microsoft.Extensions.AI;
using OrderFlow.Assistant.Catalog;

namespace OrderFlow.Assistant.Chat;

/// <summary>
/// Answers one shopper message. The model searches the real catalog through tools (it never invents
/// products) and picks products to show as cards; the reply is plain text plus those cards.
/// </summary>
public sealed class ShoppingAssistant(IChatClient chat, CatalogClient catalog, DailyQuota quota, ILogger<ShoppingAssistant> logger)
{
    private const int MaxHistory = 12;

    public async Task<ChatReply> ReplyAsync(ChatRequest request, string? shopperName, CancellationToken ct)
    {
        if (!quota.TryTake())
            throw new AssistantUnavailableException("The assistant has used up today's free quota. Please try again tomorrow.");

        IReadOnlyList<CatalogProduct> products;
        try
        {
            products = await catalog.GetAllAsync(ct);
        }
        catch (Exception ex) when (ex is (HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Assistant could not load the catalog");
            throw new AssistantUnavailableException("The catalog is still waking up. Please try again in a few seconds.");
        }

        var tools = new ShoppingTools(products);
        var options = new ChatOptions
        {
            MaxOutputTokens = 1024,
            Tools =
            [
                AIFunctionFactory.Create(tools.SearchProducts, "search_products"),
                AIFunctionFactory.Create(tools.ShowProducts, "show_products")
            ]
        };

        List<ChatMessage> messages = [new(ChatRole.System, SystemPrompt(products, shopperName))];
        messages.AddRange(request.Messages.TakeLast(MaxHistory).Select(m =>
            new ChatMessage(m.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, m.Content)));

        try
        {
            var response = await chat.GetResponseAsync(messages, options, ct);
            var text = response.Text.Trim();
            if (text.Length == 0)
                text = tools.Shown.Count > 0 ? "Here are a few options I'd suggest:" : "Sorry, I couldn't come up with an answer. Could you rephrase?";
            return new ChatReply(text, tools.Shown);
        }
        catch (ClientResultException ex) when (ex.Status == StatusCodes.Status429TooManyRequests)
        {
            // The provider's message says which quota was hit (e.g. a model with no free-tier quota at all).
            logger.LogWarning("Model provider rate-limited the assistant: {Message}", ex.Message);
            throw new AssistantUnavailableException("The assistant is busy right now (free-tier rate limit). Please try again in a minute.");
        }
        catch (ClientResultException ex)
        {
            logger.LogWarning(ex, "Model provider returned {Status}", ex.Status);
            throw new AssistantUnavailableException("The assistant couldn't answer right now. Please try again shortly.");
        }
    }

    private static string SystemPrompt(IReadOnlyList<CatalogProduct> products, string? shopperName)
    {
        var categories = string.Join(", ", products
            .GroupBy(p => p.Category)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key} ({g.Count()})"));
        // The store is small, so the model always sees every product name: a synonym ("fridge") or a product
        // added since the conversation started can't make it claim the store doesn't sell something.
        var inventory = string.Join("\n", products
            .OrderBy(p => p.Category).ThenBy(p => p.Name)
            .Select(p => $"- {p.Name} ({p.Category}, ${p.Price:0.00}{(p.Stock == 0 ? ", out of stock" : "")})"));
        var firstName = shopperName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var name = firstName is null ? "the shopper" : firstName[..Math.Min(firstName.Length, 40)];

        return $"""
            You are the shopping assistant for OrderFlow, a demo online store. You are talking to {name}.
            Help them find products from this store's catalog and decide what to buy.

            How to work:
            - Use search_products to look things up. Only talk about products it returns; never invent products, prices, stock or features.
            - When you recommend products, call show_products with their ids (best first, at most {ShoppingTools.MaxShown}). The shopper then sees cards with an "Add to cart" button. Never write ids in your reply.
            - Keep replies short and friendly: two to four sentences, prices in USD. Say why each pick fits what they asked for.
            - If the store doesn't sell what they want, say so and suggest the closest alternatives.
            - Products with stock 0 can't be ordered; mention it when stock is below 5.
            - You can't place orders, change the cart or see order history. The shopper adds items with the card buttons and checks out from the cart.
            - Politely decline anything unrelated to shopping in this store.
            - The catalog below is current and changes over time. If it disagrees with something said earlier in this conversation, the catalog is right.
            - Match what the shopper means, not just their exact words ("fridge" is a refrigerator, "sneakers" are shoes). Search by the product's name from the list below.

            Categories: {categories}.

            Every product in the store right now:
            {inventory}
            """;
    }
}
