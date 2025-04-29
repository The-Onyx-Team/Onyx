using Microsoft.EntityFrameworkCore;
using Onyx.Data.DataBaseSchema;
using Onyx.Data.DataBaseSchema.TableEntities;

namespace Onyx.App.Web.Api;

public static class DataEndpoints
{
    public static IEndpointRouteBuilder MapDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dataApi = endpoints.MapGroup("/api/data")
            .WithTags("Data API")
            .RequireAuthorization();
        
        
        
        return endpoints;
    }
}