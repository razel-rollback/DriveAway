public class RoleManagementController : Controller
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IAuditService _audit;

    public RoleManagementController(
        RoleManager<IdentityRole> roleManager,
        UserManager<IdentityUser> userManager,
        IAuditService audit)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _audit = audit;
    }
}