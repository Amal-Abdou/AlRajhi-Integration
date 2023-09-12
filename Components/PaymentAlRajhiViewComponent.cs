using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Plugin.Payments.AlRajhi.Models;
using Nop.Web.Framework.Components;
using System.Collections.Generic;

namespace Nop.Plugin.Payments.AlRajhi.Components
{
    [ViewComponent(Name = "PaymentAlRajhi")]
    public class PaymentAlRajhiViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var model = new PaymentInfoModel()
            {
                CreditCardTypes = new List<SelectListItem>
                {
                    new SelectListItem { Text = "Visa", Value = "visa" },
                    new SelectListItem { Text = "Credit Card", Value = "cc" },
                }
            };

            return View("~/Plugins/Croxees.Payments.AlRajhiBank/Views/PaymentInfo.cshtml", model);
        }
    }
}
