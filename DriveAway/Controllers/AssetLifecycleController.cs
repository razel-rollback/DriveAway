using DriveAway.Data;
using DriveAway.Models;
using DriveAway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DriveAway.Controllers
{
    [Authorize(Roles = "Admin,Super Admin,Business Owner")]
    public class AssetLifecycleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly INhtsaService _nhtsaService;
        private readonly IAuditService _audit;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailService _email;

        public AssetLifecycleController(ApplicationDbContext context, INhtsaService nhtsaService, IAuditService audit, IWebHostEnvironment env, UserManager<IdentityUser> userManager, IEmailService email)
        {
            _context = context;
            _nhtsaService = nhtsaService;
            _audit = audit;
            _env = env;
            _userManager = userManager;
            _email = email;
        }

        // ─── Helper: get current admin's branch ─────────────────────────
        private async Task<int?> GetCurrentUserBranchId()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return null;
            var ub = await _context.UserBranches.FirstOrDefaultAsync(u => u.UserId == user.Id);
            return ub?.BranchId;
        }

        private async Task<IQueryable<Vehicle>> GetBranchFilteredVehicles()
        {
            if (User.IsInRole("Business Owner") || User.IsInRole("Super Admin"))
            {
                return _context.Vehicles.Include(v => v.Branch);
            }
            // Admin sees only their branch
            var branchId = await GetCurrentUserBranchId();
            if (branchId.HasValue)
            {
                return _context.Vehicles.Include(v => v.Branch).Where(v => v.BranchId == branchId.Value);
            }
            return _context.Vehicles.Include(v => v.Branch);
        }

        // ─── ASSET REGISTRATION ──────────────────────────────────────────────

        public async Task<IActionResult> Registration()
        {
            var query = await GetBranchFilteredVehicles();
            var vehicles = await query.OrderByDescending(v => v.CreatedAt).ToListAsync();
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            return View(vehicles);
        }

        [Authorize(Roles = "Super Admin,Business Owner")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            return View(new Vehicle { AcquisitionDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Super Admin,Business Owner")]
        public async Task<IActionResult> Create(Vehicle vehicle, IFormFile? vehicleImage)
        {
            ModelState.Remove(nameof(vehicleImage));

            // Additional validation for nullable fields that shouldn't be null
            if (string.IsNullOrWhiteSpace(vehicle.VIN))
            {
                ModelState.AddModelError(nameof(vehicle.VIN), "VIN is required.");
            }
            if (string.IsNullOrWhiteSpace(vehicle.PlateNumber))
            {
                ModelState.AddModelError(nameof(vehicle.PlateNumber), "Plate Number is required.");
            }
            if (string.IsNullOrWhiteSpace(vehicle.Make))
            {
                ModelState.AddModelError(nameof(vehicle.Make), "Make is required.");
            }
            if (string.IsNullOrWhiteSpace(vehicle.Model))
            {
                ModelState.AddModelError(nameof(vehicle.Model), "Model is required.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Handle image upload
                    if (vehicleImage != null && vehicleImage.Length > 0)
                    {
                        vehicle.ImagePath = await SaveVehicleImage(vehicleImage);
                    }

                    vehicle.CurrentMileage = vehicle.InitialMileage;
                    vehicle.Status = VehicleStatus.Available;
                    vehicle.CreatedAt = DateTime.UtcNow;

                    _context.Vehicles.Add(vehicle);
                    await _context.SaveChangesAsync();

                    _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
                    {
                        VehicleId = vehicle.Id,
                        EventType = LifecycleEventType.Acquired,
                        EventDate = vehicle.AcquisitionDate,
                        Notes = $"Vehicle acquired from {vehicle.Supplier ?? "N/A"}. Initial mileage: {vehicle.InitialMileage:N0} km.",
                        Mileage = vehicle.InitialMileage
                    });
                    await _context.SaveChangesAsync();

                    await _audit.LogAsync(AuditAction.Create, AuditModule.Asset, "Vehicle",
                        vehicle.Id.ToString(),
                        $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})",
                        $"VIN: {vehicle.VIN}. Acquired from {vehicle.Supplier ?? "N/A"}. Cost: ₱{vehicle.PurchaseCost:N2}.");

                    TempData["Success"] = $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber}) registered successfully.";
                    return RedirectToAction(nameof(Registration));
                }
                catch (Exception ex)
                {
                    var logger = _audit as AuditService;
                    ModelState.AddModelError(string.Empty, $"An error occurred while saving the vehicle: {ex.Message}");
                }
            }
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            return View(vehicle);
        }

        [Authorize(Roles = "Super Admin,Business Owner")]
        public async Task<IActionResult> Edit(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            return View(vehicle);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Super Admin,Business Owner")]
        public async Task<IActionResult> Edit(int id, Vehicle vehicle, IFormFile? vehicleImage)
        {
            if (id != vehicle.Id) return NotFound();
            ModelState.Remove(nameof(vehicleImage));

                if (ModelState.IsValid)
            {
                var existing = await _context.Vehicles.FindAsync(id);
                if (existing == null) return NotFound();

                // Handle image upload
                if (vehicleImage != null && vehicleImage.Length > 0)
                {
                    existing.ImagePath = await SaveVehicleImage(vehicleImage);
                }

                existing.VIN = vehicle.VIN;
                existing.PlateNumber = vehicle.PlateNumber;
                existing.Make = vehicle.Make;
                existing.Model = vehicle.Model;
                existing.Year = vehicle.Year;
                existing.Category = vehicle.Category;
                existing.BodyClass = vehicle.BodyClass;
                existing.Manufacturer = vehicle.Manufacturer;
                existing.PurchaseCost = vehicle.PurchaseCost;
                existing.AcquisitionDate = vehicle.AcquisitionDate;
                existing.Supplier = vehicle.Supplier;
                existing.UsefulLifeYears = vehicle.UsefulLifeYears;
                existing.SalvageValue = vehicle.SalvageValue;
                existing.InitialMileage = vehicle.InitialMileage;
                existing.CurrentMileage = vehicle.CurrentMileage;
                existing.InsuranceExpiry = vehicle.InsuranceExpiry;
                existing.RegistrationExpiry = vehicle.RegistrationExpiry;
                // Handle Branch Change (Transfer)
                string? transferDetails = null;
                if (existing.BranchId != vehicle.BranchId)
                {
                    var oldBranch = existing.BranchId.HasValue ? await _context.Branches.FindAsync(existing.BranchId.Value) : null;
                    var newBranch = vehicle.BranchId.HasValue ? await _context.Branches.FindAsync(vehicle.BranchId.Value) : null;
                    
                    transferDetails = $"Branch changed from {oldBranch?.Name ?? "Unassigned"} to {newBranch?.Name ?? "Unassigned"}.";
                    
                    _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
                    {
                        VehicleId = existing.Id,
                        EventType = LifecycleEventType.BranchTransferred,
                        EventDate = DateTime.UtcNow,
                        Notes = transferDetails,
                        Mileage = existing.CurrentMileage
                    });
                    
                    existing.BranchId = vehicle.BranchId;
                }

                await _context.SaveChangesAsync();

                // Log Audit
                var details = new List<string>();
                if (transferDetails != null) details.Add(transferDetails);
                if (!details.Any()) details.Add("Vehicle details updated.");

                await _audit.LogAsync(
                    (transferDetails != null) ? AuditAction.Transfer : AuditAction.Update,
                    AuditModule.Asset, "Vehicle",
                    existing.Id.ToString(),
                    $"{existing.Year} {existing.Make} {existing.Model} ({existing.PlateNumber})",
                    string.Join(" ", details));

                TempData["Success"] = "Vehicle updated successfully.";
                return RedirectToAction(nameof(Registration));
            }
            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();
            return View(vehicle);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Super Admin,Business Owner")]
        public async Task<IActionResult> Transfer(int vehicleId, int? newBranchId)
        {
            var vehicle = await _context.Vehicles.Include(v => v.Branch).FirstOrDefaultAsync(v => v.Id == vehicleId);
            if (vehicle == null) return NotFound();

            if (vehicle.BranchId == newBranchId)
            {
                TempData["Info"] = "Vehicle is already assigned to this branch.";
                return RedirectToAction(nameof(Registration));
            }

            var oldBranchName = vehicle.Branch?.Name ?? "Unassigned";
            var newBranch = newBranchId.HasValue ? await _context.Branches.FindAsync(newBranchId.Value) : null;
            var newBranchName = newBranch?.Name ?? "Unassigned";

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.BranchTransferred,
                EventDate = DateTime.UtcNow,
                Notes = $"Transferred from {oldBranchName} to {newBranchName}.",
                Mileage = vehicle.CurrentMileage
            });

            vehicle.BranchId = newBranchId;
            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Transfer, AuditModule.Asset, "Vehicle",
                vehicle.Id.ToString(),
                $"{vehicle.PlateNumber}",
                $"Transferred from {oldBranchName} to {newBranchName}.");

            TempData["Success"] = $"{vehicle.PlateNumber} transferred to {newBranchName}.";
            return RedirectToAction(nameof(Registration));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null) return NotFound();

            vehicle.Status = VehicleStatus.Retired;
            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicle.Id,
                EventType = LifecycleEventType.Retired,
                EventDate = DateTime.UtcNow,
                Notes = "Vehicle archived by administrator.",
                Mileage = vehicle.CurrentMileage
            });
            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.Archive, AuditModule.Asset, "Vehicle",
                vehicle.Id.ToString(),
                $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})",
                "Vehicle archived by administrator.");

            TempData["Success"] = $"{vehicle.Year} {vehicle.Make} {vehicle.Model} has been archived.";
            return RedirectToAction(nameof(Registration));
        }

        [HttpGet]
        public async Task<IActionResult> FetchVin(string vin)
        {
            if (string.IsNullOrWhiteSpace(vin) || vin.Length != 17)
                return BadRequest(new { error = "VIN must be exactly 17 characters." });

            var info = await _nhtsaService.DecodeVinAsync(vin);
            if (info == null)
                return StatusCode(503, new { error = "Could not reach NHTSA API. Please try again." });

            return Ok(info);
        }

        // ─── LIFECYCLE TRACKING ──────────────────────────────────────────────

        public async Task<IActionResult> Index(int? vehicleId)
        {
            var query = await GetBranchFilteredVehicles();
            var vehicles = await query.OrderBy(v => v.PlateNumber).ToListAsync();

            Vehicle? selected = null;
            List<VehicleLifecycleEvent> events = new();

            if (vehicleId.HasValue)
            {
                selected = await _context.Vehicles
                    .Include(v => v.LifecycleEvents)
                    .Include(v => v.Branch)
                    .FirstOrDefaultAsync(v => v.Id == vehicleId.Value);

                if (selected != null)
                    events = selected.LifecycleEvents.OrderByDescending(e => e.EventDate).ToList();
            }

            ViewBag.Vehicles = vehicles;
            ViewBag.SelectedVehicle = selected;
            ViewBag.Events = events;
            return View();
        }

        // ─── ASSET STATUS MONITORING ─────────────────────────────────────────

        public async Task<IActionResult> StatusMonitoring()
        {
            var query = await GetBranchFilteredVehicles();
            var vehicles = await query.OrderBy(v => v.PlateNumber).ToListAsync();

            var today = DateTime.Today;
            var soon = today.AddDays(30);

            ViewBag.Total = vehicles.Count;
            ViewBag.Available = vehicles.Count(v => v.Status == VehicleStatus.Available);
            ViewBag.Rented = vehicles.Count(v => v.Status == VehicleStatus.Rented);
            ViewBag.Reserved = vehicles.Count(v => v.Status == VehicleStatus.Reserved);
            ViewBag.UnderMaintenance = vehicles.Count(v => v.Status == VehicleStatus.UnderMaintenance);
            ViewBag.OutOfService = vehicles.Count(v => v.Status == VehicleStatus.OutOfService);
            ViewBag.Retired = vehicles.Count(v => v.Status == VehicleStatus.Retired);
            ViewBag.ExpiredRegistration = vehicles.Count(v => v.RegistrationExpiry.HasValue && v.RegistrationExpiry < today && v.Status != VehicleStatus.Retired);
            ViewBag.ExpiredInsurance = vehicles.Count(v => v.InsuranceExpiry.HasValue && v.InsuranceExpiry < today && v.Status != VehicleStatus.Retired);
            ViewBag.ExpiringSoonRegistration = vehicles.Count(v => v.RegistrationExpiry.HasValue && v.RegistrationExpiry >= today && v.RegistrationExpiry <= soon);
            ViewBag.ExpiringSoonInsurance = vehicles.Count(v => v.InsuranceExpiry.HasValue && v.InsuranceExpiry >= today && v.InsuranceExpiry <= soon);

            ViewBag.Branches = await _context.Branches.Where(b => b.IsActive).ToListAsync();

            return View(vehicles);
        }

        // ─── ASSET DISPOSAL ──────────────────────────────────────────────────

        public async Task<IActionResult> Disposal()
        {
            var query = await GetBranchFilteredVehicles();
            var vehicles = await query
                .Where(v => v.Status != VehicleStatus.Retired)
                .OrderBy(v => v.PlateNumber)
                .ToListAsync();

            var disposalRequests = await _context.DisposalRequests
                .Include(d => d.Vehicle)
                .OrderByDescending(d => d.RequestedAt)
                .ToListAsync();

            ViewBag.Vehicles = vehicles;
            ViewBag.DisposalRequests = disposalRequests;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitDisposal(int vehicleId, string reason, decimal estimatedRepairCost, decimal recommendedDisposalValue)
        {
            var vehicle = await _context.Vehicles.FindAsync(vehicleId);
            if (vehicle == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);

            var request = new DisposalRequest
            {
                VehicleId = vehicleId,
                Reason = reason,
                EstimatedRepairCost = estimatedRepairCost,
                RecommendedDisposalValue = recommendedDisposalValue,
                Status = DisposalRequestStatus.Pending,
                RequestedByUserId = user?.Id,
                RequestedByEmail = user?.Email,
                RequestedAt = DateTime.UtcNow
            };
            _context.DisposalRequests.Add(request);

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = vehicleId,
                EventType = LifecycleEventType.DisposalRequested,
                EventDate = DateTime.UtcNow,
                Notes = $"Disposal recommended. Reason: {reason}. Est. repair cost: ₱{estimatedRepairCost:N2}.",
                Mileage = vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.DisposalRequest, AuditModule.Disposal, "Vehicle",
                vehicle.Id.ToString(),
                $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})",
                $"Disposal requested. Reason: {reason}. Repair cost: ₱{estimatedRepairCost:N2}. Disposal value: ₱{recommendedDisposalValue:N2}.");

            TempData["Success"] = $"Disposal recommendation submitted for {vehicle.PlateNumber}.";

            // Notify all Business Owners about the new disposal request
            var businessOwners = await _userManager.GetUsersInRoleAsync("Business Owner");
            var ownerEmails = businessOwners.Where(o => !string.IsNullOrEmpty(o.Email)).Select(o => o.Email!).ToList();
            if (ownerEmails.Any())
            {
                var emailBody = $@"
                    <h2>New Disposal Recommendation</h2>
                    <p>A disposal recommendation has been submitted for review:</p>
                    <table style='border-collapse:collapse;'>
                        <tr><td style='padding:4px 12px;'><strong>Vehicle:</strong></td><td>{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.PlateNumber})</td></tr>
                        <tr><td style='padding:4px 12px;'><strong>Reason:</strong></td><td>{reason}</td></tr>
                        <tr><td style='padding:4px 12px;'><strong>Est. Repair Cost:</strong></td><td>₱{estimatedRepairCost:N2}</td></tr>
                        <tr><td style='padding:4px 12px;'><strong>Disposal Value:</strong></td><td>₱{recommendedDisposalValue:N2}</td></tr>
                        <tr><td style='padding:4px 12px;'><strong>Submitted By:</strong></td><td>{user?.Email}</td></tr>
                    </table>
                    <p>Please log in to review and approve or reject this request.</p>";

                try { await _email.SendEmailAsync(ownerEmails, $"Disposal Recommendation — {vehicle.PlateNumber}", emailBody); }
                catch { /* logged by SmtpEmailService */ }
            }

            return RedirectToAction(nameof(Disposal));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Business Owner")]
        public async Task<IActionResult> ApproveDisposal(int requestId, string? reviewNotes)
        {
            var request = await _context.DisposalRequests.Include(d => d.Vehicle).FirstOrDefaultAsync(d => d.Id == requestId);
            if (request == null) return NotFound();

            if (request.Status != DisposalRequestStatus.Pending)
            {
                TempData["Error"] = "This request has already been reviewed.";
                return RedirectToAction(nameof(Disposal));
            }

            var user = await _userManager.GetUserAsync(User);

            request.Status = DisposalRequestStatus.Approved;
            request.ReviewedByUserId = user?.Id;
            request.ReviewedByEmail = user?.Email;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNotes = reviewNotes;

            // Update Vehicle Status
            request.Vehicle.Status = VehicleStatus.Retired;
            request.Vehicle.DisposalDate = DateTime.UtcNow;
            request.Vehicle.DisposalValue = request.RecommendedDisposalValue;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = request.VehicleId,
                EventType = LifecycleEventType.Disposed,
                EventDate = DateTime.UtcNow,
                Notes = $"Disposal approved by owner. Value: ₱{request.RecommendedDisposalValue:N2}. Notes: {reviewNotes ?? "No notes provided."}",
                Mileage = request.Vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.DisposalApprove, AuditModule.Disposal, "Vehicle",
                request.VehicleId.ToString(),
                $"{request.Vehicle.PlateNumber}",
                $"Disposal approved. Value: ₱{request.RecommendedDisposalValue:N2}. Notes: {reviewNotes}");

            TempData["Success"] = $"Disposal approved for {request.Vehicle.PlateNumber}. Asset is now Retired.";

            // Notify the requester
            var approveBodyAL = $@"
                <h2>Disposal Request Approved</h2>
                <p>Your disposal request for <strong>{request.Vehicle.PlateNumber}</strong> ({request.Vehicle.Year} {request.Vehicle.Make} {request.Vehicle.Model}) has been <strong>approved</strong>.</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Disposal Value:</strong></td><td>₱{request.RecommendedDisposalValue:N2}</td></tr>
                    <tr><td style='padding:4px 12px;'><strong>Notes:</strong></td><td>{reviewNotes ?? "N/A"}</td></tr>
                </table>
                <p>The vehicle has been marked as <strong>Retired</strong>.</p>";
            try { await _email.SendEmailAsync(request.RequestedByEmail, $"Disposal Approved — {request.Vehicle.PlateNumber}", approveBodyAL); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Disposal));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Business Owner")]
        public async Task<IActionResult> RejectDisposal(int requestId, string? reviewNotes)
        {
            var request = await _context.DisposalRequests.Include(d => d.Vehicle).FirstOrDefaultAsync(d => d.Id == requestId);
            if (request == null) return NotFound();

            if (request.Status != DisposalRequestStatus.Pending)
            {
                TempData["Error"] = "This request has already been reviewed.";
                return RedirectToAction(nameof(Disposal));
            }

            var user = await _userManager.GetUserAsync(User);

            request.Status = DisposalRequestStatus.Rejected;
            request.ReviewedByUserId = user?.Id;
            request.ReviewedByEmail = user?.Email;
            request.ReviewedAt = DateTime.UtcNow;
            request.ReviewNotes = reviewNotes;

            _context.VehicleLifecycleEvents.Add(new VehicleLifecycleEvent
            {
                VehicleId = request.VehicleId,
                EventType = LifecycleEventType.StatusChanged,
                EventDate = DateTime.UtcNow,
                Notes = $"Disposal recommendation rejected by owner. Notes: {reviewNotes ?? "No notes provided."}",
                Mileage = request.Vehicle.CurrentMileage
            });

            await _context.SaveChangesAsync();

            await _audit.LogAsync(AuditAction.DisposalReject, AuditModule.Disposal, "Vehicle",
                request.VehicleId.ToString(),
                $"{request.Vehicle.PlateNumber}",
                $"Disposal rejected. Notes: {reviewNotes}");

            TempData["Success"] = $"Disposal recommendation rejected for {request.Vehicle.PlateNumber}.";

            // Notify the requester
            var rejectBodyAL = $@"
                <h2>Disposal Request Rejected</h2>
                <p>Your disposal request for <strong>{request.Vehicle.PlateNumber}</strong> ({request.Vehicle.Year} {request.Vehicle.Make} {request.Vehicle.Model}) has been <strong>rejected</strong>.</p>
                <table style='border-collapse:collapse;'>
                    <tr><td style='padding:4px 12px;'><strong>Notes:</strong></td><td>{reviewNotes ?? "N/A"}</td></tr>
                </table>";
            try { await _email.SendEmailAsync(request.RequestedByEmail, $"Disposal Rejected — {request.Vehicle.PlateNumber}", rejectBodyAL); }
            catch { /* logged by SmtpEmailService */ }

            return RedirectToAction(nameof(Disposal));
        }



        // ─── AJAX: Get vehicles by category ─────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetVehiclesByCategory(string category)
        {
            var today = DateTime.Today;
            var vehicles = await _context.Vehicles
                .Where(v => v.Status == VehicleStatus.Available
                    && v.Category == category
                    && (v.InsuranceExpiry == null || v.InsuranceExpiry >= today)
                    && (v.RegistrationExpiry == null || v.RegistrationExpiry >= today))
                .OrderBy(v => v.PlateNumber)
                .Select(v => new
                {
                    v.Id,
                    Display = $"{v.PlateNumber} — {v.Make} {v.Model} ({v.Year})",
                    v.ImagePath
                })
                .ToListAsync();

            return Json(vehicles);
        }

        // ─── Image Helper ──────────────────────────────────────────────────
        private async Task<string> SaveVehicleImage(IFormFile file)
        {
            // ── Security: Validate file size (max 5 MB) ──
            const long maxFileSize = 5 * 1024 * 1024;
            if (file.Length > maxFileSize)
                throw new InvalidOperationException("Image file size must not exceed 5 MB.");

            // ── Security: Validate file extension (whitelist) ──
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                throw new InvalidOperationException("Only .jpg, .jpeg, .png, and .webp image files are allowed.");

            if (string.IsNullOrEmpty(_env.WebRootPath))
            {
                throw new InvalidOperationException("WebRootPath is not configured. Ensure the wwwroot folder exists in your project.");
            }

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "vehicles");
            Directory.CreateDirectory(uploadsDir);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/vehicles/{fileName}";
        }
    }
}
