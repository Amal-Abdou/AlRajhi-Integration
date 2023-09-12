using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Vendors;
using Nop.Core.Infrastructure;
using Nop.Plugin.Payments.AlRajhi;
using Nop.Services.Orders;
using Nop.Services.Vendors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Croxees.Nop.Plugins.Payments.AlRajhiBank.Controllers
{
    public class CallbackController : Controller
    {

        [HttpPost]
        [Route("Plugins/Callback/Handler")]
        public IActionResult Handler()
        {
            var orderService = EngineContext.Current.Resolve<IOrderService>();
            var orderProcessingService = EngineContext.Current.Resolve<IOrderProcessingService>();
            var alRajhiPaymentSettings = EngineContext.Current.Resolve<AlRajhiPaymentSettings>();

            var Entransdata = Request.Form["trandata"];
            var hexWithSeparator = FormatString(Entransdata, "-", 2);
            byte[] data = Array.ConvertAll<string, byte>(hexWithSeparator.Split('-'), s => Convert.ToByte(s, 16));

            var decrypttransdata = DecryptStringFromBytes_Aes(data, Encoding.ASCII.GetBytes(alRajhiPaymentSettings.TerminalResourcekey), Encoding.ASCII.GetBytes("PGKEYENCDECIVSPC"));


            using JsonDocument doc = JsonDocument.Parse(decrypttransdata);
            JsonElement root = doc.RootElement;
            var trackId = root[0].GetProperty("trackId").GetString();
            var result = root[0].GetProperty("result").GetString();

            var order = orderService.GetOrderById(int.Parse(trackId));
            if (order != null && result == "CAPTURED")
            {
                //orderProcessingService.MarkOrderAsPaid(order);
                order.PaymentStatusId = (int)PaymentStatus.Paid;
                order.OrderStatusId = (int)OrderStatus.Processing;
                orderService.UpdateOrder(order);
                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
            else if (order != null && result == "CANCELED")
            {
                order.OrderStatusId = (int)OrderStatus.Cancelled;
                orderService.UpdateOrder(order);
                orderService.InsertOrderNote(new OrderNote() { 
                    Note="Order Cancelled because the user cancelled the payment",
                    OrderId=order.Id,
                    CreatedOnUtc=DateTime.UtcNow
                });
            }

            return RedirectToAction("Index", "Home", new { area = string.Empty });
        }


        [HttpPost]
        [Route("Plugins/Callback/HandlerPackage")]
        public IActionResult HandlerPackage()
        {
            var vendorSubscribePackageService = EngineContext.Current.Resolve<IVendorSubscribePackageService>();
            var vendorService = EngineContext.Current.Resolve<IVendorService>();
            var alRajhiPaymentSettings = EngineContext.Current.Resolve<AlRajhiPaymentSettings>();

            var Entransdata = Request.Form["trandata"];
            var hexWithSeparator = FormatString(Entransdata, "-", 2);
            byte[] data = Array.ConvertAll<string, byte>(hexWithSeparator.Split('-'), s => Convert.ToByte(s, 16));

            var decrypttransdata = DecryptStringFromBytes_Aes(data, Encoding.ASCII.GetBytes(alRajhiPaymentSettings.TerminalResourcekey), Encoding.ASCII.GetBytes("PGKEYENCDECIVSPC"));


            using JsonDocument doc = JsonDocument.Parse(decrypttransdata);
            JsonElement root = doc.RootElement;
            var trackId = root[0].GetProperty("trackId").GetString();
            var result = root[0].GetProperty("result").GetString();

            var package = vendorSubscribePackageService.GetVendorSubscribePackageById(int.Parse(trackId.Remove(0, 3)));
            if (package != null && result == "CAPTURED")
            {
                var days = 0;
                package.StartOnUtc = DateTime.UtcNow;
                if(package.VendorSubscriptionPackageId == (int)VendorSubscriptionPackage.OneMonth)
                    package.EndOnUtc = DateTime.UtcNow.AddMonths(1);
                else if (package.VendorSubscriptionPackageId == (int)VendorSubscriptionPackage.SixMonths)
                    package.EndOnUtc = DateTime.UtcNow.AddMonths(6);
                else if (package.VendorSubscriptionPackageId == (int)VendorSubscriptionPackage.TwelveMonths)
                    package.EndOnUtc = DateTime.UtcNow.AddMonths(12);
                var packages = vendorSubscribePackageService.GetSubscriptionPackagesByVendorId(package.VendorId);
                if (packages != null && packages.Count > 1)
                {
                    var oldpackage = packages.SkipLast(1).Last();
                    days = (int)(oldpackage.EndOnUtc.Date - oldpackage.StartOnUtc.Date).TotalDays;
                    if (days > 0)
                        package.EndOnUtc=package.EndOnUtc.AddDays(days);
                }
                vendorSubscribePackageService.UpdateVendorSubscribePackage(package);

                var vendor = vendorService.GetVendorById(package.VendorId);
                vendor.VendorSubscriptionStatusId = (int)VendorSubscriptionStatus.Active;
                vendor.VendorSubscriptionPackageId = package.VendorSubscriptionPackageId;
                vendorService.UpdateVendor(vendor);
            }
            else if (package != null && result == "CANCELED")
            {
                var packages = vendorSubscribePackageService.GetSubscriptionPackagesByVendorId(package.VendorId);
                if (packages != null && packages.Count > 1)
                {
                    var days = 0;
                    var oldpackage = packages.SkipLast(1).Last();
                    days = (int)(oldpackage.EndOnUtc.Date - oldpackage.StartOnUtc.Date).TotalDays;
                    if (days > 0)
                    {
                        package.StartOnUtc = DateTime.UtcNow.Date;
                        package.EndOnUtc=package.EndOnUtc = DateTime.UtcNow.Date;
                        package.EndOnUtc.AddDays(days);
                    }
                    vendorSubscribePackageService.UpdateVendorSubscribePackage(package);

                    var vendor = vendorService.GetVendorById(package.VendorId);
                    vendor.VendorSubscriptionStatusId = (int)VendorSubscriptionStatus.Active;
                    vendor.VendorSubscriptionPackageId = package.VendorSubscriptionPackageId;
                    vendorService.UpdateVendor(vendor);
                }
                else
                {
                    var vendor = vendorService.GetVendorById(package.VendorId);
                    vendor.VendorSubscriptionStatusId = (int)VendorSubscriptionStatus.NotActive;
                    vendorService.UpdateVendor(vendor);
                }
            }

            return RedirectToAction("Index", "Home", new { area = string.Empty });
        }

        [HttpPost]
        [Route("Plugins/Callback/HandlerApi")]
        public IActionResult HandlerApi()
        {

            var orderService = EngineContext.Current.Resolve<IOrderService>();
            var orderProcessingService = EngineContext.Current.Resolve<IOrderProcessingService>();
            var alRajhiPaymentSettings = EngineContext.Current.Resolve<AlRajhiPaymentSettings>();

            var Entransdata = Request.Form["trandata"];
            var hexWithSeparator = FormatString(Entransdata, "-", 2);
            byte[] data = Array.ConvertAll<string, byte>(hexWithSeparator.Split('-'), s => Convert.ToByte(s, 16));

            var decrypttransdata = DecryptStringFromBytes_Aes(data, Encoding.ASCII.GetBytes(alRajhiPaymentSettings.TerminalResourcekey), Encoding.ASCII.GetBytes("PGKEYENCDECIVSPC"));


            using JsonDocument doc = JsonDocument.Parse(decrypttransdata);
            JsonElement root = doc.RootElement;
            var trackId = root[0].GetProperty("trackId").GetString();
            var result = root[0].GetProperty("result").GetString();

            var order = orderService.GetOrderById(int.Parse(trackId));
            if (order != null && result == "CAPTURED")
            {
                order.PaymentStatusId = (int)PaymentStatus.Paid;
                order.OrderStatusId = (int)OrderStatus.Processing;
                orderService.UpdateOrder(order);
                return Redirect("minisouq://?status=paid&orderId=" + order.Id);
            }
            else if (order != null && result == "CANCELED")
            {
                order.OrderStatusId = (int)OrderStatus.Cancelled;
                orderService.UpdateOrder(order);
                orderService.InsertOrderNote(new OrderNote()
                {
                    Note = "Order Cancelled because the user cancelled the payment",
                    OrderId = order.Id,
                    CreatedOnUtc = DateTime.UtcNow
                });
            }

            return Redirect("minisouq://?status=cancel");
        }

        string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[] IV)
        {
            try
            {
                // Check arguments.
                if (cipherText == null || cipherText.Length <= 0)
                    throw new ArgumentNullException("cipherText");
                if (Key == null || Key.Length <= 0)
                    throw new ArgumentNullException("Key");
                if (IV == null || IV.Length <= 0)
                    throw new ArgumentNullException("IV");
                // Declare the string used to hold
                // the decrypted text.
                string plaintext = null;
                // Create an AesManaged object
                // with the specified key and IV.
                using (AesManaged aesAlg = new AesManaged())
                {
                    aesAlg.Key = Key;
                    aesAlg.IV = IV;
                    //   aesAlg.Padding = PaddingMode.PKCS7;
                    // Create a decrytor to perform the stream transform.
                    ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key,
                   aesAlg.IV);
                    // Create the streams used for decryption.
                    using (MemoryStream msDecrypt = new MemoryStream(cipherText))
                    {
                        using (CryptoStream csDecrypt = new CryptoStream(msDecrypt,
                       decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader srDecrypt = new StreamReader(csDecrypt, Encoding.UTF8))
                            {
                                // Read the decrypted bytes from the decrypting stream
                                // and place them in a string.
                                plaintext = srDecrypt.ReadToEnd();
                            }
                        }
                    }
                }


                plaintext = plaintext.Replace("%5B", "[");
                plaintext = plaintext.Replace("%7B", "{");
                plaintext = plaintext.Replace("%22", "\"");
                plaintext = plaintext.Replace("%3A", ":");
                plaintext = plaintext.Replace("%2C", ",");
                plaintext = plaintext.Replace("%5D", "]");
                plaintext = plaintext.Replace("%7D", "}");
                return plaintext;
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        private string FormatString(string key, string seperator, int afterEvery)
        {
            var formattedKey = "";
            for (int i = 0; i < key.Length; i++)
            {
                var ch = key[i];

                if (i % afterEvery == 0 && i != 0)
                    formattedKey = formattedKey + seperator + ch;
                else
                    formattedKey = formattedKey + ch;

            }

            return formattedKey;
        }



















        // static string DecryptStringFromBytes_Aes(byte[] cipherText, byte[] Key, byte[]
        //IV)
        // {
        //         try
        //         {
        //             // Check arguments.
        //             if (cipherText == null || cipherText.Length <= 0)
        //                 throw new ArgumentNullException("cipherText");
        //             if (Key == null || Key.Length <= 0)
        //                 throw new ArgumentNullException("Key");
        //             if (IV == null || IV.Length <= 0)
        //                 throw new ArgumentNullException("IV");
        //             // Declare the string used to hold
        //             // the decrypted text.
        //             string plaintext = null;
        //             // Create an AesManaged object
        //             // with the specified key and IV.
        //             using (AesManaged aesAlg = new AesManaged())
        //             {
        //                 aesAlg.KeySize = 256;
        //                aesAlg.BlockSize = 128;
        //                 aesAlg.Mode = CipherMode.CBC;
        //                 aesAlg.Padding = PaddingMode.Zeros;
        //                 aesAlg.Key = Key;
        //                 aesAlg.IV = IV;

        //                 // Create a decrytor to perform the stream transform.
        //                 ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key,
        //                aesAlg.IV);
        //                 // Create the streams used for decryption.
        //                 using (MemoryStream msDecrypt = new MemoryStream(cipherText))
        //                 {
        //                     using (CryptoStream csDecrypt = new CryptoStream(msDecrypt,
        //                    decryptor, CryptoStreamMode.Read))
        //                     {
        //                         using (StreamReader srDecrypt = new StreamReader(csDecrypt))
        //                         {
        //                             // Read the decrypted bytes from the decrypting stream
        //                             // and place them in a string.
        //                             plaintext = srDecrypt.ReadToEnd();

        //                         }
        //                     }


        //                     }
        //                 }
        //             return plaintext;

        //         }
        //         catch(Exception ex)
        //         {
        //             return "";
        //         }
        // }
    }
}





