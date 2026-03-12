namespace DriveAway.Services
{
    public class NhtsaVehicleInfo
    {
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;
        public string BodyClass { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public string? ErrorText { get; set; }
    }

    public interface INhtsaService
    {
        Task<NhtsaVehicleInfo?> DecodeVinAsync(string vin);
    }
}
