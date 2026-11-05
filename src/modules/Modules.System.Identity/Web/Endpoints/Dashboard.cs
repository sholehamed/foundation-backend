using Modules.System.Identity.Application.Features.Dashboard;

namespace Modules.System.Identity.Web.Endpoints
{
    public class Dashboard : EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this).RequireAuthorization("SystemAdministrator")

                .MapGet(GetSuperDashboard, "super-dashboard")

            ;
        }

        public async Task<Ok<SuperDashboardDto>> GetSuperDashboard(IDispatcher sender, CancellationToken cancellationToken)
        {
            var res = await sender.Query(new SuperDashboardQuery(), cancellationToken);
            return TypedResults.Ok(res);
        }

    }
}
