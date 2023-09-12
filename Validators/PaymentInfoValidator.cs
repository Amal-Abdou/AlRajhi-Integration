using System;
using FluentValidation;
using Nop.Plugin.Payments.AlRajhi.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Payments.AlRajhi.Validators
{
    public partial class PaymentInfoValidator : BaseNopValidator<PaymentInfoModel>
    {
        public PaymentInfoValidator(ILocalizationService localizationService)
        {

            RuleFor(x => x.CreditCardType).NotEmpty().WithMessage(localizationService.GetResource("Payment.CreditCardType.Required"));

        }
    }
}