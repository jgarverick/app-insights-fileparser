using AppInsightsFileParser.FileSystem;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AppInsightsFileParser
{
    public class Startup
    {

        public IConfiguration Configuration { get; private set; }

        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var fileFilter = Configuration.GetValue<string>("LogFileFilter") ?? "*";
            services.AddApplicationInsightsTelemetry();
            services.AddSingleton<BackgroundLogWatcher>(new BackgroundLogWatcher(fileFilter));

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            var svc = app.ApplicationServices.GetService<BackgroundLogWatcher>();
            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/", async context =>
                {

                    if (!svc.IsOperational)
                    {
                        await svc.StartAsync(System.Threading.CancellationToken.None);
                    }
                    await context.Response.WriteAsync("Site is live");
                });
                endpoints.MapGet("/health", async context =>
                {
                    await context.Response.WriteAsync("OK");
                });
                endpoints.MapGet("/ready", async context =>
                {
                    var logDirectory = Environment.GetEnvironmentVariable("LOG_PATH") ?? string.Empty;
                    var responseMessage = "OK";
                    var returnCode = 200;
                    if (logDirectory == string.Empty)
                    {
                        returnCode = 500;
                        responseMessage = "Path provided is invalid. Check configuration to ensure path provided is accessible.";

                    }
                    else
                    {
                        if (svc.IsOperational)
                        {
                            responseMessage = "Service is up and running.\r\n";
                            var fileCount = System.IO.Directory.GetFiles(logDirectory);
                            responseMessage += $"Found {fileCount.Count()} file(s) in directory {logDirectory}.\r\n";
                            foreach (var entry in svc.LogFileLineCounts)
                            {
                                responseMessage += $"{entry.Key} has been processed to line {entry.Value}.\r\n";
                            }
                        }
                        else
                        {
                            try
                            {
                                await svc.StartAsync(System.Threading.CancellationToken.None);
                                responseMessage = "Service is up and running.\r\nPlease refresh to retrieve file metrics.\r\n";
                                if (!svc.IsOperational) throw new Exception("Could not start the background service.");
                            }
                            catch (Exception ex)
                            {
                                returnCode = 500;
                                responseMessage = "Service is non-responsive or does not exist.";
                                var client = new TelemetryClient(new TelemetryConfiguration(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")));
                                client.TrackException(ex);

                            }

                        }

                    }

                    context.Response.StatusCode = returnCode;
                    await context.Response.WriteAsync(responseMessage);

                });
            });
        }
    }
}
