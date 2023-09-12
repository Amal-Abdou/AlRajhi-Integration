using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.AlRajhi.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AlRajhi.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }
        public bool UseSandbox_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AlRajhi.Fields.TranportalId")]
        public string TranportalId { get; set; }
        public bool TranportalId_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AlRajhi.Fields.TranportalPassword")]
        public string TranportalPassword { get; set; }
        public bool TranportalPassword_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.AlRajhi.Fields.TerminalResourcekey")]
        public string TerminalResourcekey { get; set; }
        public bool TerminalResourcekey_OverrideForStore { get; set; }

    }
}