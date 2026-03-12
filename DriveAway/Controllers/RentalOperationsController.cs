using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin,Super Admin,Business Owner,Staff")]
    public class RentalOperationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _audit;
        private readonly IPayMongoService _payMongo;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailService _email;

        public RentalOperationsController(ApplicationDbContext context, IAuditService audit, IPayMongoService payMongo, UserManager<IdentityUser> userManager, IEmailService email)
        {
            _context = context;
            _audit = audit;
            _payMongo = payMongo;
            _userManager = userManager;
            _email = email;
        }

        private async Task<int?> GetCurrentUserBranchId()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return null;
            var ub = await _context.UserBranches.FirstOrDefaultAsync(u => u.UserId == user.Id);
            return ub?.BranchId;
        }

        // ───────── Dashboard ─────────
        [Authorize(Roles = "Admin,Super Admin,Business Owner,Staff")]
        public async Task<IActionResult> Index()
        {
            var vehicles = await _context.Vehicles.ToListAsync();
            var contracts = await _context.RentalContracts
                .Include(c => c.Vehicle)
                .Include(c => c.Payments)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // Sync PayMongo statuses
            var pendingPayments = contracts
                .SelectMany(c => c.Payments)
                .Where(p => p.PaymentStatus == PaymentStatus.Pending && !string.IsNullOrEmpty(p.PayMongoPaymentId))
                .ToList();

            if (pendingPayments.Any())
            {
                bool synced = false;
                foreach (var payment in pendingPayments)
                {
                    var (status, paymentOption, paymentResourceId) = await _payMongo.GetPaymentLinkStatusAsync(payment.PayMongoPaymentId!);
                    if (status == "paid")
                    {
                        payment.PaymentStatus = PaymentStatus.Paid;
                        payment.OnlinePaymentOption = paymentOption;
                        payment.PayMongoPaymentResourceId = paymentResourceId;
                        synced = true;

                        // Also mark the deposit payment as Paid (they share the same PayMongo link)
                        var contract = contracts.FirstOrDefault(c => c.Id == payment.RentalContractId);
                        var depositPayment = contract?.Payments.FirstOrDefault(p => p.PaymentType == PaymentType.SecurityDeposit && p.PaymentStatus == PaymentStatus.Pending);
                        if (depositPayment != null)
                        {
                            depositPayment.PaymentStatus = PaymentStatus.Paid;
                            depositPayment.OnlinePaymentOption = paymentOption;
                            depositPayment.PayMongoPaymentResourceId = paymentResourceId;
                        }
                    }
                }
                if (synced)
                    await _context.SaveChangesAsync();
            }

            var today = DateTime.Now.Date;

            ViewBag.AvailableVehicles = vehicles.Count(v => v.Status == VehicleStatus.Available);
            ViewBag.ActiveRentals = contracts.Count(c => c.RentalStatus == RentalStatus.Active);
            ViewBag.CompletedToday = contracts.Count(c => c.RentalStatus == RentalStatus.Completed
                && c.ActualReturn.HasValue && c.ActualReturn.Value.Date == today);
            ViewBag.Overdue = contracts.Count(c => c.RentalStatus == RentalStatus.Active
                && c.RentalEnd.Date < today);
            ViewBag.TotalRented = vehicles.Count(v => v.Status == VehicleStatus.Rented);

            return View(contracts);
        }

        // ───────── Check-Out ─────────
        [HttpGet]
        public async Task<IActionResult> CheckOut()
        {
            await PopulateCheckOutData();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(RentalContract model, string paymentMethod)
        {
            // Remove navigation-property and computed fields from validation
            ModelState.Remove(nameof(model.Vehicle));
            ModelState.Remove(nameof(model.ContractNumber));
            ModelState.Remove(nameof(model.ProcessedByUserId));
            ModelState.Remove("paymentMethod");
            ModelState.Remove(nameof(model.Payments));

            if (!ModelState.IsValid)
            {
                await PopulateCheckOutData();
                return View(model);
            }

            var vehicle = await _context.Vehicles.FindAsync(model.VehicleId);
            if (vehicle == null || vehicle.Status != VehicleStatus.Available)
            {
                ModelState.AddModelError("", "Selected vehicle is not available for rental.");
                await PopulateCheckOutData();
                return View(model);
            }

            // Parse payment method
            if (!Enum.TryParse<PaymentMethodType>(paymentMethod, out var pmMethod))
                pmMethod = PaymentMethodType.Cash;

            // Compute fee
            var days = Math.Max(1, (int)Math.Ceiling((model.RentalEnd - model.RentalStart).TotalDays));
            model.TotalFee = model.DailyRate * days;

            // Generate contract number  RC-yyyyMMdd-NNN
            var todayStr = DateTime.Now.ToString("yyyyMMdd");
            var countToday = await _context.RentalContracts
                .CountAsync(c => c.ContractNumber.StartsWith($"RC-{todayStr}"));
            model.ContractNumber = $"RC-{todayStr}-{(countToday + 1):D3}";

            // Track which staff member processed it
            model.ProcessedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            model.ProcessedByEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

            model.RentalStatus = RentalStatus.Active;
            model.DepositStatus = DepositStatus.Held;
            model.CreatedAt = DateTime.Now;

            // Update vehicle status
            vehicle.Status = VehicleStatus.Rented;

            // Add lifecycle event
            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.Rented,
                EventDate = DateTime.Now,
                Notes = $"Rented to {model.CustomerName} — Contract {model.ContractNumber}",
                Mileage = vehicle.CurrentMileage
            });

            _context.RentalContracts.Add(model);
            await _context.SaveChangesAsync();

            // ── Create Payment records ──
            // Cash = immediately Paid; Online = Pending until PayMongo confirms
            var initialStatus = pmMethod == PaymentMethodType.Cash
                ? PaymentStatus.Paid
                : PaymentStatus.Pending;

            // 1) Rental payment
            var rentalPayment = new Payment
            {
                RentalContractId = model.Id,
                PaymentType = PaymentType.Rental,
                Amount = model.TotalFee,
                PaymentMethod = pmMethod,
                PaymentStatus = initialStatus,
                Notes = $"Rental: {days} day(s) × ₱{model.DailyRate:N2}",
                CreatedAt = DateTime.Now
            };
            _context.Payments.Add(rentalPayment);

            // 2) Security deposit payment
            var depositPayment = new Payment
            {
                RentalContractId = model.Id,
                PaymentType = PaymentType.SecurityDeposit,
                Amount = model.SecurityDeposit,
                PaymentMethod = pmMethod,
                PaymentStatus = initialStatus,
                Notes = "Security deposit",
                CreatedAt = DateTime.Now
            };
            _context.Payments.Add(depositPayment);
            await _context.SaveChangesAsync();

            // Create PayMongo payment link for Online payments
            string? payMongoUrl = null;
            if (pmMethod == PaymentMethodType.Online)
            {
                var paymentAmount = model.TotalFee + model.SecurityDeposit;
                var result = await _payMongo.CreatePaymentLinkAsync(
                    paymentAmount,
                    $"Rental Payment + Deposit - {model.ContractNumber} ({vehicle.PlateNumber})",
                    model.ContractNumber);

                if (result != null)
                {
                    rentalPayment.PayMongoPaymentId = result.PaymentId;
                    rentalPayment.PayMongoPaymentUrl = result.CheckoutUrl;
                    depositPayment.PayMongoPaymentId = result.PaymentId;
                    depositPayment.PayMongoPaymentUrl = result.CheckoutUrl;
                    payMongoUrl = result.CheckoutUrl;
                    await _context.SaveChangesAsync();
                }
            }

            // Audit log
            var paymentAmount2 = model.TotalFee + model.SecurityDeposit;
            await _audit.LogAsync(
                AuditAction.CheckOut,
                AuditModule.Rental,
                entityType: "RentalContract",
                entityId: model.Id.ToString(),
                entityName: model.ContractNumber,
                details: $"Vehicle {vehicle.PlateNumber} rented to {model.CustomerName}. Payment: ₱{paymentAmount2:N2} via {pmMethod}");

            // ── Send Rental Confirmation Email ──
            var paymentInfo = pmMethod == PaymentMethodType.Online && !string.IsNullOrEmpty(payMongoUrl)
                ? $"<p><strong>Payment Link:</strong> <a href='{payMongoUrl}'>Click here to pay online</a></p>"
                : $"<p><strong>Payment:</strong> ₱{paymentAmount2:N2} via {pmMethod} — Paid</p>";
            var checkoutEmailBody = $@"
                <h2>Rental Confirmation — {model.ContractNumber}</h2>
                <p>Dear {model.CustomerName},</p>
                <p>Your rental has been confirmed. Here are the details:</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Vehicle:</strong></td><td>{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Pickup:</strong></td><td>{model.RentalStart:MMM dd, yyyy}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Return:</strong></td><td>{model.RentalEnd:MMM dd, yyyy}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Daily Rate:</strong></td><td>₱{model.DailyRate:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Total Fee:</strong></td><td>₱{model.TotalFee:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Security Deposit:</strong></td><td>₱{model.SecurityDeposit:N2}</td></tr>
                </table>
                {paymentInfo}
                <p>Thank you for choosing DriveAway!</p>";
            try { await _email.SendEmailAsync(model.CustomerEmail, $"Rental Confirmation — {model.ContractNumber}", checkoutEmailBody); }
            catch { /* logged by SmtpEmailService */ }

            if (pmMethod == PaymentMethodType.Online && !string.IsNullOrEmpty(payMongoUrl))
            {
                return RedirectToAction(nameof(PaymentQr), new { id = model.Id });
            }

            var successMsg = $"Vehicle {vehicle.PlateNumber} checked out successfully. Contract: {model.ContractNumber}";
            if (!string.IsNullOrEmpty(payMongoUrl))
                successMsg += " Payment link created.";

            TempData["Success"] = successMsg;
            TempData["PaymentUrl"] = payMongoUrl;
            return RedirectToAction(nameof(Index));
        }

        // ───────── Contract Details ─────────
        [Authorize(Roles = "Admin,Super Admin,Business Owner,Staff")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var contract = await _context.RentalContracts
                .Include(c => c.Vehicle)
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contract == null) return NotFound();

            return View(contract);
        }

        // ───────── Payment QR View ─────────
        [Authorize(Roles = "Admin,Super Admin,Business Owner,Staff")]
        [HttpGet]
        public async Task<IActionResult> PaymentQr(int id)
        {
            var contract = await _context.RentalContracts
                .Include(c => c.Vehicle)
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contract == null) return NotFound();

            return View(contract);
        }

        [Authorize(Roles = "Admin,Super Admin,Business Owner,Staff")]
        [HttpGet]
        public async Task<IActionResult> CheckPaymentStatus(int id)
        {
            var contract = await _context.RentalContracts
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contract == null) return Json(new { status = "not_found" });

            var pendingPayments = contract.Payments
                .Where(p => p.PaymentStatus == PaymentStatus.Pending && !string.IsNullOrEmpty(p.PayMongoPaymentId))
                .ToList();

            if (!pendingPayments.Any())
            {
                // If there are no pending payments, it might mean they are already paid
                var hasPaid = contract.Payments.Any(p => p.PaymentStatus == PaymentStatus.Paid && !string.IsNullOrEmpty(p.PayMongoPaymentId));
                if (hasPaid)
                {
                    TempData["Success"] = "Payment received successfully!";
                }
                return Json(new { status = hasPaid ? "paid" : "no_pending" });
            }

            bool anyPaid = false;
            foreach (var payment in pendingPayments)
            {
                var (status, paymentOption, paymentResourceId) = await _payMongo.GetPaymentLinkStatusAsync(payment.PayMongoPaymentId!);
                if (status == "paid")
                {
                    payment.PaymentStatus = PaymentStatus.Paid;
                    payment.OnlinePaymentOption = paymentOption;
                    payment.PayMongoPaymentResourceId = paymentResourceId;
                    anyPaid = true;

                    // Also mark the deposit payment as Paid
                    var depositPayment = contract.Payments
                        .FirstOrDefault(p => p.PaymentType == PaymentType.SecurityDeposit && p.PaymentStatus == PaymentStatus.Pending);
                    if (depositPayment != null)
                    {
                        depositPayment.PaymentStatus = PaymentStatus.Paid;
                        depositPayment.OnlinePaymentOption = paymentOption;
                        depositPayment.PayMongoPaymentResourceId = paymentResourceId;
                    }
                }
            }

            if (anyPaid)
            {
                await _context.SaveChangesAsync();

                // Send payment confirmation email
                var totalPaid = contract.Payments.Where(p => p.PaymentStatus == PaymentStatus.Paid).Sum(p => p.Amount);
                var paymentEmailBody = $@"
                    <h2>Payment Received — {contract.ContractNumber}</h2>
                    <p>Dear {contract.CustomerName},</p>
                    <p>We have received your payment of <strong>₱{totalPaid:N2}</strong> for contract <strong>{contract.ContractNumber}</strong>.</p>
                    <p>Your rental is now fully confirmed. Thank you!</p>";
                try { await _email.SendEmailAsync(contract.CustomerEmail, $"Payment Received — {contract.ContractNumber}", paymentEmailBody); }
                catch { /* logged by SmtpEmailService */ }

                TempData["Success"] = "Payment received successfully!";
                return Json(new { status = "paid" });
            }

            return Json(new { status = "pending" });
        }

        // ───────── Check-In ─────────
        [HttpGet]
        public async Task<IActionResult> CheckIn(int id)
        {
            var contract = await _context.RentalContracts
                .Include(c => c.Vehicle)
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Id == id && c.RentalStatus == RentalStatus.Active);

            if (contract == null) return NotFound();

            return View(contract);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(int id, DateTime actualReturn,
            string? returnFuelLevel, string? returnDamageNotes, int? returnMileage,
            decimal? lateFee, decimal? damageFee, decimal? fuelFee,
            string? damageSeverity, string? balancePaymentMethod)
        {
            var contract = await _context.RentalContracts
                .Include(c => c.Vehicle)
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Id == id && c.RentalStatus == RentalStatus.Active);

            if (contract == null) return NotFound();

            contract.ActualReturn = actualReturn;
            contract.ReturnFuelLevel = returnFuelLevel;
            contract.ReturnDamageNotes = returnDamageNotes;
            contract.ReturnMileage = returnMileage;
            contract.LateFee = lateFee ?? 0;
            contract.DamageFee = damageFee ?? 0;
            contract.FuelFee = fuelFee ?? 0;

            // Determine original payment method from the rental payment
            var originalRentalPayment = contract.Payments
                .FirstOrDefault(p => p.PaymentType == PaymentType.Rental);
            var originalDepositPayment = contract.Payments
                .FirstOrDefault(p => p.PaymentType == PaymentType.SecurityDeposit);
            var originalPaymentMethod = originalRentalPayment?.PaymentMethod ?? PaymentMethodType.Cash;

            if (originalPaymentMethod == PaymentMethodType.Online && originalDepositPayment != null)
            {
                if (string.IsNullOrEmpty(originalDepositPayment.PayMongoPaymentResourceId) && !string.IsNullOrEmpty(originalDepositPayment.PayMongoPaymentId))
                {
                    var (status, paymentOption, paymentResourceId) = await _payMongo.GetPaymentLinkStatusAsync(originalDepositPayment.PayMongoPaymentId);
                    if (status == "paid" && !string.IsNullOrEmpty(paymentResourceId))
                    {
                        originalDepositPayment.PayMongoPaymentResourceId = paymentResourceId;
                        originalDepositPayment.OnlinePaymentOption = paymentOption;
                        originalDepositPayment.PaymentStatus = PaymentStatus.Paid;
                        if (originalRentalPayment != null) 
                        {
                            originalRentalPayment.PayMongoPaymentResourceId = paymentResourceId;
                            originalRentalPayment.OnlinePaymentOption = paymentOption;
                            originalRentalPayment.PaymentStatus = PaymentStatus.Paid;
                        }
                    }
                }
            }

            // Calculate extra fees and deposit refund
            var extraFees = contract.LateFee + contract.DamageFee + contract.FuelFee;
            var amountDue = Math.Max(0, extraFees - contract.SecurityDeposit);
            var depositRefund = Math.Max(0, contract.SecurityDeposit - extraFees);

            // Defensive: recalculate TotalFee if it is zero (e.g. legacy record or data issue)
            if (contract.TotalFee == 0 && contract.DailyRate > 0)
            {
                var contractDays = Math.Max(1, (int)Math.Ceiling((contract.RentalEnd - contract.RentalStart).TotalDays));
                contract.TotalFee = contract.DailyRate * contractDays;
            }

            // FinalFee = total contract cost (rental + extra fees)
            contract.FinalFee = contract.TotalFee + extraFees;
            contract.DepositRefundAmount = depositRefund;

            // ── Create fee payment records (use same method as original) ──
            // Fees covered by deposit are marked Paid; excess beyond deposit is Pending
            if (contract.LateFee > 0)
            {
                _context.Payments.Add(new Payment
                {
                    RentalContractId = contract.Id,
                    PaymentType = PaymentType.LateFee,
                    Amount = contract.LateFee,
                    PaymentMethod = originalPaymentMethod,
                    PaymentStatus = PaymentStatus.Paid,
                    Notes = "Late return fee (deducted from deposit)",
                    CreatedAt = DateTime.Now
                });
            }
            if (contract.DamageFee > 0)
            {
                _context.Payments.Add(new Payment
                {
                    RentalContractId = contract.Id,
                    PaymentType = PaymentType.DamageFee,
                    Amount = contract.DamageFee,
                    PaymentMethod = originalPaymentMethod,
                    PaymentStatus = PaymentStatus.Paid,
                    Notes = returnDamageNotes ?? "Damage fee (deducted from deposit)",
                    CreatedAt = DateTime.Now
                });
            }
            if (contract.FuelFee > 0)
            {
                _context.Payments.Add(new Payment
                {
                    RentalContractId = contract.Id,
                    PaymentType = PaymentType.FuelFee,
                    Amount = contract.FuelFee,
                    PaymentMethod = originalPaymentMethod,
                    PaymentStatus = PaymentStatus.Paid,
                    Notes = $"Fuel fee — returned at {returnFuelLevel ?? "N/A"} (deducted from deposit)",
                    CreatedAt = DateTime.Now
                });
            }

            // ── Deposit refund record ──
            if (depositRefund > 0)
            {
                var refundPayment = new Payment
                {
                    RentalContractId = contract.Id,
                    PaymentType = PaymentType.DepositRefund,
                    Amount = depositRefund,
                    PaymentMethod = originalPaymentMethod,
                    PaymentStatus = PaymentStatus.Paid,
                    Notes = "Security deposit refund to customer",
                    CreatedAt = DateTime.Now
                };

                // If original payment was Online, refund via PayMongo to same account
                if (originalPaymentMethod == PaymentMethodType.Online)
                {
                    if (originalDepositPayment?.PayMongoPaymentResourceId != null)
                    {
                        var refunded = await _payMongo.CreateRefundAsync(
                            originalDepositPayment.PayMongoPaymentResourceId,
                            depositRefund,
                            "requested_by_customer",
                            $"Deposit refund for contract {contract.ContractNumber}");
                        
                        refundPayment.Notes = refunded
                            ? "Security deposit refunded online to original payment method"
                            : "Online refund attempted via PayMongo but failed — check PayMongo dashboard or process manually";
                        
                        if (!refunded)
                            refundPayment.PaymentStatus = PaymentStatus.Pending;
                    }
                    else
                    {
                        refundPayment.Notes = "Online refund required but Payment Resource ID was missing — please process manually in PayMongo dashboard";
                        refundPayment.PaymentStatus = PaymentStatus.Pending;
                    }
                }

                _context.Payments.Add(refundPayment);
            }

            // Set deposit status
            if (extraFees == 0)
                contract.DepositStatus = DepositStatus.FullyRefunded;
            else if (depositRefund > 0)
                contract.DepositStatus = DepositStatus.PartiallyRefunded;
            else if (extraFees >= contract.SecurityDeposit)
                contract.DepositStatus = DepositStatus.Forfeited;

            contract.RentalStatus = RentalStatus.Completed;

            // Update vehicle
            var vehicle = contract.Vehicle;
            if (returnMileage.HasValue && returnMileage.Value > vehicle.CurrentMileage)
                vehicle.CurrentMileage = returnMileage.Value;

            // ── Damage Assessment → MaintenanceJob ──
            var severity = DamageSeverity.None;
            if (!string.IsNullOrEmpty(damageSeverity))
                Enum.TryParse(damageSeverity, out severity);

            if (severity == DamageSeverity.Major)
            {
                vehicle.Status = VehicleStatus.UnderMaintenance;
            }
            else
            {
                vehicle.Status = VehicleStatus.Available;
            }

            if (severity != DamageSeverity.None)
            {
                var staffEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;

                _context.MaintenanceJobs.Add(new MaintenanceJob
                {
                    VehicleId = vehicle.Id,
                    RentalContractId = contract.Id,
                    DamageSeverity = severity,
                    DamageDescription = returnDamageNotes,
                    JobStatus = MaintenanceJobStatus.Pending,
                    CreatedAt = DateTime.Now,
                    CreatedByEmail = staffEmail
                });

                _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
                {
                    VehicleId = vehicle.Id,
                    EventType = LifecycleEventType.DamageReported,
                    EventDate = DateTime.Now,
                    Notes = $"{severity} damage reported on return — Contract {contract.ContractNumber}. {returnDamageNotes}",
                    Mileage = returnMileage
                });

                await _audit.LogAsync(
                    AuditAction.DamageReport,
                    AuditModule.MaintenanceJobs,
                    entityType: "MaintenanceJob",
                    entityId: vehicle.Id.ToString(),
                    entityName: $"{vehicle.PlateNumber}",
                    details: $"{severity} damage reported on return. Contract: {contract.ContractNumber}. Notes: {returnDamageNotes}");
            }

            // Lifecycle event for return
            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.Returned,
                EventDate = DateTime.Now,
                Notes = $"Returned by {contract.CustomerName} — Contract {contract.ContractNumber}"
                    + (string.IsNullOrWhiteSpace(returnDamageNotes) ? "" : $" | Damage: {returnDamageNotes}"),
                Mileage = returnMileage
            });

            // Create PayMongo payment link if there's an amount due after deposit deduction
            // (customer needs to pay the remaining balance)
            string? payMongoUrl = null;
            if (amountDue > 0)
            {
                PaymentMethodType chosenBalanceMethod = PaymentMethodType.Cash;
                if (balancePaymentMethod == "Online") chosenBalanceMethod = PaymentMethodType.Online;

                // Create a separate "balance due" payment record for the customer
                var balancePayment = new Payment
                {
                    RentalContractId = contract.Id,
                    PaymentType = PaymentType.LateFee, // Use first applicable fee type
                    Amount = amountDue,
                    PaymentMethod = chosenBalanceMethod,
                    PaymentStatus = chosenBalanceMethod == PaymentMethodType.Cash ? PaymentStatus.Paid : PaymentStatus.Pending,
                    Notes = $"Customer balance due — fees (₱{extraFees:N2}) exceeded deposit (₱{contract.SecurityDeposit:N2})",
                    CreatedAt = DateTime.Now
                };

                if (chosenBalanceMethod == PaymentMethodType.Online)
                {
                    var result = await _payMongo.CreatePaymentLinkAsync(
                        amountDue,
                        $"Balance Due - {contract.ContractNumber} ({vehicle.PlateNumber})",
                        contract.ContractNumber);

                    if (result != null)
                    {
                        payMongoUrl = result.CheckoutUrl;
                        balancePayment.PayMongoPaymentId = result.PaymentId;
                        balancePayment.PayMongoPaymentUrl = result.CheckoutUrl;
                        balancePayment.PaymentMethod = PaymentMethodType.Online;
                    }
                }

                _context.Payments.Add(balancePayment);
            }

            await _context.SaveChangesAsync();

            // Audit log
            await _audit.LogAsync(
                AuditAction.CheckIn,
                AuditModule.Rental,
                entityType: "RentalContract",
                entityId: contract.Id.ToString(),
                entityName: contract.ContractNumber,
                details: $"Vehicle {vehicle.PlateNumber} returned by {contract.CustomerName}. Damage: {severity}. Extra Fees: ₱{extraFees:N2}, Deposit Deducted: ₱{contract.SecurityDeposit:N2}, Amount Due: ₱{amountDue:N2}, Refund: ₱{depositRefund:N2}");

            // ── Send Check-In Summary Email ──
            var balanceSection = amountDue > 0
                ? (string.IsNullOrEmpty(payMongoUrl)
                    ? $"<tr><td style='padding:4px 12px;'><strong>Balance Due:</strong></td><td>₱{amountDue:N2} (Cash)</td></tr>"
                    : $"<tr><td style='padding:4px 12px;'><strong>Balance Due:</strong></td><td>₱{amountDue:N2} — <a href='{payMongoUrl}'>Pay Online</a></td></tr>")
                : "";
            var checkInEmailBody = $@"
                <h2>Rental Return Summary — {contract.ContractNumber}</h2>
                <p>Dear {contract.CustomerName},</p>
                <p>Your rental has been checked in. Here is your summary:</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Vehicle:</strong></td><td>{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Returned:</strong></td><td>{actualReturn:MMM dd, yyyy}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Late Fee:</strong></td><td>₱{contract.LateFee:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Damage Fee:</strong></td><td>₱{contract.DamageFee:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Fuel Fee:</strong></td><td>₱{contract.FuelFee:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Deposit Refund:</strong></td><td>₱{depositRefund:N2}</td></tr>
                    {balanceSection}
                </table>
                <p>Thank you for choosing DriveAway!</p>";
            try { await _email.SendEmailAsync(contract.CustomerEmail, $"Rental Return Summary — {contract.ContractNumber}", checkInEmailBody); }
            catch { /* logged by SmtpEmailService */ }

            var successMsg = $"Vehicle {vehicle.PlateNumber} checked in successfully.";
            if (severity != DamageSeverity.None)
                successMsg += $" {severity} damage reported — maintenance job created.";
            if (depositRefund > 0)
                successMsg += $" Deposit refund: ₱{depositRefund:N2}.";
            if (amountDue > 0 && !string.IsNullOrEmpty(payMongoUrl))
                successMsg += $" Payment link created for ₱{amountDue:N2}.";
            else if (amountDue == 0)
                successMsg += " No additional payment required.";

            TempData["Success"] = successMsg;
            TempData["PaymentUrl"] = payMongoUrl;
            return RedirectToAction(nameof(Index));
        }

        // ───────── Helpers ─────────
        private async Task PopulateCheckOutData()
        {
            var query = _context.Vehicles
                .Include(v => v.Branch)
                .Where(v => v.Status == VehicleStatus.Available);

            if (!User.IsInRole("Business Owner") && !User.IsInRole("Super Admin"))
            {
                var branchId = await GetCurrentUserBranchId();
                if (branchId.HasValue)
                {
                    query = query.Where(v => v.BranchId == branchId.Value);
                }
            }

            var available = await query.OrderBy(v => v.PlateNumber).ToListAsync();

            ViewBag.Vehicles = new SelectList(
                available.Select(v => new
                {
                    v.Id,
                    Display = $"{v.PlateNumber} — {v.Make} {v.Model} ({v.Year})"
                }),
                "Id", "Display");

            ViewBag.VehicleData = available.Select(v => new
            {
                v.Id,
                v.Category,
                v.ImagePath,
                Branch = v.Branch?.Name ?? "—",
                Display = $"{v.PlateNumber} — {v.Make} {v.Model} ({v.Year})"
            }).ToList();

            var categories = available
                .Where(v => !string.IsNullOrEmpty(v.Category))
                .Select(v => v.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            ViewBag.Categories = categories;

            var rates = await _context.CategoryRates.ToListAsync();
            ViewBag.CategoryRates = rates;
        }
    }
}
