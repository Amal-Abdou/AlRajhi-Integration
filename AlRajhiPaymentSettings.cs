using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.AlRajhi
{
    public class AlRajhiPaymentSettings : ISettings
    {
        public bool UseSandbox { get; set; }

        public string TranportalId { get; set; }

        public string TranportalPassword { get; set; }

        public string TerminalResourcekey { get; set; }

    }
}
