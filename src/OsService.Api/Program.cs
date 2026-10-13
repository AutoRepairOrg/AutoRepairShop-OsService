using Microsoft.EntityFrameworkCore;
using OsService.Application.Events;
using OsService.Application.Interfaces;
using OsService.Application.Services;
using OsService.Domain.Interfaces;
using OsService.Infrastructure.Messaging;
using OsService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ── Database (SQL Server / LocalDB) ───────────────────────────────────────────
builder.Services.AddDbContext<OsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("OsDatabase")));

// ── Mensageria: RabbitMQ em produção, Stub em desenvolvimento ─────────────────
var useStub = builder.Configuration.GetValue<bool>("Messaging:UseStub");

if (useStub)
{
    builder.Services.AddScoped<IEventPublisher, StubEventPublisher>();
}
else
{
    builder.Services.AddSingleton<RabbitMQ.Client.IConnection>(_ =>
    {
        var factory = new RabbitMQ.Client.ConnectionFactory
        {
            HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
            Port     = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = builder.Configuration["RabbitMQ:User"] ?? "guest",
            Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
        };
        return factory.CreateConnectionAsync().GetAwaiter().GetResult();
    });
    builder.Services.AddScoped<IEventPublisher, RabbitMqEventPublisher>();
    builder.Services.AddHostedService<RabbitMqConsumerService>();
}

// ── DI ────────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IServiceOrderRepository, ServiceOrderRepository>();
builder.Services.AddScoped<ServiceOrderAppService>();
builder.Services.AddScoped<PaymentConfirmedHandler>();
builder.Services.AddScoped<BudgetRejectedHandler>();

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "OsService API", Version = "v1",
        Description = "Microsserviço de Ordens de Serviço — 12SOAT Fase 4" });
});

// ── Health Check ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OsDbContext>("sql-server");

var app = builder.Build();

// ── Migrations automáticas ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OsDbContext>();
    await db.Database.MigrateAsync();
}

// Middleware temporário de diagnóstico — remover antes do deploy
app.Use(async (ctx, next) => {
    try { await next(); }
    catch (Exception ex) {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new {
            error   = ex.Message,
            inner   = ex.InnerException?.Message,
            type    = ex.GetType().Name,
            stack   = ex.StackTrace?.Split('\n').Take(6)
        });
    }
});

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
