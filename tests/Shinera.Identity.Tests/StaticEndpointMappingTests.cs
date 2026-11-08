using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Web.SharedKernel.Util;
using Xunit;

namespace Shinera.Identity.Tests;

/// <summary>
/// The FeatureRegistry is a static method group; Delegate.Target is null.
/// Catch route registration regressions without requiring SQL startup.
/// </summary>
public sealed class StaticEndpointMappingTests
{
    [Fact]
    public async Task All_shared_http_verbs_accept_static_handlers_with_null_Target()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        IEndpointRouteBuilder group = app.MapGroup("/static");

        Func<IResult> read = Read;
        Func<IResult> post = Post;
        Func<IResult> put = Put;
        Func<IResult> delete = Delete;
        Func<IResult> patch = Patch;
        Func<IResult> methods = Methods;
        Func<IResult> file = File;

        Assert.Null(read.Target);
        Assert.Null(post.Target);
        IEndpointRouteBuilderExtensions.MapGet(group, read, "get");
        IEndpointRouteBuilderExtensions.MapPost(group, post, "post");
        IEndpointRouteBuilderExtensions.MapPut(group, put, "put");
        IEndpointRouteBuilderExtensions.MapDelete(group, delete, "delete");
        IEndpointRouteBuilderExtensions.MapPatch(group, patch, "patch");
        IEndpointRouteBuilderExtensions.MapMethods(group, methods, ["OPTIONS"], "methods");
        IEndpointRouteBuilderExtensions.MapFile(group, file, "file");

        await app.StartAsync();
        var client = app.GetTestClient();
        foreach (var (verb, path) in new[]
        {
            (HttpMethod.Get, "/static/get"),
            (HttpMethod.Post, "/static/post"),
            (HttpMethod.Put, "/static/put"),
            (HttpMethod.Delete, "/static/delete"),
            (HttpMethod.Patch, "/static/patch"),
            (HttpMethod.Options, "/static/methods"),
            (HttpMethod.Post, "/static/file")
        })
        {
            using var request = new HttpRequestMessage(verb, path);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static IResult Read() => Results.Ok("get");
    private static IResult Post() => Results.Ok("post");
    private static IResult Put() => Results.Ok("put");
    private static IResult Delete() => Results.Ok("delete");
    private static IResult Patch() => Results.Ok("patch");
    private static IResult Methods() => Results.Ok("methods");
    private static IResult File() => Results.Ok("file");
}
