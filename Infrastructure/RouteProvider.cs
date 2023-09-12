using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.AlRajhi.Infrastructure
{
    public partial class RouteProvider : IRouteProvider
    {
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {

            //IPN
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.AlRajhi.IPNHandler", "Plugins/PaymentAlRajhi/IPNHandler",
                 new { controller = "PaymentAlRajhi", action = "IPNHandler" });

            //Cancel
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.AlRajhi.CancelOrder", "Plugins/PaymentAlRajhi/CancelOrder",
                 new { controller = "PaymentAlRajhi", action = "CancelOrder" });

        }

        public int Priority => -1;
    }
}