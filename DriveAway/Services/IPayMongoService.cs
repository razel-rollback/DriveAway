namespace DriveAway.Services
{
    public class PayMongoResult
    {
        public string PaymentId { get; set; } = string.Empty;
        public string CheckoutUrl { get; set; } = string.Empty;
    }

    public interface IPayMongoService
    {
        Task<PayMongoResult?> CreatePaymentLinkAsync(decimal amount, string description, string contractNumber);
        Task<(string? Status, string? PaymentOption, string? PaymentResourceId)> GetPaymentLinkStatusAsync(string linkId);
        Task<bool> CreateRefundAsync(string paymentResourceId, decimal amount, string reason, string notes);
    }
}
