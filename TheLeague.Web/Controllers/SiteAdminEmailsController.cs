using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheLeague.Interfaces;
using TheLeague.Web.Extensions;

namespace TheLeague.Web.Controllers;

[Authorize]
[ApiController]
[Route("api/admin/emails")]
public class SiteAdminEmailsController(IEmailOutboxService emailOutboxService, IUserService userService) : ControllerBase
{
	[HttpGet]
	public async Task<IActionResult> List([FromQuery] int take = 50, CancellationToken cancellationToken = default)
	{
		await RequireSiteAdminAsync(cancellationToken);
		return Ok(await emailOutboxService.ListRecentAsync(take, cancellationToken));
	}

	[HttpPost("{emailId:guid}/retry")]
	public async Task<IActionResult> Retry(Guid emailId, CancellationToken cancellationToken = default)
	{
		await RequireSiteAdminAsync(cancellationToken);
		var message = await emailOutboxService.SendAsync(emailId, cancellationToken);
		return Ok(message);
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
