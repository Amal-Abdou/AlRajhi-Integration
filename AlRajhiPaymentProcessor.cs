using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Payments.AlRajhi.Models;
using Nop.Plugin.Payments.AlRajhi.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Tax;
using Nop.Plugin.Payments.AlRajhi.Validators;
using Nop.Services.Security;
using Org.BouncyCastle.Crypto.Generators;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Xml;
using Newtonsoft.Json;

namespace Nop.Plugin.Payments.AlRajhi
{
    public class AlRajhiPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly IAddressService _addressService;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private static IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderService _orderService;
        private readonly IPaymentService _paymentService;
        private readonly IProductService _productService;
        private readonly ISettingService _settingService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly AlRajhiHttpClient _alRajhiHttpClient;
        private readonly AlRajhiPaymentSettings _alRajhiPaymentSettings;
        private readonly IEncryptionService _encryptionService;

        #endregion

        #region Ctor

        public AlRajhiPaymentProcessor(CurrencySettings currencySettings,
            IAddressService addressService,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICountryService countryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IOrderService orderService,
            IPaymentService paymentService,
            IProductService productService,
            ISettingService settingService,
            IStateProvinceService stateProvinceService,
            ITaxService taxService,
            IWebHelper webHelper,
            AlRajhiHttpClient alRajhiHttpClient,
            AlRajhiPaymentSettings alRajhiPaymentSettings,
            IEncryptionService encryptionService)
        {
            _currencySettings = currencySettings;
            _addressService = addressService;
            _checkoutAttributeParser = checkoutAttributeParser;
            _countryService = countryService;
            _currencyService = currencyService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _orderService = orderService;
            _paymentService = paymentService;
            _productService = productService;
            _settingService = settingService;
            _stateProvinceService = stateProvinceService;
            _taxService = taxService;
            _webHelper = webHelper;
            _alRajhiHttpClient = alRajhiHttpClient;
            _alRajhiPaymentSettings = alRajhiPaymentSettings;
            _encryptionService = encryptionService;
        }

        #endregion

        #region Utilities
        public bool GetPdtDetails(string tx, out Dictionary<string, string> values, out string response)
        {
            response = WebUtility.UrlDecode(_alRajhiHttpClient.GetPdtDetailsAsync(tx).Result);

            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool firstLine = true, success = false;
            foreach (var l in response.Split('\n'))
            {
                var line = l.Trim();
                if (firstLine)
                {
                    success = line.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
                    firstLine = false;
                }
                else
                {
                    var equalPox = line.IndexOf('=');
                    if (equalPox >= 0)
                        values.Add(line.Substring(0, equalPox), line.Substring(equalPox + 1));
                }
            }

            return success;
        }

        public bool VerifyIpn(string formString, out Dictionary<string, string> values)
        {
            var response = WebUtility.UrlDecode(_alRajhiHttpClient.VerifyIpnAsync(formString).Result);
            var success = response.Trim().Equals("VERIFIED", StringComparison.OrdinalIgnoreCase);

            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in formString.Split('&'))
            {
                var line = l.Trim();
                var equalPox = line.IndexOf('=');
                if (equalPox >= 0)
                    values.Add(line.Substring(0, equalPox), line.Substring(equalPox + 1));
            }

