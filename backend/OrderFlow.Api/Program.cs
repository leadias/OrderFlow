using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Api.Data;
using OrderFlow.Api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrdersDb")));
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<OrderDbContext>()
    .AddSignInManager();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization(options =>
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin")));
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "OrderFlow.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    if (dbContext.Database.IsNpgsql())
    {
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        await dbContext.Database.EnsureCreatedAsync();
    }

    await SeedAdministratorAsync(scope.ServiceProvider, app.Configuration);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (OrderDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "healthy" })
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable"));

var auth = app.MapGroup("/api/auth");

auth.MapPost("/login", async (LoginRequest request, SignInManager<IdentityUser> signInManager) =>
{
    var result = await signInManager.PasswordSignInAsync(
        request.Email,
        request.Password,
        isPersistent: true,
        lockoutOnFailure: true);

    return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
})
.AllowAnonymous()
.WithName("Login");

auth.MapGet("/me", (HttpContext context) =>
    Results.Ok(new { email = context.User.Identity?.Name }))
    .RequireAuthorization()
    .WithName("GetCurrentUser");

auth.MapPost("/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.NoContent();
})
.RequireAuthorization()
.WithName("Logout");

var orders = app.MapGroup("/api/orders").RequireAuthorization("AdminOnly");

orders.MapPost("", async (CreateOrderRequest request, OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var errors = Validate(request);
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var order = new OrderEntity
    {
        Id = Guid.NewGuid(),
        CustomerId = request.CustomerId!.Trim(),
        Status = OrderStatus.Pending,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        Items = request.Items!.Select((item, index) => new OrderItemEntity
        {
            LineNumber = index + 1,
            ProductId = item.ProductId!.Trim(),
            Quantity = item.Quantity
        }).ToList()
    };

    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(cancellationToken);

    var response = ToResponse(order);
    return Results.Created($"/api/orders/{order.Id}", response);
})
.WithName("CreateOrder");

orders.MapGet("", async (OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var orders = await dbContext.Orders
        .AsNoTracking()
        .Include(order => order.Items)
        .OrderByDescending(order => order.CreatedAtUtc)
        .ToListAsync(cancellationToken);

    return Results.Ok(orders.Select(ToResponse));
})
    .WithName("GetOrders");

orders.MapGet("/{id:guid}", async (Guid id, OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var order = await dbContext.Orders
        .AsNoTracking()
        .Include(candidate => candidate.Items)
        .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

    return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
})
.WithName("GetOrder");

var inventory = app.MapGroup("/api/inventory").RequireAuthorization("AdminOnly");

inventory.MapGet("", async (OrderDbContext dbContext, CancellationToken cancellationToken) =>
{
    var items = await dbContext.InventoryItems
        .AsNoTracking()
        .OrderBy(item => item.ProductId)
        .Select(item => new InventoryItemResponse(item.ProductId, item.QuantityAvailable))
        .ToListAsync(cancellationToken);

    return Results.Ok(items);
})
.WithName("GetInventory");

inventory.MapPut("/{productId}", async (
    string productId,
    SetInventoryRequest request,
    OrderDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var normalizedProductId = productId.Trim();
    if (string.IsNullOrWhiteSpace(normalizedProductId) || normalizedProductId.Length > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["productId"] = ["ProductId must contain between 1 and 100 characters."]
        });
    }

    if (request.QuantityAvailable < 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["quantityAvailable"] = ["QuantityAvailable cannot be negative."]
        });
    }

    var item = await dbContext.InventoryItems.FindAsync([normalizedProductId], cancellationToken);
    if (item is null)
    {
        item = new InventoryItemEntity { ProductId = normalizedProductId };
        dbContext.InventoryItems.Add(item);
    }

    item.QuantityAvailable = request.QuantityAvailable;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new InventoryItemResponse(item.ProductId, item.QuantityAvailable));
})
.WithName("SetInventory");

app.Run();

static async Task SeedAdministratorAsync(IServiceProvider services, IConfiguration configuration)
{
    var email = configuration["AdminAccount:Email"];
    var password = configuration["AdminAccount:Password"];
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "AdminAccount:Email and AdminAccount:Password must be configured outside source control.");
    }

    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        var roleResult = await roleManager.CreateAsync(new IdentityRole("Admin"));
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }
    }

    var administrator = await userManager.FindByEmailAsync(email);
    if (administrator is null)
    {
        administrator = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var userResult = await userManager.CreateAsync(administrator, password);
        if (!userResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", userResult.Errors.Select(error => error.Description)));
        }
    }

    if (!await userManager.IsInRoleAsync(administrator, "Admin"))
    {
        var roleResult = await userManager.AddToRoleAsync(administrator, "Admin");
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
        }
    }
}

static Dictionary<string, string[]> Validate(CreateOrderRequest request)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.CustomerId))
    {
        errors["customerId"] = ["CustomerId is required."];
    }

    if (request.Items is null || request.Items.Count == 0)
    {
        errors["items"] = ["At least one order item is required."];
        return errors;
    }

    for (var index = 0; index < request.Items.Count; index++)
    {
        var item = request.Items[index];
        if (string.IsNullOrWhiteSpace(item.ProductId))
        {
            errors[$"items[{index}].productId"] = ["ProductId is required."];
        }

        if (item.Quantity <= 0)
        {
            errors[$"items[{index}].quantity"] = ["Quantity must be greater than zero."];
        }
    }

    return errors;
}

static OrderResponse ToResponse(OrderEntity order) =>
    new(
        order.Id,
        order.CustomerId,
        order.Items.OrderBy(item => item.LineNumber)
            .Select(item => new OrderItemResponse(item.ProductId, item.Quantity))
            .ToArray(),
        order.Status,
        order.CreatedAtUtc);

public partial class Program;
