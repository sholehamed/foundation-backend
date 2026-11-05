using Application.SharedKernel.Abstractions.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Modules.System.FileStorage.Application.Abstraction.Interfaces;
using Modules.System.FileStorage.Application.Features.UploadMedia;
using Modules.System.FileStorage.Domain.Common;
using Web.SharedKernel.Attributes;
using Web.SharedKernel.Util;


namespace Modules.System.FileStorage.Web.Endpoints;

public class MediaStorage : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        app.MapGroup(this)
            .MapFile(Create,"upload",configure: x => x.RequireAuthorization())
            .MapGet(Download, "/media/{id:guid}/download/{variant}",configure:x=>x.RequireAuthorization())
            .MapGet(Preview, "/media/{id:guid}/preview/{variant}", configure: x => x.RequireAuthorization())
;
    }

    [Resouce("", "دریافت لیست دسترسی های پکیج")]
    public async Task<Results<Accepted<UploadMediaResponse>, BadRequest<string>>> Create(
    [FromServices] IDispatcher sender,
    IFormFile? file,
    CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return TypedResults.BadRequest("File is empty.");

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);

        var command = new UploadMediaCommand(
            file.FileName,
            file.ContentType,
            ms.ToArray());

        var id = await sender.Send(command, cancellationToken);

        return TypedResults.Accepted($"{id}", value: new UploadMediaResponse(id, "Pending"));

    }

    public async Task<IResult> Download(
    Guid id,
    MediaVariantType variant,
    IMediaRepository repository,
    HttpContext httpContext,
    CancellationToken cancellationToken)
    {
        {
            await using var result = await repository.OpenDownloadStreamAsync(id, variant, cancellationToken);

            if (result is null)
                return Results.NotFound();

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = result.ContentType;
            httpContext.Response.ContentLength = result.ContentLength;
            httpContext.Response.Headers.Append(
                "Content-Disposition",
                $"inline; filename=\"{result.FileName}\"");

            await result.Content.CopyToAsync(httpContext.Response.Body, 81920, cancellationToken);

            return Results.Empty;
        }
    }
    public async Task<IResult> Preview(
    Guid id,
    MediaVariantType variant,
    IMediaRepository repository,
    CancellationToken cancellationToken)
    {
        var result = await repository.OpenDownloadStreamAsync(id, variant, cancellationToken);

        if (result is null)
            return Results.NotFound();

        return Results.File(
            fileStream: result.Content,
            contentType: result.ContentType,
            enableRangeProcessing: true);
    }
    public record UploadMediaResponse(Guid MediaId, string Status);

}