using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.AlRajhi.Services;
using Nop.Web.Framework.Infrastructure.Extensions;
using System;

namespace Nop.Plugin.Payments.AlRajhi.Infrastructure
{
    public class NopStartup : INopStartup
    {
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient<AlRajhiHttpClient>().WithProxy();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        }

        public void Configure(IApplicationBuilder application)
        {
            application.UseStaticFiles();
        }

        public int Order => 101;
    }
}