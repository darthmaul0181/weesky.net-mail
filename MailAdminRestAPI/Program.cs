using weesky.MailAdminRestAPI.Authentication.Models;
using weesky.MailAdminRestAPI.Data;
using Microsoft.EntityFrameworkCore;
using weesky.MailAdminRestAPI.Authentication.Services;
using weesky.MailAdminRestAPI.Authentication.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.PlatformAbstractions;
using weesky.MailAdminRestAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("MailUserAccountsDatabase");

builder.Services.AddDbContext<ApplicationDbContext>(options => {
	options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
	.LogTo(Console.WriteLine, LogLevel.Information)
	.EnableSensitiveDataLogging()
	.EnableDetailedErrors();
});

builder.Services.AddOptions<TokenConstants>().Configure(options => builder.Configuration.GetSection("TokenConstants").Bind(options));
builder.Services.AddScoped<IRepository, Repository>();
builder.Services.AddScoped<IUserAuthenticator, UserAuthenticator>();
builder.Services.AddScoped<ITokenManager, TokenManager>();
builder.Services.AddJwtBearerAuthentication(true);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.IgnoreNullValues = true;
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Milkyway",
        Description = "weesky.net mail administration API",
        Contact = new OpenApiContact
        {
            Name = "administrator",
            Email = "root@weesky.net"
        }
    });

    var filePath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "ApiDocumentation.xml");
    options.IncludeXmlComments(filePath);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
