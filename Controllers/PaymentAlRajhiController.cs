using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.AlRajhi.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.AlRajhi.Controllers
{
    [AutoValidateAntiforgeryToken]
    public class PaymentAlRajhiController : BasePaymentController 
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly INotificationService _notificationService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ShoppingCartSettings _shoppingCartSettings;

        #endregion

        #region Ctor

        public PaymentAlRajhiController(IGenericAttributeService genericAttributeService,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ILocalizationService localizationService,
            ILogger logger,
            INotificationService notificationService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            ShoppingCartSettings shoppingCartSettings)
        {
            _genericAttributeService = genericAttributeService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _localizationService = localizationService;
            _logger = logger;
            _notificationService = notificationService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _shoppingCartSettings = shoppingCartSettings;
        }

        #endregion

        #region Utilities

        protected virtual void ProcessRecurringPayment(string invoiceId, PaymentStatus newPaymentStatus, string transactionId, string ipnInfo)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(invoiceId);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = _orderService.GetOrderByGuid(orderNumberGuid);
            if (order == null)
            {
                _logger.Error("AlRajhi IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            var recurringPayments = _orderService.SearchRecurringPayments(initialOrderId: order.Id);

            foreach (var rp in recurringPayments)
            {
                switch (newPaymentStatus)
                {
                    case PaymentStatus.Authorized:
                    case PaymentStatus.Paid:
                        {
                            var recurringPaymentHistory = _orderService.GetRecurringPaymentHistory(rp);
                            if (!recurringPaymentHistory.Any())
                            {
                                _orderService.InsertRecurringPaymentHistory(new RecurringPaymentHistory
                                {
                                    RecurringPaymentId = rp.Id,
                                    OrderId = order.Id,
                                    CreatedOnUtc = DateTime.UtcNow
                                });
                            }
                            else
                            {
                                var processPaymentResult = new ProcessPaymentResult
                                {
                                    NewPaymentStatus = newPaymentStatus
                                };
                                if (newPaymentStatus == PaymentStatus.Authorized)
                                    processPaymentResult.AuthorizationTransactionId = transactionId;
                                else
                                    processPaymentResult.CaptureTransactionId = transactionId;

                                _orderProcessingService.ProcessNextRecurringPayment(rp,
                                    processPaymentResult);
                            }
                        }

                        break;
                    case PaymentStatus.Voided:
                        var failedPaymentResult = new ProcessPaymentResult
                        {
                            Errors = new[] { $"AlRajhi IPN. Recurring payment is {nameof(PaymentStatus.Voided).ToLower()} ." },
                            RecurringPaymentFailed = true
                        };
                        _orderProcessingService.ProcessNextRecurringPayment(rp, failedPaymentResult);
                        break;
                }
            }

            _logger.Information("AlRajhi IPN. Recurring info", new NopException(ipnInfo));
        }

        protected virtual void ProcessPayment(string orderNumber, string ipnInfo, PaymentStatus newPaymentStatus, decimal mcGross, string transactionId)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(orderNumber);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = _orderService.GetOrderByGuid(orderNumberGuid);

            if (order == null)
            {
                _logger.Error("AlRajhi IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            _orderService.InsertOrderNote(new OrderNote
            {
                OrderId = order.Id,
                Note = ipnInfo,
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            if ((newPaymentStatus == PaymentStatus.Authorized || newPaymentStatus == PaymentStatus.Paid) && !Math.Round(mcGross, 2).Equals(Math.Round(order.OrderTotal, 2)))
            {
                var errorStr = $"AlRajhi IPN. Returned order total {mcGross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                _logger.Error(errorStr);
                _orderService.InsertOrderNote(new OrderNote
                {
                    OrderId = order.Id,
                    Note = errorStr,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

                return;
            }

            switch (newPaymentStatus)
            {
                case PaymentStatus.Authorized:
                    if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                        _orderProcessingService.MarkAsAuthorized(order);
                    break;
                case PaymentStatus.Paid:
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.AuthorizationTransactionId = transactionId;
                        _orderService.UpdateOrder(order);

                        _orderProcessingService.MarkOrderAsPaid(order);
                    }

                    break;
                case PaymentStatus.Refunded:
                    var totalToRefund = Math.Abs(mcGross);
                    if (totalToRefund > 0 && Math.Round(totalToRefund, 2).Equals(Math.Round(order.OrderTotal, 2)))
                    {
                        if (_orderProcessingService.CanRefundOffline(order))
                            _orderProcessingService.RefundOffline(order);
                    }
                    else
                    {
                        if (_orderProcessingService.CanPartiallyRefundOffline(order, totalToRefund))
                            _orderProcessingService.PartiallyRefundOffline(order, totalToRefund);
                    }

                    break;
                case PaymentStatus.Voided:
                    if (_orderProcessingService.CanVoidOffline(order))
                        _orderProcessingService.VoidOffline(order);

                    break;
            }
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var alRajhiPaymentSettings = _settingService.LoadSetting<AlRajhiPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                UseSandbox = alRajhiPaymentSettings.UseSandbox,
                TranportalId = alRajhiPaymentSettings.TranportalId,
                TranportalPassword = alRajhiPaymentSettings.TranportalPassword,
                TerminalResourcekey = alRajhiPaymentSettings.TerminalResourcekey,
                ActiveStoreScopeConfiguration = storeScope
            };

            if (storeScope <= 0)
                return View("~/Plugins/Croxees.Payments.AlRajhiBank/Views/Configure.cshtml", model);

            model.UseSandbox_OverrideForStore = _settingService.SettingExists(alRajhiPaymentSettings, x => x.UseSandbox, storeScope);
            model.TranportalId_OverrideForStore = _settingService.SettingExists(alRajhiPaymentSettings, x => x.TranportalId, storeScope);
            model.TranportalPassword_OverrideForStore = _settingService.SettingExists(alRajhiPaymentSettings, x => x.TranportalPassword, storeScope);
            model.TerminalResourcekey_OverrideForStore = _settingService.SettingExists(alRajhiPaymentSettings, x => x.TerminalResourcekey, storeScope);
            return View("~/Plugins/Croxees.Payments.AlRajhiBank/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var alRajhiPaymentSettings = _settingService.LoadSetting<AlRajhiPaymentSettings>(storeScope);

            alRajhiPaymentSettings.UseSandbox = model.UseSandbox;
            alRajhiPaymentSettings.TranportalId = model.TranportalId;
            alRajhiPaymentSettings.TranportalPassword = model.TranportalPassword;
            alRajhiPaymentSettings.TerminalResourcekey = model.TerminalResourcekey;

            _settingService.SaveSettingOverridablePerStore(alRajhiPaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(alRajhiPaymentSettings, x => x.TranportalId, model.TranportalId_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(alRajhiPaymentSettings, x => x.TranportalPassword, model.TranportalPassword_OverrideForStore, storeScope, false);
            _settingService.SaveSettingOverridablePerStore(alRajhiPaymentSettings, x => x.TerminalResourcekey, model.TerminalResourcekey_OverrideForStore, storeScope, false);

            _settingService.ClearCache();

            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult RoundingWarning(bool passProductNamesAndTotals)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prices and total aren't rounded, so display warning
            if (passProductNamesAndTotals && !_shoppingCartSettings.RoundPricesDuringCalculation)
                return Json(new { Result = _localizationService.GetResource("Plugins.Payments.AlRajhi.RoundingWarning") });

            return Json(new { Result = string.Empty });
        }
        

    public IActionResult PDTHandler(string paymentId,string trandata,string error, string errorText)
        {
            //byte[] parameters;

            //using (var stream = new MemoryStream())
            //{
            //    Request.Body.CopyTo(stream);
            //    parameters = stream.ToArray();
            //}

            //var strRequest = Encoding.ASCII.GetString(parameters);

            //var order = _orderService.GetOrderById(1);
            //    if (order != null)
            //    {
            //        _orderProcessingService.MarkOrderAsPaid(order);
            //        return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            //    }

            return View("~/Plugins/Croxees.Payments.AlRajhiBank/Views/test.cshtml");
        }


        [HttpPost]
[Route("Plugins/PaymentAlRajhi/JsonStringBody")]
public IActionResult JsonStringBody()
{
            return Content("ghghh");
        }
        public IActionResult IPNHandler()
        {
            byte[] parameters;

            using (var stream = new MemoryStream())
            {
                Request.Body.CopyTo(stream);
                parameters = stream.ToArray();
            }

            var strRequest = Encoding.ASCII.GetString(parameters);

            if (!(_paymentPluginManager.LoadPluginBySystemName("Payments.AlRajhi") is AlRajhiPaymentProcessor processor) || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("AlRajhi module cannot be loaded");

            if (!processor.VerifyIpn(strRequest, out var values))
            {
                _logger.Error("AlRajhi IPN failed.", new NopException(strRequest));

                return Content(string.Empty);
            }

            var mcGross = decimal.Zero;

            try
            {
                mcGross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
            }
            catch
            {
                // ignored
            }

            values.TryGetValue("payment_status", out var paymentStatus);
            values.TryGetValue("pending_reason", out var pendingReason);
            values.TryGetValue("txn_id", out var txnId);
            values.TryGetValue("txn_type", out var txnType);
            values.TryGetValue("rp_invoice_id", out var rpInvoiceId);

            var sb = new StringBuilder();
            sb.AppendLine("AlRajhi IPN:");
            foreach (var kvp in values)
            {
                sb.AppendLine(kvp.Key + ": " + kvp.Value);
            }

            var newPaymentStatus = AlRajhiHelper.GetPaymentStatus(paymentStatus, pendingReason);
            sb.AppendLine("New payment status: " + newPaymentStatus);

            var ipnInfo = sb.ToString();

            switch (txnType)
            {
                case "recurring_payment":
                    ProcessRecurringPayment(rpInvoiceId, newPaymentStatus, txnId, ipnInfo);
                    break;
                case "recurring_payment_failed":
                    if (Guid.TryParse(rpInvoiceId, out var orderGuid))
                    {
                        var order = _orderService.GetOrderByGuid(orderGuid);
                        if (order != null)
                        {
                            var recurringPayment = _orderService.SearchRecurringPayments(initialOrderId: order.Id)
                                .FirstOrDefault();
                            //failed payment
                            if (recurringPayment != null)
                                _orderProcessingService.ProcessNextRecurringPayment(recurringPayment,
                                    new ProcessPaymentResult
                                    {
                                        Errors = new[] { txnType },
                                        RecurringPaymentFailed = true
                                    });
                        }
                    }

                    break;
                default:
                    values.TryGetValue("custom", out var orderNumber);
                    ProcessPayment(orderNumber, ipnInfo, newPaymentStatus, mcGross, txnId);

                    break;
            }

            return Content(string.Empty);
        }

        public IActionResult CancelOrder( string OrderID )
        {
            var order = _orderService.GetOrderById(int.Parse(OrderID));

            if (order != null)
            {
                _orderService.InsertOrderNote(new OrderNote
                {
                    OrderId = order.Id,
                    Note = "The order cancelled because payment failed.",
                    DisplayToCustomer = true,
                    CreatedOnUtc = DateTime.UtcNow
                });
                order.OrderStatusId = (int)OrderStatus.Cancelled;
                _orderService.UpdateOrder(order);
            }

            return RedirectToRoute("Homepage");
        }


        #endregion
    }
}