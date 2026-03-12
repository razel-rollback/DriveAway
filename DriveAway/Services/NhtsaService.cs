using System.Text.Json;

namespace DriveAway.Services
{
    public class NhtsaService : INhtsaService
    {
        private readonly HttpClient _httpClient;

        public NhtsaService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<NhtsaVehicleInfo?> DecodeVinAsync(string vin)
        {
            try
            {
                var url = $"https://vpic.nhtsa.dot.gov/api/vehicles/decodevin/{Uri.EscapeDataString(vin.Trim().ToUpper())}?format=json";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var results = doc.RootElement.GetProperty("Results");

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in results.EnumerateArray())
                {
                    if (item.TryGetProperty("Variable", out var variable) &&
                        item.TryGetProperty("Value", out var value) &&
                        value.ValueKind != JsonValueKind.Null)
                    {
                        var v = value.GetString();
                        if (!string.IsNullOrWhiteSpace(v))
                            dict[variable.GetString() ?? ""] = v;
                    }
                }

                var errorCode = dict.GetValueOrDefault("Error Code", "");
                var isValid = errorCode == "0";
                var errorText = dict.GetValueOrDefault("Error Text", "");

                return new NhtsaVehicleInfo
                {
                    Make = dict.GetValueOrDefault("Make", ""),
                    Model = dict.GetValueOrDefault("Model", ""),
                    Year = dict.GetValueOrDefault("Model Year", ""),
                    BodyClass = dict.GetValueOrDefault("Body Class", ""),
                    Manufacturer = dict.GetValueOrDefault("Manufacturer Name", ""),
                    IsValid = isValid,
                    ErrorText = isValid ? null : errorText
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
