using Modules.System.Identity.Web.Util;
﻿using Modules.System.Identity.Application.Features.Dashboard;

namespace Modules.System.Identity.Web.Endpoints
{
    public class Dashboard : EndpointGroupBase
    {
        public override void Map(WebApplication app)
        {
            app.MapGroup(this).RequireAuthorization("SystemAdministrator")

                .MapGet(GetSuperDashboard, "super-dashboard", configure: e => e.WithFeatureEndpoint("SYSTEM.DASHBOARD.SUPER-DASHBOARD", "dashboard", "DASHBOARD.READ", "GetSuperDashboard"))

            ;
        }

        public async Task<Ok<SuperDashboardDto>> GetSuperDashboard(IDispatcher sender, CancellationToken cancellationToken)
        {
            var res = await sender.Query(new SuperDashboardQuery(), cancellationToken);
            return TypedResults.Ok(res);
        }

    }
}
