using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OrderService.Api.Auth;
using OrderService.Api.Contracts;

namespace OrderService.Api.Endpoints;

/// <summary>
/// <c>POST /auth/token</c> — único endpoint anônimo da API. Valida contra o
/// usuário único semeado via configuração e emite um JWT. Sem cadastro nem
/// gestão de usuário: aqui faz as vezes de Identity Provider para este teste técnico.
/// </summary>
public static class AuthEndpoints
{
    // Hash dummy pré-computado uma vez, com o mesmo custo de um hash real.
    // Usado quando o username não bate, para que PasswordHasher.Verify seja
    // chamado com o mesmo custo computacional independente de o username
    // existir ou não — sem isso dá pra enumerar usernames por timing (ver
    // decisions.md seção 24.1).
    private static readonly string DummyPasswordHash = PasswordHasher.Hash("dummy-password-for-timing-safety");

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/auth/token", IssueTokenAsync)
            .WithTags("Auth")
            .AllowAnonymous();

        return app;
    }

    private static IResult IssueTokenAsync(
        AuthTokenRequest request,
        IOptions<AuthOptions> authOptions,
        JwtTokenService tokenService,
        HttpContext httpContext)
    {
        var auth = authOptions.Value;

        var hasCredentials = !string.IsNullOrWhiteSpace(request.Username)
            && !string.IsNullOrWhiteSpace(request.Password);

        // Verify roda sempre, mesmo com username errado — contra o hash real
        // ou, se não bateu, contra o dummy acima. Se deixasse o && dar
        // short-circuit e pular o Verify (que custa dezenas de ms de PBKDF2)
        // quando o username está errado, dava pra distinguir "username
        // errado" de "username certo, senha errada" só pela latência.
        var usernameMatches = hasCredentials
            && string.Equals(request.Username, auth.Username, StringComparison.Ordinal);
        var hashToVerify = usernameMatches ? auth.PasswordHash : DummyPasswordHash;

        // request.Password não é nulo/vazio aqui, hasCredentials já garante
        // isso — o short-circuit do && só acontece na ausência de
        // credenciais, nunca por causa do username, que é o único caso que
        // precisa de tempo constante.
        var passwordMatches = hasCredentials && PasswordHasher.Verify(request.Password!, hashToVerify);

        var isValid = usernameMatches && passwordMatches;

        if (!isValid)
        {
            // Credenciais inválidas são um resultado esperado, não uma
            // exceção — tratamos direto aqui, seguindo o mesmo formato
            // ProblemDetails do GlobalExceptionHandler, mas sem passar por ele.
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "Username or password is incorrect.",
                type: $"https://orderservice.local/errors/{StatusCodes.Status401Unauthorized}",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        var (accessToken, expiresAtUtc) = tokenService.GenerateToken(auth.Username);

        return Results.Ok(new AuthTokenResponse(accessToken, expiresAtUtc));
    }
}
