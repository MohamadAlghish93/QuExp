using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QueryExplorer.Application.Interfaces;
using QueryExplorer.Infrastructure.Providers;
using QueryExplorer.Infrastructure.Repositories;
using QueryExplorer.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

// --- DI wiring ---
// Register one IQueryProvider per supported database. To add a new database:
// implement IQueryProvider in Infrastructure/Providers and add one line here.
builder.Services.AddSingleton<QueryExplorer.Application.Interfaces.IQueryProvider, SqlServerQueryProvider>();
builder.Services.AddSingleton<QueryExplorer.Application.Interfaces.IQueryProvider, OracleQueryProvider>();
builder.Services.AddSingleton<IQueryProviderFactory, QueryProviderFactory>();

builder.Services.AddSingleton<IFavoriteQueryRepository>(_ => new SqliteFavoriteQueryRepository(dataDirectory));
builder.Services.AddSingleton<IConnectionRepository>(_ => new JsonConnectionRepository(dataDirectory));
builder.Services.AddSingleton<IUserRepository>(_ => new SqliteUserRepository(dataDirectory));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<ISecretProtector, AesSecretProtector>();
builder.Services.AddSingleton<IConnectionStringResolver, ConnectionStringResolver>();
builder.Services.AddSingleton<IConnectionHealthCache, ConnectionHealthCache>();

builder.Services.AddSingleton<IQueryHistoryRepository>(_ => new SqliteQueryHistoryRepository(dataDirectory));
builder.Services.AddSingleton<IDashboardRepository>(_ => new JsonDashboardRepository(dataDirectory));
builder.Services.AddSingleton<IAlertRepository>(_ => new JsonAlertRepository(dataDirectory));
builder.Services.AddSingleton<IAlertNotificationRepository>(_ => new SqliteAlertNotificationRepository(dataDirectory));
builder.Services.AddSingleton<IAuditLogRepository>(_ => new SqliteAuditLogRepository(dataDirectory));
builder.Services.AddSingleton<IErrorLogRepository>(_ => new SqliteErrorLogRepository(dataDirectory));
builder.Services.AddSingleton<IWorkflowRepository>(_ => new SqliteWorkflowRepository(dataDirectory));
builder.Services.AddSingleton<IPdfLayoutRepository>(_ => new JsonPdfLayoutRepository(dataDirectory));
builder.Services.AddSingleton<IPdfGenerator, QueryExplorer.Infrastructure.Services.PdfSharpCorePdfGenerator>();

// PdfSharpCore needs a real font file on Linux (no GDI fallback) - see SystemFontResolver's
// doc comment for what to install if this throws.
PdfSharpCore.Fonts.GlobalFontSettings.FontResolver = new QueryExplorer.Infrastructure.Services.SystemFontResolver();

builder.Services.AddSingleton<IAssignmentRepository>(_ => new JsonAssignmentRepository(dataDirectory));
builder.Services.AddSingleton<IPushSubscriptionRepository>(_ => new JsonPushSubscriptionRepository(dataDirectory));
builder.Services.AddSingleton<IPushNotificationSender, QueryExplorer.Infrastructure.Services.WebPushNotificationSender>();

builder.Services.AddHttpClient(); // used by AlertCheckerService for webhook delivery
builder.Services.AddHostedService<QueryExplorer.Infrastructure.Services.AlertCheckerService>();
builder.Services.AddHostedService<QueryExplorer.Infrastructure.Services.AssignmentReminderService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // The client sends enums as names ("SqlServer", "Oracle"), not numbers.
        // Without this converter model binding rejects them and the whole request
        // object comes back null.
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Lets you authorize in Swagger UI with a bearer token for manual testing.
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                      ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientApp", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Error-Logged")); // lets the SPA see which errors the server already logged
});

var jwtKey = builder.Configuration["Jwt:Key"]
             ?? throw new InvalidOperationException("Jwt:Key is not configured in appsettings.json.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Outermost app middleware: catches anything unhandled, logs it for admins, returns clean JSON.
app.UseMiddleware<QueryExplorer.Api.ExceptionLoggingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("ClientApp");

// Serves client/dist (copied into wwwroot at build time - see the BuildClient
// target in this project's .csproj) so the API and the SPA run as one process
// on one port. Not used while you run `npm run dev` separately on :5173.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html"); // lets React Router handle client-side routes on refresh

app.Run();
