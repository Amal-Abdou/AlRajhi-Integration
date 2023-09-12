using Nop.Core.Domain.Payments;

namespace Nop.Plugin.Payments.AlRajhi
{
    public class AlRajhiHelper
    {
        #region Properties

        public static string NopCommercePartnerCode => "nopCommerce_SP";

        public static string OrderTotalSentToUInterface => "OrderTotalSentToUInterface";

        #endregion

        #region Methods

        public static PaymentStatus GetPaymentStatus(string paymentStatus, string pendingReason)
        {
            var result = PaymentStatus.Pending;

            if (paymentStatus == null)
                paymentStatus = string.Empty;

            if (pendingReason == null)
                pendingReason = string.Empty;

            switch (paymentStatus.ToLowerInvariant())
            {
                case "pending":
                    result = (pendingReason.ToLowerInvariant()) switch
                    {
                        "authorization" => PaymentStatus.Authorized,
                        _ => PaymentStatus.Pending,
                    };
                    break;
                case "processed":
                case "completed":
                case "canceled_reversal":
                    result = PaymentStatus.Paid;
                    break;
                case "denied":
                case "expired":
                case "failed":
                case "voided":
                    result = PaymentStatus.Voided;
                    break;
                case "refunded":
                case "reversed":
                    result = PaymentStatus.Refunded;
                    break;
                default:
                    break;
            }

            return result;
        }

        #endregion
    }
}