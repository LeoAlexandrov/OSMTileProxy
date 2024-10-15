using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OSMTileProxy.Services;


void ConfigureServices(IServiceCollection services, ConfigurationManager config)
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

ConfigureServices(builder.Services, builder.Configuration);

var app = builder.Build();

ConfigureApp(app);
app.Run();