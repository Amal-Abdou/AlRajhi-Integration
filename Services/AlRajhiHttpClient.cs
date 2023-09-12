using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Net.Http.Headers;
using Nop.Core;

namespace Nop.Plugin.Payments.AlRajhi.Services
{

    public partial class AlRajhiHttpClient
    {
        #region Fields

        private readonly HttpClient _httpClient;
        private readonly AlRajhiPaymentSettings _alRajhiPaymentSettings;

        #endregion

        #region Ctor

        public AlRajhiHttpClient(HttpClient client,
            AlRajhiPaymentSettings alRajhiPaymentSettings)
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Add(HeaderNames.UserAgent, $"nopCommerce-{NopVersion.CurrentVersion}");

            _httpClient = client;
            _alRajhiPaymentSettings = alRajhiPaymentSettings;
        }

        #endregion

        #region Methods
        public async Task<string> GetPdtDetailsAsync(string tx)
        {
            var url = _alRajhiPaymentSettings.UseSandbox ?
                "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm" :
                "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm";
            var requestContent = new StringContent($"cmd=_notify-synch&at=&tx={tx}",
                Encoding.UTF8, MimeTypes.ApplicationXWwwFormUrlencoded);
            var response = await _httpClient.PostAsync(url, requestContent);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> VerifyIpnAsync(string formString)
        {
            var url = _alRajhiPaymentSettings.UseSandbox ?
                "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm" :
                "https://securepayments.alrajhibank.com.sa/pg/payment/hosted.htm";
            var requestContent = new StringContent($"cmd=_notify-validate&{formString}",
                Encoding.UTF8, MimeTypes.ApplicationXWwwFormUrlencoded);
            var response = await _httpClient.PostAsync(url, requestContent);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        #endregion
    }
}