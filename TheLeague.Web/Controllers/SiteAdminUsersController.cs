using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Models;
using TheLeague.Web.Extensions;
using TheLeague.Web.WebModels;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/admin/users")]
public class SiteAdminUsersController(IUserService userService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List(CancellationToken cancellationToken = default)
	{
		await RequireSiteAdminAsync(cancellationToken);
		return Ok(await userService.ListForSiteAdminAsync(cancellationToken));
	}

	[HttpPut("{userId:guid}/site-admin")]
	public async Task<IActionResult> SetSiteAdmin(Guid userId, UpdateSiteAdminRequest request, CancellationToken cancellationToken = default)
	{
		await RequireSiteAdminAsync(cancellationToken);
		var updated = await userService.SetSiteAdminAsync(User.GetRequiredUserId(), userId, request.IsSiteAdmin, cancellationToken);
		return Ok(new SiteUserAdminItem(updated.Id, updated.Name, updated.EmailAddress, updated.IsEmailVerified, updated.IsSiteAdmin, updated.IsDeleted, updated.CreatedAt, updated.EmailVerifiedAt, updated.DeletedAt));
	}

	private async Task RequireSiteAdminAsync(CancellationToken cancellationToken)
	{
		var account = await userService.GetByIdAsync(User.GetRequiredUserId(), cancellationToken);
		if (account?.IsSiteAdmin != true)
		{
			throw new UnauthorizedAccessException("Site admin access is required.");
		}
	}
}
