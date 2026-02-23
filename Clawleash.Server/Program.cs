using Clawleash.Server.Hubs;
using Clawleash.Server.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
    });

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevelopmentPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:4173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });

    options.AddPolicy("ProductionPolicy", policy =>
    {
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add E2EE services
builder.Services.AddSingleton<KeyManager>();
builder.Services.AddSingleton<E2eeMiddleware>();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentPolicy");
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseCors("ProductionPolicy");
    app.UseExceptionHandler("/Error");
}

// Static files (Svelte client)
app.UseDefaultFiles();
app.UseStaticFiles();

// API routes
app.MapControllers();

// SignalR hubs
app.MapHub<ChatHub>("/chat");
app.MapHub<SignalingHub>("/signaling");

// Fallback to SPA
app.MapFallbackToFile("index.html");

app.Run();
