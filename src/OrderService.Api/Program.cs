using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OrderService.Api.Auth;
using OrderService.Api.Endpoints;
using OrderService.Api.ErrorHandling;
using OrderService.Application.Orders.CancelOrder;
using OrderService.Application.Orders.ConfirmOrder;
using OrderService.Application.Orders.CreateOrder;
using OrderService.Application.Orders.GetOrderById;
using OrderService.Application.Orders.ListOrders;
using OrderService.Infrastructure;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Autenticação JWT com usuário único semeado via configuração. Sem RBAC,
// sem isolamento por customerId (ver decisions.md, seções 3 e 4).
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.PasswordHash),
        "Auth:Username e Auth:PasswordHash devem estar configurados.")
    .ValidateOnStart();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Issuer)
            && !string.IsNullOrWhiteSpace(options.Audience)
            && !string.IsNullOrWhiteSpace(options.Key),
        "Jwt:Issuer, Jwt:Audience e Jwt:Key devem estar configurados.")
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException($"Configuration section '{JwtOptions.SectionName}' not found.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// Scoped porque os repositórios usados aqui dependem do DbContext, que também é Scoped.
builder.Services.AddScoped<CreateOrderHandler>();
builder.Services.AddScoped<ConfirmOrderHandler>();
builder.Services.AddScoped<CancelOrderHandler>();
builder.Services.AddScoped<GetOrderByIdHandler>();
builder.Services.AddScoped<ListOrdersHandler>();

// Currency e OrderStatus vão como string no JSON, não como número (decisions.md seção 2).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Habilita o botão "Authorize" na Swagger UI. Cole só o token puro (sem
    // "Bearer "), o Swashbuckle já adiciona o prefixo no header Authorization.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Cole apenas o token JWT obtido via POST /auth/token (sem o prefixo \"Bearer \").",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

// Health check leve, não toca no banco. Usado pelo healthcheck do serviço
// `api` no docker-compose.yml. Fica anônimo porque AddAuthorization() não
// aplica nenhuma política global — só os grupos que chamam
// .RequireAuthorization() explicitamente (ver OrderEndpoints).
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();

// Swagger liga em Development, mas também via a flag `EnableSwagger`,
// independente do ASPNETCORE_ENVIRONMENT — é o que o docker-compose.yml de
// avaliação usa. De propósito desacoplado de IsDevelopment(): não pode ter
// efeito colateral em outros comportamentos ligados a ambiente, como o
// vazamento de detalhe de exceção em GlobalExceptionHandler.
var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("EnableSwagger");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 401 e 403 já saem em formato ProblemDetails direto do middleware de auth
// do ASP.NET Core, sem nada extra da nossa parte — AddProblemDetails() já
// registrado é suficiente, não precisa de UseStatusCodePages.
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapOrderEndpoints();
app.MapHealthChecks("/health");

// Auto-migrate + seed antes de aceitar requisições (checklist do README).
await app.ApplyMigrationsAsync();
await app.SeedAsync();

app.Run();

/// <summary>Público e parcial só para o <c>WebApplicationFactory&lt;Program&gt;</c> dos testes de integração conseguir enxergar essa classe.</summary>
public partial class Program;
