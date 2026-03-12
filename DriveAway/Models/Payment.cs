using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DriveAway.Models
{
    public enum PaymentType
    {
        Rental,
        SecurityDeposit,
        LateFee,
        DamageFee,
        FuelFee,
        DepositRefund
    }

    public enum PaymentMethodType
    {
        [Display(Name = "Credit Card")]
        CreditCard,

        [Display(Name = "E-Wallet")]
        EWallet,

        [Display(Name = "Cash")]
        Cash,

        [Display(Name = "Online")]
        Online
    }

    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public int RentalContractId { get; set; }

        [ForeignKey(nameof(RentalContractId))]
        public RentalContract RentalContract { get; set; } = null!;

        [Required]
        [Display(Name = "Payment Type")]
        public PaymentType PaymentType { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [Display(Name = "Payment Method")]
        public PaymentMethodType PaymentMethod { get; set; }

        [Display(Name = "Payment Status")]
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

        [StringLength(100)]
        public string? PayMongoPaymentId { get; set; }

        [StringLength(100)]
        [Display(Name = "PayMongo Payment Resource ID")]
        public string? PayMongoPaymentResourceId { get; set; }

        [StringLength(500)]
        [Display(Name = "Payment Link")]
        public string? PayMongoPaymentUrl { get; set; }

        [StringLength(50)]
        public string? OnlinePaymentOption { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