            return success;
        }

        private IDictionary<string, string> CreateQueryParameters(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var storeLocation = _webHelper.GetStoreLocation();

            var orderAddress = _addressService.GetAddressById(
                (postProcessPaymentRequest.Order.PickupInStore ? postProcessPaymentRequest.Order.PickupAddressId : postProcessPaymentRequest.Order.ShippingAddressId) ?? 0);

            return new Dictionary<string, string>
            {

                ["charset"] = "utf-8",

                ["rm"] = "2",

                ["bn"] = AlRajhiHelper.NopCommercePartnerCode,
                ["currency_code"] = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode,

                ["invoice"] = postProcessPaymentRequest.Order.CustomOrderNumber,
                ["custom"] = postProcessPaymentRequest.Order.OrderGuid.ToString(),

                ["return"] = $"{storeLocation}Plugins/PaymentAlRajhi/PDTHandler",
                ["notify_url"] = $"{storeLocation}Plugins/PaymentAlRajhi/IPNHandler",
                ["cancel_return"] = $"{storeLocation}Plugins/PaymentAlRajhi/CancelOrder",

                ["no_shipping"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "1" : "2",
                ["address_override"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "0" : "1",
                ["first_name"] = orderAddress?.FirstName,
                ["last_name"] = orderAddress?.LastName,
                ["address1"] = orderAddress?.Address1,
                ["address2"] = orderAddress?.Address2,
                ["city"] = orderAddress?.City,
                ["state"] = _stateProvinceService.GetStateProvinceByAddress(orderAddress)?.Abbreviation,
                ["country"] = _countryService.GetCountryByAddress(orderAddress)?.TwoLetterIsoCode,
                ["zip"] = orderAddress?.ZipPostalCode,
                ["email"] = orderAddress?.Email
            };
        }

        private void AddItemsParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            parameters.Add("cmd", "_cart");
            parameters.Add("upload", "1");

            var cartTotal = decimal.Zero;
            var roundedCartTotal = decimal.Zero;
            var itemCount = 1;

            foreach (var item in _orderService.GetOrderItems(postProcessPaymentRequest.Order.Id))
            {
                var roundedItemPrice = Math.Round(item.UnitPriceExclTax, 2);

                var product = _productService.GetProductById(item.ProductId);

                parameters.Add($"item_name_{itemCount}", product.Name);
                parameters.Add($"amount_{itemCount}", roundedItemPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", item.Quantity.ToString());

                cartTotal += item.PriceExclTax;
                roundedCartTotal += roundedItemPrice * item.Quantity;
                itemCount++;
            }

            var checkoutAttributeValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(postProcessPaymentRequest.Order.CheckoutAttributesXml);
            var customer = _customerService.GetCustomerById(postProcessPaymentRequest.Order.CustomerId);

            foreach (var (attribute, values) in checkoutAttributeValues)
            {
                foreach (var attributeValue in values)
                {
                    var attributePrice = _taxService.GetCheckoutAttributePrice(attribute, attributeValue, false, customer);
                    var roundedAttributePrice = Math.Round(attributePrice, 2);

                    if (attribute == null)
                        continue;

                    parameters.Add($"item_name_{itemCount}", attribute.Name);
                    parameters.Add($"amount_{itemCount}", roundedAttributePrice.ToString("0.00", CultureInfo.InvariantCulture));
                    parameters.Add($"quantity_{itemCount}", "1");

                    cartTotal += attributePrice;
                    roundedCartTotal += roundedAttributePrice;
                    itemCount++;
                }
            }

            var roundedShippingPrice = Math.Round(postProcessPaymentRequest.Order.OrderShippingExclTax, 2);
            if (roundedShippingPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Shipping fee");
                parameters.Add($"amount_{itemCount}", roundedShippingPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderShippingExclTax;
                roundedCartTotal += roundedShippingPrice;
                itemCount++;
            }

            var roundedPaymentMethodPrice = Math.Round(postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax, 2);
            if (roundedPaymentMethodPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Payment method fee");
                parameters.Add($"amount_{itemCount}", roundedPaymentMethodPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax;
                roundedCartTotal += roundedPaymentMethodPrice;
                itemCount++;
            }

            var roundedTaxAmount = Math.Round(postProcessPaymentRequest.Order.OrderTax, 2);
            if (roundedTaxAmount > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Tax amount");
                parameters.Add($"amount_{itemCount}", roundedTaxAmount.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderTax;
                roundedCartTotal += roundedTaxAmount;
            }

            if (cartTotal > postProcessPaymentRequest.Order.OrderTotal)
            {
                var discountTotal = Math.Round(cartTotal - postProcessPaymentRequest.Order.OrderTotal, 2);
                roundedCartTotal -= discountTotal;

                parameters.Add("discount_amount_cart", discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
            }

            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, AlRajhiHelper.OrderTotalSentToUInterface, roundedCartTotal);
        }

        private void AddOrderTotalParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var roundedOrderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {postProcessPaymentRequest.Order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, AlRajhiHelper.OrderTotalSentToUInterface, roundedOrderTotal);
        }

        #endregion

        #region Methods

        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult
            {
                AllowStoringCreditCardNumber = true
            };
        }

        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {

            var webHelper = Nop.Core.Infrastructure.EngineContext.Current.Resolve<Nop.Core.IWebHelper>();
            var storeUrl = webHelper.GetStoreLocation();
            var baseUrl = "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm";

            HttpClient client = new HttpClient();

            var trandata = new[] {                                
                new 
                { 
                    amt=postProcessPaymentRequest.Order.OrderTotal.ToString(),
                    action="1",
                    password=_alRajhiPaymentSettings.TranportalPassword,
                    id=_alRajhiPaymentSettings.TranportalId,
                    currencyCode="682",
                    trackId=postProcessPaymentRequest.Order.Id.ToString(),
                    responseURL= storeUrl + "Plugins/Callback/Handler",
                    errorURL=storeUrl + "Plugins/Callback/Handler",
                    langid = "ar"
               }};
            var str = JsonConvert.SerializeObject(trandata);
            var Entrandata = "";

            using (AesManaged myAes = new AesManaged())
            {
                var Encrypt= EncryptStringToBytes_Aes(str,
               Encoding.ASCII.GetBytes(_alRajhiPaymentSettings.TerminalResourcekey),
               Encoding.ASCII.GetBytes("PGKEYENCDECIVSPC"));
                Entrandata = BitConverter.ToString(Encrypt).Replace("-", string.Empty);
            }

            var values = new[] {
                new 
                {
                    id=_alRajhiPaymentSettings.TranportalId,
                    trandata=Entrandata, 
                    responseURL= storeUrl + "Plugins/Callback/Handler",
                    errorURL=storeUrl + "Plugins/Callback/Handler",

                }};

            var str1 = JsonConvert.SerializeObject(values);
            var content = new StringContent(str1, Encoding.UTF8, "application/json");

            var response = client.PostAsync(baseUrl, content).Result;

            var responseString = response.Content.ReadAsStringAsync().Result;
            responseString = responseString.Replace("[", string.Empty);
            responseString = responseString.Replace("]", string.Empty);
            if (!string.IsNullOrEmpty(responseString) && JObject.Parse(responseString).GetValue("status") != null && JObject.Parse(responseString).GetValue("status").ToString() == "1")
            {
                var payURL = JObject.Parse(responseString).GetValue("result").ToString();
                string[] splitUrl = payURL.Split(':');
                var Url = "https://securepayments.alrajhibank.com.sa/pg/paymentpage.htm?PaymentID=" + splitUrl[0];
                _httpContextAccessor.HttpContext.Response.Redirect(Url);
            }

        }

        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            return false;
        }

        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return decimal.Zero;
        }

        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return false;

            return true;
        }

        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            var warnings = new List<string>();

            //validate
            var validator = new PaymentInfoValidator(_localizationService);
            var model = new PaymentInfoModel
            {
                CreditCardType = form["CreditCardType"],
            };
            var validationResult = validator.Validate(model);
            if (!validationResult.IsValid)
                warnings.AddRange(validationResult.Errors.Select(error => error.ErrorMessage));

            return warnings;
        }

        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest
            {
                CreditCardType = form["CreditCardType"],
            };
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentAlRajhi/Configure";
        }

        public string GetPublicViewComponentName()
        {
            return "PaymentAlRajhi";
        }


        public override void Install()
        {
            _settingService.SaveSetting(new AlRajhiPaymentSettings
            {
                UseSandbox = true
            });

            _localizationService.AddPluginLocaleResource(new Dictionary<string, string>
            {
                ["Plugins.Payments.AlRajhi.Fields.TranportalId"] = "Tranportal Id",
                ["Plugins.Payments.AlRajhi.Fields.TranportalId.Hint"] = "Enter Tranportal Id.",
                ["Plugins.Payments.AlRajhi.Fields.TranportalPassword"] = "Tranportal Password",
                ["Plugins.Payments.AlRajhi.Fields.TranportalPassword.Hint"] = "Enter Tranportal Password.",
                ["Plugins.Payments.AlRajhi.Fields.TerminalResourcekey"] = "Terminal Resource key",
                ["Plugins.Payments.AlRajhi.Fields.TerminalResourcekey.Hint"] = "Enter Terminal Resource key.",
                ["Plugins.Payments.AlRajhi.Fields.RedirectionTip"] = "You will be redirected to UInterface site to complete the order.",
                ["Plugins.Payments.AlRajhi.Fields.UseSandbox"] = "Use Sandbox",
                ["Plugins.Payments.AlRajhi.Fields.UseSandbox.Hint"] = "Check to enable Sandbox (testing environment).",
            });

            base.Install();
        }

        public override void Uninstall()
        {
            _settingService.DeleteSetting<AlRajhiPaymentSettings>();

            _localizationService.DeletePluginLocaleResources("Plugins.Payments.AlRajhi");

            base.Uninstall();
        }

        #endregion

        #region Properties

        public bool SupportCapture => false;

        public bool SupportPartiallyRefund => false;

        public bool SupportRefund => false;

        public bool SupportVoid => false;

        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        public bool SkipPaymentInfo => true;

        public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.AlRajhi.PaymentMethodDescription");

        #endregion


        public static byte[] EncryptStringToBytes_Aes(string plainText, byte[] Key, byte[] IV)
        {
            if (plainText == null || plainText.Length <= 0)
                throw new ArgumentNullException("plainText");
            if (Key == null || Key.Length <= 0)
                throw new ArgumentNullException("Key");
            if (IV == null || IV.Length <= 0)
                throw new ArgumentNullException("IV");
            byte[] encrypted;
            using (AesManaged aesAlg = new AesManaged())
            {
                aesAlg.Key = Key;
                aesAlg.IV = IV;
                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key,
               aesAlg.IV);
                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt,
                   encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }
                        encrypted = msEncrypt.ToArray();
                    }
                }
            }
            return encrypted;
        }

        public string PostProcessPaymentApi(PostProcessPaymentRequest postProcessPaymentRequest)
        {

            var webHelper = Nop.Core.Infrastructure.EngineContext.Current.Resolve<Nop.Core.IWebHelper>();
            var storeUrl = webHelper.GetStoreLocation();
            var baseUrl = "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm";

            HttpClient client = new HttpClient();

            var trandata = new[] {
                new
                {
                    amt=postProcessPaymentRequest.Order.OrderTotal.ToString(),
                    action="1",
                    password=_alRajhiPaymentSettings.TranportalPassword,
                    id=_alRajhiPaymentSettings.TranportalId,
                    currencyCode="682",
                    trackId=postProcessPaymentRequest.Order.Id.ToString(),
                    responseURL= storeUrl + "Plugins/Callback/HandlerApi",
                    errorURL=storeUrl + "Plugins/Callback/HandlerApi",
                    langid = "ar"
               }};
            var str = JsonConvert.SerializeObject(trandata);
            var Entrandata = "";

            using (AesManaged myAes = new AesManaged())
            {
                var Encrypt = EncryptStringToBytes_Aes(str,
               Encoding.ASCII.GetBytes(_alRajhiPaymentSettings.TerminalResourcekey),
               Encoding.ASCII.GetBytes("PGKEYENCDECIVSPC"));
                Entrandata = BitConverter.ToString(Encrypt).Replace("-", string.Empty);
            }

            var values = new[] {
                new
                {
                    id=_alRajhiPaymentSettings.TranportalId,
                    trandata=Entrandata,
                    responseURL= storeUrl + "Plugins/Callback/HandlerApi",
                    errorURL=storeUrl + "Plugins/Callback/HandlerApi",

                }};

            var str1 = JsonConvert.SerializeObject(values);
            var content = new StringContent(str1, Encoding.UTF8, "application/json");

            var response = client.PostAsync(baseUrl, content).Result;

            var responseString = response.Content.ReadAsStringAsync().Result;
            responseString = responseString.Replace("[", string.Empty);
            responseString = responseString.Replace("]", string.Empty);
            if (!string.IsNullOrEmpty(responseString) && JObject.Parse(responseString).GetValue("status") != null && JObject.Parse(responseString).GetValue("status").ToString() == "1")
            {
                var payURL = JObject.Parse(responseString).GetValue("result").ToString();
                string[] splitUrl = payURL.Split(':');
                var Url = "https://securepayments.alrajhibank.com.sa/pg/paymentpage.htm?PaymentID=" + splitUrl[0];
                return Url;
            }

            return "";
        }

        public void PostSubscribePackagePayment(PostSubscribePackagePaymentRequest postSubscribePackagePaymentRequest)
        {
            var webHelper = Nop.Core.Infrastructure.EngineContext.Current.Resolve<Nop.Core.IWebHelper>();
            var storeUrl = webHelper.GetStoreLocation();
            var baseUrl = "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm";

            HttpClient client = new HttpClient();

            var trandata = new[] {
                new
                {
                    amt=postSubscribePackagePaymentRequest.Total,
                    action="1",
                    password=_alRajhiPaymentSettings.TranportalPassword,
                    id=_alRajhiPaymentSettings.TranportalId,
                    currencyCode="682",
                    trackId=postSubscribePackagePaymentRequest.SubscriptionPackageOrderId,
                    responseURL= storeUrl + "Plugins/Callback/HandlerPackage",
                    errorURL=storeUrl + "Plugins/Callback/HandlerPackage",
                    langid = "ar"
               }};
            var str = JsonConvert.SerializeObject(trandata);
            var Entrandata = "";

            using (AesManaged myAes = new AesManaged())
            {
                var Encrypt = EncryptStringToBytes_Aes(str,
               Encoding.ASCII.GetBytes(_alRajhiPaymentSettings.TerminalResourcekey),
               Encoding.ASCII.GetBytes("PGKEYENCDECIVSPC"));
                Entrandata = BitConverter.ToString(Encrypt).Replace("-", string.Empty);
            }

            var values = new[] {
                new
                {
                    id=_alRajhiPaymentSettings.TranportalId,
                    trandata=Entrandata,
                    responseURL= storeUrl + "Plugins/Callback/HandlerPackage",
                    errorURL=storeUrl + "Plugins/Callback/HandlerPackage",

                }};

            var str1 = JsonConvert.SerializeObject(values);
            var content = new StringContent(str1, Encoding.UTF8, "application/json");

            var response = client.PostAsync(baseUrl, content).Result;

            var responseString = response.Content.ReadAsStringAsync().Result;
            responseString = responseString.Replace("[", string.Empty);
            responseString = responseString.Replace("]", string.Empty);
            if (!string.IsNullOrEmpty(responseString) && JObject.Parse(responseString).GetValue("status") != null && JObject.Parse(responseString).GetValue("status").ToString() == "1")
            {
                var payURL = JObject.Parse(responseString).GetValue("result").ToString();
                string[] splitUrl = payURL.Split(':');
                var Url = "https://securepayments.alrajhibank.com.sa/pg/paymentpage.htm?PaymentID=" + splitUrl[0];
                _httpContextAccessor.HttpContext.Response.Redirect(Url);
            }
        }
    }
}