using ClinicaVeterinaria.Data;
using ClinicaVeterinaria.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
// Os três modos abaixo são flags sem valor; retire-as do parser de configuração.
var modos = new[] { "--importar-sql", "--verificar-banco", "--exportar-sql" };
var builder = WebApplication.CreateBuilder(args.Where(a => !modos.Contains(a)).ToArray());
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<CriptografiaService>();
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key não configurada.");
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
             {
                   context.Token = context.Request.Cookies["jwt"];
                   return Task.CompletedTask;
               },
            OnChallenge = context =>
             {
                   context.HandleResponse();
                   context.Response.Redirect("/Account/Login");
                   return Task.CompletedTask;
               }
        };
    });
builder.Services.AddAuthorization();
var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(
name: "default",
     pattern: "{controller=Home}/{action=Index}/{id?}");
if (args.Contains("--importar-sql"))
{
    await SqlEntrega.ImportarAsync(app.Services, builder.Configuration["arquivo"] ?? "../../saep_db.sql");
    return;
}
if (args.Contains("--verificar-banco"))
{
    await SqlEntrega.VerificarAsync(app.Services);
    return;
}
await DbInitializer.SeedAsync(app.Services);
if (args.Contains("--exportar-sql"))
{
    await SqlEntrega.ExportarAsync(app.Services,
        builder.Configuration["arquivo"] ?? "../../saep_db.sql",
        builder.Configuration["banco"] ?? "ClinicaVetDb");
    return;
}
app.Run();