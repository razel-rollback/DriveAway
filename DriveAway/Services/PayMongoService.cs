using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DriveAway.Services
{
    public class PayMongoService : IPayMongoService
    {
        private readonly HttpClient _http;
        private readonly string _secretKey;

        public PayMongoService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _secretKey = config["PayMongo:SecretKey"] ?? "";
            _http.BaseAddress = new Uri("https://api.paymongo.com/v1/");

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_secretKey}:"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        public async Task<PayMongoResult?> CreatePaymentLinkAsync(decimal amount, string description, string contractNumber)
        {
            try
            {
                // PayMongo expects amount in centavos (integer)
                var amountInCentavos = (int)(amount * 100);

                var payload = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            amount = amountInCentavos,
                            description = description,
                            remarks = $"Contract: {contractNumber}",
                            currency = "PHP"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync("links", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"PayMongo error: {response.StatusCode} - {errorBody}");
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                var data = doc.RootElement.GetProperty("data");

                return new PayMongoResult
                {
                    PaymentId = data.GetProperty("id").GetString() ?? "",
                    CheckoutUrl = data.GetProperty("attributes").GetProperty("checkout_url").GetString() ?? ""
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PayMongo exception: {ex.Message}");
                return null;
            }
        }

        public async Task<(string? Status, string? PaymentOption, string? PaymentResourceId)> GetPaymentLinkStatusAsync(string linkId)
        {
            try
            {
                // Use include=payments to ensure the payment resource is returned in the 'included' array
                var response = await _http.GetAsync($"links/{linkId}?include=payments");
                if (!response.IsSuccessStatusCode) return (null, null, null);

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;
                var data = root.GetProperty("data");
                var attributes = data.GetProperty("attributes");

                var status = attributes.GetProperty("status").GetString();
                string? paymentOption = null;
                string? paymentResourceId = null;

                // 1. Try to find the payment ID in the 'included' array (modern PayMongo API style)
                if (root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in included.EnumerateArray())
                    {
                        if (item.GetProperty("type").GetString() == "payment")
                        {
                            paymentResourceId = item.GetProperty("id").GetString();
                            
                            if (item.TryGetProperty("attributes", out var payAttrs) && 
                                payAttrs.TryGetProperty("source", out var source))
                            {
                                paymentOption = source.GetProperty("type").GetString();
                            }
                            break;
                        }
                    }
                }

                // 2. Fallback: check nested payments in attributes (if any)
                if (string.IsNullOrEmpty(paymentResourceId) && 
                    attributes.TryGetProperty("payments", out var payments) && 
                    payments.ValueKind == JsonValueKind.Array && payments.GetArrayLength() > 0)
                {
                    var firstPaymentWrapper = payments[0];
                    if (firstPaymentWrapper.TryGetProperty("data", out var firstPaymentData))
                    {
                        if (firstPaymentData.TryGetProperty("id", out var payId))
                        {
                            paymentResourceId = payId.GetString();
                        }

                        if (firstPaymentData.TryGetProperty("attributes", out var paymentAttrs) && paymentAttrs.TryGetProperty("source", out var source))
                        {
                            paymentOption = source.GetProperty("type").GetString();
                        }
                    }
                    else
                    {
                        // Some endpoints might return id directly without data wrapper
                        if (firstPaymentWrapper.TryGetProperty("id", out var payId))
                        {
                            paymentResourceId = payId.GetString();
                        }

                        if (firstPaymentWrapper.TryGetProperty("attributes", out var paymentAttrs) && paymentAttrs.TryGetProperty("source", out var source))
                        {
                            paymentOption = source.GetProperty("type").GetString();
                        }
                    }
                }

                return (status, paymentOption, paymentResourceId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PayMongo get status exception: {ex.Message}");
                return (null, null, null);
            }
        }

        public async Task<bool> CreateRefundAsync(string paymentResourceId, decimal amount, string reason, string notes)
        {
            try
            {
                var amountInCentavos = (int)(amount * 100);

                var payload = new
                {
                    data = new
                    {
                        attributes = new
                        {
                            amount = amountInCentavos,
                            payment_id = paymentResourceId,
                            reason = reason,
                            notes = notes
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync("refunds", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"PayMongo refund error: {response.StatusCode} - {errorBody}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PayMongo refund exception: {ex.Message}");
                return false;
            }
        }
    }
}
