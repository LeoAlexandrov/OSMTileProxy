using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using AleProjects.TileProxy;



void ConfigureServices(IServiceCollection services, ConfigurationManager configuration)
{
	services
		.AddSingleton<TileManager>()
		.AddHttpClient()
		.AddCors(options => options.AddPolicy("All", policyBuilder => policyBuilder.AllowAnyOrigin()))
		.AddControllers();
}

void ConfigureApp(WebApplication app)
{

	if (app.Environment.IsDevelopment())
	{
		app.UseDeveloperExceptionPage();
	}

	app.UseForwardedHeaders()
		.UseStaticFiles()
		.UseRouting()
		.UseCors("All");

	app.MapControllers();
}


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

// Configure application.
ConfigureApp(app);

app.Run();

